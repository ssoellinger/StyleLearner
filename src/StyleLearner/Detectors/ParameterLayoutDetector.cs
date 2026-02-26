using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class ParameterLayoutDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Parameter Layout";

    private int _singleLineCount;
    private int _multiLineCount;
    private int _closingParenOwnLine;
    private int _closingParenSameLine;
    private readonly List<int> _multiLineParamCounts = new();
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        AnalyzeParameterList(node.ParameterList, node);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        AnalyzeParameterList(node.ParameterList, node);
        base.VisitConstructorDeclaration(node);
    }

    private void AnalyzeParameterList(ParameterListSyntax paramList, SyntaxNode declaration)
    {
        if (paramList.Parameters.Count <= 1) return;

        var openParen = paramList.OpenParenToken;
        var closeParen = paramList.CloseParenToken;
        var firstParam = paramList.Parameters.First();
        var lastParam = paramList.Parameters.Last();

        var openLine = openParen.GetLocation().GetLineSpan().StartLinePosition.Line;
        var firstParamLine = firstParam.GetLocation().GetLineSpan().StartLinePosition.Line;
        var lastParamLine = lastParam.GetLocation().GetLineSpan().StartLinePosition.Line;
        var closeLine = closeParen.GetLocation().GetLineSpan().StartLinePosition.Line;

        bool isMultiLine = lastParamLine > firstParamLine || firstParamLine > openLine;

        if (isMultiLine)
        {
            _multiLineCount++;
            _multiLineParamCounts.Add(paramList.Parameters.Count);

            if (closeLine > lastParamLine)
            {
                _closingParenOwnLine++;
                _examples.TryAdd("own_line", paramList, contextBefore: 1);
            }
            else
            {
                _closingParenSameLine++;
                _examples.TryAdd("same_line", paramList, contextBefore: 1);
            }
        }
        else
        {
            _singleLineCount++;
        }
    }

    public DetectorResult GetResult()
    {
        var total = _singleLineCount + _multiLineCount;
        int threshold = _multiLineParamCounts.Count > 0
            ? (int)Math.Round(_multiLineParamCounts.Average())
            : 0;

        var closingTotal = _closingParenOwnLine + _closingParenSameLine;
        var closingParen = _closingParenOwnLine >= _closingParenSameLine ? "own_line" : "same_line";
        var closingConfidence = closingTotal > 0
            ? (double)Math.Max(_closingParenOwnLine, _closingParenSameLine) / closingTotal * 100
            : 0;

        var labels = new Dictionary<string, string>
        {
            ["own_line"] = "closing paren on own line",
            ["same_line"] = "closing paren on same line as last param",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(closingConfidence, 1),
            DominantPattern = _multiLineCount > 0
                ? $"multi-line at {threshold}+ params, closing paren: {closingParen}"
                : "all single-line",
            Details = new Dictionary<string, object>
            {
                ["SingleLineCount"] = _singleLineCount,
                ["MultiLineCount"] = _multiLineCount,
                ["MultilineThreshold"] = threshold,
                ["ClosingParen"] = closingParen,
                ["ClosingParenOwnLineCount"] = _closingParenOwnLine,
                ["ClosingParenSameLineCount"] = _closingParenSameLine,
            },
            Examples = _examples.Build(closingParen, labels),
        };
    }
}
