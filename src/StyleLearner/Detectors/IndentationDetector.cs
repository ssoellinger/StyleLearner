using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StyleLearner.Detectors;

public class IndentationDetector : IStyleDetector
{
    public string Name => "Indentation";

    private int _tabCount;
    private int _spaceCount;
    private readonly Dictionary<int, int> _indentWidths = new();
    private readonly Dictionary<int, int> _indentDeltas = new();
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        var text = tree.GetText();
        int previousIndent = 0;

        foreach (var line in text.Lines)
        {
            var lineText = line.ToString();
            if (lineText.Trim().Length == 0) continue; // skip blank lines

            // Count leading whitespace
            int spaces = 0;
            bool hasTabs = false;
            foreach (char c in lineText)
            {
                if (c == ' ') spaces++;
                else if (c == '\t') { hasTabs = true; break; }
                else break;
            }

            if (hasTabs)
            {
                _tabCount++;
                _examples.TryAdd("tabs", line.LineNumber, line.LineNumber);
            }
            else if (spaces > 0)
            {
                _spaceCount++;
                if (spaces == 4)
                    _examples.TryAdd("spaces", line.LineNumber, line.LineNumber, maxPerCategory: 1);

                // Record the absolute indent width
                if (spaces <= 64)
                {
                    _indentWidths.TryGetValue(spaces, out int wc);
                    _indentWidths[spaces] = wc + 1;
                }

                // Record the delta when indentation increases (the indent unit)
                int delta = spaces - previousIndent;
                if (delta > 0 && delta <= 16)
                {
                    _indentDeltas.TryGetValue(delta, out int dc);
                    _indentDeltas[delta] = dc + 1;
                }
            }

            previousIndent = hasTabs ? -1 : spaces; // reset on tabs
        }
    }

    public DetectorResult GetResult()
    {
        var total = _tabCount + _spaceCount;
        var style = _tabCount > _spaceCount ? "tabs" : "spaces";
        var styleConfidence = total > 0
            ? (double)Math.Max(_tabCount, _spaceCount) / total * 100
            : 0;

        // Detect indent size from deltas (indent increases)
        var (indentSize, sizeConfidence) = DetectIndentSize();

        // Combined confidence: min of both
        double confidence = Math.Min(styleConfidence, sizeConfidence);

        var labels = new Dictionary<string, string>
        {
            ["spaces"] = $"spaces (indent {indentSize})",
            ["tabs"] = "tabs",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = $"{style}, size {indentSize}",
            Details = new Dictionary<string, object>
            {
                ["Style"] = style,
                ["StyleConfidence"] = $"{styleConfidence:F1}%",
                ["Size"] = indentSize,
                ["SizeConfidence"] = $"{sizeConfidence:F1}%",
                ["TabCount"] = _tabCount,
                ["SpaceCount"] = _spaceCount,
                ["IndentDeltas"] = FormatDistribution(_indentDeltas),
                ["TopIndentWidths"] = FormatDistribution(
                    _indentWidths.OrderByDescending(kv => kv.Value).Take(8)
                        .ToDictionary(kv => kv.Key, kv => kv.Value)),
            },
            Examples = _examples.Build(style == "tabs" ? "tabs" : "spaces", labels),
        };
    }

    private (int Size, double Confidence) DetectIndentSize()
    {
        if (_indentDeltas.Count == 0)
            return (4, 0); // no data

        int totalDeltas = _indentDeltas.Values.Sum();

        // Step 1: The most common delta is the primary indent unit.
        // In a 4-space codebase, delta=4 will massively dominate.
        // In a 2-space codebase, delta=2 will dominate.
        var candidates = new[] { 2, 3, 4, 8 };
        int mostCommonDelta = _indentDeltas.OrderByDescending(kv => kv.Value).First().Key;

        // Snap to the nearest candidate
        int indentSize = candidates.OrderBy(c => Math.Abs(c - mostCommonDelta)).First();
        int exactCount = _indentDeltas.GetValueOrDefault(indentSize, 0);

        // Step 2: Validate — what % of all deltas are multiples of this size?
        int explained = 0;
        foreach (var (delta, count) in _indentDeltas)
        {
            if (delta % indentSize == 0)
                explained += count;
        }

        double explainedPct = (double)explained / totalDeltas * 100;

        // Step 3: Also check the exact match rate — how dominant is this specific delta?
        double exactPct = (double)exactCount / totalDeltas * 100;

        // Confidence = how well this indent size explains all observed deltas
        // Use the "explained by multiples" metric — it handles multi-level jumps
        // (e.g., delta=8 is explained by indent-4, delta=12 is 3 indent levels)
        double confidence = explainedPct;

        return (indentSize, Math.Round(confidence, 1));
    }

    private static string FormatDistribution(Dictionary<int, int> dict)
    {
        if (dict.Count == 0) return "{}";
        var sorted = dict.OrderBy(kv => kv.Key);
        return string.Join(", ", sorted.Select(kv => $"{kv.Key}:{kv.Value}"));
    }
}
