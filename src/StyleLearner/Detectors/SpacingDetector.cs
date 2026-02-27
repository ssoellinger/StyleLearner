using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class SpacingDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Spacing";

    private int _spaceAfterCast;
    private int _noSpaceAfterCast;
    private int _spaceAfterKeyword;
    private int _noSpaceAfterKeyword;

    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitCastExpression(CastExpressionSyntax node)
    {
        var closeParen = node.CloseParenToken;
        if (!closeParen.IsMissing)
        {
            var trivia = closeParen.TrailingTrivia;
            bool hasSpace = trivia.Any(SyntaxKind.WhitespaceTrivia);
            if (hasSpace)
            {
                _spaceAfterCast++;
                _examples.TryAdd("space_after_cast", node, maxPerCategory: 2);
            }
            else
            {
                _noSpaceAfterCast++;
                _examples.TryAdd("no_space_after_cast", node, maxPerCategory: 2);
            }
        }

        base.VisitCastExpression(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        CheckKeywordSpacing(node.IfKeyword, node.OpenParenToken);
        base.VisitIfStatement(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        CheckKeywordSpacing(node.ForKeyword, node.OpenParenToken);
        base.VisitForStatement(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        CheckKeywordSpacing(node.ForEachKeyword, node.OpenParenToken);
        base.VisitForEachStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        CheckKeywordSpacing(node.WhileKeyword, node.OpenParenToken);
        base.VisitWhileStatement(node);
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        CheckKeywordSpacing(node.SwitchKeyword, node.OpenParenToken);
        base.VisitSwitchStatement(node);
    }

    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        CheckKeywordSpacing(node.UsingKeyword, node.OpenParenToken);
        base.VisitUsingStatement(node);
    }

    public override void VisitLockStatement(LockStatementSyntax node)
    {
        CheckKeywordSpacing(node.LockKeyword, node.OpenParenToken);
        base.VisitLockStatement(node);
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        if (node.Declaration != null)
        {
            CheckKeywordSpacing(node.CatchKeyword, node.Declaration.OpenParenToken);
        }

        base.VisitCatchClause(node);
    }

    private void CheckKeywordSpacing(SyntaxToken keyword, SyntaxToken openParen)
    {
        if (keyword.IsMissing || openParen.IsMissing) return;

        // Check if keyword and open paren are on the same line
        var keywordLine = keyword.GetLocation().GetLineSpan().StartLinePosition.Line;
        var parenLine = openParen.GetLocation().GetLineSpan().StartLinePosition.Line;
        if (keywordLine != parenLine) return;

        var trivia = keyword.TrailingTrivia;
        bool hasSpace = trivia.Any(SyntaxKind.WhitespaceTrivia);
        if (hasSpace)
            _spaceAfterKeyword++;
        else
            _noSpaceAfterKeyword++;
    }

    public DetectorResult GetResult()
    {
        var totalCast = _spaceAfterCast + _noSpaceAfterCast;
        double castConfidence = totalCast > 0
            ? (double)Math.Max(_spaceAfterCast, _noSpaceAfterCast) / totalCast * 100
            : 100;
        bool spaceAfterCast = _spaceAfterCast > _noSpaceAfterCast;

        var totalKeyword = _spaceAfterKeyword + _noSpaceAfterKeyword;
        double keywordConfidence = totalKeyword > 0
            ? (double)Math.Max(_spaceAfterKeyword, _noSpaceAfterKeyword) / totalKeyword * 100
            : 100;
        bool spaceAfterKeyword = _spaceAfterKeyword > _noSpaceAfterKeyword;

        double confidence = Math.Min(castConfidence, keywordConfidence);
        var sampleCount = totalCast + totalKeyword;

        var patternParts = new List<string>();
        if (totalCast > 0)
            patternParts.Add(spaceAfterCast ? "space after cast" : "no space after cast");
        if (totalKeyword > 0)
            patternParts.Add(spaceAfterKeyword ? "space after keyword" : "no space after keyword");

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = sampleCount,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = string.Join(", ", patternParts),
            Details = new Dictionary<string, object>
            {
                ["SpaceAfterCast"] = spaceAfterCast,
                ["SpaceAfterKeyword"] = spaceAfterKeyword,
                ["SpaceAfterCastCount"] = _spaceAfterCast,
                ["NoSpaceAfterCastCount"] = _noSpaceAfterCast,
                ["SpaceAfterKeywordCount"] = _spaceAfterKeyword,
                ["NoSpaceAfterKeywordCount"] = _noSpaceAfterKeyword,
                ["CastConfidence"] = $"{castConfidence:F1}%",
                ["KeywordConfidence"] = $"{keywordConfidence:F1}%",
            },
            Examples = _examples.BuildMulti(
                new HashSet<string>
                {
                    spaceAfterCast ? "space_after_cast" : "no_space_after_cast",
                },
                new Dictionary<string, string>
                {
                    ["space_after_cast"] = "space after cast )",
                    ["no_space_after_cast"] = "no space after cast )",
                }),
        };
    }
}
