using Monitor.Server.Data;

namespace Monitor.Server.Reports;

/// <summary>Período como está no banco (horários em milissegundos UTC).</summary>
public sealed record SegRow(
    long StartMs,
    long EndMs,
    string State,
    bool Personal,
    string? Process,
    string? AppName,
    string? Domain,
    string? Title);

public sealed record TopItem(string Label, string Category, string? Key, long Ms);

/// <summary>Trecho da linha do tempo ou do registro detalhado.</summary>
/// <param name="State">active, idle, locked ou off (sem registro: desligado, suspenso ou agente parado).</param>
public sealed record TimelineItem(long StartMs, long EndMs, string State, string? Category, string? Label, string? Title);

public sealed record DaySummary(
    string Date,
    bool WorkDay,
    long? WindowStartMs,
    long? WindowEndMs,
    long? ArrivalMs,
    long? DepartureMs,
    long WindowElapsedMs,
    long ActiveMs,
    long IdleMs,
    long LockedMs,
    long AwayMs,
    long PersonalMs,
    long WorkMs,
    long UnclassifiedMs,
    long OutsideHoursActiveMs);

public sealed record DayDetail(DaySummary Summary, IReadOnlyList<TopItem> Top, IReadOnlyList<TimelineItem> Timeline, IReadOnlyList<TimelineItem> Log);

/// <summary>
/// Calcula o relatório de um dia de um computador.
/// "Fora do computador" dentro do horário = tempo do horário que não foi nem ativo nem inativo
/// (tela bloqueada, desligado, suspenso, antes da chegada ou depois da saída).
/// </summary>
public static class ReportBuilder
{
    private const long MergeGapMs = 30_000;
    private const long OffThresholdMs = 60_000;

    public static (long StartMs, long EndMs) DayBoundsUtc(DateOnly date, TimeZoneInfo tz)
    {
        var start = ToUtcMs(date, TimeOnly.MinValue, tz);
        var end = ToUtcMs(date.AddDays(1), TimeOnly.MinValue, tz);
        return (start, end);
    }

    public static long ToUtcMs(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    public static DayDetail BuildDay(
        DateOnly date,
        IEnumerable<SegRow> rows,
        DaySchedule? window,
        TimeZoneInfo tz,
        long nowMs,
        Classifier classifier,
        bool includeDetail = true)
    {
        var (dayStart, dayEnd) = DayBoundsUtc(date, tz);
        var limit = Math.Min(dayEnd, nowMs);

        long? ws = null, we = null;
        if (window is not null)
        {
            ws = ToUtcMs(date, window.Start, tz);
            we = ToUtcMs(date, window.End, tz);
        }
        var elapsedEnd = we.HasValue ? Math.Min(we.Value, nowMs) : 0;
        var windowElapsed = ws.HasValue ? Math.Max(0, elapsedEnd - ws.Value) : 0;

        long active = 0, idle = 0, locked = 0, personal = 0, work = 0, unclassified = 0, outside = 0;
        long? arrival = null, departure = null;
        var top = new Dictionary<(string Label, string Category), (string? Key, long Ms)>();
        var pieces = new List<TimelineItem>();

        foreach (var row in rows.OrderBy(r => r.StartMs))
        {
            var s = Math.Max(row.StartMs, dayStart);
            var e = Math.Min(row.EndMs, limit);
            if (e <= s) continue;

            var inWindow = ws.HasValue ? Overlap(s, e, ws.Value, elapsedEnd) : 0;

            Classified? cls = null;
            switch (row.State)
            {
                case "active":
                    cls = classifier.Classify(row.Personal, row.Process, row.AppName, row.Domain);
                    active += inWindow;
                    outside += (e - s) - inWindow;
                    if (cls.Category == Categories.Personal) personal += inWindow;
                    else if (cls.Category == Categories.Work) work += inWindow;
                    else if (cls.Category == Categories.Unclassified) unclassified += inWindow;
                    if (inWindow > 0)
                    {
                        var k = (cls.Label, cls.Category);
                        var current = top.GetValueOrDefault(k);
                        top[k] = (current.Key ?? cls.Key, current.Ms + inWindow);
                    }
                    arrival = arrival is null ? s : Math.Min(arrival.Value, s);
                    departure = departure is null ? e : Math.Max(departure.Value, e);
                    break;
                case "idle":
                    idle += inWindow;
                    break;
                case "locked":
                    locked += inWindow;
                    break;
            }

            if (includeDetail)
            {
                var title = cls is not null && cls.Category != Categories.Personal ? row.Title : null;
                pieces.Add(new TimelineItem(s, e, row.State, cls?.Category, cls?.Label, title));
            }
        }

        var away = Math.Max(0, windowElapsed - active - idle);

        var summary = new DaySummary(
            Date: date.ToString("yyyy-MM-dd"),
            WorkDay: window is not null,
            WindowStartMs: ws,
            WindowEndMs: we,
            ArrivalMs: arrival,
            DepartureMs: departure,
            WindowElapsedMs: windowElapsed,
            ActiveMs: active,
            IdleMs: idle,
            LockedMs: locked,
            AwayMs: away,
            PersonalMs: personal,
            WorkMs: work,
            UnclassifiedMs: unclassified,
            OutsideHoursActiveMs: outside);

        if (!includeDetail)
            return new DayDetail(summary, [], [], []);

        var topList = top
            .Select(kv => new TopItem(kv.Key.Label, kv.Key.Category, kv.Value.Key, kv.Value.Ms))
            .OrderByDescending(t => t.Ms)
            .ToList();

        var timeline = Merge(pieces, withTitle: false);
        var log = Merge(pieces, withTitle: true);
        return new DayDetail(summary, topList, timeline, log);
    }

    /// <summary>Junta trechos vizinhos iguais e marca lacunas de mais de 1 minuto como "off".</summary>
    private static List<TimelineItem> Merge(List<TimelineItem> pieces, bool withTitle)
    {
        var result = new List<TimelineItem>();
        foreach (var p in pieces.OrderBy(p => p.StartMs))
        {
            var item = withTitle ? p : p with { Title = null };
            if (result.Count > 0)
            {
                var last = result[^1];
                var gap = item.StartMs - last.EndMs;
                if (gap > OffThresholdMs)
                {
                    result.Add(new TimelineItem(last.EndMs, item.StartMs, "off", null, null, null));
                }
                else if (gap <= MergeGapMs
                    && last.State == item.State
                    && last.Category == item.Category
                    && last.Label == item.Label
                    && last.Title == item.Title)
                {
                    result[^1] = last with { EndMs = Math.Max(last.EndMs, item.EndMs) };
                    continue;
                }
            }
            result.Add(item);
        }
        return result;
    }

    private static long Overlap(long s1, long e1, long s2, long e2) =>
        Math.Max(0, Math.Min(e1, e2) - Math.Max(s1, s2));
}
