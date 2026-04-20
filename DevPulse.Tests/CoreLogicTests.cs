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

        var (inboxName, _) = new RuleEngine().AssignInbox(evt, [], [ruleInbox, fallbackInbox], [], new AppSettings());

        // If empty string were treated as wildcard, result would be "Conditional"
        Assert.Equal("Fallback", inboxName);
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
    public void AssignInbox_NullMessageText_RoutesToFallback()
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

        var (inboxName, _) = _engine.AssignInbox(evt, NoWatchers, inboxes, NoPacks, Settings);

        Assert.Equal("All", inboxName);
    }

    [Fact]
    public void AssignInbox_NullMessageText_DoesNotMatchKeywordRule()
    {
        var inboxes = new List<InboxDefinition>
        {
            new()
            {
                Name = "Alerts", IsEnabled = true, IsSystemInbox = false, Order = 1,
                Rules = [new InboxRule { Enabled = true, MessageContainsAny = ["ALERT"] }]
            },
            new()
            {
                Name = "Other", IsEnabled = true, IsSystemInbox = false, Order = 2,
                Rules = []
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

        var (inboxName, _) = _engine.AssignInbox(evt, NoWatchers, inboxes, NoPacks, Settings);

        Assert.Equal("Other", inboxName); // null message doesn't match "ALERT"; falls to fallback
    }

    [Fact]
    public void AssignInbox_NullMessageText_PassesThroughExcludeRule()
    {
        var inboxes = new List<InboxDefinition>
        {
            new()
            {
                Name = "Filtered", IsEnabled = true, IsSystemInbox = false, Order = 1,
                Rules = [new InboxRule { Enabled = true, ExcludeMessageContains = "noise" }]
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

        var (inboxName, _) = _engine.AssignInbox(evt, NoWatchers, inboxes, NoPacks, Settings);

        Assert.Equal("Filtered", inboxName); // empty string doesn't match "noise" exclude, rule passes, routes here
    }
}

public class WorkItemNormalizerFallbackClockTests
{
    [Fact]
    public void Normalize_MissingStateChangedDate_UsesFallbackNow()
    {
        var normalizer = new WorkItemNormalizer();
        var explicitNow = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var dto = new WorkItemDto
        {
            Id = 42,
            Title = "X",
            WorkItemType = "Bug",
            State = "Active",
            StateChangedDate = null
        };

        var item = normalizer.Normalize(dto, [], explicitNow);

        Assert.Equal(explicitNow, item.StateChangedAtUtc);
        Assert.Equal(0, item.DaysInCurrentState);
    }
}

public class MuteServiceTimezoneTests
{
    [Fact]
    public void CreateAuthorMuteToday_ExpiresAtMidnightUtcNextDay()
    {
        var nowUtc = new DateTimeOffset(2024, 6, 15, 23, 59, 0, TimeSpan.Zero);

        var entry = MuteService.CreateAuthorMuteToday("user@corp.com", nowUtc);

        Assert.Equal(new DateTimeOffset(2024, 6, 16, 0, 0, 0, TimeSpan.Zero), entry.ExpiresAtUtc);
    }

    [Fact]
    public void CreateAuthorMuteToday_PositiveOffsetTimezone_UsesUtcDate()
    {
        // UTC is June 15, but local time (+5h) is already June 16 at 03:00
        var nowWithOffset = new DateTimeOffset(2024, 6, 15, 22, 0, 0, TimeSpan.Zero)
            .ToOffset(TimeSpan.FromHours(5));

        var entry = MuteService.CreateAuthorMuteToday("user@corp.com", nowWithOffset);

        // Should expire at midnight UTC June 16 — NOT midnight UTC June 17
        Assert.Equal(new DateTimeOffset(2024, 6, 16, 0, 0, 0, TimeSpan.Zero), entry.ExpiresAtUtc);
    }
}

public class PollErrorClassifierTests
{
    [Theory]
    [InlineData(401, "Authentication failure (401)")]
    [InlineData(403, "Authorization failure (403)")]
    [InlineData(429, "Rate limited (429)")]
    [InlineData(500, "Server error (500)")]
    [InlineData(503, "Server error (503)")]
    public void Classify_KnownHttpStatus_ReturnsClassifiedMessage(int statusCode, string expected)
    {
        var ex = new HttpRequestException("msg", null, (System.Net.HttpStatusCode)statusCode);

        var result = PollErrorClassifier.Classify(ex);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Classify_HttpExceptionWithNoStatusCode_ReturnsExceptionMessage()
    {
        var ex = new HttpRequestException("network error");

        var result = PollErrorClassifier.Classify(ex);

        Assert.Equal("network error", result);
    }

    [Fact]
    public void Classify_NonHttpException_ReturnsExceptionMessage()
    {
        var ex = new InvalidOperationException("something went wrong");

        var result = PollErrorClassifier.Classify(ex);

        Assert.Equal("something went wrong", result);
    }
}
