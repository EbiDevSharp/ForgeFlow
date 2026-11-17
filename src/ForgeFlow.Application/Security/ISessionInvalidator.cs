namespace ForgeFlow.Application.Security;

/// <summary>
/// Provides atomic runtime session invalidation across active HTTP requests and Blazor circuits.
/// </summary>
public interface ISessionInvalidator
{
    /// <summary>
    /// Atomically marks a specific session as revoked in memory and notifies all active circuits.
    /// </summary>
    void RevokeSession(Guid sessionId, string reason);

    /// <summary>
    /// Atomically marks all active sessions for a user as revoked in memory (optionally excluding a specific stamp or session ID)
    /// and notifies all active circuits for that user.
    /// </summary>
    void RevokeUserSessions(long userId, string reason, string? exceptSecurityStamp = null, Guid? exceptSessionId = null);
}
