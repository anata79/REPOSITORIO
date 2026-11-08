using Monitor.Server.Data;
using Monitor.Server.Reports;
using Monitor.Server.Security;

namespace Monitor.Tests;

public class ClassifierTests
{
    private static readonly Classifier Classifier = new([
        new Rule(1, "domain", "ploomes.com", Categories.Work, "Ploomes"),
        new Rule(2, "domain", "google.com", Categories.Neutral, "Google"),
        new Rule(3, "domain", "calendar.google.com", Categories.Work, "Google Agenda"),
        new Rule(4, "domain", "facebook.com", Categories.Personal, "Rede social"),
        new Rule(5, "process", "excel", Categories.Work, "Excel"),
    ]);

    [Fact]
    public void Longest_domain_rule_wins()
    {
        Assert.Equal("Google Agenda", Classifier.Classify(false, "chrome", null, "calendar.google.com").Label);
        Assert.Equal(Categories.Neutral, Classifier.Classify(false, "chrome", null, "mail.google.com").Category);
    }

    [Fact]
    public void Unknown_site_and_program_are_unclassified_with_key()
    {
        var site = Classifier.Classify(false, "chrome", null, "segflex.com.br");
        Assert.Equal(Categories.Unclassified, site.Category);
        Assert.Equal("domain:segflex.com.br", site.Key);

        var program = Classifier.Classify(false, "segflexnet", "SegFlex NET", null);
        Assert.Equal("SegFlex NET", program.Label);
        Assert.Equal("process:segflexnet", program.Key);
    }

    [Fact]
    public void Personal_never_shows_site_name()
    {
        Assert.Equal(Classifier.PersonalLabel, Classifier.Classify(false, "chrome", null, "m.facebook.com").Label);
        Assert.Equal(Classifier.PersonalLabel, Classifier.Classify(true, null, null, null).Label);
        Assert.True(Classifier.IsPersonal("chrome", "facebook.com"));
    }
}

public class ScheduleTests
{
    [Fact]
    public void Uses_schedule_valid_on_each_date()
    {
        var old = Settings.DefaultSchedule;
        var newer = new WorkSchedule(new DateOnly(2026, 12, 1), new Dictionary<DayOfWeek, DaySchedule>
        {
            [DayOfWeek.Monday] = new(new TimeOnly(9, 0), new TimeOnly(17, 0)),
        });
        var book = new ScheduleBook([newer, old]);

        Assert.Equal(new TimeOnly(8, 0), book.For(new DateOnly(2026, 11, 30)).For(DayOfWeek.Monday)!.Start);
        Assert.Equal(new TimeOnly(9, 0), book.For(new DateOnly(2026, 12, 7)).For(DayOfWeek.Monday)!.Start);
        Assert.Null(book.For(new DateOnly(2026, 12, 8)).For(DayOfWeek.Tuesday));
    }

    [Fact]
    public void Default_schedule_matches_what_Andre_informed()
    {
        var s = Settings.DefaultSchedule;
        Assert.Equal(new DaySchedule(new TimeOnly(8, 0), new TimeOnly(18, 0)), s.For(DayOfWeek.Thursday));
        Assert.Equal(new DaySchedule(new TimeOnly(8, 0), new TimeOnly(17, 0)), s.For(DayOfWeek.Friday));
        Assert.Null(s.For(DayOfWeek.Saturday));
        Assert.Null(s.For(DayOfWeek.Sunday));
    }

    [Fact]
    public void Days_round_trip_through_json()
    {
        var json = Settings.SerializeDays(Settings.DefaultSchedule.Days);
        var parsed = Settings.ParseDays(json);
        Assert.Equal(5, parsed.Count);
        Assert.Equal(new TimeOnly(17, 0), parsed[DayOfWeek.Friday].End);
    }
}

public class ReportBuilderTests
{
    private static readonly TimeZoneInfo Tz = Settings.ResolveTimeZone(Settings.TimeZoneDefault);
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly DaySchedule Window = new(new TimeOnly(8, 0), new TimeOnly(18, 0));
    private static readonly Classifier Classifier = new([
        new Rule(1, "domain", "ploomes.com", Categories.Work, "Ploomes"),
        new Rule(2, "domain", "facebook.com", Categories.Personal, "Rede social"),
        new Rule(3, "process", "excel", Categories.Work, "Excel"),
    ]);

    private static long At(int hour, int minute = 0) => ReportBuilder.ToUtcMs(Monday, new TimeOnly(hour, minute), Tz);
    private const long Min = 60_000;

    private static SegRow Act(long s, long e, string process, string? domain = null, string? title = null, bool personal = false) =>
        new(s, e, "active", personal, personal ? null : process, null, personal ? null : domain, personal ? null : title);

    [Fact]
    public void Totals_inside_agreed_hours()
    {
        var rows = new[]
        {
            Act(At(8, 30), At(12, 0), "chrome", "app.ploomes.com", "Ploomes - Negócios"),
            new SegRow(At(12, 0), At(13, 0), "idle", false, null, null, null, null),
            Act(At(13, 0), At(13, 30), "", personal: true),
            Act(At(13, 30), At(17, 30), "excel", title: "Comissões.xlsx - Excel"),
            new SegRow(At(17, 30), At(17, 45), "locked", false, null, null, null, null),
            Act(At(18, 30), At(19, 0), "excel", title: "Comissões.xlsx - Excel"),
        };
        var now = At(23, 0);

        var report = ReportBuilder.BuildDay(Monday, rows, Window, Tz, now, Classifier);
        var s = report.Summary;

        Assert.Equal(10 * 60 * Min, s.WindowElapsedMs);
        Assert.Equal((210 + 30 + 240) * Min, s.ActiveMs);
        Assert.Equal(60 * Min, s.IdleMs);
        Assert.Equal(30 * Min, s.PersonalMs);
        Assert.Equal(450 * Min, s.WorkMs);
        Assert.Equal(15 * Min, s.LockedMs);
        Assert.Equal((600 - 480 - 60) * Min, s.AwayMs);
        Assert.Equal(30 * Min, s.OutsideHoursActiveMs);
        Assert.Equal(At(8, 30), s.ArrivalMs);
        Assert.Equal(At(19, 0), s.DepartureMs);

        Assert.Equal("Excel", report.Top[0].Label);
        Assert.Equal(240 * Min, report.Top[0].Ms);
        Assert.Contains(report.Top, t => t.Label == Classifier.PersonalLabel && t.Ms == 30 * Min);
        Assert.Contains(report.Log, l => l.Title == "Ploomes - Negócios");
        Assert.DoesNotContain(report.Log, l => l.Category == Categories.Personal && l.Title is not null);
        Assert.Contains(report.Timeline, t => t.State == "off" && t.StartMs == At(17, 45) && t.EndMs == At(18, 30));
    }

    [Fact]
    public void Today_counts_only_until_now()
    {
        var rows = new[] { Act(At(8, 0), At(10, 0), "excel") };
        var report = ReportBuilder.BuildDay(Monday, rows, Window, Tz, At(10, 0), Classifier);
        Assert.Equal(120 * Min, report.Summary.WindowElapsedMs);
        Assert.Equal(0, report.Summary.AwayMs);
    }

    [Fact]
    public void Weekend_without_schedule_counts_everything_as_outside_hours()
    {
        var saturday = new DateOnly(2026, 11, 14);
        var start = ReportBuilder.ToUtcMs(saturday, new TimeOnly(10, 0), Tz);
        var rows = new[] { Act(start, start + 30 * Min, "excel") };
        var report = ReportBuilder.BuildDay(saturday, rows, null, Tz, start + 600 * Min, Classifier);
        Assert.False(report.Summary.WorkDay);
        Assert.Equal(0, report.Summary.ActiveMs);
        Assert.Equal(30 * Min, report.Summary.OutsideHoursActiveMs);
    }

    [Fact]
    public void Brasilia_is_utc_minus_3()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 11, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), At(8, 0));
    }
}

public class SecretsTests
{
    [Fact]
    public void Password_hash_verifies_only_the_right_password()
    {
        var hash = Secrets.HashPassword("senha-forte-123");
        Assert.True(Secrets.VerifyPassword("senha-forte-123", hash));
        Assert.False(Secrets.VerifyPassword("senha-errada", hash));
    }

    [Fact]
    public void Tokens_are_unique_and_hash_is_stable()
    {
        var a = Secrets.NewAgentToken();
        var b = Secrets.NewAgentToken();
        Assert.NotEqual(a, b);
        Assert.Equal(Secrets.HashToken(a), Secrets.HashToken(" " + a + " "));
    }
}
