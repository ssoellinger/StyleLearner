using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public class MethodChainingFixer : ILayoutFixer
{
    private readonly MethodChainingRule _rule;
    private int _changes;

    public string Name => "Method Chaining";

    public MethodChainingFixer(MethodChainingRule rule)
    {
        _rule = rule;
    }

    public FixerResult Fix(SyntaxTree tree)
    {
        _changes = 0;
        var root = tree.GetRoot();

        // Find all outermost invocation chains
        // An outermost chain is an InvocationExpression whose parent path does NOT
        // lead to another InvocationExpression via MemberAccess (i.e., it's not the inner
        // part of a longer chain).
        var outerChains = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => !IsPartOfOuterChain(inv))
            .Where(inv => CollectChain(inv).Count >= 2)
            .ToList();

        if (outerChains.Count == 0)
        {
            return new FixerResult { Tree = tree, ChangesApplied = 0 };
        }

        var newRoot = root.ReplaceNodes(outerChains, (original, current) =>
        {
            var result = TransformChain((InvocationExpressionSyntax)current);
            return result ?? current;
        });

        return new FixerResult
        {
            Tree = tree.WithRootAndOptions(newRoot, tree.Options),
            ChangesApplied = _changes,
        };
    }

    private SyntaxNode? TransformChain(InvocationExpressionSyntax outermost)
    {
        var chain = CollectChain(outermost);
        if (chain.Count < 2)
            return null;

        bool shouldBeSingleLine = chain.Count <= _rule.ChainLengthThreshold;

        // Check for complex lambdas — always multi-line
        if (chain.Any(c => HasMultiLineLambda(c.Node)))
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

        return null; // No change needed
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

    private static List<ChainCall> CollectChain(ExpressionSyntax expr)
    {
        var calls = new List<ChainCall>();
        CollectChainRecursive(expr, calls);
        return calls;
    }

    private static void CollectChainRecursive(ExpressionSyntax expr, List<ChainCall> calls)
    {
        if (expr is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            calls.Add(new ChainCall
            {
                Node = invocation,
                MemberAccess = memberAccess,
                MethodName = memberAccess.Name.Identifier.Text,
                DotToken = memberAccess.OperatorToken,
            });

            CollectChainRecursive(memberAccess.Expression, calls);
        }
    }

    private static bool IsSingleLineChain(List<ChainCall> chain)
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
        List<ChainCall> chain)
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
        List<ChainCall> chain)
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

    private record ChainCall
    {
        public InvocationExpressionSyntax Node { get; init; } = null!;
        public MemberAccessExpressionSyntax MemberAccess { get; init; } = null!;
        public string MethodName { get; init; } = "";
        public SyntaxToken DotToken { get; init; }
    }
}
