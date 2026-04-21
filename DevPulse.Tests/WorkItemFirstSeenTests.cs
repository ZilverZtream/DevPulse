using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class WorkItemFirstSeenTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-fs-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    private static WorkItem MakeItem(int id, string state = "New") => new()
    {
        Id = id,
        Title = $"Item {id}",
        Type = WorkItemType.Bug,
        State = state,
        StateChangedAtUtc = DateTimeOffset.UtcNow,
        DiscoveredAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Insert_SetsFirstSeenUtc()
    {
        await _store.UpsertWorkItemsAsync([MakeItem(1)]);
        var items = await _store.GetWorkItemsAsync();
        items.Single().FirstSeenUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Upsert_DoesNotOverwriteFirstSeenUtc()
    {
        await _store.UpsertWorkItemsAsync([MakeItem(1)]);
        var firstSeen = (await _store.GetWorkItemsAsync()).Single().FirstSeenUtc;
        await Task.Delay(50);
        await _store.UpsertWorkItemsAsync([MakeItem(1, "Active")]);
        var updated = (await _store.GetWorkItemsAsync()).Single();
        updated.State.Should().Be("Active");
        updated.FirstSeenUtc.Should().Be(firstSeen);
    }
}
