using System.Collections.Concurrent;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class PasskeyChallengeStore
{
    public sealed record StoredChallenge(
        long UserId,
        string ChallengeB64Url,
        DateTimeOffset ExpiresAtUtc,
        bool StepUpVerified);

    private readonly ConcurrentDictionary<string, StoredChallenge> _challenges = new(StringComparer.Ordinal);

    public void StoreChallenge(string challengeKey, long userId, string challengeB64Url, TimeSpan lifetime, bool stepUpVerified = false)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _challenges)
        {
            if (pair.Value.ExpiresAtUtc <= now)
                _challenges.TryRemove(pair.Key, out _);
        }

        _challenges[challengeKey] = new StoredChallenge(userId, challengeB64Url, now.Add(lifetime), stepUpVerified);
    }

    public bool TryConsume(string challengeKey, long expectedUserId, out string challengeB64Url, out bool stepUpVerified)
    {
        challengeB64Url = string.Empty;
        stepUpVerified = false;

        if (string.IsNullOrWhiteSpace(challengeKey))
            return false;

        if (!_challenges.TryRemove(challengeKey, out var entry))
            return false;

        if (DateTimeOffset.UtcNow > entry.ExpiresAtUtc)
            return false;

        if (entry.UserId != expectedUserId)
            return false;

        challengeB64Url = entry.ChallengeB64Url;
        stepUpVerified = entry.StepUpVerified;
        return true;
    }
}
