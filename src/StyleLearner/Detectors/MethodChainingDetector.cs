using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class MethodChainingDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Method Chaining";

    private readonly List<ChainSample> _samples = new();
    private readonly HashSet<SyntaxNode> _visited = new();
    private readonly ExampleCollector _examples = new();

    private static readonly HashSet<string> QueryMethodNames = new(StringComparer.Ordinal)
    {
        "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "ThenBy",
        "ThenByDescending", "GroupBy", "Include", "ThenInclude", "Join", "GroupJoin",
        "Distinct", "Take", "Skip", "Any", "All", "First", "FirstOrDefault",
        "Single", "SingleOrDefault", "ToList", "ToListAsync", "ToArray",
        "ToDictionary", "Count", "Sum", "Average", "Min", "Max", "Aggregate",
        "AsNoTracking", "AsQueryable", "AsEnumerable",
    };

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        _visited.Clear();
        Visit(tree.GetRoot());
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_visited.Contains(node))
        {
            base.VisitInvocationExpression(node);
            return;
        }

        // Collect the full chain from outermost call inward
        var calls = new List<InvocationInfo>();
        CollectChain(node, calls);

        // We need at least 2 chained method calls to be interesting
        if (calls.Count >= 2)
        {
            foreach (var call in calls)
                _visited.Add(call.Node);

            AnalyzeChain(calls, node);
        }

        base.VisitInvocationExpression(node);
    }

    private void AnalyzeChain(List<InvocationInfo> calls, InvocationExpressionSyntax outermost)
    {
        // Determine if the chain is single-line or multi-line
        var firstCallLine = calls[^1].DotLine; // innermost call's dot
        var lastCallLine = calls[0].DotLine;    // outermost call's dot
        bool isMultiLine = lastCallLine > firstCallLine;

        // If any dot is on a different line than its preceding expression, it's multi-line
        if (!isMultiLine)
        {
            foreach (var call in calls)
            {
                if (call.DotLine > call.ExpressionEndLine)
                {
                    isMultiLine = true;
                    break;
                }
            }
        }

        // Measure chain properties
        int chainLength = calls.Count;
        int hypotheticalLineLength = MeasureUnwrappedChainLength(calls, outermost);
        bool hasComplexLambda = calls.Any(c => c.HasMultiLineLambda);
        bool hasAnyLambda = calls.Any(c => c.HasLambda);
        int maxSingleArgLength = calls.Max(c => c.ArgumentTextLength);

        // Detect context
        var context = ClassifyChainContext(outermost);

        // Detect LINQ/EF query chains: 2+ method calls are in the query methods set
        int queryMethodCount = calls.Count(c => QueryMethodNames.Contains(c.MethodName));
        bool hasQueryMethods = queryMethodCount >= 2;

        _samples.Add(new ChainSample
        {
            IsMultiLine = isMultiLine,
            ChainLength = chainLength,
            HypotheticalLineLength = hypotheticalLineLength,
            HasComplexLambda = hasComplexLambda,
            HasAnyLambda = hasAnyLambda,
            MaxSingleArgLength = maxSingleArgLength,
            Context = context,
            HasQueryMethods = hasQueryMethods,
        });

        _examples.TryAdd(isMultiLine ? "multi_line" : "single_line", outermost);
    }

    private static void CollectChain(ExpressionSyntax expr, List<InvocationInfo> calls)
    {
        if (expr is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var dotLine = memberAccess.OperatorToken.GetLocation().GetLineSpan().StartLinePosition.Line;
            var exprEndLine = memberAccess.Expression.GetLocation().GetLineSpan().EndLinePosition.Line;

            // Analyze arguments for lambda complexity
            bool hasLambda = false;
            bool hasMultiLineLambda = false;
            int argTextLength = 0;

            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var argText = CollapseWhitespace(arg.ToString());
                argTextLength = Math.Max(argTextLength, argText.Length);

                if (arg.Expression is LambdaExpressionSyntax lambda)
                {
                    hasLambda = true;
                    var lambdaSpan = lambda.GetLocation().GetLineSpan();
                    if (lambdaSpan.EndLinePosition.Line > lambdaSpan.StartLinePosition.Line)
                        hasMultiLineLambda = true;
                }
            }

            calls.Add(new InvocationInfo
            {
                Node = invocation,
                MethodName = memberAccess.Name.Identifier.Text,
                DotLine = dotLine,
                ExpressionEndLine = exprEndLine,
                HasLambda = hasLambda,
                HasMultiLineLambda = hasMultiLineLambda,
                ArgumentTextLength = argTextLength,
            });

            CollectChain(memberAccess.Expression, calls);
        }
    }

    private static int MeasureUnwrappedChainLength(List<InvocationInfo> calls, InvocationExpressionSyntax outermost)
    {
        // Walk to the root expression of the chain (the thing before the first dot)
        ExpressionSyntax? root = outermost;
        while (root is InvocationExpressionSyntax inv &&
               inv.Expression is MemberAccessExpressionSyntax ma)
        {
            root = ma.Expression;
        }

        int length = CollapseWhitespace(root?.ToString() ?? "").Length;

        // Add each call: .MethodName(args)
        foreach (var call in calls)
        {
            var argsText = CollapseWhitespace(call.Node.ArgumentList.ToString());
            length += 1 + call.MethodName.Length + argsText.Length; // .Name(args)
        }

        return length;
    }

    private static ChainContext ClassifyChainContext(InvocationExpressionSyntax outermost)
    {
        var parent = outermost.Parent;

        // Walk through intermediate nodes (await, cast, etc.)
        while (parent is AwaitExpressionSyntax or ParenthesizedExpressionSyntax or CastExpressionSyntax)
            parent = parent.Parent;

        return parent switch
        {
            EqualsValueClauseSyntax => ChainContext.VariableInit,
            AssignmentExpressionSyntax => ChainContext.Assignment,
            ReturnStatementSyntax => ChainContext.Return,
            ArrowExpressionClauseSyntax => ChainContext.ExpressionBody,
            ArgumentSyntax => ChainContext.Argument,
            InitializerExpressionSyntax => ChainContext.Initializer,
            _ => ChainContext.Other,
        };
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
            else
            {
                sb.Append(c);
                inWhitespace = false;
            }
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

        var singleLine = _samples.Where(s => !s.IsMultiLine).ToList();
        var multiLine = _samples.Where(s => s.IsMultiLine).ToList();

        // --- Predictor 1: hypothetical line length threshold ---
        var (lengthThreshold, lengthAccuracy) = FindOptimalThreshold(
            singleLine.Select(s => s.HypotheticalLineLength).ToList(),
            multiLine.Select(s => s.HypotheticalLineLength).ToList());

        // --- Predictor 2: chain length threshold ---
        var (chainThreshold, chainAccuracy) = FindOptimalThreshold(
            singleLine.Select(s => s.ChainLength).ToList(),
            multiLine.Select(s => s.ChainLength).ToList());

        // --- Predictor 3: complex lambda rule ---
        // "multi-line if has complex lambda, otherwise use length threshold"
        var nonComplexSingle = singleLine.Where(s => !s.HasComplexLambda).ToList();
        var nonComplexMulti = multiLine.Where(s => !s.HasComplexLambda).ToList();
        var (nonComplexLenThreshold, _) = FindOptimalThreshold(
            nonComplexSingle.Select(s => s.HypotheticalLineLength).ToList(),
            nonComplexMulti.Select(s => s.HypotheticalLineLength).ToList());

        int lambdaRuleCorrect = 0;
        foreach (var s in _samples)
        {
            bool predictMulti = s.HasComplexLambda || s.HypotheticalLineLength > nonComplexLenThreshold;
            if (predictMulti == s.IsMultiLine) lambdaRuleCorrect++;
        }

        double lambdaRuleAccuracy = (double)lambdaRuleCorrect / _samples.Count * 100;

        // --- Predictor 4: query-aware (LINQ/EF chains with 3+ calls → always multi-line) ---
        var queryAwareResult = BuildQueryAwarePredictor(nonComplexLenThreshold);

        // --- Predictor 5: composite (context + lambda + length) ---
        var compositeResult = BuildCompositePredictor();

        // Pick the best
        var predictors = new List<(string Name, int Threshold, double Accuracy, string Desc)>
        {
            ("line length", lengthThreshold, lengthAccuracy,
                $"single-line when <={lengthThreshold} chars"),
            ("chain length", chainThreshold, chainAccuracy,
                $"single-line when <={chainThreshold} calls"),
            ("lambda+length", nonComplexLenThreshold, lambdaRuleAccuracy,
                $"multi-line if complex lambda, else single-line when <={nonComplexLenThreshold} chars"),
        };

        if (queryAwareResult != null)
        {
            predictors.Add(("query-aware", queryAwareResult.Value.Threshold,
                queryAwareResult.Value.Accuracy, queryAwareResult.Value.Desc));
        }

        if (compositeResult != null)
        {
            predictors.Add(("context-aware", compositeResult.Value.Threshold,
                compositeResult.Value.Accuracy, compositeResult.Value.Desc));
        }

        var best = predictors.OrderByDescending(p => p.Accuracy).First();

        // Context breakdown
        var contextGroups = _samples
            .GroupBy(s => s.Context)
            .ToDictionary(g => g.Key, g => new
            {
                Total = g.Count(),
                MultiLine = g.Count(s => s.IsMultiLine),
            });

        var details = new Dictionary<string, object>
        {
            ["SingleLineChains"] = singleLine.Count,
            ["MultiLineChains"] = multiLine.Count,
            ["BestPredictor"] = best.Name,
            ["BestPredictorRule"] = best.Desc,
            ["Accuracy"] = $"{best.Accuracy:F1}%",
            ["SingleLineAvgLength"] = singleLine.Count > 0 ? (int)singleLine.Average(s => s.HypotheticalLineLength) : 0,
            ["SingleLineMaxLength"] = singleLine.Count > 0 ? singleLine.Max(s => s.HypotheticalLineLength) : 0,
            ["MultiLineAvgLength"] = multiLine.Count > 0 ? (int)multiLine.Average(s => s.HypotheticalLineLength) : 0,
            ["MultiLineMinLength"] = multiLine.Count > 0 ? multiLine.Min(s => s.HypotheticalLineLength) : 0,
            ["ChainsWithQueryMethods"] = _samples.Count(s => s.HasQueryMethods),
            ["QueryChainsMultiLine%"] = _samples.Where(s => s.HasQueryMethods).Any()
                ? $"{(double)_samples.Count(s => s.HasQueryMethods && s.IsMultiLine) / _samples.Count(s => s.HasQueryMethods) * 100:F0}%"
                : "n/a",
            ["ChainsWithComplexLambda"] = _samples.Count(s => s.HasComplexLambda),
            ["ComplexLambdaMultiLine%"] = _samples.Where(s => s.HasComplexLambda).Count() > 0
                ? $"{(double)_samples.Count(s => s.HasComplexLambda && s.IsMultiLine) / _samples.Count(s => s.HasComplexLambda) * 100:F0}%"
                : "n/a",
        };

        foreach (var (ctx, stats) in contextGroups.OrderByDescending(kv => kv.Value.Total))
        {
            details[$"Context_{ctx}"] = $"{stats.MultiLine}/{stats.Total} multi-line";
        }

        foreach (var p in predictors)
        {
            details[$"Predictor_{p.Name}"] = $"accuracy={p.Accuracy:F1}%";
        }

        var chainLabels = new Dictionary<string, string>
        {
            ["single_line"] = "single-line chain",
            ["multi_line"] = "multi-line chain",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = _samples.Count,
            Confidence = Math.Round(best.Accuracy, 1),
            DominantPattern = best.Desc,
            Details = details,
            Examples = _examples.BuildMulti(new HashSet<string> { "single_line", "multi_line" }, chainLabels),
        };
    }

    private (int Threshold, double Accuracy, string Desc)? BuildQueryAwarePredictor(int fallbackLengthThreshold)
    {
        // Only useful if there are query chains in the codebase
        int queryChainCount = _samples.Count(s => s.HasQueryMethods);
        if (queryChainCount < 2) return null;

        // Find optimal length threshold from non-query, non-complex-lambda samples
        var nonQuerySamples = _samples.Where(s => !s.HasQueryMethods && !s.HasComplexLambda).ToList();
        int lengthThreshold = fallbackLengthThreshold;
        if (nonQuerySamples.Count > 0)
        {
            var (t, _) = FindOptimalThreshold(
                nonQuerySamples.Where(s => !s.IsMultiLine).Select(s => s.HypotheticalLineLength).ToList(),
                nonQuerySamples.Where(s => s.IsMultiLine).Select(s => s.HypotheticalLineLength).ToList());
            lengthThreshold = t;
        }

        // Score: query chains with 3+ calls → always multi-line
        //        complex lambda → always multi-line
        //        otherwise → use length threshold
        int correct = 0;
        foreach (var s in _samples)
        {
            bool predictMulti;
            if (s.HasQueryMethods && s.ChainLength >= 3)
                predictMulti = true;
            else if (s.HasComplexLambda)
                predictMulti = true;
            else
                predictMulti = s.HypotheticalLineLength > lengthThreshold;

            if (predictMulti == s.IsMultiLine) correct++;
        }

        double accuracy = (double)correct / _samples.Count * 100;

        return (lengthThreshold, accuracy,
            $"LINQ/EF query chains (3+ calls) → multi-line; complex lambda → multi-line; else single-line when <={lengthThreshold} chars");
    }

    private (int Threshold, double Accuracy, string Desc)? BuildCompositePredictor()
    {
        if (_samples.Count < 10) return null;

        // Rule: complex lambda → always multi-line
        // Then per-context: find contexts with strong bias
        var nonComplexSamples = _samples.Where(s => !s.HasComplexLambda).ToList();
        if (nonComplexSamples.Count == 0) return null;

        var contextGroups = nonComplexSamples
            .GroupBy(s => s.Context)
            .Where(g => g.Count() >= 3)
            .ToList();

        var alwaysMultiContexts = new List<ChainContext>();
        var alwaysSingleContexts = new List<ChainContext>();
        var lengthDependentSamples = new List<ChainSample>();

        foreach (var group in contextGroups)
        {
            double multiRate = (double)group.Count(s => s.IsMultiLine) / group.Count();

            if (multiRate >= 0.85)
                alwaysMultiContexts.Add(group.Key);
            else if (multiRate <= 0.15)
                alwaysSingleContexts.Add(group.Key);
            else
                lengthDependentSamples.AddRange(group);
        }

        // Add unclassified context samples to length-dependent pool
        var classified = contextGroups.Select(g => g.Key).ToHashSet();
        lengthDependentSamples.AddRange(nonComplexSamples.Where(s => !classified.Contains(s.Context)));

        int lengthThreshold = 80;
        if (lengthDependentSamples.Count > 0)
        {
            var (t, _) = FindOptimalThreshold(
                lengthDependentSamples.Where(s => !s.IsMultiLine).Select(s => s.HypotheticalLineLength).ToList(),
                lengthDependentSamples.Where(s => s.IsMultiLine).Select(s => s.HypotheticalLineLength).ToList());
            lengthThreshold = t;
        }

        // Score the composite predictor across ALL samples
        // Query chains with 3+ calls are an additional multi-line signal
        int correct = 0;
        foreach (var s in _samples)
        {
            bool predictMulti;
            if (s.HasComplexLambda)
                predictMulti = true;
            else if (s.HasQueryMethods && s.ChainLength >= 3)
                predictMulti = true;
            else if (alwaysMultiContexts.Contains(s.Context))
                predictMulti = true;
            else if (alwaysSingleContexts.Contains(s.Context))
                predictMulti = false;
            else
                predictMulti = s.HypotheticalLineLength > lengthThreshold;

            if (predictMulti == s.IsMultiLine) correct++;
        }

        double accuracy = (double)correct / _samples.Count * 100;

        // Build description
        var rules = new List<string>();
        rules.Add("complex lambda → multi-line");
        if (_samples.Any(s => s.HasQueryMethods))
            rules.Add("LINQ/EF query chains (3+ calls) → multi-line");
        foreach (var ctx in alwaysMultiContexts)
            rules.Add($"{ctx} → multi-line");
        foreach (var ctx in alwaysSingleContexts)
            rules.Add($"{ctx} → single-line");
        rules.Add($"else single-line when <={lengthThreshold} chars");

        return (lengthThreshold, accuracy, string.Join("; ", rules));
    }

    private static (int Threshold, double Accuracy) FindOptimalThreshold(
        List<int> singleLineValues, List<int> multiLineValues)
    {
        if (singleLineValues.Count == 0 && multiLineValues.Count == 0) return (80, 0);
        if (singleLineValues.Count == 0) return (multiLineValues.Min() - 1, 100);
        if (multiLineValues.Count == 0) return (singleLineValues.Max(), 100);

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

        int rounded = (int)(Math.Round(bestThreshold / 5.0) * 5);
        int roundedScore = singleLineValues.Count(v => v <= rounded) + multiLineValues.Count(v => v > rounded);
        double roundedAcc = (double)roundedScore / total * 100;
        double rawAcc = (double)bestScore / total * 100;

        return rawAcc - roundedAcc > 2 ? (bestThreshold, rawAcc) : (rounded, roundedAcc);
    }

    private record InvocationInfo
    {
        public InvocationExpressionSyntax Node { get; init; } = null!;
        public string MethodName { get; init; } = "";
        public int DotLine { get; init; }
        public int ExpressionEndLine { get; init; }
        public bool HasLambda { get; init; }
        public bool HasMultiLineLambda { get; init; }
        public int ArgumentTextLength { get; init; }
    }

    private record ChainSample
    {
        public bool IsMultiLine { get; init; }
        public int ChainLength { get; init; }
        public int HypotheticalLineLength { get; init; }
        public bool HasComplexLambda { get; init; }
        public bool HasAnyLambda { get; init; }
        public int MaxSingleArgLength { get; init; }
        public ChainContext Context { get; init; }
        public bool HasQueryMethods { get; init; }
    }

    private enum ChainContext
    {
        VariableInit,
        Assignment,
        Return,
        ExpressionBody,
        Argument,
        Initializer,
        Other,
    }
}
