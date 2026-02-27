using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class ArgumentLayoutFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly ContinuationIndentRule _rule;
    private int _changes;

    public string Name => "Argument Layout";

    public ArgumentLayoutFixer(ContinuationIndentRule rule)
    {
        _rule = rule;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        if (_rule.Style != "relative")
        {
            return new FixerResult { Tree = tree, ChangesApplied = 0 };
        }

        _changes = 0;
        var newRoot = Visit(tree.GetRoot());
        return new FixerResult
        {
            Tree = tree.WithRootAndOptions(newRoot, tree.Options),
            ChangesApplied = _changes,
        };
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        node = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

        var argList = node.ArgumentList;
        if (argList.Arguments.Count < 1) return node;

        var openParen = argList.OpenParenToken;
        var closeParen = argList.CloseParenToken;
        var openLine = openParen.GetLocation().GetLineSpan().StartLinePosition.Line;
        var closeLine = closeParen.GetLocation().GetLineSpan().StartLinePosition.Line;

        // Only fix multi-line argument lists
        if (closeLine == openLine) return node;

        // Find the indent of the call line
        string callIndent = TriviaHelper.GetLineIndentFromSourceText(openParen);
        string argIndent = TriviaHelper.IndentPlus(callIndent);

        // Try to collapse: if all arguments are single-line and short enough to fit
        // on one line with the method call, put them there with ')' on its own line.
        var collapsed = TryCollapseArguments(node, argList, openParen, callIndent);
        if (collapsed != null) return collapsed;

        // First pass: determine which arguments need indent changes
        bool anyArgChanged = false;
        var argChanges = new bool[argList.Arguments.Count];
        for (int i = 0; i < argList.Arguments.Count; i++)
        {
            var arg = argList.Arguments[i];
            var argLine = arg.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (argLine > openLine)
            {
                string currentArgIndent = TriviaHelper.GetLineIndentFromSourceText(arg.GetFirstToken());
                if (currentArgIndent != argIndent)
                {
                    argChanges[i] = true;
                    anyArgChanged = true;
                }
            }
        }

        // Check closing paren
        bool closeParenNeedsChange = false;
        if (closeLine > openLine)
        {
            var lastArgEndLine = argList.Arguments.Last().GetLocation().GetLineSpan().EndLinePosition.Line;
            if (closeLine > lastArgEndLine)
            {
                string closeIndent = TriviaHelper.GetLineIndentFromSourceText(closeParen);
                if (closeIndent != callIndent)
                    closeParenNeedsChange = true;
            }
        }

        if (!anyArgChanged && !closeParenNeedsChange) return node;

        // Build new argument list
        var newArgs = new List<SyntaxNodeOrToken>();
        for (int i = 0; i < argList.Arguments.Count; i++)
        {
            var arg = argList.Arguments[i];

            if (argChanges[i])
            {
                var newArg = arg.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(argIndent));
                newArgs.Add(newArg);
            }
            else
            {
                newArgs.Add(arg);
            }

            if (i < argList.Arguments.Count - 1)
            {
                var separator = argList.Arguments.GetSeparator(i);
                // Only strip comma trailing trivia when the next argument's indent changed
                if (argChanges[i + 1])
                    separator = separator.WithTrailingTrivia(SyntaxTriviaList.Empty);
                newArgs.Add(separator);
            }
        }

        // Strip trailing trivia from open paren when the first argument's indent changed
        SyntaxToken newOpenParen = openParen;
        if (argChanges.Length > 0 && argChanges[0])
            newOpenParen = openParen.WithTrailingTrivia(SyntaxTriviaList.Empty);

        // Fix closing paren indent; strip last argument's trailing trivia to avoid blank line
        SyntaxToken newCloseParen = closeParen;
        if (closeParenNeedsChange)
        {
            newCloseParen = closeParen.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(callIndent));

            // Strip trailing whitespace/newlines from last argument to prevent blank line before ')'
            if (newArgs.Count > 0 && newArgs[^1].IsNode && newArgs[^1].AsNode() is ArgumentSyntax lastArg)
            {
                var trimmedLastArg = lastArg.WithTrailingTrivia(SyntaxTriviaList.Empty);
                newArgs[^1] = trimmedLastArg;
            }
        }

        var newArgList = argList
            .WithOpenParenToken(newOpenParen)
            .WithArguments(SyntaxFactory.SeparatedList<ArgumentSyntax>(newArgs))
            .WithCloseParenToken(newCloseParen);

        _changes++;
        return node.WithArgumentList(newArgList);
    }

    /// <summary>
    /// When all arguments are single-line and fit on one line with the method call,
    /// collapse them: args on the same line as '(', close ')' on its own line at callIndent.
    /// </summary>
    private InvocationExpressionSyntax? TryCollapseArguments(
        InvocationExpressionSyntax node,
        ArgumentListSyntax argList,
        SyntaxToken openParen,
        string callIndent)
    {
        if (argList.Arguments.Count == 0) return null;

        // All arguments must be single-line
        foreach (var arg in argList.Arguments)
        {
            if (TriviaHelper.IsMultiLine(arg)) return null;
        }

        // Measure collapsed argument text: "arg1, arg2, arg3"
        var argTexts = new List<string>();
        foreach (var arg in argList.Arguments)
        {
            argTexts.Add(CollapseWhitespace(arg.ToString()));
        }

        string collapsedArgs = string.Join(", ", argTexts);

        // Measure the call prefix up to and including '('
        // The open paren column + 2 (for "( ") + args must fit in ~120 chars
        int openParenColumn = openParen.GetLocation().GetLineSpan().StartLinePosition.Character;
        int lineLength = openParenColumn + 2 + collapsedArgs.Length; // "( args..."

        if (lineLength > 120) return null;

        // Build collapsed argument list: " arg1, arg2, arg3"
        var newArgs = new List<SyntaxNodeOrToken>();
        for (int i = 0; i < argList.Arguments.Count; i++)
        {
            var arg = argList.Arguments[i];
            var cleanedArg = arg.WithLeadingTrivia(
                i == 0
                    ? SyntaxFactory.TriviaList(SyntaxFactory.Space)
                    : SyntaxFactory.TriviaList(SyntaxFactory.Space));
            cleanedArg = cleanedArg.WithTrailingTrivia(SyntaxTriviaList.Empty);
            newArgs.Add(cleanedArg);

            if (i < argList.Arguments.Count - 1)
            {
                var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                    .WithTrailingTrivia(SyntaxTriviaList.Empty);
                newArgs.Add(comma);
            }
        }

        // Open paren: strip trailing trivia
        var newOpenParen = openParen.WithTrailingTrivia(SyntaxTriviaList.Empty);

        // Close paren on own line at callIndent
        var newCloseParen = argList.CloseParenToken
            .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(callIndent));

        var newArgList = argList
            .WithOpenParenToken(newOpenParen)
            .WithArguments(SyntaxFactory.SeparatedList<ArgumentSyntax>(newArgs))
            .WithCloseParenToken(newCloseParen);

        _changes++;
        return node.WithArgumentList(newArgList);
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

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        node = (IfStatementSyntax)base.VisitIfStatement(node)!;
        return FixConditionWrapping(node, node.IfKeyword, node.OpenParenToken, node.Condition, node.CloseParenToken,
            (n, openParen, condition, closeParen) =>
                ((IfStatementSyntax)n)
                    .WithOpenParenToken(openParen)
                    .WithCondition((ExpressionSyntax)condition)
                    .WithCloseParenToken(closeParen));
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        node = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
        return FixConditionWrapping(node, node.WhileKeyword, node.OpenParenToken, node.Condition, node.CloseParenToken,
            (n, openParen, condition, closeParen) =>
                ((WhileStatementSyntax)n)
                    .WithOpenParenToken(openParen)
                    .WithCondition((ExpressionSyntax)condition)
                    .WithCloseParenToken(closeParen));
    }

    private SyntaxNode? FixConditionWrapping(
        SyntaxNode node,
        SyntaxToken keyword,
        SyntaxToken openParen,
        ExpressionSyntax condition,
        SyntaxToken closeParen,
        Func<SyntaxNode, SyntaxToken, SyntaxNode, SyntaxToken, SyntaxNode> rebuild)
    {
        // Only apply if the condition spans multiple lines
        var conditionSpan = condition.GetLocation().GetLineSpan();
        if (conditionSpan.EndLinePosition.Line == conditionSpan.StartLinePosition.Line) return node;

        var keywordLine = keyword.GetLocation().GetLineSpan().StartLinePosition.Line;
        var conditionStartLine = conditionSpan.StartLinePosition.Line;
        var closeParenLine = closeParen.GetLocation().GetLineSpan().StartLinePosition.Line;

        string keywordIndent = TriviaHelper.GetLineIndentFromSourceText(keyword);

        // If condition already starts on a different line, just fix close paren blank lines/indent
        if (conditionStartLine != keywordLine)
        {
            bool needsFix = false;

            // Check for blank line before close paren
            if (TriviaHelper.HasBlankLineBefore(closeParen))
                needsFix = true;

            // Check close paren indent
            if (closeParenLine > keywordLine)
            {
                string closeIndent = TriviaHelper.GetLineIndentFromSourceText(closeParen);
                if (closeIndent != keywordIndent)
                    needsFix = true;
            }

            if (needsFix)
            {
                // Strip trailing trivia from condition's last token to remove the extra newline
                var condLastToken = condition.GetLastToken();
                var trimmedCond = condition.ReplaceToken(
                    condLastToken, TriviaHelper.WithTrailingTrimmed(condLastToken));
                var fixedCloseParen = closeParen.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(keywordIndent));
                _changes++;
                return rebuild(node, openParen, trimmedCond, fixedCloseParen);
            }
            return node;
        }

        string conditionIndent = TriviaHelper.IndentPlus(keywordIndent);

        // Move condition to next line after '('
        var newCondition = condition.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(conditionIndent));

        // Move ')' to own line at keyword indent
        SyntaxToken newCloseParen = closeParen;
        if (closeParenLine > keywordLine)
        {
            string closeIndent = TriviaHelper.GetLineIndentFromSourceText(closeParen);
            if (closeIndent != keywordIndent)
            {
                newCloseParen = closeParen.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(keywordIndent));
            }
        }
        else
        {
            newCloseParen = closeParen.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(keywordIndent));
        }

        _changes++;
        return rebuild(node, openParen, newCondition, newCloseParen);
    }
}
