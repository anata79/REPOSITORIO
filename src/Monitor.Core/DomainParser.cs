namespace Monitor.Core;

/// <summary>Extrai o domínio de um texto de barra de endereço de navegador.</summary>
public static class DomainParser
{
    /// <summary>
    /// Converte o valor da barra de endereço ("app.ploomes.com/negocios", "https://www.google.com/...")
    /// no domínio sem "www." ("app.ploomes.com", "google.com"). Retorna null se não parecer um endereço.
    /// </summary>
    public static string? FromAddressBar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        // Texto de busca digitado ou placeholder ("Pesquisar no Google ou digitar URL").
        if (text.Contains(' '))
            return null;

        if (text.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("edge://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || !host.Contains('.'))
            return host == "localhost" ? host : null;

        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        return host;
    }

    /// <summary>
    /// Verifica se o domínio é igual à regra ou um subdomínio dela.
    /// "app.ploomes.com" combina com "ploomes.com"; "naoploomes.com" não combina.
    /// </summary>
    public static bool Matches(string domain, string rule)
    {
        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(rule))
            return false;
        if (domain.Equals(rule, StringComparison.OrdinalIgnoreCase))
            return true;
        return domain.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase);
    }
}
