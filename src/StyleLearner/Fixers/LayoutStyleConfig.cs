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
    public SpacingRule? Spacing { get; init; }
    public NewLineKeywordRule? NewLineKeywords { get; init; }
    public ContinuationIndentRule? ContinuationIndent { get; init; }
    public UsingDirectiveRule? UsingDirectives { get; init; }
    public BraceStyleRule? BraceStyle { get; init; }
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
    public bool BlankLineAfterCloseBrace { get; init; }
    public bool BlankLineAfterRegion { get; init; } = true;
    public bool BlankLineBeforeEndRegion { get; init; } = true;
}

public class SpacingRule
{
    public bool SpaceAfterCast { get; init; }
    public bool SpaceAfterKeyword { get; init; } = true;
}

public class NewLineKeywordRule
{
    public bool NewLineBeforeCatch { get; init; } = true;
    public bool NewLineBeforeElse { get; init; } = true;
    public bool NewLineBeforeFinally { get; init; } = true;
}

public class ContinuationIndentRule
{
    /// <summary>"relative" or "column"</summary>
    public string Style { get; init; } = "relative";
}

public class BraceStyleRule
{
    /// <summary>"allman" or "kr"</summary>
    public string Style { get; init; } = "allman";
}

public class UsingDirectiveRule
{
    public bool AlphabeticallySorted { get; init; } = true;
    public bool SystemFirst { get; init; } = false;
    public bool SeparateGroups { get; init; } = false;
    public string Placement { get; init; } = "outside_namespace";
}
