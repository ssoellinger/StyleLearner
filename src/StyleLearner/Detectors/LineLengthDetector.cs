using Microsoft.CodeAnalysis;

namespace StyleLearner.Detectors;

public class LineLengthDetector : IStyleDetector
{
    public string Name => "Line Length";

    private readonly List<int> _lineLengths = new();
    private readonly ExampleCollector _examples = new();

    // Track the longest lines for examples
    private int _longestLineLength;
    private int _longestLineNumber;
    private int _secondLongestLength;
    private int _secondLongestLineNumber;

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        var text = tree.GetText();
        foreach (var line in text.Lines)
        {
            var lineText = line.ToString();
            // Skip empty lines and lines that are just whitespace
            if (lineText.Trim().Length > 0)
            {
                int len = lineText.TrimEnd().Length;
                _lineLengths.Add(len);

                if (len > _longestLineLength)
                {
                    _secondLongestLength = _longestLineLength;
                    _secondLongestLineNumber = _longestLineNumber;
                    _longestLineLength = len;
                    _longestLineNumber = line.LineNumber;
                    _examples.TryAdd("longest", line.LineNumber, line.LineNumber, maxPerCategory: 1);
                }
            }
        }
    }

    public DetectorResult GetResult()
    {
        if (_lineLengths.Count == 0)
        {
            return new DetectorResult
            {
                DetectorName = Name,
                SampleCount = 0,
                Confidence = 0,
                DominantPattern = "no data",
                Details = new Dictionary<string, object>(),
            };
        }

        _lineLengths.Sort();
        var max = _lineLengths[^1];
        var avg = _lineLengths.Average();
        var p90 = Percentile(90);
        var p95 = Percentile(95);
        var p99 = Percentile(99);

        var labels = new Dictionary<string, string>
        {
            ["longest"] = $"longest line ({max} chars)",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = _lineLengths.Count,
            Confidence = 100,
            DominantPattern = $"max: {max}, avg: {avg:F0}, P90: {p90}, P95: {p95}, P99: {p99}",
            Details = new Dictionary<string, object>
            {
                ["Max"] = max,
                ["Avg"] = (int)Math.Round(avg),
                ["P90"] = p90,
                ["P95"] = p95,
                ["P99"] = p99,
            },
            Examples = _examples.BuildMulti(new HashSet<string>(), labels),
        };
    }

    private int Percentile(int percentile)
    {
        int index = (int)Math.Ceiling(percentile / 100.0 * _lineLengths.Count) - 1;
        return _lineLengths[Math.Max(0, Math.Min(index, _lineLengths.Count - 1))];
    }
}
