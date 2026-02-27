using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class SpacingFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly SpacingRule _rule;
    private int _changes;

    public string Name => "Spacing";

    public SpacingFixer(SpacingRule rule)
    {
        _rule = rule;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        _changes = 0;
        var newRoot = Visit(tree.GetRoot());
        return new FixerResult
        {
            Tree = tree.WithRootAndOptions(newRoot, tree.Options),
            ChangesApplied = _changes,
        };
    }

    public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
    {
        node = (CastExpressionSyntax)base.VisitCastExpression(node)!;

        var closeParen = node.CloseParenToken;
        if (closeParen.IsMissing) return node;

        var trivia = closeParen.TrailingTrivia;
        bool hasSpace = trivia.Any(SyntaxKind.WhitespaceTrivia);

        if (_rule.SpaceAfterCast && !hasSpace)
        {
            var newToken = closeParen.WithTrailingTrivia(
                new SyntaxTriviaList(SyntaxFactory.Space).AddRange(trivia));
            _changes++;
            return node.WithCloseParenToken(newToken);
        }

        if (!_rule.SpaceAfterCast && hasSpace)
        {
            var newTrivia = trivia.Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia));
            var newToken = closeParen.WithTrailingTrivia(newTrivia);
            _changes++;
            return node.WithCloseParenToken(newToken);
        }

        return node;
    }

    public override SyntaxToken VisitToken(SyntaxToken token)
    {
        token = base.VisitToken(token);

        if (!IsControlFlowKeyword(token.Kind()))
            return token;

        // Check if next meaningful token is (
        var nextToken = token.GetNextToken();
        if (!nextToken.IsKind(SyntaxKind.OpenParenToken))
            return token;

        // Ensure they're on the same line
        var keywordLine = token.GetLocation().GetLineSpan().StartLinePosition.Line;
        var parenLine = nextToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        if (keywordLine != parenLine) return token;

        var trivia = token.TrailingTrivia;
        bool hasSpace = trivia.Any(SyntaxKind.WhitespaceTrivia);

        if (_rule.SpaceAfterKeyword && !hasSpace)
        {
            var newToken = token.WithTrailingTrivia(
                new SyntaxTriviaList(SyntaxFactory.Space).AddRange(trivia));
            _changes++;
            return newToken;
        }

        if (!_rule.SpaceAfterKeyword && hasSpace)
        {
            var newTrivia = trivia.Where(t => !t.IsKind(SyntaxKind.WhitespaceTrivia));
            var newToken = token.WithTrailingTrivia(newTrivia);
            _changes++;
            return newToken;
        }

        return token;
    }

    private static bool IsControlFlowKeyword(SyntaxKind kind)
    {
        return kind is SyntaxKind.IfKeyword
            or SyntaxKind.ForKeyword
            or SyntaxKind.ForEachKeyword
            or SyntaxKind.WhileKeyword
            or SyntaxKind.SwitchKeyword
            or SyntaxKind.UsingKeyword
            or SyntaxKind.LockKeyword
            or SyntaxKind.CatchKeyword;
    }
}
