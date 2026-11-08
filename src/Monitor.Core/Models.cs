namespace Monitor.Core;

/// <summary>Estado do computador num momento.</summary>
public enum ActivityState
{
    /// <summary>Teclado ou mouse usados dentro do limite de inatividade.</summary>
    Active,
    /// <summary>Sem teclado e mouse por pelo menos o limite de inatividade.</summary>
    Idle,
    /// <summary>Tela bloqueada.</summary>
    Locked,
}

/// <summary>Uma leitura feita pelo agente (a cada poucos segundos).</summary>
/// <param name="At">Momento da leitura.</param>
/// <param name="Locked">Tela bloqueada.</param>
/// <param name="IdleFor">Tempo desde o último uso de teclado ou mouse.</param>
/// <param name="Process">Nome do processo em primeiro plano, sem ".exe", em minúsculas.</param>
/// <param name="AppName">Nome amigável do programa (descrição do executável), se houver.</param>
/// <param name="Domain">Domínio do site, quando o programa é um navegador e o endereço foi lido.</param>
/// <param name="Title">Título da janela em primeiro plano.</param>
public sealed record Sample(
    DateTimeOffset At,
    bool Locked,
    TimeSpan IdleFor,
    string? Process,
    string? AppName,
    string? Domain,
    string? Title);

/// <summary>Um período contínuo com o mesmo estado e a mesma janela.</summary>
public sealed class Segment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public ActivityState State { get; init; }
    public string? Process { get; init; }
    public string? AppName { get; init; }
    public string? Domain { get; init; }
    public string? Title { get; init; }
    /// <summary>Uso pessoal: programa e site não são guardados, só o tempo.</summary>
    public bool Personal { get; init; }

    public TimeSpan Duration => End - Start;

    public bool SameWindowAs(Segment other) =>
        State == other.State
        && Personal == other.Personal
        && string.Equals(Process, other.Process, StringComparison.Ordinal)
        && string.Equals(Domain, other.Domain, StringComparison.Ordinal)
        && string.Equals(Title, other.Title, StringComparison.Ordinal);

    public Segment CloneWith(DateTimeOffset start, DateTimeOffset end) => new()
    {
        Start = start,
        End = end,
        State = State,
        Process = Process,
        AppName = AppName,
        Domain = Domain,
        Title = Title,
        Personal = Personal,
    };
}
