using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class BraceStyleFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly BraceStyleRule _rule;
    private int _changes;

    public string Name => "Brace Style";

    public BraceStyleFixer(BraceStyleRule rule)
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

    public override SyntaxToken VisitToken(SyntaxToken token)
    {
        if (!token.IsKind(SyntaxKind.OpenBraceToken))
            return base.VisitToken(token);

        var parent = token.Parent;
        if (parent == null) return token;

        // Find the owner node — the declaration or statement this brace belongs to
        var ownerNode = GetBraceOwner(token, parent);
        if (ownerNode == null) return token;

        // Find the reference token for line comparison
        var refToken = GetReferenceToken(ownerNode);
        if (refToken == default || refToken.IsMissing) return token;

        // Skip single-line constructs (e.g., "catch { }", "interface IFoo { }")
        if (IsSingleLineConstruct(token, parent))
            return token;

        var braceLine = token.GetLocation().GetLineSpan().StartLinePosition.Line;
        var refLine = refToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        bool isOnNewLine = braceLine > refLine;
        bool wantNewLine = _rule.Style == "allman";

        if (wantNewLine == isOnNewLine) return token;

        if (wantNewLine)
        {
            // Move brace to new line with indentation matching the owning declaration/statement
            var indent = TriviaHelper.GetLineIndent(ownerNode.GetFirstToken());

            // Preserve any comments in the brace's existing leading trivia
            var preservedTrivia = new List<SyntaxTrivia>();
            foreach (var trivia in token.LeadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                {
                    preservedTrivia.Add(trivia);
                }
            }

            var newTrivia = new List<SyntaxTrivia>();
            if (preservedTrivia.Count > 0)
            {
                // Comment before brace: put comment on its own line, then brace on next line
                newTrivia.Add(SyntaxFactory.EndOfLine("\n"));
                newTrivia.Add(SyntaxFactory.Whitespace(indent));
                newTrivia.AddRange(preservedTrivia);
                newTrivia.Add(SyntaxFactory.EndOfLine("\n"));
                newTrivia.Add(SyntaxFactory.Whitespace(indent));
            }
            else
            {
                newTrivia.Add(SyntaxFactory.EndOfLine("\n"));
                newTrivia.Add(SyntaxFactory.Whitespace(indent));
            }

            _changes++;
            return token.WithLeadingTrivia(SyntaxFactory.TriviaList(newTrivia));
        }
        else
        {
            // K&R: move brace to same line as the reference, with a space before it
            _changes++;
            return token.WithLeadingTrivia(SyntaxFactory.Space);
        }
    }

    private static bool IsSingleLineConstruct(SyntaxToken openBrace, SyntaxNode parent)
    {
        // For BlockSyntax: check if the block's open and close brace are on the same line
        if (parent is BlockSyntax block && !block.CloseBraceToken.IsMissing)
        {
            var openLine = openBrace.GetLocation().GetLineSpan().StartLinePosition.Line;
            var closeLine = block.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line;
            return openLine == closeLine;
        }

        // For type/namespace declarations: check if open and close brace are on same line
        SyntaxToken closeBrace = parent switch
        {
            ClassDeclarationSyntax n => n.CloseBraceToken,
            StructDeclarationSyntax n => n.CloseBraceToken,
            InterfaceDeclarationSyntax n => n.CloseBraceToken,
            EnumDeclarationSyntax n => n.CloseBraceToken,
            RecordDeclarationSyntax n => n.CloseBraceToken,
            NamespaceDeclarationSyntax n => n.CloseBraceToken,
            SwitchStatementSyntax n => n.CloseBraceToken,
            _ => default,
        };

        if (closeBrace != default && !closeBrace.IsMissing)
        {
            var openLine = openBrace.GetLocation().GetLineSpan().StartLinePosition.Line;
            var closeLine = closeBrace.GetLocation().GetLineSpan().StartLinePosition.Line;
            return openLine == closeLine;
        }

        return false;
    }

    private static SyntaxNode? GetBraceOwner(SyntaxToken braceToken, SyntaxNode parent)
    {
        // Type declarations and switch — brace is directly on the node
        if (parent is ClassDeclarationSyntax
            or StructDeclarationSyntax
            or InterfaceDeclarationSyntax
            or EnumDeclarationSyntax
            or RecordDeclarationSyntax
            or NamespaceDeclarationSyntax
            or SwitchStatementSyntax)
        {
            return parent;
        }

        // Block syntax — brace is on the block, owner is the block's parent
        if (parent is BlockSyntax)
        {
            var grandParent = parent.Parent;
            if (grandParent is MethodDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DestructorDeclarationSyntax
                or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                or IfStatementSyntax
                or ElseClauseSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or WhileStatementSyntax
                or DoStatementSyntax
                or TryStatementSyntax
                or CatchClauseSyntax
                or FinallyClauseSyntax
                or UsingStatementSyntax
                or LockStatementSyntax
                or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax)
            {
                return grandParent;
            }
        }

        return null;
    }

    private static SyntaxToken GetReferenceToken(SyntaxNode ownerNode)
    {
        return ownerNode switch
        {
            ClassDeclarationSyntax n => n.Identifier,
            StructDeclarationSyntax n => n.Identifier,
            InterfaceDeclarationSyntax n => n.Identifier,
            EnumDeclarationSyntax n => n.Identifier,
            RecordDeclarationSyntax n => n.Identifier,
            NamespaceDeclarationSyntax n => n.NamespaceKeyword,
            MethodDeclarationSyntax n => n.Identifier,
            ConstructorDeclarationSyntax n => n.Identifier,
            DestructorDeclarationSyntax n => n.Identifier,
            OperatorDeclarationSyntax n => n.OperatorKeyword,
            ConversionOperatorDeclarationSyntax n => n.OperatorKeyword,
            IfStatementSyntax n => n.IfKeyword,
            ElseClauseSyntax n => n.ElseKeyword,
            ForStatementSyntax n => n.ForKeyword,
            ForEachStatementSyntax n => n.ForEachKeyword,
            WhileStatementSyntax n => n.WhileKeyword,
            DoStatementSyntax n => n.DoKeyword,
            TryStatementSyntax n => n.TryKeyword,
            CatchClauseSyntax n => n.CatchKeyword,
            FinallyClauseSyntax n => n.FinallyKeyword,
            UsingStatementSyntax n => n.UsingKeyword,
            LockStatementSyntax n => n.LockKeyword,
            SwitchStatementSyntax n => n.SwitchKeyword,
            LocalFunctionStatementSyntax n => n.Identifier,
            AccessorDeclarationSyntax n => n.Keyword,
            _ => default,
        };
    }
}
