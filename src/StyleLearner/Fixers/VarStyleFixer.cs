using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Fixers;

public interface ISemanticFixer
{
    string Name { get; }
    FixerResult Fix(SyntaxTree tree, SemanticModel model);
}

public class VarStyleFixer : CSharpSyntaxRewriter, ISemanticFixer
{
    private readonly string _targetStyle;
    private SemanticModel _model = null!;
    private int _changes;

    public string Name => "Var Style";

    /// <param name="targetStyle">"explicit" to replace var→explicit, "var" to replace explicit→var</param>
    public VarStyleFixer(string targetStyle = "explicit")
    {
        _targetStyle = targetStyle;
    }

    private static readonly SymbolDisplayFormat DisplayFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public FixerResult Fix(SyntaxTree tree, SemanticModel model)
    {
        _model = model;
        _changes = 0;
        var newRoot = Visit(tree.GetRoot());
        return new FixerResult
        {
            Tree = tree.WithRootAndOptions(newRoot, tree.Options),
            ChangesApplied = _changes,
        };
    }

    public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        // Save original type before base call rewrites children (which would
        // make the type node no longer belong to the original syntax tree)
        var originalType = node.Type;
        var originalNode = node;

        node = (VariableDeclarationSyntax)base.VisitVariableDeclaration(node)!;

        // Skip field declarations — they can't use var
        if (node.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
            return node;

        if (_targetStyle == "explicit")
        {
            if (!IsVar(originalType))
                return node;

            var resolvedType = ResolveType(originalType);
            if (resolvedType == null)
                return node;

            return ReplaceWithExplicitType(node, resolvedType);
        }
        else // "var"
        {
            if (IsVar(originalType))
                return node;

            if (!CanUseVar(originalNode))
                return node;

            // Method groups — Func<int,bool> f = int.IsEven; → var won't compile
            // Needs semantic model since syntactically it looks like any identifier
            if (IsMethodGroup(originalNode))
                return node;

            return ReplaceWithVar(node);
        }
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var originalType = node.Type;

        node = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;

        if (_targetStyle == "explicit")
        {
            if (!IsVar(originalType))
                return node;

            var typeInfo = _model.GetTypeInfo(originalType);
            var typeSymbol = typeInfo.ConvertedType ?? typeInfo.Type;

            if (!IsReplaceable(typeSymbol))
                return node;

            var typeName = typeSymbol!.ToDisplayString(DisplayFormat);
            var newType = SyntaxFactory.ParseTypeName(typeName)
                .WithLeadingTrivia(node.Type.GetLeadingTrivia())
                .WithTrailingTrivia(node.Type.GetTrailingTrivia());

            _changes++;
            return node.WithType(newType);
        }
        else // "var"
        {
            if (IsVar(originalType))
                return node;

            var varType = SyntaxFactory.IdentifierName("var")
                .WithLeadingTrivia(node.Type.GetLeadingTrivia())
                .WithTrailingTrivia(node.Type.GetTrailingTrivia());

            _changes++;
            return node.WithType(varType);
        }
    }

    private VariableDeclarationSyntax ReplaceWithExplicitType(VariableDeclarationSyntax node, string typeName)
    {
        var newType = SyntaxFactory.ParseTypeName(typeName)
            .WithLeadingTrivia(node.Type.GetLeadingTrivia())
            .WithTrailingTrivia(node.Type.GetTrailingTrivia());

        _changes++;
        return node.WithType(newType);
    }

    private VariableDeclarationSyntax ReplaceWithVar(VariableDeclarationSyntax node)
    {
        var varType = SyntaxFactory.IdentifierName("var")
            .WithLeadingTrivia(node.Type.GetLeadingTrivia())
            .WithTrailingTrivia(node.Type.GetTrailingTrivia());

        _changes++;
        return node.WithType(varType);
    }

    /// <summary>
    /// Checks whether a variable declaration can safely use var.
    /// </summary>
    private static bool CanUseVar(VariableDeclarationSyntax node)
    {
        // Must have exactly one declarator — var doesn't support: int x = 1, y = 2;
        if (node.Variables.Count != 1)
            return false;

        var declarator = node.Variables[0];

        // Must have an initializer — var requires: var x = ...;
        if (declarator.Initializer == null)
            return false;

        // Skip const declarations — const var is not allowed
        if (node.Parent is LocalDeclarationStatementSyntax localDecl &&
            localDecl.Modifiers.Any(SyntaxKind.ConstKeyword))
            return false;

        // Skip when initializer is null/default — compiler can't infer type
        // e.g. string n = null;  → var n = null;  won't compile
        //      string n = default; → var n = default; won't compile
        var initValue = declarator.Initializer.Value;
        if (initValue is LiteralExpressionSyntax literal &&
            literal.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression)
            return false;

        // null! / default! — null-forgiving doesn't help inference
        if (initValue is PostfixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax innerLiteral }
            && innerLiteral.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression)
            return false;

        // Target-typed new — List<int> list = new(); → var list = new(); won't compile
        if (initValue is ImplicitObjectCreationExpressionSyntax)
            return false;

        // Bare array initializer — int[] arr = { 1, 2, 3 }; → var arr = { 1, 2, 3 }; won't compile
        if (initValue is InitializerExpressionSyntax)
            return false;

        // Lambdas — Func<int,int> f = x => x * 2; → var changes or loses the type
        if (initValue is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            return false;

        return true;
    }

    private string? ResolveType(TypeSyntax typeSyntax)
    {
        var typeInfo = _model.GetTypeInfo(typeSyntax);
        var typeSymbol = typeInfo.ConvertedType ?? typeInfo.Type;

        if (!IsReplaceable(typeSymbol))
            return null;

        return typeSymbol!.ToDisplayString(DisplayFormat);
    }

    private static bool IsReplaceable(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // Error type — compilation couldn't resolve
        if (typeSymbol is IErrorTypeSymbol || typeSymbol.TypeKind == TypeKind.Error)
            return false;

        // Anonymous type
        if (typeSymbol.IsAnonymousType)
            return false;

        // dynamic — keep as var
        if (typeSymbol.TypeKind == TypeKind.Dynamic)
            return false;

        // Compiler-generated delegate types (C# 10+ lambda natural types)
        // These have names like <>f__AnonymousDelegate0
        if (typeSymbol.TypeKind == TypeKind.Delegate && typeSymbol.IsAnonymousType)
            return false;

        // Also catch delegates with compiler-generated names
        if (typeSymbol.Name.Contains("<>") || typeSymbol.Name.Contains("__AnonymousDelegate"))
            return false;

        return true;
    }

    private bool IsMethodGroup(VariableDeclarationSyntax node)
    {
        var initValue = node.Variables[0].Initializer?.Value;
        if (initValue == null) return false;

        var symbolInfo = _model.GetSymbolInfo(initValue);
        // A method group has no single symbol but has candidate methods
        if (symbolInfo.Symbol is IMethodSymbol)
            return true;
        if (symbolInfo.CandidateSymbols.Any(s => s is IMethodSymbol))
            return true;

        return false;
    }

    private static bool IsVar(TypeSyntax type)
    {
        return type is IdentifierNameSyntax id && id.Identifier.Text == "var";
    }
}
