using Monitor.Core;
using Monitor.Server.Data;

namespace Monitor.Server.Reports;

public sealed record Rule(long Id, string Kind, string Pattern, string Category, string? Label);

/// <summary>Resultado da classificação de um período ativo.</summary>
/// <param name="Category">work, personal, neutral ou unclassified.</param>
/// <param name="Label">Nome mostrado no painel (ex.: "Ploomes", "SegFlexNET", "Uso pessoal").</param>
/// <param name="Key">Chave para classificar com um clique ("domain:ploomes.com", "process:segflexnet").</param>
public sealed record Classified(string Category, string Label, string? Key);

/// <summary>Aplica as regras de classificação (trabalho, pessoal, neutro) a programas e sites.</summary>
public sealed class Classifier
{
    public const string PersonalLabel = "Uso pessoal";

    private readonly Rule[] _domainRules;
    private readonly Dictionary<string, Rule> _processRules;

    public Classifier(IEnumerable<Rule> rules)
    {
        var list = rules.ToList();
        _domainRules = list.Where(r => r.Kind == "domain")
            .OrderByDescending(r => r.Pattern.Length)
            .ToArray();
        _processRules = list.Where(r => r.Kind == "process")
            .GroupBy(r => PersonalFilter.NormalizeProcess(r.Pattern))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public static Classifier Load(Microsoft.Data.Sqlite.SqliteConnection conn) =>
        new(conn.Query("SELECT id, kind, pattern, category, label FROM rules", r =>
            new Rule(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetStringOrNull(4))));

    public Rule? MatchDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain)) return null;
        foreach (var rule in _domainRules)
            if (DomainParser.Matches(domain, rule.Pattern)) return rule;
        return null;
    }

    public Rule? MatchProcess(string? process)
    {
        if (string.IsNullOrEmpty(process)) return null;
        return _processRules.GetValueOrDefault(PersonalFilter.NormalizeProcess(process));
    }

    public bool IsPersonal(string? process, string? domain)
    {
        var d = MatchDomain(domain);
        if (d is not null) return d.Category == Categories.Personal;
        return MatchProcess(process)?.Category == Categories.Personal;
    }

    public Classified Classify(bool personal, string? process, string? appName, string? domain)
    {
        if (personal) return new Classified(Categories.Personal, PersonalLabel, null);

        if (!string.IsNullOrEmpty(domain))
        {
            var rule = MatchDomain(domain);
            return rule is null
                ? new Classified(Categories.Unclassified, domain, "domain:" + domain)
                : new Classified(rule.Category, rule.Category == Categories.Personal ? PersonalLabel : rule.Label ?? domain, "domain:" + rule.Pattern);
        }

        var name = !string.IsNullOrWhiteSpace(appName) ? appName : process ?? "Programa desconhecido";
        if (!string.IsNullOrEmpty(process))
        {
            var rule = MatchProcess(process);
            var key = "process:" + PersonalFilter.NormalizeProcess(process);
            return rule is null
                ? new Classified(Categories.Unclassified, name, key)
                : new Classified(rule.Category, rule.Category == Categories.Personal ? PersonalLabel : rule.Label ?? name, key);
        }

        return new Classified(Categories.Unclassified, name, null);
    }

    /// <summary>Lista enviada ao agente para filtrar uso pessoal no próprio computador.</summary>
    public AgentConfig ToAgentConfig(int idleMinutes) => new(
        idleMinutes,
        _domainRules.Where(r => r.Category == Categories.Personal).Select(r => r.Pattern).ToList(),
        _processRules.Values.Where(r => r.Category == Categories.Personal).Select(r => r.Pattern).ToList());
}
