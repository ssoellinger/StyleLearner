using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class ObjectInitializerDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Object Initializer";

    private int _singleLineCount;
    private int _multiLineCount;
    private int _trailingCommaCount;
    private int _noTrailingCommaCount;
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.ObjectInitializerExpression) &&
            !node.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            base.VisitInitializerExpression(node);
            return;
        }

        if (node.Expressions.Count == 0)
        {
            base.VisitInitializerExpression(node);
            return;
        }

        var openBraceLine = node.OpenBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var closeBraceLine = node.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line;

        if (closeBraceLine > openBraceLine)
        {
            _multiLineCount++;
            _examples.TryAdd("multi_line", node);
        }
        else
        {
            _singleLineCount++;
            _examples.TryAdd("single_line", node, contextBefore: 1);
        }

        // Check trailing comma: look at the last separator
        if (node.Expressions.Count > 0)
        {
            var separators = node.Expressions.GetSeparators().ToList();
            // If there are as many separators as expressions, there's a trailing comma
            if (separators.Count >= node.Expressions.Count)
            {
                _trailingCommaCount++;
                _examples.TryAdd("trailing_comma", node);
            }
            else
            {
                _noTrailingCommaCount++;
                _examples.TryAdd("no_trailing_comma", node);
            }
        }

        base.VisitInitializerExpression(node);
    }

    public DetectorResult GetResult()
    {
        var total = _singleLineCount + _multiLineCount;
        var trailingTotal = _trailingCommaCount + _noTrailingCommaCount;
        var hasTrailingComma = _trailingCommaCount > _noTrailingCommaCount;
        var confidence = trailingTotal > 0
            ? (double)Math.Max(_trailingCommaCount, _noTrailingCommaCount) / trailingTotal * 100
            : 0;

        var conforming = new HashSet<string> { "multi_line", hasTrailingComma ? "trailing_comma" : "no_trailing_comma" };
        var labels = new Dictionary<string, string>
        {
            ["multi_line"] = "multi-line initializer",
            ["single_line"] = "single-line initializer",
            ["trailing_comma"] = "trailing comma present",
            ["no_trailing_comma"] = "no trailing comma",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = $"multi-line: {_multiLineCount}, single-line: {_singleLineCount}, " +
                $"trailing comma: {(hasTrailingComma ? "yes" : "no")}",
            Details = new Dictionary<string, object>
            {
                ["SingleLineCount"] = _singleLineCount,
                ["MultiLineCount"] = _multiLineCount,
                ["TrailingComma"] = hasTrailingComma,
                ["TrailingCommaCount"] = _trailingCommaCount,
                ["NoTrailingCommaCount"] = _noTrailingCommaCount,
            },
            Examples = _examples.BuildMulti(conforming, labels),
        };
    }
}
