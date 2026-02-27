using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class NewLineKeywordDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Newline Before Keywords";

    private int _newLineBeforeCatch;
    private int _sameLineBeforeCatch;
    private int _newLineBeforeElse;
    private int _sameLineBeforeElse;
    private int _newLineBeforeFinally;
    private int _sameLineBeforeFinally;

    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitTryStatement(TryStatementSyntax node)
    {
        // Check catch clauses
        foreach (var catchClause in node.Catches)
        {
            var closeBrace = GetPrecedingCloseBrace(catchClause.CatchKeyword);
            if (closeBrace != null)
            {
                CheckNewLineBeforeKeyword(closeBrace.Value, catchClause.CatchKeyword,
                    ref _newLineBeforeCatch, ref _sameLineBeforeCatch,
                    "newline_catch", "sameline_catch");
            }
        }

        // Check finally clause
        if (node.Finally != null)
        {
            var closeBrace = GetPrecedingCloseBrace(node.Finally.FinallyKeyword);
            if (closeBrace != null)
            {
                CheckNewLineBeforeKeyword(closeBrace.Value, node.Finally.FinallyKeyword,
                    ref _newLineBeforeFinally, ref _sameLineBeforeFinally,
                    "newline_finally", "sameline_finally");
            }
        }

        base.VisitTryStatement(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        // Check else clause
        if (node.Else != null && node.Statement is BlockSyntax block)
        {
            CheckNewLineBeforeKeyword(block.CloseBraceToken, node.Else.ElseKeyword,
                ref _newLineBeforeElse, ref _sameLineBeforeElse,
                "newline_else", "sameline_else");
        }

        base.VisitIfStatement(node);
    }

    private void CheckNewLineBeforeKeyword(SyntaxToken closeBrace, SyntaxToken keyword,
        ref int newLineCount, ref int sameLineCount,
        string newLineCategory, string sameLineCategory)
    {
        if (closeBrace.IsMissing || keyword.IsMissing) return;

        var braceLine = closeBrace.GetLocation().GetLineSpan().StartLinePosition.Line;
        var keywordLine = keyword.GetLocation().GetLineSpan().StartLinePosition.Line;

        if (keywordLine > braceLine)
        {
            newLineCount++;
            _examples.TryAdd(newLineCategory, braceLine, keywordLine, maxPerCategory: 2);
        }
        else
        {
            sameLineCount++;
            _examples.TryAdd(sameLineCategory, braceLine, keywordLine, maxPerCategory: 2);
        }
    }

    private static SyntaxToken? GetPrecedingCloseBrace(SyntaxToken keyword)
    {
        var prevToken = keyword.GetPreviousToken();
        if (prevToken.IsKind(SyntaxKind.CloseBraceToken))
            return prevToken;
        return null;
    }

    public DetectorResult GetResult()
    {
        var totalCatch = _newLineBeforeCatch + _sameLineBeforeCatch;
        double catchConfidence = totalCatch > 0
            ? (double)Math.Max(_newLineBeforeCatch, _sameLineBeforeCatch) / totalCatch * 100
            : 100;
        bool newLineBeforeCatch = _newLineBeforeCatch > _sameLineBeforeCatch;

        var totalElse = _newLineBeforeElse + _sameLineBeforeElse;
        double elseConfidence = totalElse > 0
            ? (double)Math.Max(_newLineBeforeElse, _sameLineBeforeElse) / totalElse * 100
            : 100;
        bool newLineBeforeElse = _newLineBeforeElse > _sameLineBeforeElse;

        var totalFinally = _newLineBeforeFinally + _sameLineBeforeFinally;
        double finallyConfidence = totalFinally > 0
            ? (double)Math.Max(_newLineBeforeFinally, _sameLineBeforeFinally) / totalFinally * 100
            : 100;
        bool newLineBeforeFinally = _newLineBeforeFinally > _sameLineBeforeFinally;

        var confidences = new List<double>();
        if (totalCatch > 0) confidences.Add(catchConfidence);
        if (totalElse > 0) confidences.Add(elseConfidence);
        if (totalFinally > 0) confidences.Add(finallyConfidence);
        double confidence = confidences.Count > 0 ? confidences.Min() : 100;

        var sampleCount = totalCatch + totalElse + totalFinally;

        var patternParts = new List<string>();
        if (totalCatch > 0)
            patternParts.Add(newLineBeforeCatch ? "newline before catch" : "same line catch");
        if (totalElse > 0)
            patternParts.Add(newLineBeforeElse ? "newline before else" : "same line else");
        if (totalFinally > 0)
            patternParts.Add(newLineBeforeFinally ? "newline before finally" : "same line finally");

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = sampleCount,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = patternParts.Count > 0 ? string.Join(", ", patternParts) : "no samples",
            Details = new Dictionary<string, object>
            {
                ["NewLineBeforeCatch"] = newLineBeforeCatch,
                ["NewLineBeforeElse"] = newLineBeforeElse,
                ["NewLineBeforeFinally"] = newLineBeforeFinally,
                ["NewLineBeforeCatchCount"] = _newLineBeforeCatch,
                ["SameLineBeforeCatchCount"] = _sameLineBeforeCatch,
                ["NewLineBeforeElseCount"] = _newLineBeforeElse,
                ["SameLineBeforeElseCount"] = _sameLineBeforeElse,
                ["NewLineBeforeFinallyCount"] = _newLineBeforeFinally,
                ["SameLineBeforeFinallyCount"] = _sameLineBeforeFinally,
                ["CatchConfidence"] = $"{catchConfidence:F1}%",
                ["ElseConfidence"] = $"{elseConfidence:F1}%",
                ["FinallyConfidence"] = $"{finallyConfidence:F1}%",
            },
            Examples = _examples.BuildMulti(
                new HashSet<string>
                {
                    newLineBeforeCatch ? "newline_catch" : "sameline_catch",
                    newLineBeforeElse ? "newline_else" : "sameline_else",
                    newLineBeforeFinally ? "newline_finally" : "sameline_finally",
                },
                new Dictionary<string, string>
                {
                    ["newline_catch"] = "newline before catch",
                    ["sameline_catch"] = "same line catch",
                    ["newline_else"] = "newline before else",
                    ["sameline_else"] = "same line else",
                    ["newline_finally"] = "newline before finally",
                    ["sameline_finally"] = "same line finally",
                }),
        };
    }
}
