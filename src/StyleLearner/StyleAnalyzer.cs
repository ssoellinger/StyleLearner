using Microsoft.CodeAnalysis.CSharp;
using StyleLearner.Detectors;

namespace StyleLearner;

public class StyleAnalyzer
{
    private readonly List<IStyleDetector> _detectors;
    private readonly List<string> _excludePatterns;

    public StyleAnalyzer(List<string>? excludePatterns = null)
    {
        _excludePatterns = excludePatterns ?? new List<string> { "**/obj/**", "**/bin/**", "**/*.g.cs", "**/*.Designer.cs" };
        _detectors = new List<IStyleDetector>
        {
            new IndentationDetector(),
            new BraceStyleDetector(),
            new ParameterLayoutDetector(),
            new ExpressionBodyDetector(),
            new InheritanceLayoutDetector(),
            new ObjectInitializerDetector(),
            new MethodChainingDetector(),
            new LambdaDetector(),
            new TernaryDetector(),
            new LineLengthDetector(),
            new UsingLayoutDetector(),
            new BlankLineDetector(),
            new SpacingDetector(),
            new NewLineKeywordDetector(),
            new ContinuationIndentDetector(),
        };
    }

    public StyleReport Analyze(string directoryPath)
    {
        var csFiles = FindCsFiles(directoryPath);
        int totalLines = 0;

        foreach (var filePath in csFiles)
        {
            var sourceText = File.ReadAllText(filePath);
            totalLines += sourceText.Split('\n').Length;
            var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);

            foreach (var detector in _detectors)
            {
                detector.Analyze(tree, filePath);
            }
        }

        var results = _detectors.Select(d => d.GetResult()).ToList();

        return new StyleReport
        {
            AnalyzedPath = directoryPath,
            TotalFiles = csFiles.Count,
            TotalLines = totalLines,
            Results = results,
        };
    }

    public List<string> FindCsFiles(string directoryPath)
    {
        var allFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
        return allFiles.Where(f => !IsExcluded(f, directoryPath)).ToList();
    }

    private bool IsExcluded(string filePath, string basePath)
    {
        var relativePath = Path.GetRelativePath(basePath, filePath).Replace('\\', '/');

        foreach (var pattern in _excludePatterns)
        {
            if (MatchGlob(relativePath, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchGlob(string path, string pattern)
    {
        // Simple glob matching for common patterns
        var normalizedPattern = pattern.Replace('\\', '/');

        // Handle **/ prefix (match any directory depth)
        if (normalizedPattern.StartsWith("**/"))
        {
            var suffix = normalizedPattern[3..];
            // Check if any segment matches
            if (suffix.StartsWith("*."))
            {
                var ext = suffix[1..]; // e.g., ".g.cs"
                return path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
            }

            // Match directory pattern like "**/obj/**"
            if (suffix.EndsWith("/**"))
            {
                var dirName = suffix[..^3]; // e.g., "obj"
                return path.Split('/').Any(segment =>
                    segment.Equals(dirName, StringComparison.OrdinalIgnoreCase));
            }

            return path.Contains(suffix, StringComparison.OrdinalIgnoreCase);
        }

        // Handle simple wildcard like "*.g.cs"
        if (normalizedPattern.StartsWith("*."))
        {
            var ext = normalizedPattern[1..];
            return path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        return path.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }
}
