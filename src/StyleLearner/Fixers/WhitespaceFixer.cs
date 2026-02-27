using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StyleLearner.Fixers;

public class WhitespaceFixer : ILayoutFixer
{
    public string Name => "Whitespace";

    public FixerResult Fix(SyntaxTree tree)
    {
        var source = tree.GetRoot().ToFullString();
        var lines = new List<string>(source.Split('\n'));
        int changes = 0;

        changes += StripBom(lines);
        changes += NormalizeLineEndings(lines);
        changes += StripTrailingWhitespace(lines);
        changes += CollapseInternalWhitespace(lines);
        changes += EnsureFinalNewline(lines);

        if (changes == 0)
            return new FixerResult { Tree = tree, ChangesApplied = 0 };

        var newSource = string.Join('\n', lines);
        var newTree = CSharpSyntaxTree.ParseText(newSource, path: tree.FilePath);

        return new FixerResult
        {
            Tree = newTree,
            ChangesApplied = changes,
        };
    }

    private static int StripBom(List<string> lines)
    {
        if (lines.Count == 0) return 0;

        var first = lines[0];
        if (first.Length > 0 && first[0] == '\uFEFF')
        {
            lines[0] = first[1..];
            return 1;
        }

        return 0;
    }

    private static int NormalizeLineEndings(List<string> lines)
    {
        // Detect dominant ending from \r presence
        int crCount = 0;
        int noCrCount = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].EndsWith('\r'))
                crCount++;
            else
                noCrCount++;
        }

        // Normalize to LF (strip all \r) — our pipeline uses Split('\n')
        // so \r at the end of lines means the file had \r\n
        int changes = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
                changes++;
            }
        }

        return changes;
    }

    private static int StripTrailingWhitespace(List<string> lines)
    {
        int changes = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Preserve \r from Split('\n')
            bool hasCr = line.EndsWith('\r');
            var content = hasCr ? line[..^1] : line;

            if (content.Length > 0 && content[^1] is ' ' or '\t')
            {
                var trimmed = content.TrimEnd(' ', '\t');
                lines[i] = hasCr ? trimmed + "\r" : trimmed;
                changes++;
            }
        }

        return changes;
    }

    private static int CollapseInternalWhitespace(List<string> lines)
    {
        int changes = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            bool hasCr = line.EndsWith('\r');
            var content = hasCr ? line[..^1] : line;

            // Find end of leading whitespace (preserve indentation)
            int leadingEnd = 0;
            while (leadingEnd < content.Length && content[leadingEnd] is ' ' or '\t')
                leadingEnd++;

            if (leadingEnd >= content.Length) continue;

            var rest = content[leadingEnd..];
            if (!rest.Contains("  ")) continue; // fast path: no double spaces

            var collapsed = CollapseSpacesOutsideStrings(rest);
            if (collapsed != rest)
            {
                lines[i] = hasCr
                    ? content[..leadingEnd] + collapsed + "\r"
                    : content[..leadingEnd] + collapsed;
                changes++;
            }
        }

        return changes;
    }

    private static int EnsureFinalNewline(List<string> lines)
    {
        if (lines.Count == 0)
        {
            lines.Add("");
            return 1;
        }

        // After Split('\n'), a file ending with \n produces an empty last element.
        // If the last element is non-empty, file doesn't end with a newline.
        if (lines[^1].Trim().Length != 0)
        {
            lines.Add("");
            return 1;
        }

        return 0;
    }

    private static string CollapseSpacesOutsideStrings(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool inString = false;
        bool inVerbatim = false;
        bool inChar = false;
        bool escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            // Inside char literal
            if (inChar)
            {
                sb.Append(c);
                if (c == '\\') escaped = true;
                else if (c == '\'') inChar = false;
                continue;
            }

            // Inside verbatim string (@"..." or @$"..." or $@"...")
            if (inVerbatim)
            {
                sb.Append(c);
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // skip escaped ""
                    }
                    else
                    {
                        inVerbatim = false;
                    }
                }
                continue;
            }

            // Inside regular/interpolated string
            if (inString)
            {
                sb.Append(c);
                if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            // Not inside any literal — check for string/char starts
            if (c == '"')
            {
                // Check for verbatim: @" or $@" or @$"
                bool isVerbatim = (i > 0 && text[i - 1] == '@');
                if (isVerbatim)
                    inVerbatim = true;
                else
                    inString = true;
                sb.Append(c);
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                sb.Append(c);
                continue;
            }

            // Line comment — rest of line is comment, still collapse spaces
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                sb.Append(text[i..]);
                break;
            }

            // Collapse multiple spaces to one
            if (c == ' ')
            {
                sb.Append(' ');
                while (i + 1 < text.Length && text[i + 1] == ' ')
                    i++;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
