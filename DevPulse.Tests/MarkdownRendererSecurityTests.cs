using System.Text.RegularExpressions;
using DevPulse.App.UI;
using FluentAssertions;

namespace DevPulse.Tests;

public class MarkdownRendererSecurityTests
{
    // An RTF control word survives only if a SINGLE backslash precedes it. Since the
    // renderer escapes every '\' from input to '\\', an attacker's '\objdata' becomes
    // '\\objdata' in the output: the second '\' is just literal text. We assert this
    // semantic property by matching backslash-runs preceding control words.
    private static bool HasUnescapedControlWord(string rtf, string word)
    {
        // Look for a backslash-run of ODD length followed by the word: an odd number means
        // one literal '\' is left over to start a control word.
        var matches = Regex.Matches(rtf, @"(\\+)" + Regex.Escape(word));
        foreach (Match m in matches)
        {
            if (m.Groups[1].Length % 2 == 1) return true;
        }
        return false;
    }

    [Fact]
    public void ToRtf_LiteralObjdata_DoesNotEmitRtfControlWord()
    {
        var rtf = MarkdownRenderer.ToRtf(@"\objdata 0102");

        HasUnescapedControlWord(rtf, "objdata").Should().BeFalse();
        // And the escaped form must be present (proves we didn't simply drop the input).
        rtf.Should().Contain(@"\\objdata");
    }

    [Fact]
    public void ToRtf_LiteralRtlchBrace_DoesNotEmitGroupOpener()
    {
        var rtf = MarkdownRenderer.ToRtf(@"{\rtlch hostile}");

        HasUnescapedControlWord(rtf, "rtlch").Should().BeFalse();
        rtf.Should().Contain(@"\{");
        rtf.Should().Contain(@"\}");
        rtf.Should().Contain(@"\\rtlch");
    }

    [Fact]
    public void ToRtf_LiteralFonttbl_IsNeutralized()
    {
        var rtf = MarkdownRenderer.ToRtf(@"\fonttbl{\f99 Hostile;}");

        // The renderer's own header legitimately contains '\fonttbl{...}' once; strip it
        // before scanning the body for an attacker-supplied copy.
        var headerEnd = rtf.IndexOf(@"\fs20 ", StringComparison.Ordinal) + @"\fs20 ".Length;
        var body = rtf[headerEnd..];

        HasUnescapedControlWord(body, "fonttbl").Should().BeFalse();
        HasUnescapedControlWord(body, "f99").Should().BeFalse();
    }

    [Fact]
    public void ToRtf_LiteralBin_IsNeutralized()
    {
        var rtf = MarkdownRenderer.ToRtf(@"\bin1024 payload");

        HasUnescapedControlWord(rtf, "bin1024").Should().BeFalse();
    }

    [Fact]
    public void ToRtf_BracesEscaped()
    {
        var rtf = MarkdownRenderer.ToRtf("a{b}c");
        rtf.Should().Contain(@"\{");
        rtf.Should().Contain(@"\}");
    }

    [Fact]
    public void ToRtf_NonAsciiBecomesUnicodeEscape()
    {
        // 'e-acute' (U+00E9 = 233). Should appear as \u233?
        var input = "caf" + (char)0x00E9;
        var rtf = MarkdownRenderer.ToRtf(input);

        rtf.Should().Contain(@"\u233?");
        rtf.Should().NotContain(((char)0x00E9).ToString());
    }

    [Fact]
    public void ToRtf_HighBmpCharacter_BecomesUnicodeEscape()
    {
        // U+4E2D = 20013. Non-ASCII must be emitted as \uNNNN? and the literal codepoint
        // must not appear in the output stream.
        var input = ((char)0x4E2D).ToString();
        var rtf = MarkdownRenderer.ToRtf(input);

        rtf.Should().Contain("20013?");
        rtf.Should().NotContain(input);
    }

    [Fact]
    public void ToRtf_BellControlChar_IsStripped()
    {
        var input = "foo" + (char)0x07 + "bar";
        var rtf = MarkdownRenderer.ToRtf(input);
        rtf.Should().NotContain(((char)0x07).ToString());
        rtf.Should().Contain("foobar");
    }

    [Fact]
    public void ToRtf_NulControlChar_IsStripped()
    {
        var input = "foo" + (char)0x00 + "bar";
        var rtf = MarkdownRenderer.ToRtf(input);
        rtf.Should().NotContain(((char)0x00).ToString());
        rtf.Should().Contain("foobar");
    }

    [Fact]
    public void ToRtf_VerticalTabControlChar_IsStripped()
    {
        var input = "foo" + (char)0x0B + "bar";
        var rtf = MarkdownRenderer.ToRtf(input);
        rtf.Should().NotContain(((char)0x0B).ToString());
        rtf.Should().Contain("foobar");
    }

    [Fact]
    public void ToRtf_TabIsPreserved()
    {
        // Tab is a benign whitespace character; keep it.
        var rtf = MarkdownRenderer.ToRtf("foo\tbar");
        rtf.Should().Contain("foo\tbar");
    }

    [Fact]
    public void ToRtf_RenderedOutputContainsNoRawDangerousControlWords()
    {
        // Mash known dangerous RTF control words through the renderer; verify none survive
        // unescaped (i.e., as a single-backslash control word an RTF reader would honour).
        var hostile = string.Join("\n", new[]
        {
            @"\objdata 010203",
            @"\bin42 binary",
            @"\fonttbl{\f0 Foo;}",
            @"\pict\jpegblip",
            @"{\object\objemb}"
        });

        var rtf = MarkdownRenderer.ToRtf(hostile);

        HasUnescapedControlWord(rtf, "objdata").Should().BeFalse();
        HasUnescapedControlWord(rtf, "bin42").Should().BeFalse();
        // The renderer's own header legitimately contains '\fonttbl{...}' once; strip it
        // before scanning the body for hostile copies.
        var headerEnd = rtf.IndexOf(@"\fs20 ", StringComparison.Ordinal) + @"\fs20 ".Length;
        var body = rtf[headerEnd..];
        HasUnescapedControlWord(body, "fonttbl").Should().BeFalse();
        HasUnescapedControlWord(body, "pict").Should().BeFalse();
        HasUnescapedControlWord(body, "object").Should().BeFalse();
        HasUnescapedControlWord(body, "jpegblip").Should().BeFalse();
        HasUnescapedControlWord(body, "objemb").Should().BeFalse();
    }
}
