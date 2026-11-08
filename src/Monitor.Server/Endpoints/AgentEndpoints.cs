using Microsoft.Data.Sqlite;
using Monitor.Core;
using Monitor.Server.Data;
using Monitor.Server.Reports;
using Monitor.Server.Security;

namespace Monitor.Server.Endpoints;

/// <summary>Rotas usadas pelo programa instalado nos computadores.</summary>
public static class AgentEndpoints
{
    private const int MaxSegmentsPerUpload = 5000;
    private static readonly TimeSpan MaxSegmentLength = TimeSpan.FromHours(24);

    public static void MapAgentEndpoints(this WebApplication app)
    {
        app.MapGet(Protocol.ConfigPath, (HttpRequest request, Db db) =>
        {
            using var conn = db.Open();
            var machineId = Authenticate(request, conn);
            if (machineId is null) return Results.Unauthorized();

            var settings = Settings.Load(conn);
            var classifier = Classifier.Load(conn);
            return Results.Json(classifier.ToAgentConfig(settings.IdleMinutes), Protocol.Json);
        });

        app.MapPost(Protocol.UploadPath, async (HttpRequest request, Db db) =>
        {
            UploadRequest? upload;
            try
            {
                upload = await request.ReadFromJsonAsync<UploadRequest>(Protocol.Json);
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "Conteúdo inválido." });
            }
            if (upload is null) return Results.BadRequest(new { error = "Conteúdo vazio." });
            if (upload.Segments.Count > MaxSegmentsPerUpload)
                return Results.BadRequest(new { error = $"Máximo de {MaxSegmentsPerUpload} períodos por envio." });

            using var conn = db.Open();
            var machineId = Authenticate(request, conn);
            if (machineId is null) return Results.Unauthorized();

            var classifier = Classifier.Load(conn);
            var settings = Settings.Load(conn);
            var now = DateTimeOffset.UtcNow;
            var oldest = now.AddDays(-settings.RetentionDays);
            var inserted = 0;

            using (var tx = conn.BeginTransaction())
            {
                foreach (var seg in upload.Segments)
                {
                    if (!IsValid(seg, oldest, now)) continue;

                    // Segurança extra: se a lista de uso pessoal do servidor for mais nova que a do agente,
                    // o detalhe também é descartado aqui.
                    var personal = seg.Personal
                        || (seg.State == ActivityState.Active && classifier.IsPersonal(seg.Process, seg.Domain));
                    var keepDetail = seg.State == ActivityState.Active && !personal;

                    inserted += conn.Exec(tx,
                        """
                        INSERT OR IGNORE INTO segments(id, machine_id, start_utc, end_utc, state, personal, process, app_name, domain, title)
                        VALUES ($id, $m, $s, $e, $st, $p, $proc, $app, $dom, $title)
                        """,
                        ("$id", seg.Id.ToString()),
                        ("$m", machineId.Value),
                        ("$s", seg.Start.ToUnixTimeMilliseconds()),
                        ("$e", seg.End.ToUnixTimeMilliseconds()),
                        ("$st", StateName(seg.State)),
                        ("$p", personal ? 1 : 0),
                        ("$proc", keepDetail ? Clip(seg.Process, 120)?.ToLowerInvariant() : null),
                        ("$app", keepDetail ? Clip(seg.AppName, 120) : null),
                        ("$dom", keepDetail ? Clip(seg.Domain, 253)?.ToLowerInvariant() : null),
                        ("$title", keepDetail ? Clip(seg.Title, PeriodTracker.MaxTitleLength) : null));
                }

                conn.Exec(tx,
                    """
                    UPDATE machines SET last_seen_utc = $t, agent_version = $v, hostname = $h, windows_user = $u
                    WHERE id = $id
                    """,
                    ("$t", now.ToUnixTimeMilliseconds()),
                    ("$v", Clip(upload.AgentVersion, 40)),
                    ("$h", Clip(upload.Hostname, 100)),
                    ("$u", Clip(upload.WindowsUser, 100)),
                    ("$id", machineId.Value));
                tx.Commit();
            }

            return Results.Json(new UploadResponse(upload.Segments.Count, inserted), Protocol.Json);
        });
    }

    private static long? Authenticate(HttpRequest request, SqliteConnection conn)
    {
        var token = request.Headers[Protocol.TokenHeader].ToString();
        if (string.IsNullOrWhiteSpace(token)) return null;
        return conn.Scalar<long?>(
            "SELECT id FROM machines WHERE token_hash = $h AND active = 1",
            ("$h", Secrets.HashToken(token)));
    }

    private static bool IsValid(SegmentDto seg, DateTimeOffset oldest, DateTimeOffset now)
    {
        if (seg.Id == Guid.Empty) return false;
        if (seg.End <= seg.Start) return false;
        if (seg.End - seg.Start > MaxSegmentLength) return false;
        if (seg.End < oldest) return false;
        if (seg.Start > now.AddMinutes(10)) return false;
        return Enum.IsDefined(seg.State);
    }

    public static string StateName(ActivityState state) => state switch
    {
        ActivityState.Active => "active",
        ActivityState.Idle => "idle",
        ActivityState.Locked => "locked",
        _ => "active",
    };

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }
}
