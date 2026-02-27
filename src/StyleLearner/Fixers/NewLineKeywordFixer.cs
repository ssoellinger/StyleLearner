using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class NewLineKeywordFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly NewLineKeywordRule _rule;
    private int _changes;

    public string Name => "Newline Before Keywords";

    public NewLineKeywordFixer(NewLineKeywordRule rule)
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

    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        node = (IfStatementSyntax)base.VisitIfStatement(node)!;

        if (node.Else == null || node.Statement is not BlockSyntax block)
            return node;

        var closeBrace = block.CloseBraceToken;
        var elseKeyword = node.Else.ElseKeyword;

        var adjusted = AdjustKeywordPlacement(closeBrace, elseKeyword, _rule.NewLineBeforeElse);
        if (adjusted == null) return node;

        var newElse = node.Else.WithElseKeyword(adjusted.Value);
        _changes++;
        return node.WithElse(newElse);
    }

    public override SyntaxNode? VisitTryStatement(TryStatementSyntax node)
    {
        node = (TryStatementSyntax)base.VisitTryStatement(node)!;

        // Fix catch clauses
        var newCatches = new SyntaxList<CatchClauseSyntax>();
        bool catchChanged = false;
        foreach (var catchClause in node.Catches)
        {
            var prevToken = catchClause.CatchKeyword.GetPreviousToken();
            if (prevToken.IsKind(SyntaxKind.CloseBraceToken))
            {
                var adjusted = AdjustKeywordPlacement(prevToken, catchClause.CatchKeyword, _rule.NewLineBeforeCatch);
                if (adjusted != null)
                {
                    newCatches = newCatches.Add(catchClause.WithCatchKeyword(adjusted.Value));
                    catchChanged = true;
                    _changes++;
                    continue;
                }
            }

            newCatches = newCatches.Add(catchClause);
        }

        if (catchChanged)
            node = node.WithCatches(newCatches);

        // Fix finally clause
        if (node.Finally != null)
        {
            var prevToken = node.Finally.FinallyKeyword.GetPreviousToken();
            if (prevToken.IsKind(SyntaxKind.CloseBraceToken))
            {
                var adjusted = AdjustKeywordPlacement(prevToken, node.Finally.FinallyKeyword, _rule.NewLineBeforeFinally);
                if (adjusted != null)
                {
                    node = node.WithFinally(node.Finally.WithFinallyKeyword(adjusted.Value));
                    _changes++;
                }
            }
        }

        return node;
    }

    private static SyntaxToken? AdjustKeywordPlacement(SyntaxToken closeBrace, SyntaxToken keyword, bool wantNewLine)
    {
        if (closeBrace.IsMissing || keyword.IsMissing) return null;

        var braceLine = closeBrace.GetLocation().GetLineSpan().StartLinePosition.Line;
        var keywordLine = keyword.GetLocation().GetLineSpan().StartLinePosition.Line;
        bool isOnNewLine = keywordLine > braceLine;

        if (wantNewLine && !isOnNewLine)
        {
            // Move keyword to new line — get indentation from the close brace
            var braceIndent = GetIndentation(closeBrace);
            var newLeadingTrivia = SyntaxFactory.TriviaList(
                SyntaxFactory.EndOfLine("\n"),
                SyntaxFactory.Whitespace(braceIndent));
            return keyword.WithLeadingTrivia(newLeadingTrivia);
        }

        if (!wantNewLine && isOnNewLine)
        {
            // Move keyword to same line as }
            var newLeadingTrivia = SyntaxFactory.TriviaList(SyntaxFactory.Space);
            return keyword.WithLeadingTrivia(newLeadingTrivia);
        }

        return null;
    }

    private static string GetIndentation(SyntaxToken token)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                return trivia.ToString();
        }

        return "";
    }
}
