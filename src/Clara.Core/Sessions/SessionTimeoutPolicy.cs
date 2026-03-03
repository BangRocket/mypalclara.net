namespace Clara.Core.Sessions;

public class SessionTimeoutPolicy
{
    private readonly int _idleMinutes;

    public SessionTimeoutPolicy(int idleMinutes = 30) => _idleMinutes = idleMinutes;

    public bool IsExpired(Session session) =>
        DateTime.UtcNow - session.LastActivityAt > TimeSpan.FromMinutes(_idleMinutes);
}
