using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SqliteAiAttemptStoreTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-ai-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task RecordAndRetrieve_RoundTripsAllFields()
    {
        var attempt = new AiAttempt
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = 42,
            Project = "MyProject",
            TemplateId = "bug-default",
            ProviderId = "claude-cli",
            Model = "claude-3-5-sonnet",
            Status = AiAttemptStatus.Success,
            ValidationPassed = true,
            MissingSections = [],
            SpecFilePath = @"C:\devops\MyProject\42\spec-ts.md",
            PromptFilePath = @"C:\devops\MyProject\42\prompt-ts.md",
            DurationMs = 1234,
            TokensIn = 500,
            TokensOut = 800,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ErrorMessage = null
        };

        await _store.RecordAiAttemptAsync(attempt);

        var list = await _store.GetAiAttemptsForWorkItemAsync(42);
        list.Should().HaveCount(1);
        var loaded = list[0];
        loaded.Id.Should().Be(attempt.Id);
        loaded.Status.Should().Be(AiAttemptStatus.Success);
        loaded.ValidationPassed.Should().BeTrue();
        loaded.SpecFilePath.Should().Be(attempt.SpecFilePath);
        loaded.DurationMs.Should().Be(1234);
    }

    [Fact]
    public async Task GetAttempts_OrdersNewestFirst()
    {
        var older = new AiAttempt
        {
            Id = "a1", WorkItemId = 7, Status = AiAttemptStatus.Success,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var newer = new AiAttempt
        {
            Id = "a2", WorkItemId = 7, Status = AiAttemptStatus.ProviderError,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await _store.RecordAiAttemptAsync(older);
        await _store.RecordAiAttemptAsync(newer);

        var list = await _store.GetAiAttemptsForWorkItemAsync(7);
        list[0].Id.Should().Be("a2");
        list[1].Id.Should().Be("a1");
    }

    [Fact]
    public async Task Record_ValidationFailed_PersistsMissingSectionsCommaSeparated()
    {
        var attempt = new AiAttempt
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = 1,
            Status = AiAttemptStatus.ValidationFailed,
            ValidationPassed = false,
            MissingSections = ["Edge cases", "Test plan"],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await _store.RecordAiAttemptAsync(attempt);
        var loaded = (await _store.GetAiAttemptsForWorkItemAsync(1))[0];
        loaded.MissingSections.Should().BeEquivalentTo("Edge cases", "Test plan");
    }
}
