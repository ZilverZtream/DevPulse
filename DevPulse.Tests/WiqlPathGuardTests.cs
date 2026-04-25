using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

public class WiqlPathGuardTests
{
    [Fact]
    public void ValidatePath_ProjectNameWithSingleQuote_Throws()
    {
        // Project names flow into WIQL queries; a stray single quote would close the literal
        // and let the rest of the project string be reinterpreted as WIQL syntax.
        var act = () => WiqlPathGuard.ValidatePath("My' OR 1=1--", "Project");
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Project");
    }

    [Fact]
    public void ValidatePath_ProjectNameWithSemicolon_Throws()
    {
        var act = () => WiqlPathGuard.ValidatePath("Foo;DROP", "Project");
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Project");
    }

    [Fact]
    public void ValidatePath_ProjectNameWithControlChar_Throws()
    {
        var act = () => WiqlPathGuard.ValidatePath("Foo" + (char)0x07 + "Bar", "Project");
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Project");
    }

    [Fact]
    public void ValidatePath_LegitimateProjectName_ReturnsInput()
    {
        const string input = "MyProject (Platform)";
        WiqlPathGuard.ValidatePath(input, "Project").Should().Be(input);
    }
}
