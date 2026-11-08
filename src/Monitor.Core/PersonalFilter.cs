namespace Monitor.Core;

/// <summary>
/// Decide, no próprio computador, se a janela em uso é de uso pessoal.
/// Uso pessoal é registrado só como tempo: programa, site e título não saem da máquina.
/// </summary>
public sealed class PersonalFilter
{
    private readonly string[] _domains;
    private readonly HashSet<string> _processes;

    public PersonalFilter(IEnumerable<string>? domains, IEnumerable<string>? processes)
    {
        _domains = (domains ?? [])
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToArray();
        _processes = new HashSet<string>(
            (processes ?? []).Select(p => NormalizeProcess(p)).Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    public static PersonalFilter Empty { get; } = new([], []);

    public bool IsPersonal(string? process, string? domain)
    {
        if (!string.IsNullOrEmpty(process) && _processes.Contains(NormalizeProcess(process)))
            return true;
        if (!string.IsNullOrEmpty(domain))
        {
            foreach (var rule in _domains)
            {
                if (DomainParser.Matches(domain, rule))
                    return true;
            }
        }
        return false;
    }

    public static string NormalizeProcess(string process)
    {
        var p = process.Trim().ToLowerInvariant();
        return p.EndsWith(".exe", StringComparison.Ordinal) ? p[..^4] : p;
    }
}
