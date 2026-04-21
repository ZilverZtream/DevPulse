using System.Text;
using System.Text.RegularExpressions;

namespace DevPulse.App.UI;

public static class MarkdownRenderer
{
    public static string ToRtf(string markdown)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}{\f1 Consolas;}}\fs20 ");

        if (string.IsNullOrEmpty(markdown))
        {
            sb.Append('}');
            return sb.ToString();
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        bool inFence = false;

        foreach (var raw in lines)
        {
            if (raw.StartsWith("```"))
            {
                inFence = !inFence;
                sb.Append(inFence ? @"\f1\fs18 " : @"\f0\fs20 ");
                sb.Append(@"\par ");
                continue;
            }

            if (inFence)
            {
                sb.Append(Escape(raw));
                sb.Append(@"\par ");
                continue;
            }

            var h = Regex.Match(raw, @"^(#{1,4})\s+(.*)$");
            if (h.Success)
            {
                var level = h.Groups[1].Length;
                var size = level switch { 1 => 32, 2 => 26, 3 => 22, _ => 20 };
                sb.Append($@"\b\fs{size} ");
                sb.Append(Escape(h.Groups[2].Value));
                sb.Append(@"\b0\fs20\par ");
                continue;
            }

            var ul = Regex.Match(raw, @"^\s*[-*]\s+(.*)$");
            if (ul.Success)
            {
                sb.Append(@"\bullet\tab ");
                sb.Append(Escape(ul.Groups[1].Value));
                sb.Append(@"\par ");
                continue;
            }

            var ol = Regex.Match(raw, @"^\s*\d+\.\s+(.*)$");
            if (ol.Success)
            {
                sb.Append(Escape(ol.Value.TrimStart()));
                sb.Append(@"\par ");
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                sb.Append(@"\par ");
                continue;
            }

            sb.Append(Escape(raw));
            sb.Append(@"\par ");
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                default:
                    if (c > 127) sb.Append($@"\u{(int)c}?");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
