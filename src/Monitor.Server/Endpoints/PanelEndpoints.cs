using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.Sqlite;
using Monitor.Server.Data;
using Monitor.Server.Reports;
using Monitor.Server.Security;

namespace Monitor.Server.Endpoints;

/// <summary>Rotas do painel usado por André e Renata.</summary>
public static class PanelEndpoints
{
    /// <summary>Cabeçalho exigido em toda alteração, para impedir envio de formulário por outro site.</summary>
    public const string CsrfHeader = "X-Requested-With";
    public const string CsrfValue = "monitor";

    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record PasswordRequest(string? Current, string? New);
    public sealed record MachineRequest(string? Name, string? Employee, bool? Active);
    public sealed record RuleRequest(string? Kind, string? Pattern, string? Category, string? Label);
    public sealed record SettingsRequest(int? IdleMinutes);
    public sealed record DayHours(string? Start, string? End);
    public sealed record ScheduleRequest(string? EffectiveFrom, Dictionary<string, DayHours?>? Days);

    public static void MapPanelEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/login", async (LoginRequest body, HttpContext ctx, Db db, LoginThrottle throttle) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
            if (throttle.IsLocked(ip, out var remaining))
                return Results.Json(new { error = $"Muitas tentativas. Tente de novo em {Math.Ceiling(remaining.TotalMinutes)} minutos." }, statusCode: 429);

            using var conn = db.Open();
            var user = conn.Query(
                "SELECT username, display_name, password_hash FROM users WHERE username = $u",
                r => (Username: r.GetString(0), Display: r.GetString(1), Hash: r.GetString(2)),
                ("$u", (body.Username ?? "").Trim())).FirstOrDefault();

            if (user.Username is null || !Secrets.VerifyPassword(body.Password ?? "", user.Hash))
            {
                throttle.Fail(ip);
                Audit(conn, ctx, "login_falhou", body.Username);
                return Results.Json(new { error = "Usuário ou senha incorretos." }, statusCode: 401);
            }

            throttle.Success(ip);
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, user.Username), new Claim("display", user.Display)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
            Audit(conn, ctx, "login", null, user.Username);
            return Results.Ok(new { username = user.Username, displayName = user.Display });
        }).AddEndpointFilter(RequireCsrfHeader);

        var panel = api.MapGroup("").RequireAuthorization().AddEndpointFilter(RequireCsrfHeader);

        panel.MapPost("/logout", async (HttpContext ctx, Db db) =>
        {
            using (var conn = db.Open()) Audit(conn, ctx, "logout", null);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        panel.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            username = user.Identity?.Name,
            displayName = user.FindFirst("display")?.Value ?? user.Identity?.Name,
        }));

        panel.MapPost("/me/password", (PasswordRequest body, ClaimsPrincipal user, HttpContext ctx, Db db) =>
        {
            var problem = Secrets.ValidatePasswordRules(body.New ?? "");
            if (problem is not null) return Results.BadRequest(new { error = problem });

            using var conn = db.Open();
            var hash = conn.Scalar<string>("SELECT password_hash FROM users WHERE username = $u", ("$u", user.Identity!.Name));
            if (hash is null || !Secrets.VerifyPassword(body.Current ?? "", hash))
                return Results.BadRequest(new { error = "A senha atual não confere." });

            conn.Exec("UPDATE users SET password_hash = $h WHERE username = $u",
                ("$h", Secrets.HashPassword(body.New!)), ("$u", user.Identity!.Name));
            Audit(conn, ctx, "senha_alterada", null);
            return Results.Ok();
        });

        // ---------- Computadores ----------

        panel.MapGet("/machines", (Db db) =>
        {
            using var conn = db.Open();
            return Results.Ok(LoadMachines(conn, includeInactive: true));
        });

        panel.MapPost("/machines", (MachineRequest body, HttpContext ctx, Db db) =>
        {
            var name = (body.Name ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { error = "Informe um nome para o computador." });

            using var conn = db.Open();
            var token = Secrets.NewAgentToken();
            conn.Exec(
                "INSERT INTO machines(name, employee, token_hash, created_utc) VALUES ($n, $e, $h, $t)",
                ("$n", name), ("$e", NullIfEmpty(body.Employee)), ("$h", Secrets.HashToken(token)),
                ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            var id = conn.Scalar<long>("SELECT last_insert_rowid()");
            Audit(conn, ctx, "computador_criado", $"{id} {name}");
            return Results.Ok(new { id, token });
        });

        panel.MapPut("/machines/{id:long}", (long id, MachineRequest body, HttpContext ctx, Db db) =>
        {
            using var conn = db.Open();
            var changed = conn.Exec(
                """
                UPDATE machines SET
                    name = COALESCE($n, name),
                    employee = CASE WHEN $e IS NULL THEN employee ELSE NULLIF($e, '') END,
                    active = COALESCE($a, active)
                WHERE id = $id
                """,
                ("$n", NullIfEmpty(body.Name)), ("$e", body.Employee?.Trim()),
                ("$a", body.Active is null ? null : body.Active.Value ? 1 : 0), ("$id", id));
            if (changed == 0) return Results.NotFound();
            Audit(conn, ctx, "computador_alterado", id.ToString(CultureInfo.InvariantCulture));
            return Results.Ok();
        });

        panel.MapPost("/machines/{id:long}/token", (long id, HttpContext ctx, Db db) =>
        {
            using var conn = db.Open();
            var token = Secrets.NewAgentToken();
            var changed = conn.Exec("UPDATE machines SET token_hash = $h WHERE id = $id",
                ("$h", Secrets.HashToken(token)), ("$id", id));
            if (changed == 0) return Results.NotFound();
            Audit(conn, ctx, "chave_regerada", id.ToString(CultureInfo.InvariantCulture));
            return Results.Ok(new { id, token });
        });

        // Instalador para Windows (.zip montado na hora com o programa e os scripts).
        // Só para quem está logado no painel.
        panel.MapGet("/download/agente", async (HttpContext ctx, Db db) =>
        {
            var dir = Environment.GetEnvironmentVariable("AGENT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "agente");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
            if (!files.Any(f => Path.GetFileName(f).Equals("MonitorPortoconseg.exe", StringComparison.OrdinalIgnoreCase)))
                return Results.NotFound(new { error = "Instalador não encontrado no servidor." });

            using (var conn = db.Open()) Audit(conn, ctx, "baixou_instalador", null);

            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition = "attachment; filename=\"MonitorPortoconseg-instalador.zip\"";
            await using var body = ctx.Response.BodyWriter.AsStream();
            using (var zip = new System.IO.Compression.ZipArchive(body, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files.OrderBy(f => f))
                {
                    var entry = zip.CreateEntry(Path.GetFileName(file), System.IO.Compression.CompressionLevel.Fastest);
                    await using var input = File.OpenRead(file);
                    await using var output = entry.Open();
                    await input.CopyToAsync(output);
                }
            }
            return Results.Empty;
        });

        // ---------- Relatórios ----------

        panel.MapGet("/report/day", (string? date, HttpContext ctx, Db db) =>
        {
            using var conn = db.Open();
            var settings = Settings.Load(conn);
            var tz = Settings.ResolveTimeZone(settings.TimeZoneId);
            var day = ParseDateOrToday(date, tz);
            var schedules = Settings.LoadSchedules(conn);
            var schedule = schedules.For(day).For(day.DayOfWeek);
            var classifier = Classifier.Load(conn);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var (start, end) = ReportBuilder.DayBoundsUtc(day, tz);

            var machines = LoadMachines(conn, includeInactive: false).Select(m =>
            {
                var rows = LoadRows(conn, m.Id, start, end);
                return new { machine = m, report = ReportBuilder.BuildDay(day, rows, schedule, tz, now, classifier) };
            }).ToList();

            Audit(conn, ctx, "viu_relatorio_dia", day.ToString("yyyy-MM-dd"));
            return Results.Ok(new
            {
                date = day.ToString("yyyy-MM-dd"),
                generatedMs = now,
                timeZone = settings.TimeZoneId,
                idleMinutes = settings.IdleMinutes,
                schedule = schedule is null ? null : new { start = schedule.Start.ToString("HH:mm"), end = schedule.End.ToString("HH:mm") },
                machines,
            });
        });

        panel.MapGet("/report/week", (string? date, HttpContext ctx, Db db) =>
        {
            using var conn = db.Open();
            var settings = Settings.Load(conn);
            var tz = Settings.ResolveTimeZone(settings.TimeZoneId);
            var anyDay = ParseDateOrToday(date, tz);
            var monday = anyDay.AddDays(-(((int)anyDay.DayOfWeek + 6) % 7));
            var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToList();
            var schedules = Settings.LoadSchedules(conn);
            var classifier = Classifier.Load(conn);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var (weekStart, _) = ReportBuilder.DayBoundsUtc(days[0], tz);
            var (_, weekEnd) = ReportBuilder.DayBoundsUtc(days[^1], tz);

            var machines = LoadMachines(conn, includeInactive: false).Select(m =>
            {
                var rows = LoadRows(conn, m.Id, weekStart, weekEnd);
                var summaries = days.Select(d =>
                {
                    var (ds, de) = ReportBuilder.DayBoundsUtc(d, tz);
                    var dayRows = rows.Where(r => r.EndMs > ds && r.StartMs < de);
                    var schedule = schedules.For(d).For(d.DayOfWeek);
                    return ReportBuilder.BuildDay(d, dayRows, schedule, tz, now, classifier, includeDetail: false).Summary;
                }).ToList();
                return new
                {
                    machine = m,
                    days = summaries,
                    totals = new
                    {
                        windowElapsedMs = summaries.Sum(s => s.WindowElapsedMs),
                        activeMs = summaries.Sum(s => s.ActiveMs),
                        idleMs = summaries.Sum(s => s.IdleMs),
                        awayMs = summaries.Sum(s => s.AwayMs),
                        personalMs = summaries.Sum(s => s.PersonalMs),
                        workMs = summaries.Sum(s => s.WorkMs),
                        outsideHoursActiveMs = summaries.Sum(s => s.OutsideHoursActiveMs),
                    },
                };
            }).ToList();

            Audit(conn, ctx, "viu_relatorio_semana", monday.ToString("yyyy-MM-dd"));
            return Results.Ok(new
            {
                weekStart = monday.ToString("yyyy-MM-dd"),
                days = days.Select(d => d.ToString("yyyy-MM-dd")),
                generatedMs = now,
                machines,
            });
        });

        // ---------- Classificação ----------

        panel.MapGet("/rules", (Db db) =>
        {
            using var conn = db.Open();
            return Results.Ok(conn.Query(
                "SELECT id, kind, pattern, category, label FROM rules ORDER BY category, kind, pattern",
                r => new Rule(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetStringOrNull(4))));
        });

        panel.MapPost("/rules", (RuleRequest body, HttpContext ctx, Db db) =>
        {
            var kind = body.Kind?.Trim().ToLowerInvariant();
            var pattern = body.Pattern?.Trim().ToLowerInvariant();
            if (kind is not ("domain" or "process") || string.IsNullOrEmpty(pattern))
                return Results.BadRequest(new { error = "Informe se é site ou programa e o endereço ou nome." });
            if (!Categories.IsValid(body.Category))
                return Results.BadRequest(new { error = "Escolha trabalho, pessoal ou neutro." });
            if (kind == "domain")
            {
                pattern = Monitor.Core.DomainParser.FromAddressBar(pattern) ?? pattern;
            }
            else
            {
                pattern = Monitor.Core.PersonalFilter.NormalizeProcess(pattern);
            }

            using var conn = db.Open();
            conn.Exec(
                """
                INSERT INTO rules(kind, pattern, category, label) VALUES ($k, $p, $c, $l)
                ON CONFLICT(kind, pattern) DO UPDATE SET category = excluded.category, label = excluded.label
                """,
                ("$k", kind), ("$p", pattern), ("$c", body.Category), ("$l", NullIfEmpty(body.Label)));

            var scrubbed = 0;
            if (body.Category == Categories.Personal)
                scrubbed = ScrubPersonal(conn, kind, pattern);

            Audit(conn, ctx, "regra_salva", $"{kind}:{pattern} => {body.Category}");
            return Results.Ok(new { scrubbed });
        });

        panel.MapDelete("/rules/{id:long}", (long id, HttpContext ctx, Db db) =>
        {
            using var conn = db.Open();
            var rule = conn.Query("SELECT kind, pattern FROM rules WHERE id = $id",
                r => $"{r.GetString(0)}:{r.GetString(1)}", ("$id", id)).FirstOrDefault();
            if (rule is null) return Results.NotFound();
            conn.Exec("DELETE FROM rules WHERE id = $id", ("$id", id));
            Audit(conn, ctx, "regra_removida", rule);
            return Results.Ok();
        });

        panel.MapGet("/unclassified", (int? days, Db db) =>
        {
            using var conn = db.Open();
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days ?? 30, 1, 365)).ToUnixTimeMilliseconds();
            var classifier = Classifier.Load(conn);
            var rows = conn.Query(
                """
                SELECT process, app_name, domain, title, end_utc - start_utc
                FROM segments WHERE state = 'active' AND personal = 0 AND end_utc >= $s
                """,
                r => (Process: r.GetStringOrNull(0), App: r.GetStringOrNull(1), Domain: r.GetStringOrNull(2),
                      Title: r.GetStringOrNull(3), Ms: r.GetInt64(4)),
                ("$s", since));

            var items = rows
                .Select(r => (Row: r, Cls: classifier.Classify(false, r.Process, r.App, r.Domain)))
                .Where(x => x.Cls.Category == Categories.Unclassified && x.Cls.Key is not null)
                .GroupBy(x => x.Cls.Key!)
                .Select(g =>
                {
                    var key = g.Key;
                    var sep = key.IndexOf(':');
                    return new
                    {
                        kind = key[..sep],
                        pattern = key[(sep + 1)..],
                        label = g.First().Cls.Label,
                        ms = g.Sum(x => x.Row.Ms),
                        titles = g.Where(x => x.Row.Title is not null)
                            .GroupBy(x => x.Row.Title!)
                            .OrderByDescending(t => t.Sum(x => x.Row.Ms))
                            .Take(3)
                            .Select(t => t.Key)
                            .ToList(),
                    };
                })
                .OrderByDescending(i => i.ms)
                .ToList();
            return Results.Ok(items);
        });

        // ---------- Configurações e horários ----------

        panel.MapGet("/settings", (Db db) =>
        {
            using var conn = db.Open();
            var settings = Settings.Load(conn);
            var schedules = Settings.LoadSchedules(conn);
            return Results.Ok(new
            {
                settings.IdleMinutes,
                settings.RetentionDays,
                settings.TimeZoneId,
                schedules = schedules.Items.Select(s => new
                {
                    effectiveFrom = s.EffectiveFrom.ToString("yyyy-MM-dd"),
                    days = s.Days.OrderBy(d => ((int)d.Key + 6) % 7).ToDictionary(
                        d => d.Key.ToString().ToLowerInvariant(),
                        d => new { start = d.Value.Start.ToString("HH:mm"), end = d.Value.End.ToString("HH:mm") }),
                }).Reverse(),
            });
        });

        panel.MapPut("/settings", (SettingsRequest body, HttpContext ctx, Db db) =>
        {
            if (body.IdleMinutes is null or < 1 or > 60)
                return Results.BadRequest(new { error = "O tempo para considerar inativo deve ficar entre 1 e 60 minutos." });
            using var conn = db.Open();
            Settings.SaveValue(conn, "idle_minutes", body.IdleMinutes.Value.ToString(CultureInfo.InvariantCulture));
            Audit(conn, ctx, "config_alterada", $"inativo={body.IdleMinutes} min");
            return Results.Ok();
        });

        panel.MapPost("/schedules", (ScheduleRequest body, ClaimsPrincipal user, HttpContext ctx, Db db) =>
        {
            if (!DateOnly.TryParseExact(body.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                return Results.BadRequest(new { error = "Informe a data a partir da qual o horário vale." });

            var days = new Dictionary<DayOfWeek, DaySchedule>();
            foreach (var (name, hours) in body.Days ?? [])
            {
                if (hours is null || string.IsNullOrWhiteSpace(hours.Start) && string.IsNullOrWhiteSpace(hours.End))
                    continue;
                if (!Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var dow))
                    return Results.BadRequest(new { error = $"Dia inválido: {name}." });
                if (!Settings.TryParseTime(hours.Start, out var s) || !Settings.TryParseTime(hours.End, out var e) || e <= s)
                    return Results.BadRequest(new { error = $"Horário inválido em {name}: use HH:mm e o fim depois do início." });
                days[dow] = new DaySchedule(s, e);
            }

            using var conn = db.Open();
            Settings.SaveSchedule(conn, new WorkSchedule(from, days), user.Identity?.Name);
            Audit(conn, ctx, "horario_salvo", $"a partir de {from:yyyy-MM-dd}: {Settings.SerializeDays(days)}");
            return Results.Ok();
        });

        panel.MapDelete("/schedules/{date}", (string date, HttpContext ctx, Db db) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                return Results.BadRequest(new { error = "Data inválida." });
            using var conn = db.Open();
            if (conn.Scalar<long>("SELECT COUNT(*) FROM schedules") <= 1)
                return Results.BadRequest(new { error = "É preciso manter pelo menos um horário." });
            Settings.DeleteSchedule(conn, from);
            Audit(conn, ctx, "horario_removido", date);
            return Results.Ok();
        });

        panel.MapGet("/audit", (int? limit, Db db) =>
        {
            using var conn = db.Open();
            return Results.Ok(conn.Query(
                "SELECT at_utc, username, action, detail, ip FROM audit ORDER BY at_utc DESC LIMIT $l",
                r => new { atMs = r.GetInt64(0), username = r.GetStringOrNull(1), action = r.GetString(2), detail = r.GetStringOrNull(3), ip = r.GetStringOrNull(4) },
                ("$l", Math.Clamp(limit ?? 200, 1, 1000))));
        });
    }

    public sealed record MachineView(
        long Id, string Name, string? Employee, bool Active, long? LastSeenMs,
        string? AgentVersion, string? Hostname, string? WindowsUser, bool Online);

    private static List<MachineView> LoadMachines(SqliteConnection conn, bool includeInactive)
    {
        var onlineSince = DateTimeOffset.UtcNow.Subtract(OnlineWindow).ToUnixTimeMilliseconds();
        return conn.Query(
            $"""
            SELECT id, name, employee, active, last_seen_utc, agent_version, hostname, windows_user
            FROM machines {(includeInactive ? "" : "WHERE active = 1")} ORDER BY name
            """,
            r => new MachineView(
                r.GetInt64(0), r.GetString(1), r.GetStringOrNull(2), r.GetInt64(3) == 1, r.GetInt64OrNull(4),
                r.GetStringOrNull(5), r.GetStringOrNull(6), r.GetStringOrNull(7),
                r.GetInt64OrNull(4) is { } seen && seen >= onlineSince));
    }

    private static List<SegRow> LoadRows(SqliteConnection conn, long machineId, long startMs, long endMs) =>
        conn.Query(
            """
            SELECT start_utc, end_utc, state, personal, process, app_name, domain, title
            FROM segments WHERE machine_id = $m AND end_utc > $s AND start_utc < $e
            ORDER BY start_utc
            """,
            r => new SegRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3) == 1,
                r.GetStringOrNull(4), r.GetStringOrNull(5), r.GetStringOrNull(6), r.GetStringOrNull(7)),
            ("$m", machineId), ("$s", startMs), ("$e", endMs));

    /// <summary>Ao marcar um site ou programa como pessoal, apaga o detalhe já guardado.</summary>
    private static int ScrubPersonal(SqliteConnection conn, string kind, string pattern) =>
        kind == "domain"
            ? conn.Exec(
                """
                UPDATE segments SET personal = 1, process = NULL, app_name = NULL, domain = NULL, title = NULL
                WHERE domain = $p OR domain LIKE '%.' || $p
                """, ("$p", pattern))
            : conn.Exec(
                """
                UPDATE segments SET personal = 1, process = NULL, app_name = NULL, domain = NULL, title = NULL
                WHERE process = $p
                """, ("$p", pattern));

    private static DateOnly ParseDateOrToday(string? value, TimeZoneInfo tz)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Audit(SqliteConnection conn, HttpContext ctx, string action, string? detail, string? username = null) =>
        conn.Exec(
            "INSERT INTO audit(at_utc, username, action, detail, ip) VALUES ($t, $u, $a, $d, $ip)",
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ("$u", username ?? ctx.User.Identity?.Name),
            ("$a", action),
            ("$d", detail),
            ("$ip", ctx.Connection.RemoteIpAddress?.ToString()));

    private static async ValueTask<object?> RequireCsrfHeader(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && request.Headers[CsrfHeader] != CsrfValue)
            return Results.Json(new { error = "Requisição recusada." }, statusCode: 400);
        return await next(context);
    }
}
