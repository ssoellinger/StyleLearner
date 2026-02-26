using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class UsingLayoutDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Using Layout";

    private int _outsideNamespaceCount;
    private int _insideNamespaceCount;
    private readonly ExampleCollector _examples = new();
    private int _globalUsingCount;
    private int _systemFirstCount;
    private int _systemNotFirstCount;
    private int _sortedCount;
    private int _unsortedCount;
    private int _fileScopedNamespaceCount;

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        // Check for file-scoped namespace
        var fileScopedNs = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScopedNs != null)
        {
            _fileScopedNamespaceCount++;
        }

        // Usings at compilation unit level
        var topLevelUsings = root.Usings.Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).ToList();
        var globalUsings = root.Usings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).ToList();

        _globalUsingCount += globalUsings.Count;

        if (topLevelUsings.Count > 0)
        {
            _outsideNamespaceCount++;
            AnalyzeUsingList(topLevelUsings);
            if (topLevelUsings.Count >= 2)
            {
                int firstLine = topLevelUsings.First().GetLocation().GetLineSpan().StartLinePosition.Line;
                int lastLine = topLevelUsings.Last().GetLocation().GetLineSpan().EndLinePosition.Line;
                _examples.TryAdd("outside", firstLine, lastLine);
            }
        }

        // Check traditional namespace declarations for internal usings
        foreach (var ns in root.Members.OfType<NamespaceDeclarationSyntax>())
        {
            if (ns.Usings.Count > 0)
            {
                _insideNamespaceCount++;
                AnalyzeUsingList(ns.Usings.ToList());
                int firstLine = ns.Usings.First().GetLocation().GetLineSpan().StartLinePosition.Line;
                int lastLine = ns.Usings.Last().GetLocation().GetLineSpan().EndLinePosition.Line;
                _examples.TryAdd("inside", firstLine, lastLine);
            }
        }
    }

    private void AnalyzeUsingList(List<UsingDirectiveSyntax> usings)
    {
        if (usings.Count == 0) return;

        // Check System-first ordering
        var names = usings.Select(u => u.Name?.ToString() ?? "").ToList();
        var firstUsing = names.FirstOrDefault() ?? "";

        bool hasSystem = names.Any(n => n.StartsWith("System"));
        if (hasSystem)
        {
            var systemUsings = names.Where(n => n.StartsWith("System")).ToList();
            var nonSystemUsings = names.Where(n => !n.StartsWith("System")).ToList();

            // Check if all System usings come before non-System
            int lastSystemIndex = -1;
            int firstNonSystemIndex = names.Count;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].StartsWith("System"))
                    lastSystemIndex = i;
                else if (firstNonSystemIndex == names.Count)
                    firstNonSystemIndex = i;
            }

            if (lastSystemIndex < firstNonSystemIndex)
                _systemFirstCount++;
            else
                _systemNotFirstCount++;

            // Check alphabetical sorting within groups
            bool systemSorted = IsSorted(systemUsings);
            bool nonSystemSorted = IsSorted(nonSystemUsings);

            if (systemSorted && nonSystemSorted)
                _sortedCount++;
            else
                _unsortedCount++;
        }
        else
        {
            if (IsSorted(names))
                _sortedCount++;
            else
                _unsortedCount++;
        }
    }

    private static bool IsSorted(List<string> items)
    {
        for (int i = 1; i < items.Count; i++)
        {
            if (string.Compare(items[i - 1], items[i], StringComparison.Ordinal) > 0)
                return false;
        }

        return true;
    }

    public DetectorResult GetResult()
    {
        var placementTotal = _outsideNamespaceCount + _insideNamespaceCount;
        var placement = _outsideNamespaceCount >= _insideNamespaceCount ? "outside" : "inside";
        var placementConfidence = placementTotal > 0
            ? (double)Math.Max(_outsideNamespaceCount, _insideNamespaceCount) / placementTotal * 100
            : 0;

        var systemTotal = _systemFirstCount + _systemNotFirstCount;
        var systemFirst = _systemFirstCount >= _systemNotFirstCount;

        var sortTotal = _sortedCount + _unsortedCount;
        var sorted = _sortedCount >= _unsortedCount;

        var usingLabels = new Dictionary<string, string>
        {
            ["outside"] = "usings outside namespace",
            ["inside"] = "usings inside namespace",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = placementTotal,
            Confidence = Math.Round(placementConfidence, 1),
            DominantPattern = $"placement: {placement}, system first: {systemFirst}, sorted: {sorted}",
            Details = new Dictionary<string, object>
            {
                ["Placement"] = placement,
                ["OutsideCount"] = _outsideNamespaceCount,
                ["InsideCount"] = _insideNamespaceCount,
                ["SystemFirst"] = systemFirst,
                ["SystemFirstCount"] = _systemFirstCount,
                ["SystemNotFirstCount"] = _systemNotFirstCount,
                ["Sorted"] = sorted,
                ["SortedCount"] = _sortedCount,
                ["UnsortedCount"] = _unsortedCount,
                ["GlobalUsingCount"] = _globalUsingCount,
                ["FileScopedNamespaceCount"] = _fileScopedNamespaceCount,
            },
            Examples = _examples.Build(placement, usingLabels),
        };
    }
}
