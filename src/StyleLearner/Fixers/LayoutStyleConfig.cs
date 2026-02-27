namespace StyleLearner.Fixers;

public class LayoutStyleConfig
{
    public ParameterLayoutRule? ParameterLayout { get; init; }
    public InheritanceLayoutRule? InheritanceLayout { get; init; }
    public ArrowPlacementRule? ArrowPlacement { get; init; }
    public MethodChainingRule? MethodChaining { get; init; }
    public TernaryLayoutRule? TernaryLayout { get; init; }
    public TrailingCommaRule? TrailingComma { get; init; }
    public NamespaceStyleRule? NamespaceStyle { get; init; }
    public BlankLineRule? BlankLines { get; init; }
}

public class ParameterLayoutRule
{
    public int MultilineThreshold { get; init; }
    public string ClosingParen { get; init; } = "own_line";
}

public class InheritanceLayoutRule
{
    public string ColonPlacement { get; init; } = "new_line";
}

public class ArrowPlacementRule
{
    public bool ArrowOnNewLine { get; init; }
}

public class MethodChainingRule
{
    public int ChainLengthThreshold { get; init; }
    public string BestPredictorRule { get; init; } = "";
}

public class TernaryLayoutRule
{
    public int Threshold { get; init; }
    public string DominantMultiLinePattern { get; init; } = "AlignedOperators";
    public List<string>? ContextRules { get; init; }
    public string BestPredictor { get; init; } = "expression length";
}

public class TrailingCommaRule
{
    public bool HasTrailingComma { get; init; }
}

public class NamespaceStyleRule
{
    /// <summary>"block_scoped" or "file_scoped"</summary>
    public string Style { get; init; } = "block_scoped";
}

public class BlankLineRule
{
    public int MaxConsecutiveBlankLines { get; init; } = 1;
    public bool BlankLineAfterOpenBrace { get; init; }
    public bool BlankLineBeforeCloseBrace { get; init; }
    public bool BlankLineAfterRegion { get; init; } = true;
    public bool BlankLineBeforeEndRegion { get; init; } = true;
}
