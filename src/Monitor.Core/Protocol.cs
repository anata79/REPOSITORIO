using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monitor.Core;

/// <summary>Contrato entre o agente (Windows) e o servidor.</summary>
public static class Protocol
{
    public const string TokenHeader = "X-Agent-Token";
    public const string ConfigPath = "/api/agent/config";
    public const string UploadPath = "/api/agent/segments";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Configuração que o servidor envia ao agente.</summary>
public sealed record AgentConfig(
    int IdleMinutes,
    IReadOnlyList<string> PersonalDomains,
    IReadOnlyList<string> PersonalProcesses);

/// <summary>Período enviado pelo agente.</summary>
public sealed record SegmentDto(
    Guid Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    ActivityState State,
    bool Personal,
    string? Process,
    string? AppName,
    string? Domain,
    string? Title)
{
    public static SegmentDto From(Segment s) =>
        new(s.Id, s.Start, s.End, s.State, s.Personal, s.Process, s.AppName, s.Domain, s.Title);
}

/// <summary>Lote enviado pelo agente.</summary>
public sealed record UploadRequest(
    string AgentVersion,
    string? Hostname,
    string? WindowsUser,
    IReadOnlyList<SegmentDto> Segments);

public sealed record UploadResponse(int Received, int Inserted);
