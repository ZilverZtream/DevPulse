using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SettingsAiTemplateStoreTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;
    private SettingsService _settings = null!;
    private SettingsAiTemplateStore _sut = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tpl-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
        _settings = new SettingsService(_store);
        _sut = new SettingsAiTemplateStore(_settings);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task GetTemplates_EmptyStore_ReturnsShippedDefaults()
    {
        var list = await _sut.GetTemplatesAsync();
        list.Should().NotBeEmpty();
        list.Select(t => t.Id).Should().Contain(new[] { "bug-default", "userstory-default", "feature-default", "task-default" });
    }

    [Fact]
    public async Task SaveThenGet_RoundTrips()
    {
        var mine = new AiTemplate
        {
            Id = "custom",
            Name = "My Custom",
            AppliesTo = ["Bug"],
            RequiredHeaders = ["Header A", "Header B"],
            PromptBody = "Body with {title}"
        };
        await _sut.SaveTemplatesAsync([mine]);
        var list = await _sut.GetTemplatesAsync();
        list.Should().ContainSingle().Which.Id.Should().Be("custom");
    }

    [Fact]
    public async Task GetDefaultTemplateFor_MatchesByWorkItemType()
    {
        var t = await _sut.GetDefaultTemplateForAsync("Bug");
        t.Should().NotBeNull();
        t!.AppliesTo.Should().Contain("Bug");
    }

    [Fact]
    public async Task GetDefaultTemplateFor_UnknownType_ReturnsNull()
    {
        var t = await _sut.GetDefaultTemplateForAsync("Unicorn");
        t.Should().BeNull();
    }
}
