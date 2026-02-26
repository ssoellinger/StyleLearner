using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StyleLearner.Detectors;

public class ExpressionBodyDetector : CSharpSyntaxWalker, IStyleDetector
{
    public string Name => "Expression Body";

    private readonly List<MemberSample> _methodSamples = new();
    private int _propertyExpressionBody;
    private int _propertyBlockBody;
    private int _arrowSameLineCount;
    private int _arrowNewLineCount;
    private readonly ExampleCollector _examples = new();

    public void Analyze(SyntaxTree tree, string filePath)
    {
        _examples.SetContext(tree, filePath);
        Visit(tree.GetRoot());
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        bool isOverride = node.Modifiers.Any(SyntaxKind.OverrideKeyword);
        bool isVirtual = node.Modifiers.Any(SyntaxKind.VirtualKeyword);
        bool isAsync = node.Modifiers.Any(SyntaxKind.AsyncKeyword);

        if (node.ExpressionBody != null)
        {
            CheckArrowPlacement(node.ExpressionBody, node.ParameterList.CloseParenToken);
            int exprLength = CollapseWhitespace(node.ExpressionBody.Expression.ToString()).Length;

            _methodSamples.Add(new MemberSample
            {
                UsesExpressionBody = true,
                IsAsync = isAsync,
                IsOverride = isOverride,
                IsVirtual = isVirtual,
                MemberKind = MemberKind.Method,
                ExpressionLength = exprLength,
            });

            _examples.TryAdd("expr_body", node);
        }
        else if (node.Body != null)
        {
            var analysis = AnalyzeBlockBody(node.Body);
            _methodSamples.Add(new MemberSample
            {
                UsesExpressionBody = false,
                IsAsync = isAsync,
                IsOverride = isOverride,
                IsVirtual = isVirtual,
                MemberKind = MemberKind.Method,
                StatementCount = analysis.StatementCount,
                HasControlFlow = analysis.HasControlFlow,
                HasLocalDeclarations = analysis.HasLocalDeclarations,
                HasTryCatch = analysis.HasTryCatch,
                IsEmpty = analysis.IsEmpty,
                IsSingleReturn = analysis.IsSingleReturn,
                IsSingleExpression = analysis.IsSingleExpression,
                IsSingleThrow = analysis.IsSingleThrow,
                ExpressionLength = analysis.SingleExpressionLength,
                Complexity = analysis.Complexity,
            });

            if (analysis.Complexity == BlockComplexity.SingleExpression)
                _examples.TryAdd("block_single_expr", node);
        }

        base.VisitMethodDeclaration(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        bool hasBaseInitializer = node.Initializer != null;

        if (node.ExpressionBody != null)
        {
            CheckArrowPlacement(node.ExpressionBody, node.ParameterList.CloseParenToken);
            int exprLength = CollapseWhitespace(node.ExpressionBody.Expression.ToString()).Length;

            _methodSamples.Add(new MemberSample
            {
                UsesExpressionBody = true,
                MemberKind = MemberKind.Constructor,
                HasBaseInitializer = hasBaseInitializer,
                ExpressionLength = exprLength,
            });
        }
        else if (node.Body != null)
        {
            var analysis = AnalyzeBlockBody(node.Body);
            _methodSamples.Add(new MemberSample
            {
                UsesExpressionBody = false,
                MemberKind = MemberKind.Constructor,
                HasBaseInitializer = hasBaseInitializer,
                StatementCount = analysis.StatementCount,
                HasControlFlow = analysis.HasControlFlow,
                HasLocalDeclarations = analysis.HasLocalDeclarations,
                HasTryCatch = analysis.HasTryCatch,
                IsEmpty = analysis.IsEmpty,
                IsSingleReturn = analysis.IsSingleReturn,
                IsSingleExpression = analysis.IsSingleExpression,
                IsSingleThrow = analysis.IsSingleThrow,
                ExpressionLength = analysis.SingleExpressionLength,
                Complexity = analysis.Complexity,
            });
        }

        base.VisitConstructorDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (node.ExpressionBody != null)
        {
            _propertyExpressionBody++;
            CheckArrowPlacement(node.ExpressionBody, node.Identifier);
        }
        else if (node.AccessorList != null)
        {
            _propertyBlockBody++;
        }

        base.VisitPropertyDeclaration(node);
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        if (node.ExpressionBody != null)
        {
            CheckArrowPlacement(node.ExpressionBody, node.ParameterList.CloseParenToken);
            int exprLength = CollapseWhitespace(node.ExpressionBody.Expression.ToString()).Length;

            _methodSamples.Add(new MemberSample
            {
                UsesExpressionBody = true,
                MemberKind = MemberKind.LocalFunction,
                ExpressionLength = exprLength,
            });
        }
        else if (node.Body != null)
        {
            var analysis = AnalyzeBlockBody(node.Body);
            _methodSamples.Add(new MemberSample
            {
                UsesExpressionBody = false,
                MemberKind = MemberKind.LocalFunction,
                StatementCount = analysis.StatementCount,
                HasControlFlow = analysis.HasControlFlow,
                HasLocalDeclarations = analysis.HasLocalDeclarations,
                HasTryCatch = analysis.HasTryCatch,
                IsEmpty = analysis.IsEmpty,
                IsSingleReturn = analysis.IsSingleReturn,
                IsSingleExpression = analysis.IsSingleExpression,
                IsSingleThrow = analysis.IsSingleThrow,
                ExpressionLength = analysis.SingleExpressionLength,
                Complexity = analysis.Complexity,
            });
        }

        base.VisitLocalFunctionStatement(node);
    }

    private static BlockAnalysis AnalyzeBlockBody(BlockSyntax block)
    {
        int statementCount = block.Statements.Count;
        bool isEmpty = statementCount == 0;

        bool hasControlFlow = block.Statements.Any(s =>
            s is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                or WhileStatementSyntax or SwitchStatementSyntax);

        bool hasTryCatch = block.Statements.Any(s => s is TryStatementSyntax);
        bool hasLocalDeclarations = block.Statements.Any(s => s is LocalDeclarationStatementSyntax);

        bool isSingleReturn = statementCount == 1 && block.Statements[0] is ReturnStatementSyntax;
        bool isSingleExpression = statementCount == 1 && block.Statements[0] is ExpressionStatementSyntax;
        bool isSingleThrow = statementCount == 1 && block.Statements[0] is ThrowStatementSyntax;

        // Measure the expression length if it's a single return/expression
        int singleExpressionLength = 0;
        if (isSingleReturn && block.Statements[0] is ReturnStatementSyntax ret && ret.Expression != null)
        {
            singleExpressionLength = CollapseWhitespace(ret.Expression.ToString()).Length;
        }
        else if (isSingleExpression && block.Statements[0] is ExpressionStatementSyntax expr)
        {
            singleExpressionLength = CollapseWhitespace(expr.Expression.ToString()).Length;
        }

        // Classify complexity
        var complexity = BlockComplexity.Complex;
        if (isEmpty)
            complexity = BlockComplexity.Empty;
        else if (statementCount == 1 && !hasControlFlow && !hasTryCatch && !hasLocalDeclarations)
            complexity = isSingleReturn || isSingleExpression
                ? BlockComplexity.SingleExpression
                : isSingleThrow
                    ? BlockComplexity.SingleThrow
                    : BlockComplexity.Other;
        else if (statementCount > 1 || hasControlFlow || hasTryCatch || hasLocalDeclarations)
            complexity = BlockComplexity.Complex;

        return new BlockAnalysis
        {
            StatementCount = statementCount,
            HasControlFlow = hasControlFlow,
            HasTryCatch = hasTryCatch,
            HasLocalDeclarations = hasLocalDeclarations,
            IsEmpty = isEmpty,
            IsSingleReturn = isSingleReturn,
            IsSingleExpression = isSingleExpression,
            IsSingleThrow = isSingleThrow,
            SingleExpressionLength = singleExpressionLength,
            Complexity = complexity,
        };
    }

    private void CheckArrowPlacement(ArrowExpressionClauseSyntax arrow, SyntaxToken referenceToken)
    {
        var arrowLine = arrow.ArrowToken.GetLocation().GetLineSpan().StartLinePosition.Line;
        var refLine = referenceToken.GetLocation().GetLineSpan().StartLinePosition.Line;

        if (arrowLine > refLine)
        {
            _arrowNewLineCount++;
            _examples.TryAdd("arrow_new_line", refLine, arrow.Expression.GetLocation().GetLineSpan().EndLinePosition.Line);
        }
        else
        {
            _arrowSameLineCount++;
            _examples.TryAdd("arrow_same_line", refLine, arrow.Expression.GetLocation().GetLineSpan().EndLinePosition.Line);
        }
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inWhitespace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace) { sb.Append(' '); inWhitespace = true; }
            }
            else { sb.Append(c); inWhitespace = false; }
        }

        return sb.ToString().Trim();
    }

    public DetectorResult GetResult()
    {
        if (_methodSamples.Count == 0)
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

        var exprBodySamples = _methodSamples.Where(s => s.UsesExpressionBody).ToList();
        var blockBodySamples = _methodSamples.Where(s => !s.UsesExpressionBody).ToList();

        // Classify block-body methods by why they use block body
        int blockComplex = blockBodySamples.Count(s => s.Complexity == BlockComplexity.Complex);
        int blockEmpty = blockBodySamples.Count(s => s.Complexity == BlockComplexity.Empty);
        int blockSingleThrow = blockBodySamples.Count(s => s.Complexity == BlockComplexity.SingleThrow);
        int blockSingleExpr = blockBodySamples.Count(s => s.Complexity == BlockComplexity.SingleExpression);
        int blockOther = blockBodySamples.Count(s => s.Complexity == BlockComplexity.Other);

        // The truly ambiguous cases: block body with a single expression that COULD be =>
        // These are the ones where style choice matters
        var ambiguousBlock = blockBodySamples.Where(s => s.Complexity == BlockComplexity.SingleExpression).ToList();

        // Predict using multiple rules and pick the best
        var predictors = new List<PredictorCandidate>();

        // Rule 1: Base rule — block when complex/empty/throw, expression otherwise
        predictors.Add(ScoreBaseRule(exprBodySamples, blockBodySamples));

        // Rule 2: Base rule + override/virtual methods always use block
        predictors.Add(ScoreOverrideRule(exprBodySamples, blockBodySamples));

        // Rule 3: Base rule + constructor with multiple assignments stays block
        predictors.Add(ScoreConstructorRule(exprBodySamples, blockBodySamples));

        // Rule 4: Block body preferred — when expression body rate is very low (<10%),
        // the project deliberately avoids expression body for methods
        predictors.Add(ScoreBlockBodyPreferredRule(exprBodySamples, blockBodySamples, ambiguousBlock));

        var best = predictors.OrderByDescending(p => p.Accuracy).First();

        // Arrow placement
        var arrowTotal = _arrowSameLineCount + _arrowNewLineCount;
        var arrowOnNewLine = _arrowNewLineCount > _arrowSameLineCount;
        var arrowConfidence = arrowTotal > 0
            ? (double)Math.Max(_arrowSameLineCount, _arrowNewLineCount) / arrowTotal * 100
            : 0;

        // Build pattern description
        var rules = new List<string>();
        rules.AddRange(best.Rules);
        rules.Add($"arrow on {(arrowOnNewLine ? "new line" : "same line")}");

        if (_propertyBlockBody > 0 && _propertyExpressionBody == 0)
            rules.Add("properties: never expression body");
        else if (_propertyExpressionBody > _propertyBlockBody)
            rules.Add("properties: expression body preferred");

        var details = new Dictionary<string, object>
        {
            ["MethodExpressionBodyCount"] = exprBodySamples.Count,
            ["MethodBlockBodyCount"] = blockBodySamples.Count,
            ["BlockComplex"] = blockComplex,
            ["BlockEmpty"] = blockEmpty,
            ["BlockSingleThrow"] = blockSingleThrow,
            ["BlockSingleExpression"] = blockSingleExpr,
            ["BlockOther"] = blockOther,
            ["AmbiguousBlockMethods"] = ambiguousBlock.Count,
            ["PropertyExpressionBody"] = _propertyExpressionBody,
            ["PropertyBlockBody"] = _propertyBlockBody,
            ["ArrowOnNewLine"] = arrowOnNewLine,
            ["ArrowSameLineCount"] = _arrowSameLineCount,
            ["ArrowNewLineCount"] = _arrowNewLineCount,
            ["ArrowConfidence"] = $"{arrowConfidence:F1}%",
        };

        foreach (var p in predictors)
        {
            details[$"Predictor_{p.Name}"] = $"accuracy={p.Accuracy:F1}%";
        }

        // Breakdown of the ambiguous cases
        if (ambiguousBlock.Count > 0)
        {
            details["Ambiguous_Override"] = ambiguousBlock.Count(s => s.IsOverride);
            details["Ambiguous_Virtual"] = ambiguousBlock.Count(s => s.IsVirtual);
            details["Ambiguous_AvgExprLength"] = (int)ambiguousBlock.Average(s => s.ExpressionLength);
            details["Ambiguous_Constructor"] = ambiguousBlock.Count(s => s.MemberKind == MemberKind.Constructor);
        }

        // When block-body-preferred wins, block_single_expr is conforming (not non-conforming)
        var conformingCats = new HashSet<string> { arrowOnNewLine ? "arrow_new_line" : "arrow_same_line" };
        if (best.Name == "block-body-preferred")
            conformingCats.Add("block_single_expr");
        else
            conformingCats.Add("expr_body");

        var exLabels = new Dictionary<string, string>
        {
            ["expr_body"] = "expression body method",
            ["block_single_expr"] = "block body — could be expression body",
            ["arrow_new_line"] = "arrow (=>) on new line",
            ["arrow_same_line"] = "arrow (=>) on same line",
        };

        return new DetectorResult
        {
            DetectorName = Name,
            SampleCount = _methodSamples.Count + _propertyExpressionBody + _propertyBlockBody,
            Confidence = Math.Round(best.Accuracy, 1),
            DominantPattern = string.Join("; ", rules),
            Details = details,
            Examples = _examples.BuildMulti(conformingCats, exLabels),
        };
    }

    private PredictorCandidate ScoreBaseRule(List<MemberSample> exprBody, List<MemberSample> blockBody)
    {
        // Block when: complex, empty, single throw, other
        // Expression when: single expression with no complexity
        int correct = exprBody.Count; // all expression body = correct

        foreach (var s in blockBody)
        {
            bool predictBlock = s.Complexity != BlockComplexity.SingleExpression;
            if (predictBlock) correct++;
        }

        double accuracy = (double)correct / _methodSamples.Count * 100;

        return new PredictorCandidate
        {
            Name = "base",
            Accuracy = accuracy,
            Rules = new List<string>
            {
                "expression body for single-expression methods",
                "block body for complex/empty/multi-statement methods",
            },
        };
    }

    private PredictorCandidate ScoreOverrideRule(List<MemberSample> exprBody, List<MemberSample> blockBody)
    {
        // Same as base, but override/virtual methods always predict block
        int correct = 0;

        foreach (var s in exprBody)
        {
            if (s.IsOverride || s.IsVirtual)
            {
                // We'd predict block for override, but it used expression — wrong
            }
            else
            {
                correct++;
            }
        }

        foreach (var s in blockBody)
        {
            bool predictBlock = s.Complexity != BlockComplexity.SingleExpression
                || s.IsOverride || s.IsVirtual;
            if (predictBlock) correct++;
        }

        double accuracy = (double)correct / _methodSamples.Count * 100;

        return new PredictorCandidate
        {
            Name = "override-aware",
            Accuracy = accuracy,
            Rules = new List<string>
            {
                "expression body for single-expression non-override methods",
                "block body for complex/empty/override/virtual methods",
            },
        };
    }

    private PredictorCandidate ScoreConstructorRule(List<MemberSample> exprBody, List<MemberSample> blockBody)
    {
        // Base rule, but constructors with >1 statement always block,
        // and constructors with base initializer always block
        int correct = exprBody.Count;

        foreach (var s in blockBody)
        {
            bool predictBlock = s.Complexity != BlockComplexity.SingleExpression;

            // Constructor-specific: block if has base initializer or multiple statements
            if (!predictBlock && s.MemberKind == MemberKind.Constructor && s.HasBaseInitializer)
                predictBlock = true;

            if (predictBlock) correct++;
        }

        double accuracy = (double)correct / _methodSamples.Count * 100;

        return new PredictorCandidate
        {
            Name = "constructor-aware",
            Accuracy = accuracy,
            Rules = new List<string>
            {
                "expression body for single-expression methods",
                "block body for complex/empty/multi-statement methods",
                "constructors with base() initializer: block body",
            },
        };
    }

    private PredictorCandidate ScoreBlockBodyPreferredRule(
        List<MemberSample> exprBody, List<MemberSample> blockBody, List<MemberSample> ambiguousBlock)
    {
        // Calculate expression body rate for methods:
        // exprBodyCount / (exprBodyCount + ambiguousBlockCount)
        int exprBodyCount = exprBody.Count;
        int ambiguousCount = ambiguousBlock.Count;
        double exprBodyRate = (exprBodyCount + ambiguousCount) > 0
            ? (double)exprBodyCount / (exprBodyCount + ambiguousCount)
            : 0;

        if (exprBodyRate < 0.10)
        {
            // Project prefers block body for methods — predict block for ALL methods
            // Correct: all block body samples (including ambiguous ones)
            // Wrong: all expression body samples (the rare exceptions)
            int correct = blockBody.Count;
            double accuracy = (double)correct / _methodSamples.Count * 100;

            return new PredictorCandidate
            {
                Name = "block-body-preferred",
                Accuracy = accuracy,
                Rules = new List<string>
                {
                    "block body for all methods",
                    "expression body for properties only",
                },
            };
        }

        // Expression body rate is not low enough — this predictor is not applicable,
        // return with 0 accuracy so it won't be selected
        return new PredictorCandidate
        {
            Name = "block-body-preferred",
            Accuracy = 0,
            Rules = new List<string> { "n/a — expression body rate too high" },
        };
    }

    private record MemberSample
    {
        public bool UsesExpressionBody { get; init; }
        public bool IsAsync { get; init; }
        public bool IsOverride { get; init; }
        public bool IsVirtual { get; init; }
        public bool HasBaseInitializer { get; init; }
        public MemberKind MemberKind { get; init; }
        public int StatementCount { get; init; }
        public bool HasControlFlow { get; init; }
        public bool HasLocalDeclarations { get; init; }
        public bool HasTryCatch { get; init; }
        public bool IsEmpty { get; init; }
        public bool IsSingleReturn { get; init; }
        public bool IsSingleExpression { get; init; }
        public bool IsSingleThrow { get; init; }
        public int ExpressionLength { get; init; }
        public BlockComplexity Complexity { get; init; }
    }

    private record BlockAnalysis
    {
        public int StatementCount { get; init; }
        public bool HasControlFlow { get; init; }
        public bool HasTryCatch { get; init; }
        public bool HasLocalDeclarations { get; init; }
        public bool IsEmpty { get; init; }
        public bool IsSingleReturn { get; init; }
        public bool IsSingleExpression { get; init; }
        public bool IsSingleThrow { get; init; }
        public int SingleExpressionLength { get; init; }
        public BlockComplexity Complexity { get; init; }
    }

    private enum MemberKind
    {
        Method,
        Constructor,
        LocalFunction,
    }

    private enum BlockComplexity
    {
        Empty,
        SingleExpression,
        SingleThrow,
        Other,
        Complex,
    }

    private record PredictorCandidate
    {
        public string Name { get; init; } = "";
        public double Accuracy { get; init; }
        public List<string> Rules { get; init; } = new();
    }
}
