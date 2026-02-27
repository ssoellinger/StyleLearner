using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class NamespaceStyleFixer : ILayoutFixer
{
    private readonly NamespaceStyleRule _rule;

    public string Name => "Namespace Style";

    public NamespaceStyleFixer(NamespaceStyleRule rule)
    {
        _rule = rule;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        var root = (CompilationUnitSyntax)tree.GetRoot();

        if (_rule.Style == "block_scoped")
        {
            var fileScopedNs = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            if (fileScopedNs != null)
            {
                var newRoot = ConvertToBlockScoped(root, fileScopedNs);
                return new FixerResult
                {
                    Tree = tree.WithRootAndOptions(newRoot, tree.Options),
                    ChangesApplied = 1,
                };
            }
        }
        else if (_rule.Style == "file_scoped")
        {
            var blockNs = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (blockNs != null)
            {
                var newRoot = ConvertToFileScoped(root, blockNs);
                return new FixerResult
                {
                    Tree = tree.WithRootAndOptions(newRoot, tree.Options),
                    ChangesApplied = 1,
                };
            }
        }

        return new FixerResult { Tree = tree, ChangesApplied = 0 };
    }

    private static CompilationUnitSyntax ConvertToBlockScoped(
        CompilationUnitSyntax root,
        FileScopedNamespaceDeclarationSyntax fileScopedNs)
    {
        // Detect indent unit from existing code (default 4 spaces)
        var indentUnit = DetectIndentUnit(root);

        // Re-indent all members: add one indent level
        var indentedMembers = new SyntaxList<MemberDeclarationSyntax>(
            fileScopedNs.Members.Select(m => AddIndent(m, indentUnit)));

        var indentedUsings = new SyntaxList<UsingDirectiveSyntax>(
            fileScopedNs.Usings.Select(u => AddIndentToUsing(u, indentUnit)));

        var indentedExterns = new SyntaxList<ExternAliasDirectiveSyntax>(
            fileScopedNs.Externs.Select(e => AddIndentToExtern(e, indentUnit)));

        // Build block namespace: namespace Foo\n{\n    members\n}
        var blockNs = SyntaxFactory.NamespaceDeclaration(fileScopedNs.Name)
            .WithNamespaceKeyword(fileScopedNs.NamespaceKeyword)
            .WithExterns(indentedExterns)
            .WithUsings(indentedUsings)
            .WithMembers(indentedMembers)
            .WithOpenBraceToken(
                SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(TriviaHelper.NewLineAndIndent(""))
                    .WithTrailingTrivia(SyntaxFactory.TriviaList(TriviaHelper.EndOfLineTrivia())))
            .WithCloseBraceToken(
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(SyntaxFactory.TriviaList(TriviaHelper.IndentTrivia("")))
                    .WithTrailingTrivia(SyntaxFactory.TriviaList(TriviaHelper.EndOfLineTrivia())));

        // Transfer any trivia from the semicolon to the open brace
        var semiTrivia = fileScopedNs.SemicolonToken.TrailingTrivia;
        if (semiTrivia.Any())
        {
            var existingTrailing = blockNs.OpenBraceToken.TrailingTrivia;
            blockNs = blockNs.WithOpenBraceToken(
                blockNs.OpenBraceToken.WithTrailingTrivia(existingTrailing));
        }

        return root.ReplaceNode(fileScopedNs, blockNs);
    }

    private static CompilationUnitSyntax ConvertToFileScoped(
        CompilationUnitSyntax root,
        NamespaceDeclarationSyntax blockNs)
    {
        // Detect indent unit from existing code
        var indentUnit = DetectIndentUnit(root);

        // Un-indent all members: remove one indent level
        var unindentedMembers = new SyntaxList<MemberDeclarationSyntax>(
            blockNs.Members.Select(m => RemoveIndent(m, indentUnit)));

        var unindentedUsings = new SyntaxList<UsingDirectiveSyntax>(
            blockNs.Usings.Select(u => RemoveIndentFromUsing(u, indentUnit)));

        var unindentedExterns = new SyntaxList<ExternAliasDirectiveSyntax>(
            blockNs.Externs.Select(e => RemoveIndentFromExtern(e, indentUnit)));

        // Build file-scoped namespace: namespace Foo;\n
        var fileScopedNs = SyntaxFactory.FileScopedNamespaceDeclaration(blockNs.Name)
            .WithNamespaceKeyword(blockNs.NamespaceKeyword)
            .WithSemicolonToken(
                SyntaxFactory.Token(SyntaxKind.SemicolonToken)
                    .WithTrailingTrivia(SyntaxFactory.TriviaList(TriviaHelper.EndOfLineTrivia())))
            .WithExterns(unindentedExterns)
            .WithUsings(unindentedUsings)
            .WithMembers(unindentedMembers);

        return root.ReplaceNode(blockNs, fileScopedNs);
    }

    private static string DetectIndentUnit(SyntaxNode root)
    {
        // Look at the first member declaration to detect indent style
        var firstMember = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault();

        if (firstMember != null)
        {
            var indent = TriviaHelper.GetLineIndent(firstMember.GetFirstToken());
            if (indent.Length > 0)
            {
                if (indent.Contains('\t'))
                    return "\t";
                // Use the smallest indent found as the unit
                return indent.Length <= 8 ? indent : "    ";
            }
        }

        return "    ";
    }

    private static MemberDeclarationSyntax AddIndent(MemberDeclarationSyntax member, string indentUnit)
    {
        return (MemberDeclarationSyntax)IndentRewriter.AddIndent(member, indentUnit);
    }

    private static UsingDirectiveSyntax AddIndentToUsing(UsingDirectiveSyntax node, string indentUnit)
    {
        return (UsingDirectiveSyntax)IndentRewriter.AddIndent(node, indentUnit);
    }

    private static ExternAliasDirectiveSyntax AddIndentToExtern(ExternAliasDirectiveSyntax node, string indentUnit)
    {
        return (ExternAliasDirectiveSyntax)IndentRewriter.AddIndent(node, indentUnit);
    }

    private static MemberDeclarationSyntax RemoveIndent(MemberDeclarationSyntax member, string indentUnit)
    {
        return (MemberDeclarationSyntax)IndentRewriter.RemoveIndent(member, indentUnit);
    }

    private static UsingDirectiveSyntax RemoveIndentFromUsing(UsingDirectiveSyntax node, string indentUnit)
    {
        return (UsingDirectiveSyntax)IndentRewriter.RemoveIndent(node, indentUnit);
    }

    private static ExternAliasDirectiveSyntax RemoveIndentFromExtern(ExternAliasDirectiveSyntax node, string indentUnit)
    {
        return (ExternAliasDirectiveSyntax)IndentRewriter.RemoveIndent(node, indentUnit);
    }

    /// <summary>
    /// Rewrites all tokens in a node to add or remove one level of indentation.
    /// </summary>
    private class IndentRewriter : CSharpSyntaxRewriter
    {
        private readonly string _indentUnit;
        private readonly bool _add;

        private IndentRewriter(string indentUnit, bool add)
        {
            _indentUnit = indentUnit;
            _add = add;
        }

        public static SyntaxNode AddIndent(SyntaxNode node, string indentUnit)
        {
            return new IndentRewriter(indentUnit, add: true).Visit(node);
        }

        public static SyntaxNode RemoveIndent(SyntaxNode node, string indentUnit)
        {
            return new IndentRewriter(indentUnit, add: false).Visit(node);
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            // Only treat leading trivia at i==0 as indentation if the token
            // is actually the first on its line (previous token ends with EOL).
            bool startsLine = IsFirstOnLine(token);
            var newLeading = RewriteTrivia(token.LeadingTrivia, treatFirstAsIndent: startsLine);
            var newTrailing = RewriteTrivia(token.TrailingTrivia, treatFirstAsIndent: false);
            return token.WithLeadingTrivia(newLeading).WithTrailingTrivia(newTrailing);
        }

        private static bool IsFirstOnLine(SyntaxToken token)
        {
            var prevToken = token.GetPreviousToken();
            if (prevToken == default) return true;
            return prevToken.TrailingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        }

        private SyntaxTriviaList RewriteTrivia(SyntaxTriviaList triviaList, bool treatFirstAsIndent)
        {
            var result = new List<SyntaxTrivia>();
            bool previousWasEol = false;

            for (int i = 0; i < triviaList.Count; i++)
            {
                var trivia = triviaList[i];

                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    previousWasEol = true;
                    result.Add(trivia);
                    continue;
                }

                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) && (previousWasEol || (i == 0 && treatFirstAsIndent)))
                {
                    // This is line-leading whitespace — adjust indent
                    var currentIndent = trivia.ToString();
                    string newIndent;

                    if (_add)
                    {
                        newIndent = _indentUnit + currentIndent;
                    }
                    else
                    {
                        newIndent = RemoveOneLevel(currentIndent, _indentUnit);
                    }

                    result.Add(SyntaxFactory.Whitespace(newIndent));
                    previousWasEol = false;
                    continue;
                }

                // After EOL with no whitespace following — insert indent (for add)
                // or leave as-is (for remove, nothing to remove)
                if (previousWasEol && _add && !trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                {
                    // Non-whitespace right after newline — add indent before it
                    if (!trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                    {
                        result.Add(SyntaxFactory.Whitespace(_indentUnit));
                    }
                }

                previousWasEol = false;
                result.Add(trivia);
            }

            // After the loop: for LEADING trivia only, if the token starts a line
            // but has no indent whitespace yet, insert indent before the token.
            // Guard with treatFirstAsIndent (only true for leading trivia of
            // line-starting tokens) to avoid adding indent at end of trailing trivia.
            if (_add && treatFirstAsIndent && (previousWasEol || triviaList.Count == 0))
            {
                result.Add(SyntaxFactory.Whitespace(_indentUnit));
            }

            return SyntaxFactory.TriviaList(result);
        }

        private static string RemoveOneLevel(string indent, string unit)
        {
            if (unit == "\t")
            {
                return indent.StartsWith('\t') ? indent[1..] : indent;
            }

            if (indent.StartsWith(unit))
            {
                return indent[unit.Length..];
            }

            // Fallback: remove up to unit.Length spaces from the start
            int spacesToRemove = Math.Min(unit.Length, indent.Length);
            return indent[spacesToRemove..];
        }
    }
}
