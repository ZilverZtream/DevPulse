using DevPulse.App.UI;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class WindowBoundsServiceTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;
    private WindowBoundsService _bounds = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-bounds-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
        _bounds = new WindowBoundsService(_store);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task LoadAsync_NoEntry_ReturnsNull()
    {
        var result = await _bounds.LoadAsync("window.bounds.Missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsAllFields()
    {
        var record = new WindowBoundsRecord(100, 200, 1024, 768, true);
        await _bounds.SaveAsync(WindowBoundsService.BoardFormKey, record);

        var loaded = await _bounds.LoadAsync(WindowBoundsService.BoardFormKey);

        loaded.Should().NotBeNull();
        loaded!.X.Should().Be(100);
        loaded.Y.Should().Be(200);
        loaded.Width.Should().Be(1024);
        loaded.Height.Should().Be(768);
        loaded.Maximized.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsNullWithoutThrowing()
    {
        // Stash garbage directly under the key — service should swallow the JsonException.
        await _store.SetSettingAsync(WindowBoundsService.DebugWindowKey, "{not json");

        var act = () => _bounds.LoadAsync(WindowBoundsService.DebugWindowKey);

        var loaded = await act.Should().NotThrowAsync();
        loaded.Subject.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_OverwritesPriorRecord()
    {
        await _bounds.SaveAsync(WindowBoundsService.InboxEventsFormKey, new(0, 0, 800, 600, false));
        await _bounds.SaveAsync(WindowBoundsService.InboxEventsFormKey, new(50, 60, 1200, 900, true));

        var loaded = await _bounds.LoadAsync(WindowBoundsService.InboxEventsFormKey);

        loaded!.X.Should().Be(50);
        loaded.Width.Should().Be(1200);
        loaded.Maximized.Should().BeTrue();
    }
}
