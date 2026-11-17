using System.Collections.Concurrent;
using System.Security.Claims;
using ForgeFlow.Application.Security;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SessionConcurrencyAndSyncTests
{
    private static ServiceProvider BuildAuthServices(Action<RuntimeSessionOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddDataProtection();
        services.AddAntiforgery();
        var config = new ConfigurationBuilder().Build();
        services.AddAuthenticationBackend(config);
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }
        return services.BuildServiceProvider();
    }

    [Fact]
    public void RuntimeSessions_Tracks_And_Retrieves_Registered_Session_State()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 42L;
        var stamp = "test-stamp-123";
        var now = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, now, now.AddHours(8), now);

        Assert.True(sessions.TryGetSessionState(sessionId, out var state));
        Assert.NotNull(state);
        Assert.Equal(sessionId, state!.SessionId);
        Assert.Equal(userId, state.UserId);
        Assert.Equal(stamp, state.SecurityStamp);
        Assert.False(state.IsRevoked);
        Assert.Equal(1, sessions.GetActiveSessionCount(userId));
    }

    [Fact]
    public async Task RuntimeSessions_Concurrent_Heartbeats_Are_ThreadSafe_And_Monotonic()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 100L;
        var stamp = "heartbeat-stamp";
        var start = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, start, start.AddHours(8), start);

        // Fire 50 concurrent heartbeats on the same session in parallel
        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            await Task.Yield();
            var reference = new SessionReference(userId, sessionId, stamp);
            await sessions.TouchSessionActivityAsync(reference, CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        Assert.True(sessions.TryGetSessionState(sessionId, out var state));
        Assert.NotNull(state);
        Assert.True(state!.LastActivityAtUtc >= start);
    }

    [Fact]
    public async Task RuntimeSessions_Concurrent_Session_Registrations_And_Revocations_Maintain_Consistent_Count()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var userId = 200L;
        var now = DateTime.UtcNow;
        var sessionIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();

        // Register 100 sessions in parallel
        await Task.WhenAll(sessionIds.Select(async sId =>
        {
            await Task.Yield();
            sessions.RegisterSessionInMemory(sId, userId, "stamp-" + sId, now, now.AddHours(8), now);
        }));

        Assert.Equal(100, sessions.GetActiveSessionCount(userId));

        // Concurrently revoke 50 of them in parallel
        var toRevoke = sessionIds.Take(50).ToList();
        await Task.WhenAll(toRevoke.Select(async sId =>
        {
            await Task.Yield();
            sessions.RevokeSession(sId, "ConcurrencyTestRevoke");
        }));

        Assert.Equal(50, sessions.GetActiveSessionCount(userId));

        // Verify remaining 50 are active and first 50 are revoked
        foreach (var sId in toRevoke)
        {
            Assert.True(sessions.TryGetSessionState(sId, out var state));
            Assert.True(state!.IsRevoked);
            Assert.Equal("ConcurrencyTestRevoke", state.RevokeReason);
        }

        foreach (var sId in sessionIds.Skip(50))
        {
            Assert.True(sessions.TryGetSessionState(sId, out var state));
            Assert.False(state!.IsRevoked);
        }
    }

    [Fact]
    public void Circuit_Registration_And_Deregistration_Does_Not_Evict_Session()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var circuit1 = Guid.NewGuid();
        var circuit2 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, 300L, "circuit-stamp", now, now.AddHours(8), now);

        sessions.RegisterCircuit(sessionId, circuit1);
        Assert.True(sessions.TryGetSessionState(sessionId, out var state));
        Assert.Equal(1, state!.ActiveCircuits);

        sessions.RegisterCircuit(sessionId, circuit2);
        Assert.Equal(2, state.ActiveCircuits);

        // Disconnect circuit 1 (e.g., closing one tab)
        sessions.UnregisterCircuit(sessionId, circuit1);
        Assert.Equal(1, state.ActiveCircuits);
        Assert.False(state.IsRevoked);

        // Disconnect circuit 2 (e.g., page refresh / F5)
        sessions.UnregisterCircuit(sessionId, circuit2);
        Assert.Equal(0, state.ActiveCircuits);

        // The session must NOT be prematurely evicted or marked revoked
        Assert.False(state.IsRevoked);
        Assert.Equal(1, sessions.GetActiveSessionCount(300L));
    }

    [Fact]
    public async Task SessionAuthenticationStateProvider_Immediately_Invalidates_On_SessionRevoked_Event()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 400L;
        var stamp = "blazor-stamp-1";
        var now = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, now, now.AddHours(8), now);

        var sessionRef = new SessionReference(userId, sessionId, stamp, "Test User");
        var principal = SessionClaims.Create(sessionRef);

        var provider = new SessionAuthenticationStateProvider(NullLoggerFactory.Instance, sessions);

        var authStateChangedFired = false;
        AuthenticationState? updatedState = null;
        provider.AuthenticationStateChanged += task =>
        {
            authStateChangedFired = true;
            updatedState = task.Result;
        };

        // Simulate Blazor circuit initializing with the authenticated principal
        provider.AttachSession(new AuthenticationState(principal));
        Assert.True(sessions.TryGetSessionState(sessionId, out var state));
        Assert.False(state!.IsRevoked);

        // Atomically revoke the session via RuntimeSessions
        sessions.RevokeSession(sessionId, "AdminRevoked");

        Assert.True(state.IsRevoked);
        Assert.True(authStateChangedFired);
        Assert.NotNull(updatedState);
        Assert.False(updatedState!.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task SessionAuthenticationStateProvider_Immediately_Invalidates_On_UserSessionsRevoked_Event()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var userId = 500L;
        var stamp = "user-stamp-1";
        var now = DateTime.UtcNow;

        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        sessions.RegisterSessionInMemory(session1, userId, stamp, now, now.AddHours(8), now);
        sessions.RegisterSessionInMemory(session2, userId, stamp, now, now.AddHours(8), now);

        var ref1 = new SessionReference(userId, session1, stamp);
        var ref2 = new SessionReference(userId, session2, stamp);

        var provider1 = new SessionAuthenticationStateProvider(NullLoggerFactory.Instance, sessions);
        var provider2 = new SessionAuthenticationStateProvider(NullLoggerFactory.Instance, sessions);

        var fired1 = false;
        var fired2 = false;
        provider1.AuthenticationStateChanged += _ => fired1 = true;
        provider2.AuthenticationStateChanged += _ => fired2 = true;

        provider1.AttachSession(new AuthenticationState(SessionClaims.Create(ref1)));
        provider2.AttachSession(new AuthenticationState(SessionClaims.Create(ref2)));

        // Revoke all sessions for user (e.g., user deactivated)
        sessions.RevokeUserSessions(userId, "AccountDisabled");

        Assert.True(fired1);
        Assert.True(fired2);
        Assert.Equal(0, sessions.GetActiveSessionCount(userId));
    }

    [Fact]
    public void SessionAuthenticationStateProvider_PasswordReset_Preserves_New_Session_While_Revoking_Old()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var userId = 600L;
        var oldStamp = "old-stamp";
        var newStamp = "new-stamp";
        var now = DateTime.UtcNow;

        var oldSessionId = Guid.NewGuid();
        var newSessionId = Guid.NewGuid();

        sessions.RegisterSessionInMemory(oldSessionId, userId, oldStamp, now, now.AddHours(8), now);
        sessions.RegisterSessionInMemory(newSessionId, userId, newStamp, now, now.AddHours(8), now);

        var oldRef = new SessionReference(userId, oldSessionId, oldStamp);
        var newRef = new SessionReference(userId, newSessionId, newStamp);

        var oldProvider = new SessionAuthenticationStateProvider(NullLoggerFactory.Instance, sessions);
        var newProvider = new SessionAuthenticationStateProvider(NullLoggerFactory.Instance, sessions);

        var oldFired = false;
        var newFired = false;
        oldProvider.AuthenticationStateChanged += _ => oldFired = true;
        newProvider.AuthenticationStateChanged += _ => newFired = true;

        oldProvider.AttachSession(new AuthenticationState(SessionClaims.Create(oldRef)));
        newProvider.AttachSession(new AuthenticationState(SessionClaims.Create(newRef)));

        // Revoke with exceptSessionId = newSessionId
        sessions.RevokeUserSessions(userId, "PasswordReset", exceptSessionId: newSessionId);

        Assert.True(oldFired);
        Assert.False(newFired);

        Assert.True(sessions.TryGetSessionState(oldSessionId, out var oldState));
        Assert.True(oldState!.IsRevoked);

        Assert.True(sessions.TryGetSessionState(newSessionId, out var newState));
        Assert.False(newState!.IsRevoked);
    }

    [Fact]
    public async Task ValidateAsync_Rejects_Revoked_Session_Immediately_Without_Db_Roundtrip()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 700L;
        var stamp = "validate-stamp";
        var now = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, now, now.AddHours(8), now);
        var reference = new SessionReference(userId, sessionId, stamp);

        // Revoke in memory
        sessions.RevokeSession(sessionId, "TestRevoke");

        // ValidateAsync should fast-path return Auth.SessionInvalid immediately
        var result = await sessions.ValidateAsync(reference);
        Assert.Equal("Auth.SessionInvalid", result);
    }

    [Fact]
    public async Task ValidateAsync_Rejects_Expired_Session_Immediately()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 800L;
        var stamp = "expired-stamp";
        var past = DateTime.UtcNow.AddHours(-10);

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, past, past.AddHours(8), past);
        var reference = new SessionReference(userId, sessionId, stamp);

        var result = await sessions.ValidateAsync(reference);
        Assert.Equal("Auth.SessionInvalid", result);
    }

    [Fact]
    public async Task ValidateAsync_Rejects_Stamp_Mismatch_Immediately()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 900L;
        var stamp = "correct-stamp";
        var now = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, now, now.AddHours(8), now);
        var reference = new SessionReference(userId, sessionId, "different-stamp");

        var result = await sessions.ValidateAsync(reference);
        Assert.Equal("Auth.SessionInvalid", result);
    }

    [Fact]
    public async Task Concurrent_Validations_For_Same_Session_Are_ThreadSafe()
    {
        using var sp = BuildAuthServices();
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var sessionId = Guid.NewGuid();
        var userId = 1000L;
        var stamp = "concurrent-validate-stamp";
        var now = DateTime.UtcNow;

        sessions.RegisterSessionInMemory(sessionId, userId, stamp, now, now.AddHours(8), now);
        sessions.RevokeSession(sessionId, "ConcurrentTest");

        var reference = new SessionReference(userId, sessionId, stamp);

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(async _ =>
        {
            await Task.Yield();
            return await sessions.ValidateAsync(reference);
        }));

        Assert.All(results, r => Assert.Equal("Auth.SessionInvalid", r));
    }

    [Fact]
    public void RuntimeSessions_Enforces_Concurrent_Session_Limit_Evicting_Oldest()
    {
        using var sp = BuildAuthServices(options =>
        {
            options.MaxConcurrentSessionsPerUser = 2;
            options.EvictOldestOnLimitReached = true;
        });
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var userId = 1100L;
        var now = DateTime.UtcNow;

        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();
        var session3 = Guid.NewGuid();

        var evictedFired = false;
        Guid? evictedFiredId = null;
        sessions.SessionRevoked += (sId, reason) =>
        {
            if (reason == "ConcurrentLimitExceeded")
            {
                evictedFired = true;
                evictedFiredId = sId;
            }
        };

        // Register session 1 at t=0
        sessions.RegisterSessionInMemory(session1, userId, "stamp-1", now, now.AddHours(8), now);
        Assert.Equal(1, sessions.GetActiveSessionCount(userId));

        // Register session 2 at t=1
        sessions.RegisterSessionInMemory(session2, userId, "stamp-2", now.AddMinutes(1), now.AddHours(8), now.AddMinutes(1));
        Assert.Equal(2, sessions.GetActiveSessionCount(userId));

        // Register session 3 at t=2 -> must evict session 1 (oldest)
        sessions.RegisterSessionInMemory(session3, userId, "stamp-3", now.AddMinutes(2), now.AddHours(8), now.AddMinutes(2));

        Assert.Equal(2, sessions.GetActiveSessionCount(userId));
        Assert.True(evictedFired);
        Assert.Equal(session1, evictedFiredId);

        Assert.True(sessions.TryGetSessionState(session1, out var state1));
        Assert.True(state1!.IsRevoked);
        Assert.Equal("ConcurrentLimitExceeded", state1.RevokeReason);

        Assert.True(sessions.TryGetSessionState(session2, out var state2));
        Assert.False(state2!.IsRevoked);

        Assert.True(sessions.TryGetSessionState(session3, out var state3));
        Assert.False(state3!.IsRevoked);
    }

    [Fact]
    public void RuntimeSessions_Enforces_Concurrent_Session_Limit_Rejecting_When_Eviction_Disabled()
    {
        using var sp = BuildAuthServices(options =>
        {
            options.MaxConcurrentSessionsPerUser = 2;
            options.EvictOldestOnLimitReached = false;
        });
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var userId = 1200L;
        var now = DateTime.UtcNow;

        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        sessions.RegisterSessionInMemory(session1, userId, "stamp-1", now, now.AddHours(8), now);
        sessions.RegisterSessionInMemory(session2, userId, "stamp-2", now.AddMinutes(1), now.AddHours(8), now.AddMinutes(1));
        Assert.Equal(2, sessions.GetActiveSessionCount(userId));

        // Check if new session is allowed
        var allowed = sessions.TryEnforceLimitInMemory(userId, out var evictedId);
        Assert.False(allowed);
        Assert.Null(evictedId);
        Assert.Equal(2, sessions.GetActiveSessionCount(userId));
    }

    [Fact]
    public async Task RuntimeSessions_Parallel_SignIns_With_Limit_Never_Exceed_MaxConcurrentSessions()
    {
        using var sp = BuildAuthServices(options =>
        {
            options.MaxConcurrentSessionsPerUser = 3;
            options.EvictOldestOnLimitReached = true;
        });
        var sessions = sp.GetRequiredService<RuntimeSessions>();

        var userId = 1300L;
        var now = DateTime.UtcNow;

        // Concurrently register 30 sessions in parallel
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async i =>
        {
            await Task.Yield();
            var sId = Guid.NewGuid();
            sessions.RegisterSessionInMemory(sId, userId, "stamp-" + i, now.AddSeconds(i), now.AddHours(8), now.AddSeconds(i));
        }));

        var activeCount = sessions.GetActiveSessionCount(userId);
        Assert.Equal(3, activeCount);
    }
}
