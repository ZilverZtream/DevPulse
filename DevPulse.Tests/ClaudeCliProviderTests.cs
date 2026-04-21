using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class ClaudeCliProviderTests
{
    private static string EchoFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FakeCliEcho.cmd");
    private static string FailFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FakeCliFail.cmd");

    [Fact]
    public void Kind_IsCli_PolicyIsLocal()
    {
        var sut = new ClaudeCliProvider(EchoFixture);
        sut.Kind.Should().Be(AiProviderKind.Cli);
        sut.DataPolicy.Should().Be(AiDataPolicy.Local);
        sut.Id.Should().Be("claude-cli");
    }

    [Fact]
    public async Task HealthCheck_FixtureExists_Ok()
    {
        var sut = new ClaudeCliProvider(EchoFixture);
        (await sut.HealthCheckAsync()).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_PathMissing_NotOk()
    {
        var sut = new ClaudeCliProvider(@"C:\does\not\exist\claude.exe");
        (await sut.HealthCheckAsync()).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_EchoFixtureReturnsMarkdown()
    {
        var sut = new ClaudeCliProvider(EchoFixture);
        var result = await sut.GenerateAsync(
            new AiGenerateRequest("anything", "model", TimeSpan.FromSeconds(10)));
        result.Markdown.Should().Contain("## Context summary");
        result.Markdown.Should().Contain("## Test plan");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_FailingFixtureThrows()
    {
        var sut = new ClaudeCliProvider(FailFixture);
        var act = () => sut.GenerateAsync(new AiGenerateRequest("x", "m", TimeSpan.FromSeconds(5)));
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("exit code 2");
    }
}
