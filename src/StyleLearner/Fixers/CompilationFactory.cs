using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StyleLearner.Fixers;

public static class CompilationFactory
{
    public static CSharpCompilation Create(IEnumerable<SyntaxTree> trees, string? targetPath = null)
    {
        var references = GetReferences(targetPath);
        return CSharpCompilation.Create(
            "StyleLearnerAnalysis",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static List<MetadataReference> GetReferences(string? targetPath)
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: .NET runtime assemblies from TRUSTED_PLATFORM_ASSEMBLIES
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa != null)
        {
            var separator = Path.PathSeparator;
            foreach (var assemblyPath in tpa.Split(separator))
            {
                if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                    continue;

                var name = Path.GetFileNameWithoutExtension(assemblyPath);
                references[name] = MetadataReference.CreateFromFile(assemblyPath);
            }
        }

        // Phase 2: project-specific DLLs from bin/ folders
        if (targetPath != null && Directory.Exists(targetPath))
        {
            var patterns = new[] { "bin/Debug", "bin/Release" };
            foreach (var pattern in patterns)
            {
                var binDir = Path.Combine(targetPath, pattern);
                if (!Directory.Exists(binDir))
                    continue;

                foreach (var dll in Directory.GetFiles(binDir, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(dll);
                        // Project DLLs override runtime — they may have project-specific types
                        references[name] = MetadataReference.CreateFromFile(dll);
                    }
                    catch
                    {
                        // Skip unreadable DLLs
                    }
                }
            }
        }

        return references.Values.ToList();
    }
}
