using Monitor.Core;

namespace Monitor.Tests;

public class DomainParserTests
{
    [Theory]
    [InlineData("app.ploomes.com/negocios", "app.ploomes.com")]
    [InlineData("https://www.google.com/search?q=x", "google.com")]
    [InlineData("www.portoseguro.com.br", "portoseguro.com.br")]
    [InlineData("HTTPS://Clicksign.COM/", "clicksign.com")]
    [InlineData("hpanel.hostinger.com", "hpanel.hostinger.com")]
    [InlineData("localhost:5000/x", "localhost")]
    public void Extracts_domain(string value, string expected) =>
        Assert.Equal(expected, DomainParser.FromAddressBar(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pesquisar no Google ou digitar URL")]
    [InlineData("chrome://settings")]
    [InlineData("about:blank")]
    [InlineData("seguro auto")]
    [InlineData("palavra")]
    public void Ignores_non_addresses(string? value) =>
        Assert.Null(DomainParser.FromAddressBar(value));

    [Theory]
    [InlineData("app.ploomes.com", "ploomes.com", true)]
    [InlineData("ploomes.com", "ploomes.com", true)]
    [InlineData("naoploomes.com", "ploomes.com", false)]
    [InlineData("ploomes.com.br", "ploomes.com", false)]
    public void Matches_subdomains_only(string domain, string rule, bool expected) =>
        Assert.Equal(expected, DomainParser.Matches(domain, rule));
}

public class PersonalFilterTests
{
    private readonly PersonalFilter _filter = new(["facebook.com", "itau.com.br"], ["spotify.exe"]);

    [Fact]
    public void Personal_by_domain_and_subdomain()
    {
        Assert.True(_filter.IsPersonal("chrome", "facebook.com"));
        Assert.True(_filter.IsPersonal("chrome", "internetpf5.itau.com.br"));
        Assert.False(_filter.IsPersonal("chrome", "app.ploomes.com"));
    }

    [Fact]
    public void Personal_by_process_ignoring_exe_and_case()
    {
        Assert.True(_filter.IsPersonal("Spotify", null));
        Assert.False(_filter.IsPersonal("excel", null));
    }
}

public class PeriodTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 9, 11, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static Sample Active(DateTimeOffset at, string process = "excel", string? title = "Planilha", string? domain = null, double idleSeconds = 1) =>
        new(at, false, TimeSpan.FromSeconds(idleSeconds), process, null, domain, title);

    [Fact]
    public void Same_window_becomes_one_period()
    {
        var tracker = new PeriodTracker(Five);
        for (var i = 0; i <= 120; i++) tracker.Observe(Active(T0.AddSeconds(i * 5)), PersonalFilter.Empty);

        var all = tracker.TakeAll();
        var seg = Assert.Single(all);
        Assert.Equal(T0, seg.Start);
        Assert.Equal(T0.AddMinutes(10), seg.End);
        Assert.Equal(ActivityState.Active, seg.State);
        Assert.Equal("Planilha", seg.Title);
    }

    [Fact]
    public void Window_change_splits_contiguous_periods()
    {
        var tracker = new PeriodTracker(Five);
        for (var i = 0; i < 12; i++) tracker.Observe(Active(T0.AddSeconds(i * 5), "excel"), PersonalFilter.Empty);
        for (var i = 12; i <= 24; i++) tracker.Observe(Active(T0.AddSeconds(i * 5), "chrome", "Ploomes", "app.ploomes.com"), PersonalFilter.Empty);

        var all = tracker.TakeAll();
        Assert.Equal(2, all.Count);
        Assert.Equal(all[0].End, all[1].Start);
        Assert.Equal(T0.AddSeconds(60), all[1].Start);
        Assert.Equal("app.ploomes.com", all[1].Domain);
    }

    [Fact]
    public void Idle_starts_at_last_input_and_corrects_trailing_active_time()
    {
        var tracker = new PeriodTracker(Five);
        // Usa teclado e mouse até T0+2min, depois para.
        for (var i = 0; i <= 24; i++) tracker.Observe(Active(T0.AddSeconds(i * 5)), PersonalFilter.Empty);
        for (var s = 125; s <= 600; s += 5)
        {
            var idle = TimeSpan.FromSeconds(s - 120);
            tracker.Observe(new Sample(T0.AddSeconds(s), false, idle, "excel", null, null, "Planilha"), PersonalFilter.Empty);
        }

        var all = tracker.TakeAll();
        Assert.Equal(2, all.Count);
        Assert.Equal(ActivityState.Active, all[0].State);
        Assert.Equal(T0, all[0].Start);
        Assert.Equal(T0.AddMinutes(2), all[0].End);
        Assert.Equal(ActivityState.Idle, all[1].State);
        Assert.Equal(T0.AddMinutes(2), all[1].Start);
        Assert.Equal(T0.AddMinutes(10), all[1].End);
        Assert.Null(all[1].Title);
    }

    [Fact]
    public void Returning_from_idle_starts_new_active_period()
    {
        var tracker = new PeriodTracker(Five);
        for (var s = 0; s <= 400; s += 5)
            tracker.Observe(new Sample(T0.AddSeconds(s), false, TimeSpan.FromSeconds(s), "excel", null, null, "x"), PersonalFilter.Empty);
        tracker.Observe(Active(T0.AddSeconds(405)), PersonalFilter.Empty);
        tracker.Observe(Active(T0.AddSeconds(410)), PersonalFilter.Empty);

        var all = tracker.TakeAll();
        Assert.Equal(ActivityState.Idle, all[0].State);
        Assert.Equal(T0, all[0].Start);
        Assert.Equal(T0.AddSeconds(405), all[0].End);
        Assert.Equal(ActivityState.Active, all[^1].State);
        Assert.Equal(T0.AddSeconds(405), all[^1].Start);
    }

    [Fact]
    public void Gap_from_sleep_is_not_counted()
    {
        var tracker = new PeriodTracker(Five);
        for (var i = 0; i <= 12; i++) tracker.Observe(Active(T0.AddSeconds(i * 5)), PersonalFilter.Empty);
        // Suspenso por 1 hora; ao voltar, o tempo sem teclado inclui a suspensão.
        var back = T0.AddMinutes(61);
        tracker.Observe(new Sample(back, false, TimeSpan.FromMinutes(61), "excel", null, null, "Planilha"), PersonalFilter.Empty);
        tracker.Observe(Active(back.AddSeconds(5)), PersonalFilter.Empty);
        tracker.Observe(Active(back.AddSeconds(10)), PersonalFilter.Empty);

        var all = tracker.TakeAll();
        Assert.DoesNotContain(all, s => s.Start < back && s.End > T0.AddMinutes(1));
        var idle = Assert.Single(all, s => s.State == ActivityState.Idle);
        Assert.Equal(back, idle.Start);
    }

    [Fact]
    public void Personal_use_keeps_only_time()
    {
        var tracker = new PeriodTracker(Five);
        var filter = new PersonalFilter(["facebook.com"], []);
        for (var i = 0; i <= 6; i++) tracker.Observe(Active(T0.AddSeconds(i * 5), "chrome", "Facebook - Fulano", "facebook.com"), filter);

        var seg = Assert.Single(tracker.TakeAll());
        Assert.True(seg.Personal);
        Assert.Null(seg.Title);
        Assert.Null(seg.Domain);
        Assert.Null(seg.Process);
    }

    [Fact]
    public void Locked_screen_is_its_own_state()
    {
        var tracker = new PeriodTracker(Five);
        for (var i = 0; i <= 6; i++) tracker.Observe(Active(T0.AddSeconds(i * 5)), PersonalFilter.Empty);
        for (var i = 7; i <= 20; i++) tracker.Observe(new Sample(T0.AddSeconds(i * 5), true, TimeSpan.Zero, null, null, null, null), PersonalFilter.Empty);

        var all = tracker.TakeAll();
        Assert.Equal([ActivityState.Active, ActivityState.Locked], all.Select(s => s.State));
    }

    [Fact]
    public void TakeReady_holds_recent_time_and_cuts_long_open_period()
    {
        var tracker = new PeriodTracker(Five);
        for (var i = 0; i <= 240; i++) tracker.Observe(Active(T0.AddSeconds(i * 5)), PersonalFilter.Empty);
        var now = T0.AddMinutes(20);

        var ready = tracker.TakeReady(now);
        var seg = Assert.Single(ready);
        Assert.Equal(T0, seg.Start);
        Assert.Equal(now - Five - TimeSpan.FromSeconds(15), seg.End);

        var rest = tracker.TakeAll();
        Assert.Equal(seg.End, rest[0].Start);
        Assert.Equal(T0.AddMinutes(20), rest[^1].End);
    }

    [Fact]
    public void Idle_never_rewrites_time_already_sent()
    {
        var tracker = new PeriodTracker(Five);
        for (var i = 0; i <= 120; i++) tracker.Observe(Active(T0.AddSeconds(i * 5)), PersonalFilter.Empty);
        var sent = tracker.TakeReady(T0.AddMinutes(10));
        var sentUntil = sent.Max(s => s.End);

        // Detecção de inatividade que diz que o último uso foi há 20 minutos (antes do que já foi enviado).
        tracker.Observe(new Sample(T0.AddMinutes(10).AddSeconds(5), false, TimeSpan.FromMinutes(20), "excel", null, null, "Planilha"), PersonalFilter.Empty);
        var idle = Assert.Single(tracker.TakeAll(), s => s.State == ActivityState.Idle);
        Assert.True(idle.Start >= sentUntil);
    }
}
