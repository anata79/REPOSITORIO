using Monitor.Core;

namespace Monitor.Agent;

/// <summary>
/// Laço principal: lê a janela a cada 5 segundos, monta os períodos, grava na fila
/// e envia ao servidor a cada minuto.
/// </summary>
internal sealed class AgentWorker : IDisposable
{
    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ConfigEvery = TimeSpan.FromMinutes(10);

    private readonly ServerClient _client;
    private readonly WindowProbe _probe = new();
    private readonly OfflineQueue _queue = new();
    private readonly PeriodTracker _tracker;
    private readonly object _trackerLock = new();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly SemaphoreSlim _sending = new(1, 1);
    private PersonalFilter _filter;
    private Thread? _thread;
    private volatile bool _locked;

    public AgentWorker(AgentSettings settings)
    {
        _client = new ServerClient(settings);
        var cached = ConfigCache.Load();
        _tracker = new PeriodTracker(TimeSpan.FromMinutes(cached?.IdleMinutes ?? 5));
        _filter = cached is null ? PersonalFilter.Empty : new PersonalFilter(cached.PersonalDomains, cached.PersonalProcesses);
    }

    public ServerClient Client => _client;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Monitor" };
        _thread.Start();
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        Sample();
    }

    /// <summary>Grava e tenta enviar tudo agora (antes de suspender, desligar ou sair).</summary>
    public void FlushAll()
    {
        IReadOnlyList<Segment> all;
        lock (_trackerLock) all = _tracker.TakeAll();
        _queue.Append(all);
        try
        {
            SendAsync().Wait(TimeSpan.FromSeconds(4));
        }
        catch (Exception)
        {
            // Fica na fila para o próximo envio.
        }
    }

    private void Run()
    {
        Log.Write($"Agente {ServerClient.Version} iniciado para {Environment.UserName} em {Environment.MachineName}.");
        var lastSend = DateTimeOffset.MinValue;
        var lastConfig = DateTimeOffset.MinValue;

        while (!_stop.IsSet)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;

                if (now - lastConfig >= ConfigEvery)
                {
                    lastConfig = now;
                    RefreshConfig();
                }

                Sample();

                if (now - lastSend >= SendEvery)
                {
                    lastSend = now;
                    IReadOnlyList<Segment> ready;
                    lock (_trackerLock) ready = _tracker.TakeReady(DateTimeOffset.UtcNow);
                    _queue.Append(ready);
                    SendAsync().Wait(TimeSpan.FromSeconds(45));
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Erro no laço principal: {ex}");
            }

            _stop.Wait(SampleEvery);
        }
    }

    private void Sample()
    {
        try
        {
            var sample = _probe.Read(DateTimeOffset.UtcNow, _locked);
            lock (_trackerLock) _tracker.Observe(sample, _filter);
        }
        catch (Exception ex)
        {
            Log.Write($"Falha ao ler a janela: {ex.Message}");
        }
    }

    private async Task SendAsync()
    {
        if (!await _sending.WaitAsync(0)) return;
        try
        {
            var sent = await _queue.DrainAsync(_client.UploadAsync);
            if (sent == 0)
                await _client.UploadAsync([]); // sinal de vida: o painel mostra "conectado"
        }
        finally
        {
            _sending.Release();
        }
    }

    private void RefreshConfig()
    {
        var config = _client.GetConfigAsync().GetAwaiter().GetResult();
        if (config is null) return;
        ConfigCache.Save(config);
        _filter = new PersonalFilter(config.PersonalDomains, config.PersonalProcesses);
        lock (_trackerLock) _tracker.IdleThreshold = TimeSpan.FromMinutes(Math.Clamp(config.IdleMinutes, 1, 60));
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _client.Dispose();
        _stop.Dispose();
    }
}
