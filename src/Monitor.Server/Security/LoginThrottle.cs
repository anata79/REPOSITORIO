using System.Collections.Concurrent;

namespace Monitor.Server.Security;

/// <summary>Bloqueia por 15 minutos um endereço após 5 senhas erradas seguidas.</summary>
public sealed class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset LockedUntil)> _state = new();

    public bool IsLocked(string key, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (!_state.TryGetValue(key, out var s)) return false;
        var now = DateTimeOffset.UtcNow;
        if (s.LockedUntil > now)
        {
            remaining = s.LockedUntil - now;
            return true;
        }
        return false;
    }

    public void Fail(string key) =>
        _state.AddOrUpdate(key,
            _ => (1, DateTimeOffset.MinValue),
            (_, s) =>
            {
                var failures = s.LockedUntil > DateTimeOffset.MinValue && s.LockedUntil <= DateTimeOffset.UtcNow ? 1 : s.Failures + 1;
                return failures >= MaxFailures ? (0, DateTimeOffset.UtcNow.Add(LockFor)) : (failures, DateTimeOffset.MinValue);
            });

    public void Success(string key) => _state.TryRemove(key, out _);
}
