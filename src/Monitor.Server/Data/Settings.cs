using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Monitor.Server.Data;

/// <summary>Horário de um dia da semana.</summary>
public sealed record DaySchedule(TimeOnly Start, TimeOnly End);

/// <summary>Horário combinado, válido a partir de uma data.</summary>
public sealed record WorkSchedule(DateOnly EffectiveFrom, IReadOnlyDictionary<DayOfWeek, DaySchedule> Days)
{
    public DaySchedule? For(DayOfWeek day) => Days.TryGetValue(day, out var d) ? d : null;
}

/// <summary>
/// Histórico de horários. Cada alteração vale a partir de uma data, então relatórios de
/// semanas anteriores continuam calculados com o horário que valia na época.
/// </summary>
public sealed class ScheduleBook
{
    private readonly List<WorkSchedule> _items;

    public ScheduleBook(IEnumerable<WorkSchedule> items)
    {
        _items = items.OrderBy(i => i.EffectiveFrom).ToList();
        if (_items.Count == 0) _items.Add(Settings.DefaultSchedule);
    }

    public IReadOnlyList<WorkSchedule> Items => _items;

    public WorkSchedule For(DateOnly date)
    {
        WorkSchedule current = _items[0];
        foreach (var item in _items)
        {
            if (item.EffectiveFrom <= date) current = item;
            else break;
        }
        return current;
    }
}

public sealed record AppSettings(int IdleMinutes, int RetentionDays, string TimeZoneId);

public static class Settings
{
    public const string TimeZoneDefault = "America/Sao_Paulo";

    // Horário informado pelo André em 07/11/2026: seg a qui 8h–18h, sex 8h–17h, sábado e domingo descanso.
    public static WorkSchedule DefaultSchedule { get; } = new(
        new DateOnly(2000, 1, 1),
        new Dictionary<DayOfWeek, DaySchedule>
        {
            [DayOfWeek.Monday] = new(new TimeOnly(8, 0), new TimeOnly(18, 0)),
            [DayOfWeek.Tuesday] = new(new TimeOnly(8, 0), new TimeOnly(18, 0)),
            [DayOfWeek.Wednesday] = new(new TimeOnly(8, 0), new TimeOnly(18, 0)),
            [DayOfWeek.Thursday] = new(new TimeOnly(8, 0), new TimeOnly(18, 0)),
            [DayOfWeek.Friday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
        });

    public static void EnsureDefaults(SqliteConnection conn)
    {
        conn.Exec("INSERT OR IGNORE INTO settings(key, value) VALUES ('idle_minutes', '5')");
        conn.Exec("INSERT OR IGNORE INTO settings(key, value) VALUES ('retention_days', '90')");
        conn.Exec($"INSERT OR IGNORE INTO settings(key, value) VALUES ('timezone', '{TimeZoneDefault}')");

        if (conn.Scalar<long>("SELECT COUNT(*) FROM schedules") == 0)
            SaveSchedule(conn, DefaultSchedule, "sistema");
    }

    public static AppSettings Load(SqliteConnection conn)
    {
        var values = conn.Query("SELECT key, value FROM settings", r => (r.GetString(0), r.GetString(1)))
            .ToDictionary(x => x.Item1, x => x.Item2);
        return new AppSettings(
            IdleMinutes: ParseInt(values.GetValueOrDefault("idle_minutes"), 5, 1, 60),
            RetentionDays: ParseInt(values.GetValueOrDefault("retention_days"), 90, 7, 3650),
            TimeZoneId: values.GetValueOrDefault("timezone") ?? TimeZoneDefault);
    }

    public static void SaveValue(SqliteConnection conn, string key, string value) =>
        conn.Exec(
            "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$k", key), ("$v", value));

    public static ScheduleBook LoadSchedules(SqliteConnection conn) =>
        new(conn.Query("SELECT effective_from, days_json FROM schedules", r =>
            new WorkSchedule(
                DateOnly.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ParseDays(r.GetString(1)))));

    public static void SaveSchedule(SqliteConnection conn, WorkSchedule schedule, string? by) =>
        conn.Exec(
            """
            INSERT INTO schedules(effective_from, days_json, created_utc, created_by)
            VALUES ($f, $d, $t, $b)
            ON CONFLICT(effective_from) DO UPDATE SET days_json = excluded.days_json,
                created_utc = excluded.created_utc, created_by = excluded.created_by
            """,
            ("$f", schedule.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$d", SerializeDays(schedule.Days)),
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ("$b", by));

    public static void DeleteSchedule(SqliteConnection conn, DateOnly effectiveFrom) =>
        conn.Exec("DELETE FROM schedules WHERE effective_from = $f",
            ("$f", effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    public static string SerializeDays(IReadOnlyDictionary<DayOfWeek, DaySchedule> days) =>
        JsonSerializer.Serialize(days.OrderBy(d => (int)d.Key).ToDictionary(
            d => d.Key.ToString().ToLowerInvariant(),
            d => new { start = d.Value.Start.ToString("HH:mm"), end = d.Value.End.ToString("HH:mm") }));

    public static Dictionary<DayOfWeek, DaySchedule> ParseDays(string json)
    {
        var result = new Dictionary<DayOfWeek, DaySchedule>();
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!Enum.TryParse<DayOfWeek>(prop.Name, ignoreCase: true, out var day)) continue;
            var start = prop.Value.GetProperty("start").GetString();
            var end = prop.Value.GetProperty("end").GetString();
            if (TryParseTime(start, out var s) && TryParseTime(end, out var e) && e > s)
                result[day] = new DaySchedule(s, e);
        }
        return result;
    }

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception) when (id == TimeZoneDefault)
        {
            // Sem base de fusos no sistema: Brasília é UTC−3 sem horário de verão desde 2019.
            return TimeZoneInfo.CreateCustomTimeZone(TimeZoneDefault, TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }

    private static int ParseInt(string? value, int fallback, int min, int max) =>
        int.TryParse(value, out var v) ? Math.Clamp(v, min, max) : fallback;
}
