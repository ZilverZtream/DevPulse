using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SqliteEventIdBatchTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-test-batch-{Guid.NewGuid():N}.db");
    private SqliteStateStore? _store;

    public async Task InitializeAsync()
    {
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_store != null) await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task GetExistingEventIdsAsync_HandlesLargeIdSet_AcrossChunkBoundary()
    {
        // 2500 ids: must span multiple chunks (chunk size 900 → 3 chunks).
        const int count = 2500;
        var ids = Enumerable.Range(0, count).Select(i => $"evt-{i:D6}").ToList();

        // Persist half of them.
        var events = ids.Take(count / 2).Select(id => new DevOpsEvent
        {
            EventId = id,
            PullRequestId = 1,
            DiscoveredAtUtc = DateTimeOffset.UtcNow
        });
        await _store!.SaveEventsAsync(events);

        var existing = await _store.GetExistingEventIdsAsync(ids);

        existing.Count.Should().Be(count / 2);
        existing.Should().Contain("evt-000000");
        existing.Should().Contain("evt-001249");
        existing.Should().NotContain("evt-001250");
        existing.Should().NotContain($"evt-{(count - 1):D6}");
    }

    [Fact]
    public async Task GetExistingEventIdsAsync_EmptyInput_ReturnsEmpty()
    {
        var existing = await _store!.GetExistingEventIdsAsync([]);
        existing.Should().BeEmpty();
    }
}
