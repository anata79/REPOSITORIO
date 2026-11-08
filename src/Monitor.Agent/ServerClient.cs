using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Monitor.Core;

namespace Monitor.Agent;

/// <summary>Comunicação com o servidor (HTTPS) usando a chave do computador.</summary>
internal sealed class ServerClient : IDisposable
{
    private readonly HttpClient _http;

    public ServerClient(AgentSettings settings)
    {
        _http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add(Protocol.TokenHeader, settings.Token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"MonitorPortoconseg/{Version}");
    }

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public DateTime? LastSuccess { get; private set; }
    public string? LastError { get; private set; }

    public async Task<AgentConfig?> GetConfigAsync()
    {
        try
        {
            var config = await _http.GetFromJsonAsync<AgentConfig>(Protocol.ConfigPath.TrimStart('/'), Protocol.Json);
            LastError = null;
            return config;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            Fail("Chave recusada pelo servidor. Gere uma nova chave no painel e reinstale.");
        }
        catch (Exception ex)
        {
            Fail($"Sem conexão com o servidor: {ex.Message}");
        }
        return null;
    }

    public async Task<bool> UploadAsync(IReadOnlyList<SegmentDto> segments)
    {
        try
        {
            var body = new UploadRequest(Version, Environment.MachineName, Environment.UserName, segments);
            using var response = await _http.PostAsJsonAsync(Protocol.UploadPath.TrimStart('/'), body, Protocol.Json);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Fail("Chave recusada pelo servidor. Gere uma nova chave no painel e reinstale.");
                return false;
            }
            if (!response.IsSuccessStatusCode)
            {
                Fail($"Servidor respondeu {(int)response.StatusCode}.");
                return false;
            }
            LastSuccess = DateTime.Now;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Fail($"Sem conexão com o servidor: {ex.Message}");
            return false;
        }
    }

    private void Fail(string message)
    {
        if (message != LastError) Log.Write(message);
        LastError = message;
    }

    public void Dispose() => _http.Dispose();
}
