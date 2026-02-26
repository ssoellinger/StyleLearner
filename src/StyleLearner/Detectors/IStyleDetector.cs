using Microsoft.CodeAnalysis;

namespace StyleLearner.Detectors;

public interface IStyleDetector
{
    string Name { get; }
    void Analyze(SyntaxTree tree, string filePath);
    DetectorResult GetResult();
}

public class DetectorResult
{
    public string DetectorName { get; init; } = "";
    public int SampleCount { get; init; }
    public double Confidence { get; init; }
    public string DominantPattern { get; init; } = "";
    public Dictionary<string, object> Details { get; init; } = new();
    public List<StyleExample> Examples { get; init; } = new();
}

public record StyleExample
{
    public string FilePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string Snippet { get; init; } = "";
    public bool IsConforming { get; init; }
    public string Label { get; init; } = "";
}

public class ExampleCollector
{
    private readonly List<(string FilePath, int LineNumber, string Snippet, string Category)> _entries = new();
    private SyntaxTree? _tree;
    private string? _filePath;

    public void SetContext(SyntaxTree tree, string filePath)
    {
        _tree = tree;
        _filePath = filePath;
    }

    public bool HasEnough(string category, int max = 2)
        => _entries.Count(e => e.Category == category) >= max;

    public void TryAdd(string category, SyntaxNode node, int contextBefore = 0, int contextAfter = 0, int maxPerCategory = 2)
    {
        if (HasEnough(category, maxPerCategory)) return;
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line;
        _entries.Add((_filePath!, line + 1, SnippetHelper.Extract(_tree!, node, contextBefore, contextAfter), category));
    }

    public void TryAdd(string category, int startLine, int endLine, int maxPerCategory = 2)
    {
        if (HasEnough(category, maxPerCategory)) return;
        _entries.Add((_filePath!, startLine + 1, SnippetHelper.Extract(_tree!, startLine, endLine), category));
    }

    public List<StyleExample> Build(string dominantCategory, Dictionary<string, string>? labels = null)
    {
        return _entries.Select(e => new StyleExample
        {
            FilePath = e.FilePath,
            LineNumber = e.LineNumber,
            Snippet = e.Snippet,
            IsConforming = e.Category == dominantCategory,
            Label = labels?.GetValueOrDefault(e.Category, e.Category) ?? e.Category,
        }).ToList();
    }

    public List<StyleExample> BuildMulti(HashSet<string> conformingCategories, Dictionary<string, string>? labels = null)
    {
        return _entries.Select(e => new StyleExample
        {
            FilePath = e.FilePath,
            LineNumber = e.LineNumber,
            Snippet = e.Snippet,
            IsConforming = conformingCategories.Contains(e.Category),
            Label = labels?.GetValueOrDefault(e.Category, e.Category) ?? e.Category,
        }).ToList();
    }
}

public static class SnippetHelper
{
    public static string Extract(SyntaxTree tree, int startLine, int endLine, int maxLines = 6)
    {
        var text = tree.GetText();
        startLine = Math.Max(0, startLine);
        endLine = Math.Min(text.Lines.Count - 1, endLine);

        var lines = new List<string>();
        for (int i = startLine; i <= endLine && lines.Count < maxLines; i++)
            lines.Add(text.Lines[i].ToString().TrimEnd());

        if (endLine - startLine + 1 > maxLines)
            lines.Add("...");

        int minIndent = lines
            .Where(l => l.Trim().Length > 0 && l != "...")
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join("\n",
            lines.Select(l => l == "..." ? l : (l.Trim().Length > 0 && l.Length > minIndent ? l[minIndent..] : l.TrimStart())));
    }

    public static string Extract(SyntaxTree tree, SyntaxNode node, int contextBefore = 0, int contextAfter = 0, int maxLines = 6)
    {
        var span = node.GetLocation().GetLineSpan();
        return Extract(tree, span.StartLinePosition.Line - contextBefore, span.EndLinePosition.Line + contextAfter, maxLines);
    }
}
