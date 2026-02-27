using Microsoft.CodeAnalysis;

namespace StyleLearner.Detectors;

public class BlankLineDetector : IStyleDetector
{
    public string Name => "Blank Lines";

    private int _consecutiveBlankViolations;
    private int _singleBlankLineCount;

    private int _blankAfterBraceCount;
    private int _noBlankAfterBraceCount;

    private int _blankBeforeCloseBraceCount;
    private int _noBlankBeforeCloseBraceCount;

    private int _blankAfterRegionCount;
    private int _noBlankAfterRegionCount;

    private int _blankBeforeEndRegionCount;
    private int _noBlankBeforeEndRegionCount;

    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        var text = tree.GetText();
        var lines = text.Lines;

        int consecutiveBlanks = 0;
        int blankRunStart = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var lineText = lines[i].ToString();
            bool isBlank = lineText.Trim().Length == 0;

            if (isBlank)
            {
                if (consecutiveBlanks == 0)
                    blankRunStart = i;
                consecutiveBlanks++;
                continue;
            }

            // Non-blank line reached — process any accumulated blank run
            if (consecutiveBlanks > 0)
            {
                // Consecutive blank line check
                if (consecutiveBlanks > 1)
                {
                    _consecutiveBlankViolations++;
                    _examples.TryAdd("consecutive_violation", blankRunStart, i, maxPerCategory: 2);
                }
                else
                {
                    _singleBlankLineCount++;
                }

                // Check line before the blank run
                if (blankRunStart > 0)
                {
                    var prevLine = lines[blankRunStart - 1].ToString().Trim();

                    // Blank after opening brace
                    if (prevLine == "{")
                    {
                        _blankAfterBraceCount++;
                        _examples.TryAdd("blank_after_brace", blankRunStart - 1, i, maxPerCategory: 2);
                    }

                    // Blank after #region
                    if (prevLine.StartsWith("#region"))
                    {
                        _blankAfterRegionCount++;
                    }
                }

                // Check current line (first non-blank after the run)
                var currentTrimmed = lineText.Trim();

                // Blank before closing brace
                if (currentTrimmed == "}")
                {
                    _blankBeforeCloseBraceCount++;
                    _examples.TryAdd("blank_before_close_brace", blankRunStart, i, maxPerCategory: 2);
                }

                // Blank before #endregion
                if (currentTrimmed.StartsWith("#endregion"))
                {
                    _blankBeforeEndRegionCount++;
                }
            }
            else
            {
                // No blank lines before this line — check "no blank" cases
                if (i > 0)
                {
                    var prevLine = lines[i - 1].ToString().Trim();
                    var currentTrimmed = lineText.Trim();

                    // No blank after opening brace (prev is {, current is not blank, not })
                    if (prevLine == "{" && currentTrimmed != "}")
                    {
                        _noBlankAfterBraceCount++;
                    }

                    // No blank before closing brace (current is }, prev is not blank, not {)
                    if (currentTrimmed == "}" && prevLine != "{")
                    {
                        _noBlankBeforeCloseBraceCount++;
                    }

                    // No blank after #region
                    if (prevLine.StartsWith("#region"))
                    {
                        _noBlankAfterRegionCount++;
                    }

                    // No blank before #endregion
                    if (currentTrimmed.StartsWith("#endregion"))
                    {
                        _noBlankBeforeEndRegionCount++;
                    }
                }
            }

            consecutiveBlanks = 0;
        }
    }

    public DetectorResult GetResult()
    {
        // Consecutive blank lines confidence
        var totalBlankRuns = _consecutiveBlankViolations + _singleBlankLineCount;
        double consecutiveConfidence = totalBlankRuns > 0
            ? (double)_singleBlankLineCount / totalBlankRuns * 100
            : 100;

        // Brace blank line confidence
        var totalAfterBrace = _blankAfterBraceCount + _noBlankAfterBraceCount;
        double afterBraceConfidence = totalAfterBrace > 0
            ? (double)Math.Max(_blankAfterBraceCount, _noBlankAfterBraceCount) / totalAfterBrace * 100
            : 100;
        bool blankAfterBrace = _blankAfterBraceCount > _noBlankAfterBraceCount;

        var totalBeforeCloseBrace = _blankBeforeCloseBraceCount + _noBlankBeforeCloseBraceCount;
        double beforeCloseBraceConfidence = totalBeforeCloseBrace > 0
            ? (double)Math.Max(_blankBeforeCloseBraceCount, _noBlankBeforeCloseBraceCount) / totalBeforeCloseBrace * 100
            : 100;
        bool blankBeforeCloseBrace = _blankBeforeCloseBraceCount > _noBlankBeforeCloseBraceCount;

        // Region blank line confidence
        var totalAfterRegion = _blankAfterRegionCount + _noBlankAfterRegionCount;
        double afterRegionConfidence = totalAfterRegion > 0
            ? (double)Math.Max(_blankAfterRegionCount, _noBlankAfterRegionCount) / totalAfterRegion * 100
            : 100;
        bool blankAfterRegion = _blankAfterRegionCount > _noBlankAfterRegionCount;

        var totalBeforeEndRegion = _blankBeforeEndRegionCount + _noBlankBeforeEndRegionCount;
        double beforeEndRegionConfidence = totalBeforeEndRegion > 0
            ? (double)Math.Max(_blankBeforeEndRegionCount, _noBlankBeforeEndRegionCount) / totalBeforeEndRegion * 100
            : 100;
        bool blankBeforeEndRegion = _blankBeforeEndRegionCount > _noBlankBeforeEndRegionCount;

        // Overall confidence = minimum of all sub-rule confidences
        double confidence = new[]
        {
            consecutiveConfidence,
            afterBraceConfidence,
            beforeCloseBraceConfidence,
            afterRegionConfidence,
            beforeEndRegionConfidence,
        }.Min();

        var sampleCount = totalBlankRuns + totalAfterBrace + totalBeforeCloseBrace
                          + totalAfterRegion + totalBeforeEndRegion;

        var patternParts = new List<string> { $"max 1 consecutive" };
        patternParts.Add(blankAfterBrace ? "blank after {" : "no blank after {");
        patternParts.Add(blankBeforeCloseBrace ? "blank before }" : "no blank before }");
        if (totalAfterRegion > 0)
            patternParts.Add(blankAfterRegion ? "blank after #region" : "no blank after #region");
        if (totalBeforeEndRegion > 0)
            patternParts.Add(blankBeforeEndRegion ? "blank before #endregion" : "no blank before #endregion");

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = sampleCount,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = string.Join(", ", patternParts),
            Details = new Dictionary<string, object>
            {
                ["BlankAfterOpenBrace"] = blankAfterBrace,
                ["BlankBeforeCloseBrace"] = blankBeforeCloseBrace,
                ["BlankAfterRegion"] = blankAfterRegion,
                ["BlankBeforeEndRegion"] = blankBeforeEndRegion,
                ["ConsecutiveBlankViolations"] = _consecutiveBlankViolations,
                ["SingleBlankLineCount"] = _singleBlankLineCount,
                ["BlankAfterBraceCount"] = _blankAfterBraceCount,
                ["NoBlankAfterBraceCount"] = _noBlankAfterBraceCount,
                ["BlankBeforeCloseBraceCount"] = _blankBeforeCloseBraceCount,
                ["NoBlankBeforeCloseBraceCount"] = _noBlankBeforeCloseBraceCount,
                ["BlankAfterRegionCount"] = _blankAfterRegionCount,
                ["NoBlankAfterRegionCount"] = _noBlankAfterRegionCount,
                ["BlankBeforeEndRegionCount"] = _blankBeforeEndRegionCount,
                ["NoBlankBeforeEndRegionCount"] = _noBlankBeforeEndRegionCount,
                ["AfterBraceConfidence"] = $"{afterBraceConfidence:F1}%",
                ["BeforeCloseBraceConfidence"] = $"{beforeCloseBraceConfidence:F1}%",
                ["AfterRegionConfidence"] = $"{afterRegionConfidence:F1}%",
                ["BeforeEndRegionConfidence"] = $"{beforeEndRegionConfidence:F1}%",
                ["ConsecutiveConfidence"] = $"{consecutiveConfidence:F1}%",
            },
            Examples = _examples.BuildMulti(
                new HashSet<string> { "single_blank" },
                new Dictionary<string, string>
                {
                    ["consecutive_violation"] = "2+ consecutive blank lines",
                    ["blank_after_brace"] = "blank line after {",
                    ["blank_before_close_brace"] = "blank line before }",
                }),
        };
    }
}
