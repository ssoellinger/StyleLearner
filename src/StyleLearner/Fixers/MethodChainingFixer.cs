using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class MethodChainingFixer : ILayoutFixer
{
    private readonly MethodChainingRule _rule;
    private readonly ContinuationIndentRule? _continuationIndent;
    private int _changes;

    public string Name => "Method Chaining";

    public MethodChainingFixer(MethodChainingRule rule, ContinuationIndentRule? continuationIndent = null)
    {
        _rule = rule;
        _continuationIndent = continuationIndent;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        _changes = 0;
        var root = tree.GetRoot();

        // Phase 1: Collect all outermost invocation chains (2+ invocations) for
        // collapse/expand decisions.
        var outerChains = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => !IsPartOfOuterChain(inv))
            .Where(inv => CollectInvocationChain(inv).Count >= 2)
            .ToList();

        if (outerChains.Count > 0)
        {
            root = root.ReplaceNodes(outerChains, (original, current) =>
            {
                var result = TransformChain((InvocationExpressionSyntax)current);
                return result ?? current;
            });
        }

        // Phase 2: When continuation indent normalization is enabled, also normalize
        // single-invocation and property-access chains that span multiple lines.
        if (_continuationIndent is { Style: "relative" })
        {
            var allOuterInvocations = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(inv => !IsPartOfOuterChain(inv))
                .Where(inv =>
                {
                    // Only process invocations that have at least one member access dot
                    if (inv.Expression is not MemberAccessExpressionSyntax) return false;
                    // Only multi-line expressions
                    var span = inv.GetLocation().GetLineSpan();
                    return span.EndLinePosition.Line > span.StartLinePosition.Line;
                })
                .ToList();

            if (allOuterInvocations.Count > 0)
            {
                root = root.ReplaceNodes(allOuterInvocations, (original, current) =>
                {
                    var result = NormalizeMemberAccessIndentation((InvocationExpressionSyntax)current);
                    return result ?? current;
                });
            }
        }

        return new FixerResult
        {
            Tree = tree.WithRootAndOptions(root, tree.Options),
            ChangesApplied = _changes,
        };
    }

    private SyntaxNode? TransformChain(InvocationExpressionSyntax outermost)
    {
        var chain = CollectInvocationChain(outermost);
        if (chain.Count < 2)
            return null;

        bool shouldBeSingleLine = chain.Count <= _rule.ChainLengthThreshold;

        // Check for complex lambdas — always multi-line
        if (chain.Any(c => HasMultiLineLambda(c.Invocation)))
            shouldBeSingleLine = false;

        bool isCurrentlySingleLine = IsSingleLineChain(chain);

        if (shouldBeSingleLine && !isCurrentlySingleLine)
        {
            return CollapseChainToSingleLine(outermost, chain);
        }

        if (!shouldBeSingleLine && isCurrentlySingleLine)
        {
            return ExpandChainToMultiLine(outermost, chain);
        }

        return null; // Normalization is handled in Phase 2
    }

    /// <summary>
    /// Normalizes all member-access dots (both invocations and property accesses) in
    /// a multi-line expression to use relative (4-space) indentation.
    /// Also handles P5: assignment RHS wrapping.
    /// </summary>
    private SyntaxNode? NormalizeMemberAccessIndentation(InvocationExpressionSyntax outermost)
    {
        // Collect ALL member-access dots in the expression (invocations + property accesses)
        var allDots = CollectAllMemberAccessDots(outermost);
        if (allDots.Count == 0)
            return null;

        var baseIndent = GetChainBaseIndent(outermost);
        var chainIndent = TriviaHelper.IndentPlus(baseIndent);

        var tokensToReplace = new Dictionary<SyntaxToken, SyntaxToken>();

        // P5: When a chain starts at a variable init/assignment and the chain root
        // is on the same line as '=', move the root to the next line at chainIndent.
        var chainRoot = GetExpressionRoot(outermost);
        if (chainRoot != null)
        {
            var assignToken = FindAssignmentToken(outermost);
            if (assignToken != null)
            {
                var assignLine = assignToken.Value.GetLocation().GetLineSpan().StartLinePosition.Line;
                var rootLine = chainRoot.GetLocation().GetLineSpan().StartLinePosition.Line;

                if (assignLine == rootLine)
                {
                    var rootFirstToken = chainRoot.GetFirstToken();
                    var currentIndent = GetTokenColumnIndent(rootFirstToken);
                    if (currentIndent != chainIndent.Length)
                    {
                        var newRootToken = rootFirstToken.WithLeadingTrivia(
                            TriviaHelper.NewLineAndIndent(chainIndent));

                        var cleanedAssign = TriviaHelper.WithTrailingTrimmed(assignToken.Value);
                        tokensToReplace[assignToken.Value] = cleanedAssign;
                        tokensToReplace[rootFirstToken] = newRootToken;
                    }
                }
            }
        }

        // Normalize each dot that's on its own line to chainIndent
        foreach (var (dotToken, innerExpr) in allDots)
        {
            var dotLine = dotToken.GetLocation().GetLineSpan().StartLinePosition.Line;
            var exprEndLine = innerExpr.GetLocation().GetLineSpan().EndLinePosition.Line;

            // Only process dots that are already on their own line
            if (dotLine <= exprEndLine) continue;

            var currentIndent = GetTokenColumnIndent(dotToken);
            if (currentIndent == chainIndent.Length && !TriviaHelper.HasBlankLineBefore(dotToken))
                continue; // already correct indent and no blank line above

            var newDot = dotToken.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(chainIndent));
            tokensToReplace[dotToken] = newDot;

            // Trim trailing whitespace on previous token
            var exprLastToken = innerExpr.GetLastToken();
            if (!tokensToReplace.ContainsKey(exprLastToken))
            {
                var cleaned = TriviaHelper.WithTrailingTrimmed(exprLastToken);
                tokensToReplace[exprLastToken] = cleaned;
            }
        }

        if (tokensToReplace.Count == 0)
            return null;

        var result = outermost.ReplaceTokens(
            tokensToReplace.Keys,
            (original, _) => tokensToReplace.TryGetValue(original, out var replacement) ? replacement : original);

        _changes++;
        return result;
    }

    /// <summary>
    /// Collects ALL member-access dot tokens in the expression tree, including both
    /// invocation chains (a.B().C()) and property-access chains (a.B.C()).
    /// Returns list of (dotToken, innerExpression) pairs.
    /// </summary>
    private static List<(SyntaxToken DotToken, ExpressionSyntax InnerExpression)> CollectAllMemberAccessDots(
        ExpressionSyntax expr)
    {
        var dots = new List<(SyntaxToken, ExpressionSyntax)>();
        CollectAllDotsRecursive(expr, dots);
        return dots;
    }

    private static void CollectAllDotsRecursive(
        ExpressionSyntax expr,
        List<(SyntaxToken DotToken, ExpressionSyntax InnerExpression)> dots)
    {
        MemberAccessExpressionSyntax? memberAccess = null;

        if (expr is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax invMa)
        {
            memberAccess = invMa;
        }
        else if (expr is MemberAccessExpressionSyntax directMa)
        {
            memberAccess = directMa;
        }

        if (memberAccess != null)
        {
            dots.Add((memberAccess.OperatorToken, memberAccess.Expression));
            CollectAllDotsRecursive(memberAccess.Expression, dots);
        }
    }

    /// <summary>
    /// Gets the root expression before any member-access dots (walks through both
    /// invocations and property accesses).
    /// </summary>
    private static ExpressionSyntax? GetExpressionRoot(ExpressionSyntax expr)
    {
        ExpressionSyntax current = expr;
        while (true)
        {
            if (current is InvocationExpressionSyntax inv &&
                inv.Expression is MemberAccessExpressionSyntax invMa)
            {
                current = invMa.Expression;
            }
            else if (current is MemberAccessExpressionSyntax ma)
            {
                current = ma.Expression;
            }
            else
            {
                break;
            }
        }

        return current;
    }

    private static bool IsPartOfOuterChain(InvocationExpressionSyntax node)
    {
        var parent = node.Parent;
        if (parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Parent is InvocationExpressionSyntax)
        {
            return true;
        }

        return false;
    }

    private static List<InvocationChainCall> CollectInvocationChain(ExpressionSyntax expr)
    {
        var calls = new List<InvocationChainCall>();
        CollectInvocationChainRecursive(expr, calls);
        return calls;
    }

    private static void CollectInvocationChainRecursive(ExpressionSyntax expr, List<InvocationChainCall> calls)
    {
        if (expr is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            calls.Add(new InvocationChainCall
            {
                Invocation = invocation,
                MemberAccess = memberAccess,
                MethodName = memberAccess.Name.Identifier.Text,
                DotToken = memberAccess.OperatorToken,
            });

            CollectInvocationChainRecursive(memberAccess.Expression, calls);
        }
    }

    private static bool IsSingleLineChain(List<InvocationChainCall> chain)
    {
        if (chain.Count < 2) return true;

        var firstLine = chain[^1].DotToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var lastLine = chain[0].DotToken.GetLocation().GetLineSpan().StartLinePosition.Line;

        return firstLine == lastLine;
    }

    private static bool HasMultiLineLambda(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is LambdaExpressionSyntax lambda)
            {
                var span = lambda.GetLocation().GetLineSpan();
                if (span.EndLinePosition.Line > span.StartLinePosition.Line)
                    return true;
            }
        }

        return false;
    }

    private SyntaxNode CollapseChainToSingleLine(
        InvocationExpressionSyntax outermost,
        List<InvocationChainCall> chain)
    {
        var tokensToReplace = new Dictionary<SyntaxToken, SyntaxToken>();

        foreach (var call in chain)
        {
            var dot = call.DotToken;
            var dotLine = dot.GetLocation().GetLineSpan().StartLinePosition.Line;
            var exprEndLine = call.MemberAccess.Expression.GetLocation().GetLineSpan().EndLinePosition.Line;

            if (dotLine > exprEndLine)
            {
                var newDot = dot.WithLeadingTrivia(SyntaxTriviaList.Empty);
                tokensToReplace[dot] = newDot;

                var exprLastToken = call.MemberAccess.Expression.GetLastToken();
                if (!tokensToReplace.ContainsKey(exprLastToken))
                {
                    var cleanedExprToken = TriviaHelper.WithTrailingTrimmed(exprLastToken);
                    tokensToReplace[exprLastToken] = cleanedExprToken;
                }
            }
        }

        if (tokensToReplace.Count == 0)
            return outermost;

        var result = outermost.ReplaceTokens(
            tokensToReplace.Keys,
            (original, _) => tokensToReplace.TryGetValue(original, out var replacement) ? replacement : original);

        _changes++;
        return result;
    }

    private SyntaxNode ExpandChainToMultiLine(
        InvocationExpressionSyntax outermost,
        List<InvocationChainCall> chain)
    {
        var baseIndent = GetChainBaseIndent(outermost);
        var chainIndent = TriviaHelper.IndentPlus(baseIndent);

        var tokensToReplace = new Dictionary<SyntaxToken, SyntaxToken>();

        foreach (var call in chain)
        {
            var dot = call.DotToken;
            var dotLine = dot.GetLocation().GetLineSpan().StartLinePosition.Line;
            var exprEndLine = call.MemberAccess.Expression.GetLocation().GetLineSpan().EndLinePosition.Line;

            if (dotLine == exprEndLine)
            {
                var newDot = dot.WithLeadingTrivia(TriviaHelper.NewLineAndIndent(chainIndent));
                tokensToReplace[dot] = newDot;

                var exprLastToken = call.MemberAccess.Expression.GetLastToken();
                if (!tokensToReplace.ContainsKey(exprLastToken))
                {
                    var cleaned = TriviaHelper.WithTrailingTrimmed(exprLastToken);
                    tokensToReplace[exprLastToken] = cleaned;
                }
            }
        }

        if (tokensToReplace.Count == 0)
            return outermost;

        var result = outermost.ReplaceTokens(
            tokensToReplace.Keys,
            (original, _) => tokensToReplace.TryGetValue(original, out var replacement) ? replacement : original);

        _changes++;
        return result;
    }

    private static string GetChainBaseIndent(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is MemberDeclarationSyntax ||
                current is LocalFunctionStatementSyntax ||
                current is StatementSyntax)
            {
                return TriviaHelper.GetLineIndent(current.GetFirstToken());
            }

            current = current.Parent;
        }

        return "";
    }

    private static int GetTokenColumnIndent(SyntaxToken token)
    {
        var tree = token.SyntaxTree;
        if (tree == null) return -1;

        var text = tree.GetText();
        var lineNumber = text.Lines.GetLineFromPosition(token.SpanStart).LineNumber;
        var line = text.Lines[lineNumber].ToString();
        return line.Length - line.TrimStart().Length;
    }

    private static SyntaxToken? FindAssignmentToken(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is EqualsValueClauseSyntax equalsClause)
                return equalsClause.EqualsToken;

            if (current is AssignmentExpressionSyntax assignment)
                return assignment.OperatorToken;

            // Don't walk past statement boundaries
            if (current is StatementSyntax || current is MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }

        return null;
    }

    private record InvocationChainCall
    {
        public InvocationExpressionSyntax Invocation { get; init; } = null!;
        public MemberAccessExpressionSyntax MemberAccess { get; init; } = null!;
        public string MethodName { get; init; } = "";
        public SyntaxToken DotToken { get; init; }
    }
}
