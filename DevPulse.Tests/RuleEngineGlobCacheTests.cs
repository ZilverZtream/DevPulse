using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

[Collection("RuleEngineGlobCache")]
public class RuleEngineGlobCacheTests
{
    public RuleEngineGlobCacheTests()
    {
        // Static cache is process-wide. Reset before each test for determinism.
        RuleEngine.ClearGlobCache();
    }

    [Fact]
    public void Cache_GrowsUntilCap()
    {
        for (int i = 0; i < RuleEngine.MaxGlobCacheSize; i++)
            RuleEngine.MatchesGlobForTesting("text", $"pattern-{i}-*");

        RuleEngine.GlobCacheCount.Should().Be(RuleEngine.MaxGlobCacheSize);
    }

    [Fact]
    public void Cache_AddingPastCap_EvictsLeastRecentlyUsed()
    {
        for (int i = 0; i < RuleEngine.MaxGlobCacheSize; i++)
            RuleEngine.MatchesGlobForTesting("text", $"pattern-{i}-*");

        // Cache is full. The oldest is "pattern-0-*"; adding a new one should evict it.
        RuleEngine.MatchesGlobForTesting("text", "new-pattern-*");

        RuleEngine.GlobCacheCount.Should().Be(RuleEngine.MaxGlobCacheSize);
        var keys = RuleEngine.GlobCacheKeysMruFirst();
        keys.Should().NotContain("pattern-0-*");
        keys.Should().Contain("new-pattern-*");
    }

    [Fact]
    public void Cache_Hit_MovesEntryToFront()
    {
        for (int i = 0; i < RuleEngine.MaxGlobCacheSize; i++)
            RuleEngine.MatchesGlobForTesting("text", $"pattern-{i}-*");

        // Touch the oldest entry — should move it to the head, protecting it from the next eviction.
        RuleEngine.MatchesGlobForTesting("text", "pattern-0-*");

        // Add a brand-new pattern; this should evict the now-second-oldest ("pattern-1-*"),
        // not "pattern-0-*" which was just promoted.
        RuleEngine.MatchesGlobForTesting("text", "evictor-*");

        var keys = RuleEngine.GlobCacheKeysMruFirst();
        keys.Should().Contain("pattern-0-*");
        keys.Should().NotContain("pattern-1-*");
        keys[0].Should().Be("evictor-*"); // MRU is the most recent insertion
    }

    [Fact]
    public void Cache_StaysAtCapAfterRepeatedEvictions()
    {
        // Insert 2× capacity worth of distinct patterns; cache must never exceed cap.
        for (int i = 0; i < RuleEngine.MaxGlobCacheSize * 2; i++)
        {
            RuleEngine.MatchesGlobForTesting("text", $"flood-{i}-*");
            RuleEngine.GlobCacheCount.Should().BeLessThanOrEqualTo(RuleEngine.MaxGlobCacheSize);
        }

        RuleEngine.GlobCacheCount.Should().Be(RuleEngine.MaxGlobCacheSize);
    }
}
