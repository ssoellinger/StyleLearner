using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class InheritanceLayoutDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Inheritance Layout";

    private int _sameLineCount;
    private int _newLineCount;
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        CheckBaseList(node.BaseList, node.Identifier);
        base.VisitClassDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        CheckBaseList(node.BaseList, node.Identifier);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        CheckBaseList(node.BaseList, node.Identifier);
        base.VisitStructDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        CheckBaseList(node.BaseList, node.Identifier);
        base.VisitRecordDeclaration(node);
    }

    private void CheckBaseList(BaseListSyntax? baseList, SyntaxToken identifierToken)
    {
        if (baseList == null) return;

        var colonLine = baseList.ColonToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var identLine = identifierToken.GetLocation().GetLineSpan().StartLinePosition.Line;

        if (colonLine > identLine)
        {
            _newLineCount++;
            _examples.TryAdd("new_line", identLine, colonLine + (baseList.Types.Count > 1 ? baseList.Types.Count - 1 : 0));
        }
        else
        {
            _sameLineCount++;
            _examples.TryAdd("same_line", identLine, colonLine);
        }
    }

    public DetectorResult GetResult()
    {
        var total = _sameLineCount + _newLineCount;
        var placement = _newLineCount > _sameLineCount ? "new_line" : "same_line";
        var confidence = total > 0
            ? (double)Math.Max(_sameLineCount, _newLineCount) / total * 100
            : 0;

        var labels = new Dictionary<string, string>
        {
            ["new_line"] = "colon on new line",
            ["same_line"] = "colon on same line",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = $"colon on {placement}",
            Details = new Dictionary<string, object>
            {
                ["ColonPlacement"] = placement,
                ["SameLineCount"] = _sameLineCount,
                ["NewLineCount"] = _newLineCount,
            },
            Examples = _examples.Build(placement, labels),
        };
    }
}
