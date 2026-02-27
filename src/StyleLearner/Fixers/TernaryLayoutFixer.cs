using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class TernaryLayoutFixer : ILayoutFixer
{
    private readonly TernaryLayoutRule _rule;
    private int _changes;

    public string Name => "Ternary Layout";

    public TernaryLayoutFixer(TernaryLayoutRule rule)
    {
        _rule = rule;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        _changes = 0;
        var root = tree.GetRoot();

        // Find all outermost conditional expressions (not nested inside another ternary)
        var outerTernaries = root.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .Where(n => n.Parent is not ConditionalExpressionSyntax)
            .ToList();

        if (outerTernaries.Count == 0)
        {
            return new FixerResult { Tree = tree, ChangesApplied = 0 };
        }

        var newRoot = root.ReplaceNodes(outerTernaries, (original, current) =>
        {
            var result = TransformTernary((ConditionalExpressionSyntax)current);
            return result ?? current;
        });

        return new FixerResult
        {
            Tree = tree.WithRootAndOptions(newRoot, tree.Options),
            ChangesApplied = _changes,
        };
    }

    private ConditionalExpressionSyntax? TransformTernary(ConditionalExpressionSyntax node)
    {
        int expressionLength = MeasureUnwrappedLength(node);
        bool shouldBeSingleLine = expressionLength <= _rule.Threshold;

        // Determine current layout
        var conditionEndLine = GetEndLine(node.Condition);
        var questionLine = GetStartLine(node.QuestionToken);
        var colonLine = GetStartLine(node.ColonToken);
        var whenTrueEndLine = GetEndLine(node.WhenTrue);

        bool questionOnNewLine = questionLine > conditionEndLine;
        bool colonOnNewLine = colonLine > whenTrueEndLine;
        bool isSingleLine = !questionOnNewLine && !colonOnNewLine;

        if (shouldBeSingleLine && !isSingleLine)
        {
            return CollapseToSingleLine(node);
        }

        if (!shouldBeSingleLine && isSingleLine)
        {
            return ExpandToMultiLine(node);
        }

        // Already multi-line — check if the pattern matches the dominant one
        if (!shouldBeSingleLine && !isSingleLine &&
            _rule.DominantMultiLinePattern == "AlignedOperators")
        {
            return FixAlignedOperators(node, questionOnNewLine, colonOnNewLine);
        }

        return null; // No change needed
    }

    private ConditionalExpressionSyntax CollapseToSingleLine(ConditionalExpressionSyntax node)
    {
        var newQuestion = node.QuestionToken
            .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

        var newColon = node.ColonToken
            .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

        var newCondition = node.Condition.WithTrailingTrivia(SyntaxTriviaList.Empty);
        var newWhenTrue = node.WhenTrue
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithTrailingTrivia(SyntaxTriviaList.Empty);
        var newWhenFalse = node.WhenFalse
            .WithLeadingTrivia(SyntaxTriviaList.Empty);

        _changes++;
        return node
            .WithCondition(newCondition)
            .WithQuestionToken(newQuestion)
            .WithWhenTrue(newWhenTrue)
            .WithColonToken(newColon)
            .WithWhenFalse(newWhenFalse);
    }

    private ConditionalExpressionSyntax ExpandToMultiLine(ConditionalExpressionSyntax node)
    {
        // Use the condition's line indent (not the declaration indent) so that
        // ? and : are indented relative to where the condition starts.
        var conditionIndent = GetConditionLineIndent(node);
        var operatorIndent = TriviaHelper.IndentPlus(conditionIndent);

        if (_rule.DominantMultiLinePattern == "AlignedOperators")
        {
            return FormatAlignedOperators(node, operatorIndent);
        }

        return FormatBothOnNewLine(node, operatorIndent);
    }

    private ConditionalExpressionSyntax FormatAlignedOperators(
        ConditionalExpressionSyntax node,
        string operatorIndent)
    {
        var newCondition = node.Condition.WithTrailingTrivia(SyntaxTriviaList.Empty);

        var newQuestion = node.QuestionToken
            .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(operatorIndent))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

        var newWhenTrue = node.WhenTrue
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithTrailingTrivia(SyntaxTriviaList.Empty);

        var newColon = node.ColonToken
            .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(operatorIndent))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

        var newWhenFalse = node.WhenFalse
            .WithLeadingTrivia(SyntaxTriviaList.Empty);

        _changes++;
        return node
            .WithCondition(newCondition)
            .WithQuestionToken(newQuestion)
            .WithWhenTrue(newWhenTrue)
            .WithColonToken(newColon)
            .WithWhenFalse(newWhenFalse);
    }

    private ConditionalExpressionSyntax FormatBothOnNewLine(
        ConditionalExpressionSyntax node,
        string operatorIndent)
    {
        var newCondition = node.Condition.WithTrailingTrivia(SyntaxTriviaList.Empty);

        var newQuestion = node.QuestionToken
            .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(operatorIndent))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

        var newWhenTrue = node.WhenTrue
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithTrailingTrivia(SyntaxTriviaList.Empty);

        var newColon = node.ColonToken
            .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(operatorIndent))
            .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

        var newWhenFalse = node.WhenFalse
            .WithLeadingTrivia(SyntaxTriviaList.Empty);

        _changes++;
        return node
            .WithCondition(newCondition)
            .WithQuestionToken(newQuestion)
            .WithWhenTrue(newWhenTrue)
            .WithColonToken(newColon)
            .WithWhenFalse(newWhenFalse);
    }

    private ConditionalExpressionSyntax? FixAlignedOperators(
        ConditionalExpressionSyntax node,
        bool questionOnNewLine,
        bool colonOnNewLine)
    {
        if (questionOnNewLine && colonOnNewLine)
        {
            var questionCol = node.QuestionToken.GetLocation().GetLineSpan().StartLinePosition.Character;
            var colonCol = node.ColonToken.GetLocation().GetLineSpan().StartLinePosition.Character;

            if (questionCol == colonCol)
                return null; // Already aligned

            var operatorIndent = new string(' ', questionCol);

            var newColon = node.ColonToken
                .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(operatorIndent))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

            var newWhenTrue = node.WhenTrue
                .WithTrailingTrivia(SyntaxTriviaList.Empty);

            _changes++;
            return node
                .WithWhenTrue(newWhenTrue)
                .WithColonToken(newColon);
        }

        var conditionIndent = GetConditionLineIndent(node);
        var indent = TriviaHelper.IndentPlus(conditionIndent);
        return FormatAlignedOperators(node, indent);
    }

    private static int MeasureUnwrappedLength(ConditionalExpressionSyntax node)
    {
        var conditionText = CollapseWhitespace(node.Condition.ToString());
        var whenTrueText = CollapseWhitespace(node.WhenTrue.ToString());
        var whenFalseText = CollapseWhitespace(node.WhenFalse.ToString());
        return conditionText.Length + 3 + whenTrueText.Length + 3 + whenFalseText.Length;
    }

    /// <summary>
    /// Gets the indentation of the line where the ternary condition starts.
    /// This ensures ? and : are indented relative to the condition, not the declaration.
    /// </summary>
    private static string GetConditionLineIndent(ConditionalExpressionSyntax node)
    {
        // Use the condition's first token to find its line indent
        var conditionFirstToken = node.Condition.GetFirstToken();
        return TriviaHelper.GetLineIndentFromSourceText(conditionFirstToken);
    }

    private static int GetStartLine(SyntaxToken token)
    {
        return token.GetLocation().GetLineSpan().StartLinePosition.Line;
    }

    private static int GetEndLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().EndLinePosition.Line;
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
}
