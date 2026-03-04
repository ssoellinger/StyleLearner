using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StyleLearner.Fixers;

public class LayoutFixerOrchestrator
{
    private readonly LayoutStyleConfig _config;
    private readonly bool _dryRun;

    public LayoutFixerOrchestrator(LayoutStyleConfig config, bool dryRun = false)
    {
        _config = config;
        _dryRun = dryRun;
    }

    public FixSummary FixDirectory(string directoryPath, List<string> csFiles)
    {
        var summary = new FixSummary();
        var semanticFixers = BuildSemanticFixerPipeline();

        // Phase 1: If semantic fixers exist, build a compilation for type resolution
        Dictionary<string, (SyntaxTree Tree, SemanticModel Model)>? semanticContext = null;
        if (semanticFixers.Count > 0)
        {
            var trees = new List<SyntaxTree>();
            var pathToTree = new Dictionary<string, SyntaxTree>();

            foreach (var filePath in csFiles)
            {
                var sourceText = File.ReadAllText(filePath);
                var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
                trees.Add(tree);
                pathToTree[filePath] = tree;
            }

            var compilation = CompilationFactory.Create(trees, directoryPath);
            semanticContext = new Dictionary<string, (SyntaxTree, SemanticModel)>();

            foreach (var kvp in pathToTree)
            {
                var model = compilation.GetSemanticModel(kvp.Value);
                semanticContext[kvp.Key] = (kvp.Value, model);
            }
        }

        // Phase 2: Process each file — semantic fixers first, then layout fixers
        foreach (var filePath in csFiles)
        {
            var result = FixFile(filePath, semanticFixers, semanticContext);
            if (result.TotalChanges > 0)
            {
                summary.FilesChanged++;
                summary.FileResults.Add(result);
            }

            summary.FilesProcessed++;
        }

        return summary;
    }

    private FileFixResult FixFile(
        string filePath,
        List<ISemanticFixer> semanticFixers,
        Dictionary<string, (SyntaxTree Tree, SemanticModel Model)>? semanticContext)
    {
        var result = new FileFixResult { FilePath = filePath };

        SyntaxTree tree;

        // Run semantic fixers if we have a compilation context for this file
        if (semanticContext != null && semanticContext.TryGetValue(filePath, out var ctx))
        {
            tree = ctx.Tree;
            foreach (var fixer in semanticFixers)
            {
                try
                {
                    var fixerResult = fixer.Fix(tree, ctx.Model);
                    if (fixerResult.WasModified)
                    {
                        tree = fixerResult.Tree;
                        result.FixerChanges[fixer.Name] = fixerResult.ChangesApplied;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Warning: {fixer.Name} failed on {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }
        }
        else
        {
            var sourceText = File.ReadAllText(filePath);
            tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
        }

        // Run layout fixers
        var layoutFixers = BuildFixerPipeline();
        foreach (var fixer in layoutFixers)
        {
            try
            {
                var fixerResult = fixer.Fix(tree);
                if (fixerResult.WasModified)
                {
                    tree = fixerResult.Tree;
                    result.FixerChanges[fixer.Name] = fixerResult.ChangesApplied;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Warning: {fixer.Name} failed on {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        if (result.TotalChanges > 0 && !_dryRun)
        {
            var newText = tree.GetRoot().ToFullString();
            File.WriteAllText(filePath, newText);
        }

        return result;
    }

    public FileFixResult FixFile(string filePath)
    {
        return FixFile(filePath, new List<ISemanticFixer>(), null);
    }

    private List<ILayoutFixer> BuildFixerPipeline()
    {
        var fixers = new List<ILayoutFixer>();

        // Using directives first (before namespace changes could affect placement)
        if (_config.UsingDirectives != null)
            fixers.Add(new UsingDirectiveFixer(_config.UsingDirectives));

        // Namespace style (changes indentation of entire file)
        if (_config.NamespaceStyle != null)
            fixers.Add(new NamespaceStyleFixer(_config.NamespaceStyle));

        // Then: simple/independent first, complex last
        if (_config.TrailingComma != null)
            fixers.Add(new TrailingCommaFixer(_config.TrailingComma));

        if (_config.InheritanceLayout != null)
            fixers.Add(new InheritanceLayoutFixer(_config.InheritanceLayout));

        if (_config.ParameterLayout != null)
            fixers.Add(new ParameterLayoutFixer(_config.ParameterLayout));

        if (_config.ArrowPlacement != null)
            fixers.Add(new ExpressionBodyArrowFixer(_config.ArrowPlacement));

        if (_config.TernaryLayout != null)
            fixers.Add(new TernaryLayoutFixer(_config.TernaryLayout));

        if (_config.MethodChaining != null)
            fixers.Add(new MethodChainingFixer(_config.MethodChaining, _config.ContinuationIndent));

        if (_config.ContinuationIndent != null)
            fixers.Add(new ArgumentLayoutFixer(_config.ContinuationIndent));

        // Roslyn-based spacing/keyword/brace fixers (before text-based)
        if (_config.BraceStyle != null)
            fixers.Add(new BraceStyleFixer(_config.BraceStyle));

        if (_config.Spacing != null)
            fixers.Add(new SpacingFixer(_config.Spacing));

        if (_config.NewLineKeywords != null)
            fixers.Add(new NewLineKeywordFixer(_config.NewLineKeywords));

        // Text-based fixers last — they re-parse the tree
        // Whitespace always runs (no confidence needed)
        fixers.Add(new WhitespaceFixer());

        // Blank lines always runs for consecutive collapse;
        // brace/region rules only apply when config is detected
        fixers.Add(new BlankLineFixer(_config.BlankLines));

        return fixers;
    }

    private List<ISemanticFixer> BuildSemanticFixerPipeline()
    {
        var fixers = new List<ISemanticFixer>();

        if (_config.VarStyle != null)
            fixers.Add(new VarStyleFixer(_config.VarStyle.Style));

        return fixers;
    }
}

public class FixSummary
{
    public int FilesProcessed { get; set; }
    public int FilesChanged { get; set; }
    public List<FileFixResult> FileResults { get; set; } = new();

    public int TotalChanges => FileResults.Sum(r => r.TotalChanges);
}

public class FileFixResult
{
    public string FilePath { get; init; } = "";
    public Dictionary<string, int> FixerChanges { get; init; } = new();
    public int TotalChanges => FixerChanges.Values.Sum();
}
