using DevPulse.Core.Models;
using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

public class AiTemplateRendererTests
{
    private readonly AiTemplateRenderer _sut = new();

    private static WorkItem MakeWorkItem() => new()
    {
        Id = 42,
        Title = "Add export button",
        State = "New",
        AreaPath = @"MyProject\Feature",
        IterationPath = @"MyProject\Sprint 1",
    };

    [Fact]
    public void Render_SubstitutesAllowedTokens()
    {
        var wi = MakeWorkItem();
        const string body = "Title: {title}\nState: {state}\nArea: {areaPath}";

        var result = _sut.Render(body, wi, description: "Button exports to CSV", acceptanceCriteria: "G/W/T");

        result.Should().Contain("Title: Add export button");
        result.Should().Contain("State: New");
        result.Should().Contain(@"Area: MyProject\Feature");
    }

    [Fact]
    public void Render_UnknownTokensLeftLiteral()
    {
        var result = _sut.Render("Hello {unknown}!", MakeWorkItem(), description: "", acceptanceCriteria: "");
        result.Should().Contain("{unknown}");
    }

    [Fact]
    public void Render_MissingFieldsSubstitutedAsEmpty()
    {
        var wi = new WorkItem { Id = 1 };
        var result = _sut.Render("T:{title}|S:{state}|D:{description}", wi, description: "", acceptanceCriteria: "");
        result.Should().Be("T:|S:|D:");
    }

    [Fact]
    public void Render_SubstitutesDescriptionAndAcceptance()
    {
        var result = _sut.Render(
            "{description}\n---\n{acceptanceCriteria}",
            MakeWorkItem(),
            description: "Users need to export",
            acceptanceCriteria: "Given X, When Y, Then Z");

        result.Should().Contain("Users need to export");
        result.Should().Contain("Given X, When Y, Then Z");
    }

    [Theory]
    [InlineData("{title}")]
    [InlineData("{description}")]
    [InlineData("{areaPath}")]
    [InlineData("{iterationPath}")]
    [InlineData("{type}")]
    [InlineData("{state}")]
    [InlineData("{acceptanceCriteria}")]
    public void Render_AllowListedTokensSubstitute(string token)
    {
        var result = _sut.Render(token, MakeWorkItem(), description: "d", acceptanceCriteria: "ac");
        result.Should().NotContain("{");
    }
}
