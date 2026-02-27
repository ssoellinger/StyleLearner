using Microsoft.CodeAnalysis;

namespace StyleLearner.Fixers;

public interface ILayoutFixer
{
    string Name { get; }
    FixerResult Fix(SyntaxTree tree);
}

public class FixerResult
{
    public SyntaxTree Tree { get; init; } = null!;
    public int ChangesApplied { get; init; }
    public bool WasModified => ChangesApplied > 0;
}
