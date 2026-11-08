using System.Text.Json;
using Monitor.Core;

namespace Monitor.Agent;

/// <summary>
/// Endereço do servidor e chave do computador, gravados pelo instalador em
/// C:\ProgramData\MonitorPortoconseg\config.json.
/// </summary>
internal sealed record AgentSettings(string ServerUrl, string Token)
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MonitorPortoconseg");

    public static AgentSettings? Load()
    {
        var path = Path.Combine(Folder, "config.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var url = doc.RootElement.GetProperty("serverUrl").GetString();
            var token = doc.RootElement.GetProperty("token").GetString();
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) return null;
            return new AgentSettings(url.Trim().TrimEnd('/'), token.Trim());
        }
        catch (Exception ex)
        {
            Log.Write($"Não foi possível ler {path}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Última configuração recebida do servidor, guardada para funcionar sem internet.</summary>
internal static class ConfigCache
{
    private static string FilePath => Path.Combine(Log.Folder, "config-servidor.json");

    public static AgentConfig? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(FilePath), Protocol.Json)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Save(AgentConfig config)
    {
        try
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(config, Protocol.Json));
        }
        catch (Exception ex)
        {
            Log.Write($"Não foi possível guardar a configuração: {ex.Message}");
        }
    }
}
