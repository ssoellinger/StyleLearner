using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class BraceStyleDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Brace Style";

    private int _allmanCount;
    private int _krCount;
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        CheckBrace(node.OpenBraceToken, node.Identifier);
        base.VisitClassDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        CheckBrace(node.OpenBraceToken, node.Identifier);
        base.VisitStructDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        CheckBrace(node.OpenBraceToken, node.Identifier);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Body != null)
            CheckBrace(node.Body.OpenBraceToken, node.Identifier);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        if (node.Body != null)
            CheckBrace(node.Body.OpenBraceToken, node.Identifier);
        base.VisitConstructorDeclaration(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        if (node.Statement is BlockSyntax block)
            CheckBrace(block.OpenBraceToken, node.IfKeyword);
        base.VisitIfStatement(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (node.Statement is BlockSyntax block)
            CheckBrace(block.OpenBraceToken, node.ForEachKeyword);
        base.VisitForEachStatement(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        if (node.Statement is BlockSyntax block)
            CheckBrace(block.OpenBraceToken, node.ForKeyword);
        base.VisitForStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        if (node.Statement is BlockSyntax block)
            CheckBrace(block.OpenBraceToken, node.WhileKeyword);
        base.VisitWhileStatement(node);
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        CheckBrace(node.OpenBraceToken, node.NamespaceKeyword);
        base.VisitNamespaceDeclaration(node);
    }

    private void CheckBrace(SyntaxToken openBrace, SyntaxToken referenceToken)
    {
        if (openBrace.IsMissing || referenceToken.IsMissing) return;

        var braceLineSpan = openBrace.GetLocation().GetLineSpan();
        var refLineSpan = referenceToken.GetLocation().GetLineSpan();
        int refLine = refLineSpan.StartLinePosition.Line;
        int braceLine = braceLineSpan.StartLinePosition.Line;

        if (braceLine > refLine)
        {
            _allmanCount++;
            _examples.TryAdd("allman", refLine, braceLine);
        }
        else
        {
            _krCount++;
            _examples.TryAdd("kr", refLine, braceLine);
        }
    }

    public DetectorResult GetResult()
    {
        var total = _allmanCount + _krCount;
        var style = _allmanCount >= _krCount ? "allman" : "k&r";
        var confidence = total > 0
            ? (double)Math.Max(_allmanCount, _krCount) / total * 100
            : 0;

        var labels = new Dictionary<string, string>
        {
            ["allman"] = "Allman — brace on new line",
            ["kr"] = "K&R — brace on same line",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = style,
            Details = new Dictionary<string, object>
            {
                ["Style"] = style,
                ["AllmanCount"] = _allmanCount,
                ["KRCount"] = _krCount,
            },
            Examples = _examples.Build(style == "allman" ? "allman" : "kr", labels),
        };
    }
}
