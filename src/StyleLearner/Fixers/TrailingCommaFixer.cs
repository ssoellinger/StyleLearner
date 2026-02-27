using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class TrailingCommaFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly TrailingCommaRule _rule;
    private int _changes;

    public string Name => "Trailing Comma";

    public TrailingCommaFixer(TrailingCommaRule rule)
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

    public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        node = (InitializerExpressionSyntax)base.VisitInitializerExpression(node)!;

        if (!node.IsKind(SyntaxKind.ObjectInitializerExpression) &&
            !node.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            return node;
        }

        if (node.Expressions.Count == 0)
            return node;

        var separators = node.Expressions.GetSeparators().ToList();
        bool hasTrailingComma = separators.Count >= node.Expressions.Count;

        if (!_rule.HasTrailingComma && hasTrailingComma)
        {
            // Remove trailing comma — transfer its trivia to the last expression
            var lastExpression = node.Expressions.Last();
            var trailingComma = separators.Last();

            // Transfer any trivia from the comma to the last expression's trailing trivia
            var commaTrailingTrivia = trailingComma.TrailingTrivia;
            var lastExprWithTrivia = lastExpression.WithTrailingTrivia(
                lastExpression.GetTrailingTrivia().AddRange(commaTrailingTrivia));

            // Build new separated list without the trailing comma
            var newExpressions = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < node.Expressions.Count; i++)
            {
                if (i > 0)
                    newExpressions.Add(separators[i - 1]);

                if (i == node.Expressions.Count - 1)
                    newExpressions.Add(lastExprWithTrivia);
                else
                    newExpressions.Add(node.Expressions[i]);
            }

            var newList = SyntaxFactory.SeparatedList<ExpressionSyntax>(newExpressions);
            _changes++;
            return node.WithExpressions(newList);
        }

        if (_rule.HasTrailingComma && !hasTrailingComma)
        {
            // Add trailing comma after last expression
            var lastExpression = node.Expressions.Last();

            // Move trailing trivia from last expression to the new comma
            var trailingTrivia = lastExpression.GetTrailingTrivia();
            var cleanedLast = lastExpression.WithTrailingTrivia(SyntaxTriviaList.Empty);
            var newComma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(trailingTrivia);

            var newExpressions = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < node.Expressions.Count; i++)
            {
                if (i > 0)
                    newExpressions.Add(separators[i - 1]);

                if (i == node.Expressions.Count - 1)
                    newExpressions.Add(cleanedLast);
                else
                    newExpressions.Add(node.Expressions[i]);
            }

            newExpressions.Add(newComma);

            var newList = SyntaxFactory.SeparatedList<ExpressionSyntax>(newExpressions);
            _changes++;
            return node.WithExpressions(newList);
        }

        return node;
    }
}
