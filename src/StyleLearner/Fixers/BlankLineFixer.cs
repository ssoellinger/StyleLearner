using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StyleLearner.Fixers;

public class BlankLineFixer : ILayoutFixer
{
    private readonly BlankLineRule _rule;

    public string Name => "Blank Lines";

    public BlankLineFixer(BlankLineRule rule)
    {
        _rule = rule;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        var source = tree.GetRoot().ToFullString();
        var lines = new List<string>(source.Split('\n'));
        int changes = 0;

        // Pass 0: Strip trailing whitespace
        changes += StripTrailingWhitespace(lines);

        // Pass 1: Collapse consecutive blank lines
        changes += CollapseConsecutiveBlanks(lines);

        // Pass 2: Fix blank after opening brace
        changes += FixBlankAfterOpenBrace(lines);

        // Pass 3: Fix blank before closing brace
        changes += FixBlankBeforeCloseBrace(lines);

        // Pass 4: Fix blank after #region
        changes += FixBlankAfterRegion(lines);

        // Pass 5: Fix blank before #endregion
        changes += FixBlankBeforeEndRegion(lines);

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

    private int CollapseConsecutiveBlanks(List<string> lines)
    {
        int changes = 0;
        int consecutiveBlanks = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            if (IsBlank(lines[i]))
            {
                consecutiveBlanks++;
                if (consecutiveBlanks > _rule.MaxConsecutiveBlankLines)
                {
                    lines.RemoveAt(i);
                    i--;
                    changes++;
                }
            }
            else
            {
                consecutiveBlanks = 0;
            }
        }

        return changes;
    }

    private int FixBlankAfterOpenBrace(List<string> lines)
    {
        int changes = 0;

        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (lines[i].Trim() != "{") continue;

            // Skip empty blocks { }
            if (i + 1 < lines.Count && lines[i + 1].Trim() == "}") continue;

            bool hasBlank = i + 1 < lines.Count && IsBlank(lines[i + 1]);

            if (_rule.BlankLineAfterOpenBrace && !hasBlank)
            {
                // Add blank line after {
                lines.Insert(i + 1, "");
                changes++;
            }
            else if (!_rule.BlankLineAfterOpenBrace && hasBlank)
            {
                // Remove blank line(s) after {
                while (i + 1 < lines.Count && IsBlank(lines[i + 1]))
                {
                    lines.RemoveAt(i + 1);
                    changes++;
                }
            }
        }

        return changes;
    }

    private int FixBlankBeforeCloseBrace(List<string> lines)
    {
        int changes = 0;

        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Trim() != "}") continue;

            // Skip empty blocks { }
            if (i - 1 >= 0 && lines[i - 1].Trim() == "{") continue;

            bool hasBlank = i - 1 >= 0 && IsBlank(lines[i - 1]);

            if (_rule.BlankLineBeforeCloseBrace && !hasBlank)
            {
                // Add blank line before }
                lines.Insert(i, "");
                i++; // skip past inserted line
                changes++;
            }
            else if (!_rule.BlankLineBeforeCloseBrace && hasBlank)
            {
                // Remove blank line(s) before }
                while (i - 1 >= 0 && IsBlank(lines[i - 1]))
                {
                    lines.RemoveAt(i - 1);
                    i--;
                    changes++;
                }
            }
        }

        return changes;
    }

    private int FixBlankAfterRegion(List<string> lines)
    {
        int changes = 0;

        for (int i = 0; i < lines.Count - 1; i++)
        {
            if (!lines[i].Trim().StartsWith("#region")) continue;

            bool hasBlank = i + 1 < lines.Count && IsBlank(lines[i + 1]);

            if (_rule.BlankLineAfterRegion && !hasBlank)
            {
                lines.Insert(i + 1, "");
                changes++;
            }
            else if (!_rule.BlankLineAfterRegion && hasBlank)
            {
                while (i + 1 < lines.Count && IsBlank(lines[i + 1]))
                {
                    lines.RemoveAt(i + 1);
                    changes++;
                }
            }
        }

        return changes;
    }

    private int FixBlankBeforeEndRegion(List<string> lines)
    {
        int changes = 0;

        for (int i = 1; i < lines.Count; i++)
        {
            if (!lines[i].Trim().StartsWith("#endregion")) continue;

            bool hasBlank = i - 1 >= 0 && IsBlank(lines[i - 1]);

            if (_rule.BlankLineBeforeEndRegion && !hasBlank)
            {
                lines.Insert(i, "");
                i++;
                changes++;
            }
            else if (!_rule.BlankLineBeforeEndRegion && hasBlank)
            {
                while (i - 1 >= 0 && IsBlank(lines[i - 1]))
                {
                    lines.RemoveAt(i - 1);
                    i--;
                    changes++;
                }
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

    private static bool IsBlank(string line)
    {
        // After Split('\n'), lines may retain \r — trim that too
        return line.Trim().Length == 0;
    }

}
