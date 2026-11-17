namespace ForgeFlow.Infrastructure.Identity;

/// <summary>
/// Thread-safe in-memory representation of an active runtime session.
/// </summary>
public sealed class RuntimeSessionState
{
    public Guid SessionId { get; }
    public long UserId { get; }
    public string SecurityStamp { get; private set; }
    public DateTime IssuedAtUtc { get; }
    public DateTime ExpiresAtUtc { get; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime? LastPersistedActivityAtUtc { get; set; }
    public bool IsRevoked => Volatile.Read(ref _isRevoked) == 1;
    public string? RevokeReason { get; private set; }
    public int ActiveCircuits => Volatile.Read(ref _activeCircuits);

    private int _isRevoked;
    private int _activeCircuits;

    public RuntimeSessionState(
        Guid sessionId,
        long userId,
        string securityStamp,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc,
        DateTime lastActivityAtUtc)
    {
        SessionId = sessionId;
        UserId = userId;
        SecurityStamp = securityStamp;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        LastActivityAtUtc = lastActivityAtUtc;
    }

    public void MarkRevoked(string reason)
    {
        RevokeReason = reason;
        Volatile.Write(ref _isRevoked, 1);
    }

    public void UpdateSecurityStamp(string newStamp)
    {
        SecurityStamp = newStamp;
    }

    public void IncrementCircuits() => Interlocked.Increment(ref _activeCircuits);

    public void DecrementCircuits()
    {
        int current;
        do
        {
            current = Volatile.Read(ref _activeCircuits);
            if (current <= 0) break;
        } while (Interlocked.CompareExchange(ref _activeCircuits, current - 1, current) != current);
    }
}
