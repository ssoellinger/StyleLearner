using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class ContinuationIndentDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Continuation Indent";

    private int _relativeChainDot;
    private int _columnAlignedChainDot;
    private int _relativeArgument;
    private int _columnAlignedArgument;
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Only process outermost invocations in a chain
        if (!IsPartOfOuterChain(node))
        {
            AnalyzeChainDots(node);
            AnalyzeArguments(node);
        }

        base.VisitInvocationExpression(node);
    }

    private void AnalyzeChainDots(InvocationExpressionSyntax outermost)
    {
        var chain = CollectChain(outermost);
        if (chain.Count < 2) return;

        // Find the parent statement indent
        var statementIndent = GetStatementIndent(outermost);
        if (statementIndent == null) return;

        int expectedRelative = statementIndent.Length + 4;
        if (statementIndent.Contains('\t'))
            expectedRelative = statementIndent.Length + 1; // tab-based: +1 tab

        foreach (var dotToken in chain)
        {
            var dotLine = dotToken.GetLocation().GetLineSpan().StartLinePosition.Line;
            var tree = dotToken.SyntaxTree;
            if (tree == null) continue;

            var text = tree.GetText();
            var line = text.Lines[dotLine];
            var lineText = line.ToString();
            int dotIndent = lineText.Length - lineText.TrimStart().Length;

            // Check if this dot is on its own line (not on the same line as the preceding expression)
            var prevToken = dotToken.GetPreviousToken();
            var prevLine = prevToken.GetLocation().GetLineSpan().EndLinePosition.Line;
            if (dotLine == prevLine) continue; // dot is on same line as previous — skip

            if (statementIndent.Contains('\t'))
            {
                // For tab-based indent, count tabs
                int tabCount = lineText.TakeWhile(c => c == '\t').Count();
                int stmtTabs = statementIndent.Count(c => c == '\t');
                if (tabCount == stmtTabs + 1)
                    _relativeChainDot++;
                else
                    _columnAlignedChainDot++;
            }
            else
            {
                if (dotIndent == statementIndent.Length + 4)
                    _relativeChainDot++;
                else
                    _columnAlignedChainDot++;
            }

            _examples.TryAdd(
                dotIndent == expectedRelative ? "relative_chain" : "column_chain",
                outermost, contextBefore: 1);
        }
    }

    private void AnalyzeArguments(InvocationExpressionSyntax node)
    {
        var argList = node.ArgumentList;
        if (argList.Arguments.Count < 2) return;

        var openParen = argList.OpenParenToken;
        var closeParen = argList.CloseParenToken;
        var openLine = openParen.GetLocation().GetLineSpan().StartLinePosition.Line;
        var closeLine = closeParen.GetLocation().GetLineSpan().StartLinePosition.Line;

        // Only care about multi-line argument lists
        if (closeLine == openLine) return;

        // Find the first argument that's on a different line than the open paren
        ArgumentSyntax? firstWrappedArg = null;
        foreach (var arg in argList.Arguments)
        {
            var argStartLine = arg.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (argStartLine > openLine)
            {
                firstWrappedArg = arg;
                break;
            }
        }

        if (firstWrappedArg == null) return;

        var tree = node.SyntaxTree;
        if (tree == null) return;

        var text = tree.GetText();

        // Get the indent of the call line (the line containing the method name)
        var callLineNumber = openLine;
        var callLine = text.Lines[callLineNumber].ToString();
        int callIndent = callLine.Length - callLine.TrimStart().Length;

        // Get the indent of the first wrapped argument
        var argLineNumber = firstWrappedArg.GetLocation().GetLineSpan().StartLinePosition.Line;
        var argLine = text.Lines[argLineNumber].ToString();
        int argIndent = argLine.Length - argLine.TrimStart().Length;

        // Column of the open paren + 1
        int openParenColumn = openParen.GetLocation().GetLineSpan().StartLinePosition.Character + 1;

        bool isRelative = argIndent == callIndent + 4;
        bool isColumnAligned = argIndent == openParenColumn;

        if (isRelative)
            _relativeArgument++;
        else if (isColumnAligned)
            _columnAlignedArgument++;
        else
            // Could be some other pattern — count as column for now
            _columnAlignedArgument++;

        _examples.TryAdd(
            isRelative ? "relative_arg" : "column_arg",
            node, contextBefore: 1);
    }

    private static List<SyntaxToken> CollectChain(ExpressionSyntax expr)
    {
        var dots = new List<SyntaxToken>();
        CollectChainRecursive(expr, dots);
        return dots;
    }

    private static void CollectChainRecursive(ExpressionSyntax expr, List<SyntaxToken> dots)
    {
        if (expr is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            dots.Add(memberAccess.OperatorToken);
            CollectChainRecursive(memberAccess.Expression, dots);
        }
    }

    private static bool IsPartOfOuterChain(InvocationExpressionSyntax node)
    {
        return node.Parent is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Parent is InvocationExpressionSyntax;
    }

    private static string? GetStatementIndent(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is MemberDeclarationSyntax ||
                current is LocalFunctionStatementSyntax ||
                current is StatementSyntax)
            {
                return Fixers.TriviaHelper.GetLineIndent(current.GetFirstToken());
            }

            current = current.Parent;
        }

        return null;
    }

    public DetectorResult GetResult()
    {
        int totalChain = _relativeChainDot + _columnAlignedChainDot;
        int totalArg = _relativeArgument + _columnAlignedArgument;
        int total = totalChain + totalArg;

        if (total == 0)
        {
            return new DetectorResult
            {
                DetectorName = Name,
                SampleCount = 0,
                Confidence = 0,
                DominantPattern = "no data",
                Details = new Dictionary<string, object>(),
            };
        }

        double chainRelativePct = totalChain > 0 ? (double)_relativeChainDot / totalChain * 100 : 0;
        double argRelativePct = totalArg > 0 ? (double)_relativeArgument / totalArg * 100 : 0;

        // Combined: weighted average
        int totalRelative = _relativeChainDot + _relativeArgument;
        int totalColumn = _columnAlignedChainDot + _columnAlignedArgument;
        double overallRelativePct = (double)totalRelative / total * 100;

        string chainStyle = totalChain > 0
            ? (_relativeChainDot >= _columnAlignedChainDot ? "relative" : "column")
            : "unknown";
        string argStyle = totalArg > 0
            ? (_relativeArgument >= _columnAlignedArgument ? "relative" : "column")
            : "unknown";
        string overallStyle = totalRelative >= totalColumn ? "relative" : "column";

        double confidence = Math.Max(overallRelativePct, 100 - overallRelativePct);

        var details = new Dictionary<string, object>
        {
            ["ChainDotStyle"] = chainStyle,
            ["ChainDotRelativeCount"] = _relativeChainDot,
            ["ChainDotColumnCount"] = _columnAlignedChainDot,
            ["ChainDotConfidence"] = totalChain > 0 ? $"{Math.Max(chainRelativePct, 100 - chainRelativePct):F1}%" : "n/a",
            ["ArgumentStyle"] = argStyle,
            ["ArgumentRelativeCount"] = _relativeArgument,
            ["ArgumentColumnCount"] = _columnAlignedArgument,
            ["ArgumentConfidence"] = totalArg > 0 ? $"{Math.Max(argRelativePct, 100 - argRelativePct):F1}%" : "n/a",
            ["OverallStyle"] = overallStyle,
        };

        var labels = new Dictionary<string, string>
        {
            ["relative_chain"] = "relative chain dot indent",
            ["column_chain"] = "column-aligned chain dot indent",
            ["relative_arg"] = "relative argument indent",
            ["column_arg"] = "column-aligned argument indent",
        };

        var conforming = new HashSet<string>();
        if (overallStyle == "relative")
        {
            conforming.Add("relative_chain");
            conforming.Add("relative_arg");
        }
        else
        {
            conforming.Add("column_chain");
            conforming.Add("column_arg");
        }

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = $"{overallStyle} continuation indent",
            Details = details,
            Examples = _examples.BuildMulti(conforming, labels),
        };
    }
}
