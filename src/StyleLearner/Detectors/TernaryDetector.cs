using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class TernaryDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Ternary";

    private readonly List<TernarySample> _samples = new();
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        // Skip nested ternaries (analyzed as part of the outer one)
        if (node.Parent is ConditionalExpressionSyntax)
        {
            base.VisitConditionalExpression(node);
            return;
        }

        var conditionEndLine = node.Condition.GetLocation().GetLineSpan().EndLinePosition.Line;
        var questionLine = node.QuestionToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var colonLine = node.ColonToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var whenTrueEndLine = node.WhenTrue.GetLocation().GetLineSpan().EndLinePosition.Line;

        bool questionOnNewLine = questionLine > conditionEndLine;
        bool colonOnNewLine = colonLine > whenTrueEndLine;
        bool isSingleLine = !questionOnNewLine && !colonOnNewLine;

        // Measure the hypothetical single-line length of just the ternary expression
        int expressionLength = MeasureUnwrappedLength(node);

        // Measure the starting column — how far indented is this ternary
        int startColumn = node.GetLocation().GetLineSpan().StartLinePosition.Character;

        // Hypothetical total line length if this were kept on one line
        int hypotheticalLineLength = startColumn + expressionLength;

        // Also account for any assignment / declaration prefix on the same line.
        // Walk up to find the containing statement and measure from its start to the ternary start.
        int statementLineLength = EstimateFullStatementLineLength(node, expressionLength);

        // Detect the multi-line alignment pattern
        var pattern = MultiLinePattern.None;
        if (!isSingleLine)
        {
            pattern = DetectMultiLinePattern(node, questionOnNewLine, colonOnNewLine);
        }

        // Detect context
        var context = ClassifyContext(node);

        _samples.Add(new TernarySample
        {
            IsSingleLine = isSingleLine,
            QuestionOnNewLine = questionOnNewLine,
            ColonOnNewLine = colonOnNewLine,
            ExpressionLength = expressionLength,
            StartColumn = startColumn,
            HypotheticalLineLength = hypotheticalLineLength,
            StatementLineLength = statementLineLength,
            MultiLinePattern = pattern,
            Context = context,
            HasNestedTernary = node.WhenTrue is ConditionalExpressionSyntax ||
                               node.WhenFalse is ConditionalExpressionSyntax,
        });

        string exCat = isSingleLine
            ? "single_line"
            : pattern == MultiLinePattern.AlignedOperators
                ? "multi_aligned"
                : "multi_other";
        _examples.TryAdd(exCat, node, contextBefore: 1);

        base.VisitConditionalExpression(node);
    }

    private static int MeasureUnwrappedLength(ConditionalExpressionSyntax node)
    {
        var conditionText = CollapseWhitespace(node.Condition.ToString());
        var whenTrueText = CollapseWhitespace(node.WhenTrue.ToString());
        var whenFalseText = CollapseWhitespace(node.WhenFalse.ToString());
        // "condition ? whenTrue : whenFalse"
        return conditionText.Length + 3 + whenTrueText.Length + 3 + whenFalseText.Length;
    }

    private static int EstimateFullStatementLineLength(ConditionalExpressionSyntax node, int exprLength)
    {
        // Find the containing statement or member to get the full line context
        SyntaxNode? container = node.Parent;
        while (container != null &&
               container is not StatementSyntax &&
               container is not MemberDeclarationSyntax &&
               container is not AssignmentExpressionSyntax)
        {
            container = container.Parent;
        }

        if (container == null) return exprLength;

        // Get the text from the start of the containing line up to where the ternary starts
        var containerStart = container.GetLocation().GetLineSpan().StartLinePosition;
        var ternaryStart = node.GetLocation().GetLineSpan().StartLinePosition;

        if (containerStart.Line == ternaryStart.Line)
        {
            // Same line: prefix is from container start to ternary start
            int prefixLength = ternaryStart.Character - containerStart.Character;
            // Add the indentation of the container itself
            return containerStart.Character + prefixLength + exprLength;
        }

        // Different lines: the ternary starts on its own line, use its column + expression
        return ternaryStart.Character + exprLength;
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inWhitespace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(c);
                inWhitespace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static MultiLinePattern DetectMultiLinePattern(
        ConditionalExpressionSyntax node,
        bool questionOnNewLine,
        bool colonOnNewLine)
    {
        if (questionOnNewLine && colonOnNewLine)
        {
            var questionCol = node.QuestionToken.GetLocation().GetLineSpan().StartLinePosition.Character;
            var colonCol = node.ColonToken.GetLocation().GetLineSpan().StartLinePosition.Character;

            return questionCol == colonCol
                ? MultiLinePattern.AlignedOperators
                : MultiLinePattern.BothOnNewLine;
        }

        if (questionOnNewLine) return MultiLinePattern.QuestionOnNewLine;
        if (colonOnNewLine) return MultiLinePattern.ColonOnNewLine;
        return MultiLinePattern.None;
    }

    private static TernaryContext ClassifyContext(ConditionalExpressionSyntax node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is InitializerExpressionSyntax init &&
                (init.IsKind(SyntaxKind.ObjectInitializerExpression) ||
                 init.IsKind(SyntaxKind.CollectionInitializerExpression)))
            {
                return TernaryContext.Initializer;
            }

            if (ancestor is ArgumentSyntax)
                return TernaryContext.Argument;

            if (ancestor is ReturnStatementSyntax)
                return TernaryContext.Return;

            if (ancestor is InterpolationSyntax)
                return TernaryContext.StringInterpolation;

            // Stop walking at the statement level
            if (ancestor is StatementSyntax || ancestor is MemberDeclarationSyntax)
                break;
        }

        return TernaryContext.Assignment;
    }

    public DetectorResult GetResult()
    {
        if (_samples.Count == 0)
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

        var singleLine = _samples.Where(s => s.IsSingleLine).ToList();
        var multiLine = _samples.Where(s => !s.IsSingleLine).ToList();

        // Try multiple predictors and pick the best one
        var bestResult = FindBestPredictor(singleLine, multiLine);

        // Determine dominant multi-line pattern
        var multiLinePatterns = multiLine
            .GroupBy(s => s.MultiLinePattern)
            .OrderByDescending(g => g.Count())
            .ToList();
        var dominantMultiLinePattern = multiLinePatterns.FirstOrDefault()?.Key ?? MultiLinePattern.None;
        int dominantPatternCount = multiLinePatterns.FirstOrDefault()?.Count() ?? 0;
        double multiLinePatternConfidence = multiLine.Count > 0
            ? (double)dominantPatternCount / multiLine.Count * 100
            : 0;

        // Context breakdown
        var contextGroups = _samples
            .GroupBy(s => s.Context)
            .ToDictionary(g => g.Key, g => new { Total = g.Count(), SingleLine = g.Count(s => s.IsSingleLine) });

        // Build pattern description
        string multiDesc = dominantMultiLinePattern switch
        {
            MultiLinePattern.AlignedOperators => "? and : aligned on new lines",
            MultiLinePattern.BothOnNewLine => "? and : on new lines",
            MultiLinePattern.QuestionOnNewLine => "? on new line, : inline",
            MultiLinePattern.ColonOnNewLine => "? inline, : on new line",
            _ => "mixed",
        };

        string patternDesc;
        if (bestResult.Accuracy < 75)
        {
            patternDesc = singleLine.Count >= multiLine.Count
                ? "predominantly single-line"
                : "predominantly multi-line";
        }
        else if (bestResult.CompositeRules != null)
        {
            patternDesc = string.Join("; ", bestResult.CompositeRules) + $" — multi-line uses {multiDesc}";
        }
        else
        {
            patternDesc = $"single-line when {bestResult.PredictorName} <={bestResult.Threshold}, " +
                $"multi-line ({multiDesc}) when longer";
        }

        var details = new Dictionary<string, object>
        {
            ["SingleLineCount"] = singleLine.Count,
            ["MultiLineCount"] = multiLine.Count,
            ["BestPredictor"] = bestResult.PredictorName,
            ["Threshold"] = bestResult.Threshold,
            ["ThresholdAccuracy"] = $"{bestResult.Accuracy:F1}%",
            ["SingleLineAvgLength"] = singleLine.Count > 0 ? (int)singleLine.Average(s => s.ExpressionLength) : 0,
            ["SingleLineMaxLength"] = singleLine.Count > 0 ? singleLine.Max(s => s.ExpressionLength) : 0,
            ["MultiLineAvgLength"] = multiLine.Count > 0 ? (int)multiLine.Average(s => s.ExpressionLength) : 0,
            ["MultiLineMinLength"] = multiLine.Count > 0 ? multiLine.Min(s => s.ExpressionLength) : 0,
            ["DominantMultiLinePattern"] = dominantMultiLinePattern.ToString(),
            ["MultiLinePatternConfidence"] = $"{multiLinePatternConfidence:F1}%",
            ["NestedTernaryCount"] = _samples.Count(s => s.HasNestedTernary),
        };

        // Add context breakdown
        foreach (var (ctx, stats) in contextGroups)
        {
            details[$"Context_{ctx}"] = $"{stats.SingleLine}/{stats.Total} single-line";
        }

        // Add all predictor results for comparison
        foreach (var p in bestResult.AllPredictors)
        {
            details[$"Predictor_{p.Name}"] = $"threshold={p.Threshold}, accuracy={p.Accuracy:F1}%";
            if (p.CompositeRules != null)
            {
                for (int i = 0; i < p.CompositeRules.Count; i++)
                    details[$"  Rule_{i + 1}"] = p.CompositeRules[i];
            }
        }

        var ternaryLabels = new Dictionary<string, string>
        {
            ["single_line"] = "single-line ternary",
            ["multi_aligned"] = "multi-line — ? and : aligned",
            ["multi_other"] = "multi-line — other layout",
        };
        var ternaryConforming = new HashSet<string> { "single_line", "multi_aligned" };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = _samples.Count,
            Confidence = Math.Round(bestResult.Accuracy, 1),
            DominantPattern = patternDesc,
            Details = details,
            Examples = _examples.BuildMulti(ternaryConforming, ternaryLabels),
        };
    }

    private PredictorResult FindBestPredictor(List<TernarySample> singleLine, List<TernarySample> multiLine)
    {
        var predictors = new List<(string Name, Func<TernarySample, int> Getter)>
        {
            ("expression length", s => s.ExpressionLength),
            ("statement line length", s => s.StatementLineLength),
            ("hypothetical line length", s => s.HypotheticalLineLength),
        };

        var results = new List<PredictorCandidate>();

        foreach (var (name, getter) in predictors)
        {
            var singleValues = singleLine.Select(getter).ToList();
            var multiValues = multiLine.Select(getter).ToList();
            var (threshold, accuracy) = FindOptimalThreshold(singleValues, multiValues);
            results.Add(new PredictorCandidate { Name = name, Threshold = threshold, Accuracy = accuracy });
        }

        // Try a context-aware composite predictor:
        // Identify contexts that are overwhelmingly one way (>= 80% single or multi-line)
        // and give them fixed rules, then use length threshold for the rest.
        var compositeResult = BuildCompositePredictor(_samples, singleLine, multiLine);
        if (compositeResult != null)
        {
            results.Add(compositeResult);
        }

        var best = results.OrderByDescending(r => r.Accuracy).First();

        return new PredictorResult
        {
            PredictorName = best.Name,
            Threshold = best.Threshold,
            Accuracy = best.Accuracy,
            AllPredictors = results,
            CompositeRules = best.CompositeRules,
        };
    }

    private PredictorCandidate? BuildCompositePredictor(
        List<TernarySample> all,
        List<TernarySample> singleLine,
        List<TernarySample> multiLine)
    {
        if (all.Count < 10) return null;

        // Per-context thresholds: instead of hard "always" rules, find the best
        // length threshold within each context that has enough samples.
        var contextGroups = all
            .GroupBy(s => s.Context)
            .Where(g => g.Count() >= 3)
            .ToList();

        // Nested ternary → always multi-line
        bool useNestedRule = all.Any(s => s.HasNestedTernary && !s.IsSingleLine);

        // Try each metric and score it with per-context thresholds
        var metrics = new (string Name, Func<TernarySample, int> Getter)[]
        {
            ("expression", s => s.ExpressionLength),
            ("statement line", s => s.StatementLineLength),
            ("hypothetical line", s => s.HypotheticalLineLength),
        };

        PredictorCandidate? bestResult = null;

        foreach (var (metricName, getter) in metrics)
        {
            // Find a global fallback threshold from all samples
            var allSingleVals = singleLine.Select(getter).ToList();
            var allMultiVals = multiLine.Select(getter).ToList();
            var (globalThreshold, _) = FindOptimalThreshold(allSingleVals, allMultiVals);

            // Find per-context thresholds
            var contextThresholds = new Dictionary<TernaryContext, int>();
            var contextRules = new Dictionary<TernaryContext, string>();
            var unclassifiedSamples = new List<TernarySample>();

            foreach (var group in contextGroups)
            {
                var ctxSingle = group.Where(s => s.IsSingleLine).ToList();
                var ctxMulti = group.Where(s => !s.IsSingleLine).ToList();
                double singleRate = (double)ctxSingle.Count / group.Count();

                if (singleRate >= 0.95)
                {
                    // Virtually always single-line — use a very high threshold
                    contextThresholds[group.Key] = int.MaxValue;
                    contextRules[group.Key] = $"{group.Key}: always single-line";
                }
                else if (singleRate <= 0.05)
                {
                    // Virtually always multi-line — use threshold of -1 (nothing passes)
                    contextThresholds[group.Key] = -1;
                    contextRules[group.Key] = $"{group.Key}: always multi-line";
                }
                else
                {
                    // Find the optimal threshold within this context
                    var (ctxThreshold, _) = FindOptimalThreshold(
                        ctxSingle.Select(getter).ToList(),
                        ctxMulti.Select(getter).ToList());
                    contextThresholds[group.Key] = ctxThreshold;

                    if (singleRate >= 0.80)
                        contextRules[group.Key] = $"{group.Key}: single-line (multi when {metricName} >{ctxThreshold})";
                    else if (singleRate <= 0.20)
                        contextRules[group.Key] = $"{group.Key}: multi-line (single when {metricName} <={ctxThreshold})";
                    else
                        contextRules[group.Key] = $"{group.Key}: single-line when {metricName} <={ctxThreshold}";
                }
            }

            var classifiedContexts = contextGroups.Select(g => g.Key).ToHashSet();

            // Score across all samples
            int correct = 0;
            foreach (var sample in all)
            {
                bool predictSingleLine;
                if (useNestedRule && sample.HasNestedTernary)
                    predictSingleLine = false;
                else if (contextThresholds.TryGetValue(sample.Context, out int ctxThreshold))
                    predictSingleLine = getter(sample) <= ctxThreshold;
                else
                    predictSingleLine = getter(sample) <= globalThreshold;

                if (predictSingleLine == sample.IsSingleLine)
                    correct++;
            }

            double accuracy = (double)correct / all.Count * 100;

            if (bestResult == null || accuracy > bestResult.Accuracy)
            {
                var rules = new List<string>();
                if (useNestedRule)
                    rules.Add("nested ternary: always multi-line");
                foreach (var ctx in contextRules.Keys.OrderByDescending(k => contextGroups.First(g => g.Key == k).Count()))
                    rules.Add(contextRules[ctx]);
                if (!classifiedContexts.SetEquals(all.Select(s => s.Context).ToHashSet()))
                    rules.Add($"otherwise: single-line when {metricName} <={globalThreshold}");

                bestResult = new PredictorCandidate
                {
                    Name = "context-aware",
                    Threshold = globalThreshold,
                    Accuracy = accuracy,
                    CompositeRules = rules,
                };
            }
        }

        return bestResult;
    }

    private (int Threshold, double Accuracy) FindOptimalThreshold(
        List<int> singleLineValues,
        List<int> multiLineValues)
    {
        if (singleLineValues.Count == 0 && multiLineValues.Count == 0)
            return (80, 0);
        if (singleLineValues.Count == 0)
            return (multiLineValues.Min() - 1, 100);
        if (multiLineValues.Count == 0)
            return (singleLineValues.Max(), 100);

        var all = singleLineValues.Concat(multiLineValues).ToList();
        int minLen = all.Min();
        int maxLen = all.Max();
        int total = all.Count;

        int bestThreshold = 80;
        int bestScore = 0;

        for (int t = minLen; t <= maxLen; t++)
        {
            int score = singleLineValues.Count(v => v <= t) + multiLineValues.Count(v => v > t);

            if (score > bestScore)
            {
                bestScore = score;
                bestThreshold = t;
            }
        }

        // Round to nearest 5
        int roundedThreshold = (int)(Math.Round(bestThreshold / 5.0) * 5);

        // Recalculate accuracy with rounded threshold
        int roundedScore = singleLineValues.Count(v => v <= roundedThreshold)
            + multiLineValues.Count(v => v > roundedThreshold);
        double roundedAccuracy = (double)roundedScore / total * 100;

        // Use the raw threshold if rounding hurts accuracy by more than 2%
        double rawAccuracy = (double)bestScore / total * 100;
        if (rawAccuracy - roundedAccuracy > 2)
            return (bestThreshold, rawAccuracy);

        return (roundedThreshold, roundedAccuracy);
    }

    private record TernarySample
    {
        public bool IsSingleLine { get; init; }
        public bool QuestionOnNewLine { get; init; }
        public bool ColonOnNewLine { get; init; }
        public int ExpressionLength { get; init; }
        public int StartColumn { get; init; }
        public int HypotheticalLineLength { get; init; }
        public int StatementLineLength { get; init; }
        public MultiLinePattern MultiLinePattern { get; init; }
        public TernaryContext Context { get; init; }
        public bool HasNestedTernary { get; init; }
    }

    private enum MultiLinePattern
    {
        None,
        QuestionOnNewLine,
        ColonOnNewLine,
        BothOnNewLine,
        AlignedOperators,
    }

    private enum TernaryContext
    {
        Assignment,
        Return,
        Argument,
        Initializer,
        StringInterpolation,
    }

    private record PredictorCandidate
    {
        public string Name { get; init; } = "";
        public int Threshold { get; init; }
        public double Accuracy { get; init; }
        public List<string>? CompositeRules { get; init; }
    }

    private record PredictorResult
    {
        public string PredictorName { get; init; } = "";
        public int Threshold { get; init; }
        public double Accuracy { get; init; }
        public List<PredictorCandidate> AllPredictors { get; init; } = new();
        public List<string>? CompositeRules { get; init; }
    }
}
