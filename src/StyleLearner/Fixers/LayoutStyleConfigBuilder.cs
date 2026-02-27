using StyleLearner.Detectors;

namespace StyleLearner.Fixers;

public class LayoutStyleConfigBuilder
{
    private readonly double _minConfidence;

    public LayoutStyleConfigBuilder(double minConfidence = 80.0)
    {
        _minConfidence = minConfidence;
    }

    public LayoutStyleConfig Build(StyleReport report)
    {
        return new LayoutStyleConfig
        {
            ParameterLayout = BuildParameterLayoutRule(report),
            InheritanceLayout = BuildInheritanceLayoutRule(report),
            ArrowPlacement = BuildArrowPlacementRule(report),
            MethodChaining = BuildMethodChainingRule(report),
            TernaryLayout = BuildTernaryLayoutRule(report),
            TrailingComma = BuildTrailingCommaRule(report),
            NamespaceStyle = BuildNamespaceStyleRule(report),
            BlankLines = BuildBlankLineRule(report),
        };
    }

    private ParameterLayoutRule? BuildParameterLayoutRule(StyleReport report)
    {
        var result = FindDetector(report, "Parameter Layout");
        if (result == null || result.Confidence < _minConfidence) return null;

        var threshold = GetDetail<int>(result, "MultilineThreshold");
        var closingParen = GetDetail<string>(result, "ClosingParen");

        if (threshold <= 1) return null;

        return new ParameterLayoutRule
        {
            MultilineThreshold = threshold,
            ClosingParen = closingParen ?? "own_line",
        };
    }

    private InheritanceLayoutRule? BuildInheritanceLayoutRule(StyleReport report)
    {
        var result = FindDetector(report, "Inheritance Layout");
        if (result == null || result.Confidence < _minConfidence) return null;

        var placement = GetDetail<string>(result, "ColonPlacement");
        if (placement == null) return null;

        return new InheritanceLayoutRule
        {
            ColonPlacement = placement,
        };
    }

    private ArrowPlacementRule? BuildArrowPlacementRule(StyleReport report)
    {
        var result = FindDetector(report, "Expression Body");
        if (result == null) return null;

        var arrowOnNewLine = GetDetail<bool>(result, "ArrowOnNewLine");
        var arrowConfidenceStr = GetDetail<string>(result, "ArrowConfidence");

        if (arrowConfidenceStr == null) return null;

        // Parse "96.8%" format
        var confidenceStr = arrowConfidenceStr.TrimEnd('%');
        if (!double.TryParse(confidenceStr, out double confidence)) return null;
        if (confidence < _minConfidence) return null;

        return new ArrowPlacementRule
        {
            ArrowOnNewLine = arrowOnNewLine,
        };
    }

    private MethodChainingRule? BuildMethodChainingRule(StyleReport report)
    {
        var result = FindDetector(report, "Method Chaining");
        if (result == null || result.Confidence < _minConfidence) return null;

        var bestPredictorRule = GetDetail<string>(result, "BestPredictorRule");
        if (bestPredictorRule == null) return null;

        // Parse chain length threshold from BestPredictorRule.
        // Format examples:
        //   "single-line when <=2 calls"
        //   "single-line when <=80 chars"
        int chainThreshold = 2; // default
        if (bestPredictorRule.Contains("calls"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(bestPredictorRule, @"<=(\d+)\s+calls");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed))
                chainThreshold = parsed;
        }

        return new MethodChainingRule
        {
            ChainLengthThreshold = chainThreshold,
            BestPredictorRule = bestPredictorRule,
        };
    }

    private TernaryLayoutRule? BuildTernaryLayoutRule(StyleReport report)
    {
        var result = FindDetector(report, "Ternary");
        if (result == null || result.Confidence < _minConfidence) return null;

        var threshold = GetDetail<int>(result, "Threshold");
        var dominantPattern = GetDetail<string>(result, "DominantMultiLinePattern");
        var bestPredictor = GetDetail<string>(result, "BestPredictor");

        // Collect context rules from the predictor details
        List<string>? contextRules = null;
        var compositeRuleKeys = result.Details.Keys
            .Where(k => k.StartsWith("  Rule_"))
            .OrderBy(k => k)
            .ToList();

        if (compositeRuleKeys.Count > 0)
        {
            contextRules = compositeRuleKeys
                .Select(k => result.Details[k]?.ToString() ?? "")
                .Where(r => !string.IsNullOrEmpty(r))
                .ToList();
        }

        return new TernaryLayoutRule
        {
            Threshold = threshold,
            DominantMultiLinePattern = dominantPattern ?? "AlignedOperators",
            ContextRules = contextRules,
            BestPredictor = bestPredictor ?? "expression length",
        };
    }

    private TrailingCommaRule? BuildTrailingCommaRule(StyleReport report)
    {
        var result = FindDetector(report, "Object Initializer");
        if (result == null || result.Confidence < _minConfidence) return null;

        var hasTrailingComma = GetDetail<bool>(result, "TrailingComma");

        return new TrailingCommaRule
        {
            HasTrailingComma = hasTrailingComma,
        };
    }

    private NamespaceStyleRule? BuildNamespaceStyleRule(StyleReport report)
    {
        var result = FindDetector(report, "Using Layout");
        if (result == null) return null;

        var fileScopedCount = GetDetail<int>(result, "FileScopedNamespaceCount");
        var blockScopedCount = GetDetail<int>(result, "BlockScopedNamespaceCount");
        var total = fileScopedCount + blockScopedCount;

        if (total == 0) return null;

        var dominant = fileScopedCount > blockScopedCount ? "file_scoped" : "block_scoped";
        var confidence = (double)Math.Max(fileScopedCount, blockScopedCount) / total * 100;

        if (confidence < _minConfidence) return null;

        return new NamespaceStyleRule
        {
            Style = dominant,
        };
    }

    private BlankLineRule? BuildBlankLineRule(StyleReport report)
    {
        var result = FindDetector(report, "Blank Lines");
        if (result == null || result.Confidence < _minConfidence) return null;

        var blankAfterBrace = GetDetail<bool>(result, "BlankAfterOpenBrace");
        var blankBeforeCloseBrace = GetDetail<bool>(result, "BlankBeforeCloseBrace");
        var blankAfterRegion = GetDetail<bool>(result, "BlankAfterRegion");
        var blankBeforeEndRegion = GetDetail<bool>(result, "BlankBeforeEndRegion");

        return new BlankLineRule
        {
            MaxConsecutiveBlankLines = 1,
            BlankLineAfterOpenBrace = blankAfterBrace,
            BlankLineBeforeCloseBrace = blankBeforeCloseBrace,
            BlankLineAfterRegion = blankAfterRegion,
            BlankLineBeforeEndRegion = blankBeforeEndRegion,
        };
    }

    private static DetectorResult? FindDetector(StyleReport report, string name)
    {
        return report.Results.FirstOrDefault(r => r.DetectorName == name);
    }

    private static T? GetDetail<T>(DetectorResult result, string key)
    {
        if (result.Details.TryGetValue(key, out var value))
        {
            if (value is T typed) return typed;

            // Handle numeric conversions (JSON deserialization may produce different types)
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        return default;
    }
}
