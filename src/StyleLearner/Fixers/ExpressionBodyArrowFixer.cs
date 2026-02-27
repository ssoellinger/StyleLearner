using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class ExpressionBodyArrowFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly ArrowPlacementRule _rule;
    private int _changes;

    public string Name => "Expression Body Arrow";

    public ExpressionBodyArrowFixer(ArrowPlacementRule rule)
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

    public override SyntaxNode? VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
    {
        node = (ArrowExpressionClauseSyntax)base.VisitArrowExpressionClause(node)!;

        // Skip lambdas — only fix member expression bodies
        if (IsInsideLambda(node))
            return node;

        var arrowToken = node.ArrowToken;
        var previousToken = arrowToken.GetPreviousToken();

        var arrowLine = arrowToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var prevLine = previousToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        bool arrowOnNewLine = arrowLine > prevLine;

        if (_rule.ArrowOnNewLine && !arrowOnNewLine)
        {
            // Move arrow to new line
            var declIndent = GetDeclarationIndent(node);
            var arrowIndent = TriviaHelper.IndentPlus(declIndent);

            // Arrow gets: newline + indent as leading, space as trailing
            // Note: previousToken is outside this node (e.g. close paren of params),
            // so we only modify the arrow token here. The trailing whitespace on the
            // previous token is harmless (it becomes trailing whitespace before the newline).
            var newArrowToken = arrowToken
                .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(arrowIndent))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

            _changes++;
            return node.WithArrowToken(newArrowToken);
        }

        if (!_rule.ArrowOnNewLine && arrowOnNewLine)
        {
            // Move arrow to same line: space before arrow, space after
            var newArrowToken = arrowToken
                .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

            _changes++;
            return node.WithArrowToken(newArrowToken);
        }

        return node;
    }

    private static bool IsInsideLambda(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is LambdaExpressionSyntax)
                return true;
            if (current is MemberDeclarationSyntax || current is LocalFunctionStatementSyntax)
                return false;
            current = current.Parent;
        }

        return false;
    }

    private static string GetDeclarationIndent(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is MemberDeclarationSyntax ||
                current is LocalFunctionStatementSyntax)
            {
                return TriviaHelper.GetLineIndent(current.GetFirstToken());
            }

            current = current.Parent;
        }

        return "";
    }
}
