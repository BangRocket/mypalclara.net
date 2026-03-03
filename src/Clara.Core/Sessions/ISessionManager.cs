namespace Clara.Core.Sessions;

public interface ISessionManager
{
    Task<Session> GetOrCreateAsync(string sessionKey, string? userId = null, CancellationToken ct = default);
    Task<Session?> GetAsync(string sessionKey, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task TimeoutAsync(string sessionKey, CancellationToken ct = default);
}
