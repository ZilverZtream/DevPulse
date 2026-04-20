using DevPulse.App.Services;
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

public class WiqlPathValidationTests
{
    [Theory]
    [InlineData(@"MyOrg\MyProject\Team & QA")]
    [InlineData(@"Sprint (2024-Q1)")]
    [InlineData("Area: Platform")]
    [InlineData(@"Backlog\Feature: Auth")]
    public void ValidateWiqlPath_LegitimateAdoNames_DoesNotThrow(string path)
    {
        var result = WiqlPathGuard.ValidatePath(path, "areaPath");
        Assert.Equal(path, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("path;DROP TABLE--")]
    [InlineData("path\x00")]
    [InlineData("path\ninjection")]
    [InlineData("path'injection")]
    public void ValidateWiqlPath_InvalidPaths_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => WiqlPathGuard.ValidatePath(path, "areaPath"));
    }
}

public class PollingLoopBaseTests
{
    private sealed class CountingPoller : PollingLoopBase
    {
        public int InitialPollCount;
        protected override string TrackName => "test";
        protected override Task ExecutePollAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref InitialPollCount);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Start_CalledTwice_OnlyOneInitialPollFires()
    {
        using var poller = new CountingPoller();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.PollCompleted += (_, _) => tcs.TrySetResult();

        poller.Start(60);
        poller.Start(60);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, poller.InitialPollCount);
    }
}

public class RuleEngineNullMessageTests
{
    private readonly RuleEngine _engine = new();
    private static readonly AppSettings Settings = new() { CurrentUserCanonicalKey = "me@test.com" };
    private static readonly IReadOnlyList<Watcher> NoWatchers = [];
    private static readonly IReadOnlyList<KeywordPack> NoPacks = [];

    [Fact]
    public void AssignInbox_NullMessageText_DoesNotThrow()
    {
        var inboxes = new List<InboxDefinition>
        {
            new() { Name = "All", IsEnabled = true, IsSystemInbox = false, Order = 1, Rules = [] }
        };
        var evt = new DevOpsEvent
        {
            EventId = "e1",
            EventSource = PrEventSource.Human,
            EventMeaning = EventMeaning.Comment,
            MessageText = null!,
            AuthorCanonicalKey = "other@test.com",
            IsCurrentUserReviewer = true,
            Status = "active"
        };

        var ex = Record.Exception(() => _engine.AssignInbox(evt, NoWatchers, inboxes, NoPacks, Settings));

        Assert.Null(ex);
    }

    [Fact]
    public void AssignInbox_NullMessageText_WithKeywordRule_DoesNotThrow()
    {
        var inboxes = new List<InboxDefinition>
        {
            new()
            {
                Name = "Alerts", IsEnabled = true, IsSystemInbox = false, Order = 1,
                Rules =
                [
                    new InboxRule { Enabled = true, MessageContainsAny = ["ALERT"] }
                ]
            }
        };
        var evt = new DevOpsEvent
        {
            EventId = "e2",
            EventSource = PrEventSource.Human,
            EventMeaning = EventMeaning.Comment,
            MessageText = null!,
            AuthorCanonicalKey = "other@test.com",
            IsCurrentUserReviewer = false,
            Status = "active"
        };

        var ex = Record.Exception(() => _engine.AssignInbox(evt, NoWatchers, inboxes, NoPacks, Settings));

        Assert.Null(ex);
    }

    [Fact]
    public void AssignInbox_NullMessageText_WithExcludeRule_DoesNotThrow()
    {
        var inboxes = new List<InboxDefinition>
        {
            new()
            {
                Name = "Filtered", IsEnabled = true, IsSystemInbox = false, Order = 1,
                Rules =
                [
                    new InboxRule { Enabled = true, ExcludeMessageContains = "noise" }
                ]
            }
        };
        var evt = new DevOpsEvent
        {
            EventId = "e3",
            EventSource = PrEventSource.Human,
            EventMeaning = EventMeaning.Comment,
            MessageText = null!,
            AuthorCanonicalKey = "other@test.com",
            IsCurrentUserReviewer = false,
            Status = "active"
        };

        var ex = Record.Exception(() => _engine.AssignInbox(evt, NoWatchers, inboxes, NoPacks, Settings));

        Assert.Null(ex);
    }
}
