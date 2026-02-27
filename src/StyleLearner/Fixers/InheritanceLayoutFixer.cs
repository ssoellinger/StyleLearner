using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class InheritanceLayoutFixer : CSharpSyntaxRewriter, ILayoutFixer
{
    private readonly InheritanceLayoutRule _rule;
    private int _changes;

    public string Name => "Inheritance Layout";

    public InheritanceLayoutFixer(InheritanceLayoutRule rule)
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

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        node = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
        if (node.BaseList == null) return node;

        var fixedBaseList = FixBaseList(node.BaseList, node);
        if (fixedBaseList == null) return node;

        return node.WithBaseList(fixedBaseList);
    }

    public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        node = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!;
        if (node.BaseList == null) return node;

        var fixedBaseList = FixBaseList(node.BaseList, node);
        if (fixedBaseList == null) return node;

        return node.WithBaseList(fixedBaseList);
    }

    public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
    {
        node = (StructDeclarationSyntax)base.VisitStructDeclaration(node)!;
        if (node.BaseList == null) return node;

        var fixedBaseList = FixBaseList(node.BaseList, node);
        if (fixedBaseList == null) return node;

        return node.WithBaseList(fixedBaseList);
    }

    public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        node = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!;
        if (node.BaseList == null) return node;

        var fixedBaseList = FixBaseList(node.BaseList, node);
        if (fixedBaseList == null) return node;

        return node.WithBaseList(fixedBaseList);
    }

    private BaseListSyntax? FixBaseList(BaseListSyntax baseList, SyntaxNode declaration)
    {
        var colonToken = baseList.ColonToken;
        var declIndent = TriviaHelper.GetLineIndent(declaration.GetFirstToken());

        // Find the reference token: the last token before the colon on the declaration line.
        // This could be the identifier, type parameter list close, or record parameter list close.
        var tokenBeforeColon = colonToken.GetPreviousToken();
        var colonLine = colonToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var refLine = tokenBeforeColon.GetLocation().GetLineSpan().StartLinePosition.Line;
        bool colonOnNewLine = colonLine > refLine;

        if (_rule.ColonPlacement == "new_line" && !colonOnNewLine)
        {
            // Move colon to new line with indent + 4
            var newIndent = TriviaHelper.IndentPlus(declIndent);

            // Colon gets: EndOfLine + Indent as leading, space as trailing
            // Note: tokenBeforeColon is outside BaseListSyntax, so we only modify
            // the colon token here. Trailing whitespace before newline is harmless.
            var newColonToken = colonToken
                .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(newIndent))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

            _changes++;
            return baseList.WithColonToken(newColonToken);
        }

        if (_rule.ColonPlacement == "same_line" && colonOnNewLine)
        {
            // Move colon to same line as identifier: space before colon, space after
            var newColonToken = colonToken
                .WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space))
                .WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.Space));

            _changes++;
            return baseList.WithColonToken(newColonToken);
        }

        return null;
    }
}
