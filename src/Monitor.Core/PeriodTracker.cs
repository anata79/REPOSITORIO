namespace Monitor.Core;

/// <summary>
/// Transforma leituras a cada poucos segundos em períodos contínuos (início e fim).
///
/// Regras:
/// <list type="bullet">
/// <item>Ativo: teclado ou mouse usados há menos que <see cref="IdleThreshold"/>.</item>
/// <item>Inativo: começa no momento do último uso de teclado ou mouse, não no momento em que foi detectado.
/// Por isso os últimos minutos de "ativo" são corrigidos para "inativo" quando a inatividade é detectada.</item>
/// <item>Bloqueado: tela bloqueada.</item>
/// <item>Lacunas maiores que <see cref="MaxGap"/> (computador suspenso, desligado ou agente parado)
/// ficam sem período: o servidor trata como "fora do computador".</item>
/// </list>
/// Para a correção da inatividade ser possível, só são entregues para envio os períodos que terminaram
/// há mais de <see cref="IdleThreshold"/> + <see cref="SafetyMargin"/>.
/// </summary>
public sealed class PeriodTracker
{
    public const int MaxTitleLength = 300;

    private readonly List<Segment> _closed = [];
    private Segment? _open;
    private DateTimeOffset? _lastSampleAt;
    private DateTimeOffset? _streamStart;
    private DateTimeOffset _handedOutUntil = DateTimeOffset.MinValue;

    public PeriodTracker(TimeSpan idleThreshold, TimeSpan? maxGap = null, TimeSpan? safetyMargin = null)
    {
        IdleThreshold = idleThreshold;
        MaxGap = maxGap ?? TimeSpan.FromSeconds(20);
        SafetyMargin = safetyMargin ?? TimeSpan.FromSeconds(15);
    }

    /// <summary>Tempo sem teclado e mouse para considerar inativo.</summary>
    public TimeSpan IdleThreshold { get; set; }

    /// <summary>Intervalo máximo entre leituras para considerar o período contínuo.</summary>
    public TimeSpan MaxGap { get; }

    /// <summary>Folga além do limite de inatividade antes de entregar um período para envio.</summary>
    public TimeSpan SafetyMargin { get; }

    /// <summary>Quantidade de períodos guardados aguardando envio (para diagnóstico).</summary>
    public int PendingCount => _closed.Count + (_open is null ? 0 : 1);

    public void Observe(Sample sample, PersonalFilter filter)
    {
        var at = sample.At;

        if (_lastSampleAt is { } last && at - last > MaxGap)
        {
            // Lacuna: suspensão, desligamento ou agente parado. Fecha o período como estava.
            CloseOpen();
            _streamStart = at;
        }
        _streamStart ??= at;
        _lastSampleAt = at;

        var candidate = BuildCandidate(sample, filter);

        if (candidate.State == ActivityState.Idle && (_open is null || _open.State != ActivityState.Idle))
        {
            var idleStart = at - sample.IdleFor;
            if (idleStart < _streamStart.Value) idleStart = _streamStart.Value;
            if (idleStart < _handedOutUntil) idleStart = _handedOutUntil;
            if (idleStart > at) idleStart = at;

            TrimAfter(idleStart);
            candidate.Start = idleStart;
            candidate.End = at;
            _open = candidate;
            return;
        }

        if (_open is not null)
        {
            if (_open.SameWindowAs(candidate))
            {
                _open.End = at;
                return;
            }
            _open.End = at;
            CloseOpen();
        }

        candidate.Start = at;
        candidate.End = at;
        _open = candidate;
    }

    /// <summary>
    /// Entrega os períodos que já não podem mais ser corrigidos e os remove da memória.
    /// Um período aberto e longo é cortado no limite, para o painel receber dados com pouco atraso.
    /// </summary>
    public IReadOnlyList<Segment> TakeReady(DateTimeOffset now)
    {
        var horizon = now - IdleThreshold - SafetyMargin;

        if (_open is not null && _open.Start < horizon)
        {
            var cut = _open.End < horizon ? _open.End : horizon;
            if (cut > _open.Start)
            {
                _closed.Add(_open.CloneWith(_open.Start, cut));
                _open.Start = cut;
            }
        }

        var ready = _closed.Where(s => s.End <= horizon).OrderBy(s => s.Start).ToList();
        if (ready.Count > 0)
        {
            _closed.RemoveAll(s => s.End <= horizon);
            var maxEnd = ready.Max(s => s.End);
            if (maxEnd > _handedOutUntil) _handedOutUntil = maxEnd;
        }
        return ready;
    }

    /// <summary>Entrega tudo, inclusive o período aberto. Usado ao desligar, suspender ou sair.</summary>
    public IReadOnlyList<Segment> TakeAll()
    {
        CloseOpen();
        var all = _closed.OrderBy(s => s.Start).ToList();
        _closed.Clear();
        if (all.Count > 0)
        {
            var maxEnd = all.Max(s => s.End);
            if (maxEnd > _handedOutUntil) _handedOutUntil = maxEnd;
        }
        _lastSampleAt = null;
        _streamStart = null;
        return all;
    }

    private Segment BuildCandidate(Sample sample, PersonalFilter filter)
    {
        if (sample.Locked)
            return new Segment { State = ActivityState.Locked };

        if (sample.IdleFor >= IdleThreshold)
            return new Segment { State = ActivityState.Idle };

        if (filter.IsPersonal(sample.Process, sample.Domain))
            return new Segment { State = ActivityState.Active, Personal = true };

        return new Segment
        {
            State = ActivityState.Active,
            Process = sample.Process,
            AppName = Truncate(sample.AppName, 120),
            Domain = sample.Domain,
            Title = Truncate(sample.Title, MaxTitleLength),
        };
    }

    private void TrimAfter(DateTimeOffset t)
    {
        if (_open is not null)
        {
            if (_open.Start >= t)
            {
                _open = null;
            }
            else
            {
                if (_open.End > t) _open.End = t;
                CloseOpen();
            }
        }

        for (var i = _closed.Count - 1; i >= 0; i--)
        {
            var seg = _closed[i];
            if (seg.Start >= t)
                _closed.RemoveAt(i);
            else if (seg.End > t)
                seg.End = t;
        }
    }

    private void CloseOpen()
    {
        if (_open is not null && _open.End > _open.Start)
            _closed.Add(_open);
        _open = null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }
}
