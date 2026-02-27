using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class ParameterLayoutFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly ParameterLayoutRule _rule;
    private int _changes;

    public string Name => "Parameter Layout";

    public ParameterLayoutFixer(ParameterLayoutRule rule)
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

    public override SyntaxNode? VisitParameterList(ParameterListSyntax node)
    {
        node = (ParameterListSyntax)base.VisitParameterList(node)!;

        if (node.Parameters.Count < _rule.MultilineThreshold)
            return node;

        // Determine if currently single-line
        var openLine = node.OpenParenToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var closeLine = node.CloseParenToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var firstParamLine = node.Parameters.First().GetLocation().GetLineSpan().StartLinePosition.Line;
        var lastParamLine = node.Parameters.Last().GetLocation().GetLineSpan().EndLinePosition.Line;

        bool isSingleLine = closeLine == openLine;
        bool isAlreadyMultiLine = lastParamLine > firstParamLine || firstParamLine > openLine;
        bool closeParenOnOwnLine = closeLine > lastParamLine;

        // Get the indent of the containing declaration
        string declIndent = GetContainingDeclIndent(node);
        string paramIndent = TriviaHelper.IndentPlus(declIndent);

        if (isSingleLine)
        {
            // Reformat: each param on its own line, closing paren based on rule
            return ReformatToMultiLine(node, declIndent, paramIndent);
        }

        if (isAlreadyMultiLine)
        {
            // Already multi-line — only fix closing paren placement if needed
            if (_rule.ClosingParen == "own_line" && !closeParenOnOwnLine)
            {
                return FixClosingParen(node, declIndent);
            }

            if (_rule.ClosingParen == "same_line" && closeParenOnOwnLine)
            {
                return FixClosingParenSameLine(node);
            }
        }

        return node;
    }

    private ParameterListSyntax ReformatToMultiLine(
        ParameterListSyntax node,
        string declIndent,
        string paramIndent)
    {
        var newParams = new List<SyntaxNodeOrToken>();

        for (int i = 0; i < node.Parameters.Count; i++)
        {
            var param = node.Parameters[i];

            // Each parameter gets: newline + indent as leading trivia
            var newParam = param.WithLeadingTrivia(
                TriviaHelper.NewLineAndIndent(paramIndent));

            // Strip trailing whitespace from param
            if (i < node.Parameters.Count - 1)
            {
                newParam = newParam.WithTrailingTrivia(SyntaxTriviaList.Empty);
            }

            newParams.Add(newParam);

            // Add comma separator between params (not after the last)
            if (i < node.Parameters.Count - 1)
            {
                var comma = SyntaxFactory.Token(SyntaxKind.CommaToken)
                    .WithTrailingTrivia(SyntaxTriviaList.Empty);
                newParams.Add(comma);
            }
        }

        var newParamList = SyntaxFactory.SeparatedList<ParameterSyntax>(newParams);

        // Closing paren
        SyntaxToken newCloseParen;
        if (_rule.ClosingParen == "own_line")
        {
            newCloseParen = node.CloseParenToken
                .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(declIndent));
        }
        else
        {
            // Same line as last param — just a close paren
            newCloseParen = node.CloseParenToken
                .WithLeadingTrivia(SyntaxTriviaList.Empty);
        }

        _changes++;
        return node
            .WithParameters(newParamList)
            .WithCloseParenToken(newCloseParen);
    }

    private ParameterListSyntax FixClosingParen(ParameterListSyntax node, string declIndent)
    {
        // Move closing paren to its own line
        var newCloseParen = node.CloseParenToken
            .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(declIndent));

        _changes++;
        return node.WithCloseParenToken(newCloseParen);
    }

    private ParameterListSyntax FixClosingParenSameLine(ParameterListSyntax node)
    {
        // Move closing paren to same line as last parameter
        var newCloseParen = node.CloseParenToken
            .WithLeadingTrivia(SyntaxTriviaList.Empty);

        _changes++;
        return node.WithCloseParenToken(newCloseParen);
    }

    private static string GetContainingDeclIndent(SyntaxNode node)
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
