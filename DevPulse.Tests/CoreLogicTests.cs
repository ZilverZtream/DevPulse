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
