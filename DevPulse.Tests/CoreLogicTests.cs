using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;

namespace DevPulse.Tests;

public class WorkItemNormalizerTests
{
    private static readonly IReadOnlyList<BoardColumnDefinition> NoColumns = [];

    [Fact]
    public void Normalize_FutureStateChangedDate_DaysIsZeroNotNegative()
    {
        var normalizer = new WorkItemNormalizer();
        var futureDate = DateTimeOffset.UtcNow.AddDays(2);
        var dto = new WorkItemDto
        {
            Id = 1,
            Title = "T",
            WorkItemType = "Task",
            State = "Active",
            StateChangedDate = futureDate
        };
        var now = DateTimeOffset.UtcNow;

        var item = normalizer.Normalize(dto, NoColumns, now);

        Assert.Equal(0, item.DaysInCurrentState);
    }
}

public class RuleEngineTests
{
    [Fact]
    public void AssignInbox_EmptyKeywordInMessageContainsAny_RoutesToFallback()
    {
        var ruleInbox = new InboxDefinition
        {
            Name = "Conditional",
            IsEnabled = true,
            Order = 0,
            IsSystemInbox = false,
            Rules = [new InboxRule { Enabled = true, MessageContainsAny = ["", "   ", "nope"] }]
        };
        var fallbackInbox = new InboxDefinition
        {
            Name = "Fallback",
            IsEnabled = true,
            Order = 1,
            IsSystemInbox = false,
            Rules = []
        };
        var evt = new DevOpsEvent
        {
            MessageText = "hello world",   // does not contain "nope"; blank entries must not match
            AuthorCanonicalKey = "user@corp.com",
            Status = "active", Repository = "repo", Project = "proj"
        };

        var result = new RuleEngine().AssignInbox(evt, [], [ruleInbox, fallbackInbox], [], new AppSettings());

        // If empty string were treated as wildcard, result would be "Conditional"
        Assert.Equal("Fallback", result);
    }
}

public class IdentityNormalizerTests
{
    [Fact]
    public void ClassifySource_NullDisplayName_DoesNotThrow()
    {
        var normalizer = new IdentityNormalizer([], ["bot"]);
        var identity = new IdentityRefDto { DisplayName = null!, UniqueName = "user@org.com" };

        var result = normalizer.ClassifySource(identity);

        Assert.Equal(PrEventSource.Human, result);
    }

    [Fact]
    public void ClassifySource_BotPatternInDisplayName_ReturnsBot()
    {
        var normalizer = new IdentityNormalizer([], ["[bot]"]);
        var identity = new IdentityRefDto { DisplayName = "Renovate [bot]", UniqueName = string.Empty };

        var result = normalizer.ClassifySource(identity);

        Assert.Equal(PrEventSource.Bot, result);
    }
}
