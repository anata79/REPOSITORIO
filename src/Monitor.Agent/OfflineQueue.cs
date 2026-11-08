using System.Text.Json;
using Monitor.Core;

namespace Monitor.Agent;

/// <summary>
/// Fila em arquivo (%LOCALAPPDATA%\MonitorPortoconseg\fila.jsonl), uma linha por período.
/// Sem internet, os períodos ficam aqui e são enviados quando a conexão volta.
/// </summary>
internal sealed class OfflineQueue
{
    private const int MaxLines = 200_000;
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(Log.Folder, "fila.jsonl");

    public void Append(IEnumerable<Segment> segments)
    {
        var lines = segments.Select(s => JsonSerializer.Serialize(SegmentDto.From(s), Protocol.Json)).ToList();
        if (lines.Count == 0) return;
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                File.AppendAllLines(_path, lines);
                TrimIfHuge();
            }
            catch (Exception ex)
            {
                Log.Write($"Falha ao gravar a fila: {ex.Message}");
            }
        }
    }

    /// <summary>Envia a fila em lotes. Retorna quantos períodos foram aceitos pelo servidor.</summary>
    public async Task<int> DrainAsync(Func<IReadOnlyList<SegmentDto>, Task<bool>> send, int batchSize = 1000)
    {
        List<string> lines;
        lock (_sync)
        {
            if (!File.Exists(_path)) return 0;
            lines = File.ReadAllLines(_path).Where(l => l.Length > 0).ToList();
        }

        var sent = 0;
        var position = 0;
        while (position < lines.Count)
        {
            var chunk = lines.Skip(position).Take(batchSize).ToList();
            var segments = new List<SegmentDto>(chunk.Count);
            foreach (var line in chunk)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<SegmentDto>(line, Protocol.Json);
                    if (dto is not null) segments.Add(dto);
                }
                catch (Exception)
                {
                    // Linha corrompida (ex.: computador desligou durante a gravação): descartada.
                }
            }

            if (!await send(segments)) break;
            sent += segments.Count;
            position += chunk.Count;
        }

        if (position > 0)
        {
            lock (_sync)
            {
                // Mantém o que não foi enviado e o que chegou enquanto o envio acontecia.
                var current = File.Exists(_path) ? File.ReadAllLines(_path).Where(l => l.Length > 0).ToList() : [];
                var remaining = current.Skip(position).ToList();
                if (remaining.Count == 0) File.Delete(_path);
                else File.WriteAllLines(_path, remaining);
            }
        }
        return sent;
    }

    private void TrimIfHuge()
    {
        var info = new FileInfo(_path);
        if (info.Length < 40_000_000) return;
        var lines = File.ReadAllLines(_path);
        if (lines.Length <= MaxLines) return;
        File.WriteAllLines(_path, lines.Skip(lines.Length - MaxLines));
        Log.Write("Fila muito grande: períodos mais antigos descartados.");
    }
}
