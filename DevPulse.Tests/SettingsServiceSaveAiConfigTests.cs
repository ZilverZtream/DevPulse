using DevPulse.App.Services;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SettingsServiceSaveAiConfigTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;
    private SettingsService _settings = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"saveaiconfig-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
        _settings = new SettingsService(_store);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task SaveAiConfigAsync_WritesBothProfilesAndTemplates()
    {
        var profiles = new List<AiProviderProfile>
        {
            new() { ProviderId = "claude-cli", Enabled = true, DefaultModel = "", ExecutablePath = @"C:\claude.exe" },
            new() { ProviderId = "openrouter", Enabled = true, DefaultModel = "anthropic/claude-3.5-sonnet", ExecutablePath = "" }
        };
        var templates = new List<AiTemplate>
        {
            new() { Id = "t1", Name = "Test", AppliesTo = ["Bug"], RequiredHeaders = ["H1"], PromptBody = "body" }
        };

        await _settings.SaveAiConfigAsync(profiles, templates);

        var loadedProfiles = await _settings.GetAiProviderProfilesAsync();
        loadedProfiles.Should().HaveCount(2);
        loadedProfiles.Select(p => p.ProviderId).Should().Contain(new[] { "claude-cli", "openrouter" });

        var loadedTemplates = await new SettingsAiTemplateStore(_settings).GetTemplatesAsync();
        loadedTemplates.Should().ContainSingle();
        loadedTemplates[0].Id.Should().Be("t1");
    }
}
