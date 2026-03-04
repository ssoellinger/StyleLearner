using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class VarStyleDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Var Style";

    private int _varCount;
    private int _explicitCount;
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        // Skip field/event declarations (these can't use var)
        if (node.Parent is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
        {
            base.VisitVariableDeclaration(node);
            return;
        }

        if (IsVar(node.Type))
        {
            _varCount++;
            _examples.TryAdd("var", node);
        }
        else
        {
            _explicitCount++;
            _examples.TryAdd("explicit", node);
        }

        base.VisitVariableDeclaration(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (IsVar(node.Type))
        {
            _varCount++;
            _examples.TryAdd("var", node);
        }
        else
        {
            _explicitCount++;
            _examples.TryAdd("explicit", node);
        }

        base.VisitForEachStatement(node);
    }

    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
    {
        // out var x / out int x — but skip var patterns (is var x)
        if (node.Parent is ArgumentSyntax)
        {
            if (IsVar(node.Type))
            {
                _varCount++;
                _examples.TryAdd("var", node);
            }
            else
            {
                _explicitCount++;
                _examples.TryAdd("explicit", node);
            }
        }

        base.VisitDeclarationExpression(node);
    }

    public DetectorResult GetResult()
    {
        int total = _varCount + _explicitCount;
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

        string dominant = _explicitCount >= _varCount ? "explicit" : "var";
        double confidence = (double)Math.Max(_varCount, _explicitCount) / total * 100;

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = total,
            Confidence = Math.Round(confidence, 1),
            DominantPattern = dominant,
            Details = new Dictionary<string, object>
            {
                ["Style"] = dominant,
                ["VarCount"] = _varCount,
                ["ExplicitCount"] = _explicitCount,
            },
            Examples = _examples.Build(dominant, new Dictionary<string, string>
            {
                ["var"] = "var declaration",
                ["explicit"] = "explicit type declaration",
            }),
        };
    }

    private static bool IsVar(TypeSyntax type)
    {
        return type is IdentifierNameSyntax id && id.Identifier.Text == "var";
    }
}
