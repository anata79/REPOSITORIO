using Microsoft.Data.Sqlite;

namespace Monitor.Server.Data;

/// <summary>Valores iniciais: horário combinado, limite de inatividade e listas de classificação.</summary>
public static class Seed
{
    public static void Apply(SqliteConnection conn)
    {
        Settings.EnsureDefaults(conn);

        var hasRules = conn.Scalar<long>("SELECT COUNT(*) FROM rules") > 0;
        if (hasRules) return;

        foreach (var (pattern, label) in WorkDomains)
            AddRule(conn, "domain", pattern, Categories.Work, label);
        foreach (var (pattern, label) in PersonalDomains)
            AddRule(conn, "domain", pattern, Categories.Personal, label);
        foreach (var (pattern, label) in WorkProcesses)
            AddRule(conn, "process", pattern, Categories.Work, label);
        foreach (var (pattern, label) in NeutralProcesses)
            AddRule(conn, "process", pattern, Categories.Neutral, label);
        foreach (var (pattern, label) in PersonalProcesses)
            AddRule(conn, "process", pattern, Categories.Personal, label);
    }

    private static void AddRule(SqliteConnection conn, string kind, string pattern, string category, string label) =>
        conn.Exec(
            "INSERT OR IGNORE INTO rules(kind, pattern, category, label) VALUES ($k, $p, $c, $l)",
            ("$k", kind), ("$p", pattern), ("$c", category), ("$l", label));

    // Sistemas de trabalho vistos nos prints do André (08/11/2026). Os demais aparecem como
    // "não classificado" no painel e são classificados com um clique.
    private static readonly (string, string)[] WorkDomains =
    [
        ("ploomes.com", "Ploomes"),
        ("clicksign.com", "Clicksign"),
        ("calendar.google.com", "Google Agenda"),
        ("keep.google.com", "Google Keep"),
        ("gemini.google.com", "Gemini"),
        ("hostinger.com", "Hostinger"),
        ("portoseguro.com.br", "Porto Seguro"),
        ("portoconseg.com.br", "Portoconseg"),
        ("consegconsorcioseseguros.com.br", "Conseg"),
        ("outlook.office.com", "Outlook (web)"),
        ("outlook.live.com", "Outlook (web)"),
    ];

    private static readonly (string, string)[] PersonalDomains =
    [
        // Bancos e pagamentos
        ("itau.com.br", "Banco"), ("bb.com.br", "Banco"), ("bradesco.com.br", "Banco"),
        ("santander.com.br", "Banco"), ("caixa.gov.br", "Banco"), ("nubank.com.br", "Banco"),
        ("inter.co", "Banco"), ("bancointer.com.br", "Banco"), ("c6bank.com.br", "Banco"),
        ("sicoob.com.br", "Banco"), ("sicredi.com.br", "Banco"), ("picpay.com", "Banco"),
        ("mercadopago.com.br", "Banco"), ("original.com.br", "Banco"), ("btgpactual.com", "Banco"),
        ("xpi.com.br", "Banco"), ("next.me", "Banco"), ("neon.com.br", "Banco"),
        // Redes sociais
        ("facebook.com", "Rede social"), ("instagram.com", "Rede social"), ("tiktok.com", "Rede social"),
        ("x.com", "Rede social"), ("twitter.com", "Rede social"), ("threads.net", "Rede social"),
        ("pinterest.com", "Rede social"), ("reddit.com", "Rede social"), ("kwai.com", "Rede social"),
        // Vídeo e música
        ("youtube.com", "Vídeo e música"), ("netflix.com", "Vídeo e música"), ("primevideo.com", "Vídeo e música"),
        ("disneyplus.com", "Vídeo e música"), ("max.com", "Vídeo e música"), ("globoplay.globo.com", "Vídeo e música"),
        ("spotify.com", "Vídeo e música"), ("deezer.com", "Vídeo e música"), ("twitch.tv", "Vídeo e música"),
        ("music.youtube.com", "Vídeo e música"), ("paramountplus.com", "Vídeo e música"),
        // Compras
        ("mercadolivre.com.br", "Compras"), ("amazon.com.br", "Compras"), ("amazon.com", "Compras"),
        ("shopee.com.br", "Compras"), ("aliexpress.com", "Compras"), ("magazineluiza.com.br", "Compras"),
        ("shein.com", "Compras"), ("americanas.com.br", "Compras"), ("casasbahia.com.br", "Compras"),
        ("temu.com", "Compras"), ("olx.com.br", "Compras"),
        // Viagem (Airbnb e Stay Life indicados pelo André como uso pessoal)
        ("airbnb.com.br", "Viagem"), ("airbnb.com", "Viagem"), ("booking.com", "Viagem"),
        // Jogos
        ("roblox.com", "Jogos"), ("steampowered.com", "Jogos"), ("epicgames.com", "Jogos"),
    ];

    private static readonly (string, string)[] WorkProcesses =
    [
        ("outlook", "Outlook"), ("olk", "Outlook"), ("winword", "Word"), ("excel", "Excel"),
        ("powerpnt", "PowerPoint"), ("onenote", "OneNote"), ("msaccess", "Access"),
        ("ms-teams", "Teams"), ("teams", "Teams"), ("acrord32", "Adobe Acrobat"), ("acrobat", "Adobe Acrobat"),
    ];

    private static readonly (string, string)[] NeutralProcesses =
    [
        ("chrome", "Navegador (site não identificado)"), ("msedge", "Navegador (site não identificado)"),
        ("firefox", "Navegador (site não identificado)"), ("brave", "Navegador (site não identificado)"),
        ("opera", "Navegador (site não identificado)"), ("vivaldi", "Navegador (site não identificado)"),
        ("explorer", "Pastas e área de trabalho"), ("lockapp", "Tela de bloqueio"),
        ("searchhost", "Pesquisa do Windows"), ("searchapp", "Pesquisa do Windows"),
        ("startmenuexperiencehost", "Menu Iniciar"), ("shellexperiencehost", "Windows"),
        ("systemsettings", "Configurações do Windows"), ("taskmgr", "Gerenciador de Tarefas"),
        ("monitorportoconseg", "Monitor Portoconseg"),
    ];

    private static readonly (string, string)[] PersonalProcesses =
    [
        ("spotify", "Vídeo e música"), ("steam", "Jogos"), ("epicgameslauncher", "Jogos"),
    ];
}

public static class Categories
{
    public const string Work = "work";
    public const string Personal = "personal";
    public const string Neutral = "neutral";
    public const string Unclassified = "unclassified";

    public static bool IsValid(string? c) => c is Work or Personal or Neutral;
}
