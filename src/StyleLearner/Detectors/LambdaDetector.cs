using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class LambdaDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Lambda";

    private readonly List<LambdaSample> _samples = new();
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        AnalyzeLambda(node);
        base.VisitSimpleLambdaExpression(node);
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        AnalyzeLambda(node);
        base.VisitParenthesizedLambdaExpression(node);
    }

    private void AnalyzeLambda(LambdaExpressionSyntax node)
    {
        if (node.Block != null)
        {
            int statementCount = node.Block.Statements.Count;
            bool hasControlFlow = node.Block.Statements.Any(s =>
                s is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                    or WhileStatementSyntax or TryStatementSyntax or SwitchStatementSyntax);
            bool hasMultipleStatements = statementCount > 1;
            bool hasLocalDeclarations = node.Block.Statements.Any(s => s is LocalDeclarationStatementSyntax);

            // Could this have been an expression body? (single return statement, no other logic)
            bool couldBeExpression = statementCount == 1 &&
                node.Block.Statements[0] is ReturnStatementSyntax or ExpressionStatementSyntax;

            _samples.Add(new LambdaSample
            {
                Style = LambdaStyle.Block,
                StatementCount = statementCount,
                HasControlFlow = hasControlFlow,
                HasMultipleStatements = hasMultipleStatements,
                HasLocalDeclarations = hasLocalDeclarations,
                CouldBeExpression = couldBeExpression,
                ExpressionLength = 0,
            });

            if (couldBeExpression)
                _examples.TryAdd("block_could_be_expr", node, contextBefore: 1);
            else
                _examples.TryAdd("block_body", node, contextBefore: 1);
        }
        else if (node.ExpressionBody != null)
        {
            var arrowLine = node.ArrowToken.GetLocation().GetLineSpan().StartLinePosition.Line;
            var exprEndLine = node.ExpressionBody.GetLocation().GetLineSpan().EndLinePosition.Line;
            bool isMultiLine = exprEndLine > arrowLine;

            int expressionLength = CollapseWhitespace(node.ExpressionBody.ToString()).Length;

            var lambdaStyle = isMultiLine ? LambdaStyle.MultiLineExpression : LambdaStyle.SingleLineExpression;
            _samples.Add(new LambdaSample
            {
                Style = lambdaStyle,
                ExpressionLength = expressionLength,
                StatementCount = 0,
            });

            _examples.TryAdd(isMultiLine ? "multi_line_expr" : "single_line_expr", node, contextBefore: 1);
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inWhitespace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace) { sb.Append(' '); inWhitespace = true; }
            }
            else { sb.Append(c); inWhitespace = false; }
        }

        return sb.ToString().Trim();
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

        var blockSamples = _samples.Where(s => s.Style == LambdaStyle.Block).ToList();
        var singleLineSamples = _samples.Where(s => s.Style == LambdaStyle.SingleLineExpression).ToList();
        var multiLineSamples = _samples.Where(s => s.Style == LambdaStyle.MultiLineExpression).ToList();
        var expressionSamples = singleLineSamples.Concat(multiLineSamples).ToList();

        // Rule 1: Block body used when multiple statements / control flow?
        int blockCorrect = blockSamples.Count(s => s.HasMultipleStatements || s.HasControlFlow || s.HasLocalDeclarations);
        int blockCouldBeExpr = blockSamples.Count(s => s.CouldBeExpression);
        double blockRuleAccuracy = blockSamples.Count > 0
            ? (double)blockCorrect / blockSamples.Count * 100
            : 100;

        // Rule 2: Single vs multi-line expression — find length threshold
        int lengthThreshold = 50;
        double exprThresholdAccuracy = 100;
        if (singleLineSamples.Count > 0 && multiLineSamples.Count > 0)
        {
            (lengthThreshold, exprThresholdAccuracy) = FindOptimalThreshold(
                singleLineSamples.Select(s => s.ExpressionLength).ToList(),
                multiLineSamples.Select(s => s.ExpressionLength).ToList());
        }
        else if (singleLineSamples.Count > 0)
        {
            lengthThreshold = singleLineSamples.Max(s => s.ExpressionLength);
        }

        // Overall confidence: how well does the combined rule explain the data?
        // "Block when multiple statements; expression when single expression;
        //  single-line when short, multi-line when long"
        int totalCorrect = blockCorrect; // correctly predicted blocks
        totalCorrect += blockCouldBeExpr; // blocks that could be expression — still "correct" since they chose block
        // Actually let's count: expression lambdas are always correct (they are expressions)
        // Block lambdas with multiple statements are "correctly predicted as block"
        // Block lambdas with single statement (couldBeExpression) are the mismatches

        // Better approach: predict each sample
        int correct = 0;
        foreach (var s in _samples)
        {
            switch (s.Style)
            {
                case LambdaStyle.Block:
                    // Predicted block if: multiple statements, control flow, or local vars
                    bool predictBlock = s.HasMultipleStatements || s.HasControlFlow || s.HasLocalDeclarations;
                    if (predictBlock) correct++;
                    // If it could have been an expression but used block, that's a "mismatch"
                    // unless it's still block (which it is) — this counts against the rule
                    break;
                case LambdaStyle.SingleLineExpression:
                    if (s.ExpressionLength <= lengthThreshold) correct++;
                    break;
                case LambdaStyle.MultiLineExpression:
                    if (s.ExpressionLength > lengthThreshold) correct++;
                    break;
            }
        }

        double overallAccuracy = (double)correct / _samples.Count * 100;

        // Build pattern description
        var rules = new List<string>
        {
            "block body when multiple statements/control flow",
            $"single-line expression when <={lengthThreshold} chars",
            $"multi-line expression when >{lengthThreshold} chars",
        };

        var lambdaLabels = new Dictionary<string, string>
        {
            ["single_line_expr"] = "single-line expression lambda",
            ["multi_line_expr"] = "multi-line expression lambda",
            ["block_body"] = "block body lambda (multi-statement)",
            ["block_could_be_expr"] = "block body — could be expression lambda",
        };
        var lambdaConforming = new HashSet<string> { "single_line_expr", "multi_line_expr", "block_body" };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = _samples.Count,
            Confidence = Math.Round(overallAccuracy, 1),
            DominantPattern = string.Join("; ", rules),
            Details = new Dictionary<string, object>
            {
                ["SingleLineExprCount"] = singleLineSamples.Count,
                ["MultiLineExprCount"] = multiLineSamples.Count,
                ["BlockBodyCount"] = blockSamples.Count,
                ["BlockWithMultipleStatements"] = blockSamples.Count(s => s.HasMultipleStatements),
                ["BlockWithControlFlow"] = blockSamples.Count(s => s.HasControlFlow),
                ["BlockWithLocalDeclarations"] = blockSamples.Count(s => s.HasLocalDeclarations),
                ["BlockCouldBeExpression"] = blockCouldBeExpr,
                ["BlockRuleAccuracy"] = $"{blockRuleAccuracy:F1}%",
                ["ExprLengthThreshold"] = lengthThreshold,
                ["ExprThresholdAccuracy"] = $"{exprThresholdAccuracy:F1}%",
                ["SingleLineAvgLength"] = singleLineSamples.Count > 0 ? (int)singleLineSamples.Average(s => s.ExpressionLength) : 0,
                ["SingleLineMaxLength"] = singleLineSamples.Count > 0 ? singleLineSamples.Max(s => s.ExpressionLength) : 0,
                ["MultiLineAvgLength"] = multiLineSamples.Count > 0 ? (int)multiLineSamples.Average(s => s.ExpressionLength) : 0,
                ["MultiLineMinLength"] = multiLineSamples.Count > 0 ? multiLineSamples.Min(s => s.ExpressionLength) : 0,
            },
            Examples = _examples.BuildMulti(lambdaConforming, lambdaLabels),
        };
    }

    private static (int Threshold, double Accuracy) FindOptimalThreshold(
        List<int> singleLineValues, List<int> multiLineValues)
    {
        if (singleLineValues.Count == 0 && multiLineValues.Count == 0) return (50, 0);
        if (singleLineValues.Count == 0) return (multiLineValues.Min() - 1, 100);
        if (multiLineValues.Count == 0) return (singleLineValues.Max(), 100);

        var all = singleLineValues.Concat(multiLineValues).ToList();
        int minLen = all.Min();
        int maxLen = all.Max();
        int total = all.Count;

        int bestThreshold = 50;
        int bestScore = 0;

        for (int t = minLen; t <= maxLen; t++)
        {
            int score = singleLineValues.Count(v => v <= t) + multiLineValues.Count(v => v > t);
            if (score > bestScore) { bestScore = score; bestThreshold = t; }
        }

        int rounded = (int)(Math.Round(bestThreshold / 5.0) * 5);
        int roundedScore = singleLineValues.Count(v => v <= rounded) + multiLineValues.Count(v => v > rounded);
        double roundedAcc = (double)roundedScore / total * 100;
        double rawAcc = (double)bestScore / total * 100;
        return rawAcc - roundedAcc > 2 ? (bestThreshold, rawAcc) : (rounded, roundedAcc);
    }

    private record LambdaSample
    {
        public LambdaStyle Style { get; init; }
        public int ExpressionLength { get; init; }
        public int StatementCount { get; init; }
        public bool HasControlFlow { get; init; }
        public bool HasMultipleStatements { get; init; }
        public bool HasLocalDeclarations { get; init; }
        public bool CouldBeExpression { get; init; }
    }

    private enum LambdaStyle
    {
        SingleLineExpression,
        MultiLineExpression,
        Block,
    }
}
