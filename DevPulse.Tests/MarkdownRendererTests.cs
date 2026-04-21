using DevPulse.App.UI;
using FluentAssertions;

namespace DevPulse.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void ToRtf_HeaderProducesBoldLine()
    {
        var rtf = MarkdownRenderer.ToRtf("## Heading");
        rtf.Should().Contain("\\b");
        rtf.Should().Contain("Heading");
    }

    [Fact]
    public void ToRtf_ParagraphPreservesText()
    {
        var rtf = MarkdownRenderer.ToRtf("Just a paragraph.");
        rtf.Should().Contain("Just a paragraph.");
    }

    [Fact]
    public void ToRtf_UnorderedListRendered()
    {
        var rtf = MarkdownRenderer.ToRtf("- item one\n- item two");
        rtf.Should().Contain("item one");
        rtf.Should().Contain("item two");
    }

    [Fact]
    public void ToRtf_EscapesRtfControlChars()
    {
        var rtf = MarkdownRenderer.ToRtf("a\\b{c}d");
        rtf.Should().Contain("\\\\");
        rtf.Should().Contain("\\{");
        rtf.Should().Contain("\\}");
    }

    [Fact]
    public void ToRtf_EmptyReturnsEmptyRtf()
    {
        var rtf = MarkdownRenderer.ToRtf("");
        rtf.Should().StartWith("{\\rtf1");
    }
}
