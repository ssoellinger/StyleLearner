using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StyleLearner.Fixers;

public static class TriviaHelper
{
    public static string GetLineIndent(SyntaxToken token)
    {
        // Walk backward through leading trivia to find whitespace at start of line
        foreach (var trivia in token.LeadingTrivia.Reverse())
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                return trivia.ToString();
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                break;
        }

        // Check if the first trivia is whitespace (beginning of file or after newline)
        if (token.LeadingTrivia.Count > 0 && token.LeadingTrivia[0].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            // Only return it if there's no preceding EndOfLine (meaning it IS the indent)
            bool hasEol = token.LeadingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            if (!hasEol)
                return token.LeadingTrivia[0].ToString();
        }

        return "";
    }

    public static string GetDeclarationIndent(SyntaxNode node)
    {
        // Walk up to find the parent declaration (class, method, etc.)
        var current = node.Parent;
        while (current != null)
        {
            if (current is Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax ||
                current is Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax ||
                current is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax)
            {
                return GetLineIndent(current.GetFirstToken());
            }

            current = current.Parent;
        }

        return "";
    }

    public static string IndentPlus(string baseIndent, int levels = 1)
    {
        // Detect indent style: if base uses tabs, add tabs; otherwise add 4 spaces per level
        if (baseIndent.Contains('\t'))
            return baseIndent + new string('\t', levels);

        return baseIndent + new string(' ', 4 * levels);
    }

    public static SyntaxTrivia EndOfLineTrivia()
    {
        return SyntaxFactory.EndOfLine("\r\n");
    }

    public static SyntaxTrivia IndentTrivia(string indent)
    {
        return SyntaxFactory.Whitespace(indent);
    }

    public static SyntaxTriviaList NewLineAndIndent(string indent)
    {
        return SyntaxFactory.TriviaList(EndOfLineTrivia(), IndentTrivia(indent));
    }

    /// <summary>
    /// Strips all trailing whitespace/newline trivia from a token's trailing trivia,
    /// keeping comments and other significant trivia.
    /// </summary>
    public static SyntaxToken WithTrailingTrimmed(SyntaxToken token)
    {
        var trailing = token.TrailingTrivia;
        var trimmed = new List<SyntaxTrivia>();

        foreach (var trivia in trailing)
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                continue;
            trimmed.Add(trivia);
        }

        return token.WithTrailingTrivia(SyntaxFactory.TriviaList(trimmed));
    }

    /// <summary>
    /// Gets the indentation of the line containing the given token by examining the source text.
    /// More reliable than trivia-based approaches for tokens in the middle of a line.
    /// </summary>
    public static string GetLineIndentFromSourceText(SyntaxToken token)
    {
        var tree = token.SyntaxTree;
        if (tree == null) return "";

        var text = tree.GetText();
        var lineNumber = text.Lines.GetLineFromPosition(token.SpanStart).LineNumber;
        var line = text.Lines[lineNumber];
        var lineText = line.ToString();

        int indentLength = lineText.Length - lineText.TrimStart().Length;
        return lineText[..indentLength];
    }

    /// <summary>
    /// Checks whether the given node spans multiple lines in the original source.
    /// </summary>
    public static bool IsMultiLine(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line > span.StartLinePosition.Line;
    }

    /// <summary>
    /// Checks whether the given node is on a single line in the original source.
    /// </summary>
    public static bool IsSingleLine(SyntaxNode node)
    {
        return !IsMultiLine(node);
    }

    /// <summary>
    /// Returns true if the line immediately above the given token in the source text is blank.
    /// This reliably detects blank lines even when they span across trailing/leading trivia.
    /// </summary>
    public static bool HasBlankLineBefore(SyntaxToken token)
    {
        var tree = token.SyntaxTree;
        if (tree == null) return false;

        var text = tree.GetText();
        var lineNum = text.Lines.GetLineFromPosition(token.SpanStart).LineNumber;
        if (lineNum <= 0) return false;

        var prevLine = text.Lines[lineNum - 1].ToString();
        return string.IsNullOrWhiteSpace(prevLine);
    }
}
