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

        foreach (var filePath in csFiles)
        {
            var result = FixFile(filePath);
            if (result.TotalChanges > 0)
            {
                summary.FilesChanged++;
                summary.FileResults.Add(result);
            }

            summary.FilesProcessed++;
        }

        return summary;
    }

    public FileFixResult FixFile(string filePath)
    {
        var sourceText = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
        var result = new FileFixResult { FilePath = filePath };

        var fixers = BuildFixerPipeline();

        foreach (var fixer in fixers)
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

    private List<ILayoutFixer> BuildFixerPipeline()
    {
        var fixers = new List<ILayoutFixer>();

        // Namespace style first (changes indentation of entire file)
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
            fixers.Add(new MethodChainingFixer(_config.MethodChaining));

        // Blank lines last — it re-parses the tree from text
        if (_config.BlankLines != null)
            fixers.Add(new BlankLineFixer(_config.BlankLines));

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
