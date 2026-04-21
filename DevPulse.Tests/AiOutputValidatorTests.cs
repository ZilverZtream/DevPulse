using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

public class AiOutputValidatorTests
{
    private readonly AiOutputValidator _sut = new();
    private static readonly List<string> RequiredHeaders =
        ["Context summary", "Functional requirements", "Acceptance criteria",
         "Edge cases", "Test plan", "Risks and dependencies"];

    [Fact]
    public void Validate_AllHeadersPresentWithContent_IsValid()
    {
        var md = """
            ## Context summary
            Some context.
            ## Functional requirements
            A requirement.
            ## Acceptance criteria
            Given x, when y, then z.
            ## Edge cases
            Edge case 1.
            ## Test plan
            A test.
            ## Risks and dependencies
            None known.
            """;

        var result = _sut.Validate(md, RequiredHeaders);

        result.IsValid.Should().BeTrue();
        result.MissingHeaders.Should().BeEmpty();
        result.EmptySections.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingHeader_IsInvalid()
    {
        var md = "## Context summary\nSome context.\n## Test plan\nA test.";

        var result = _sut.Validate(md, RequiredHeaders);

        result.IsValid.Should().BeFalse();
        result.MissingHeaders.Should().Contain("Functional requirements");
        result.MissingHeaders.Should().Contain("Acceptance criteria");
    }

    [Fact]
    public void Validate_HeaderPresentButEmptyBody_IsInvalid()
    {
        var md = """
            ## Context summary

            ## Functional requirements
            Has content.
            """;

        var result = _sut.Validate(md, ["Context summary", "Functional requirements"]);

        result.IsValid.Should().BeFalse();
        result.EmptySections.Should().Contain("Context summary");
        result.EmptySections.Should().NotContain("Functional requirements");
    }

    [Fact]
    public void Validate_HeaderComparisonIsCaseInsensitive()
    {
        var md = "## context SUMMARY\nSome content.";

        var result = _sut.Validate(md, ["Context summary"]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NestedH3DoesNotSplitSection()
    {
        var md = """
            ## Context summary
            Intro.
            ### Subsection
            Nested content.
            ## Test plan
            A test.
            """;

        var result = _sut.Validate(md, ["Context summary", "Test plan"]);

        result.IsValid.Should().BeTrue();
        result.EmptySections.Should().BeEmpty();
    }

    [Fact]
    public void Validate_UnknownExtraHeadersAreTolerated()
    {
        var md = """
            ## Context summary
            content
            ## Bonus section
            extra
            """;

        var result = _sut.Validate(md, ["Context summary"]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhitespaceOnlyBody_TreatedAsEmpty()
    {
        var md = "## Context summary\n   \n\t\n## Test plan\ncontent";

        var result = _sut.Validate(md, ["Context summary", "Test plan"]);

        result.EmptySections.Should().Contain("Context summary");
    }
}
