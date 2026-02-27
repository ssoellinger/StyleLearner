using StyleLearner;
using StyleLearner.Fixers;
using StyleLearner.Output;

if (args.Length == 0)
{
    Console.WriteLine("Usage: StyleLearner <directory> [--output editorconfig|html] [--report <path>] [--exclude <pattern>]... [--fix [--fix-path <dir>] [--dry-run] [--min-confidence <pct>]]");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\"");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --output editorconfig");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --output html --report report.html");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --exclude \"**/obj/**\" --exclude \"**/*.g.cs\"");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --fix");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --fix --dry-run");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --fix --min-confidence 90");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --fix --fix-path \"C:\\OtherDir\"  (learn from MyProject, apply to OtherDir)");
    return 1;
}

var directoryPath = args[0];
if (!Directory.Exists(directoryPath))
{
    Console.Error.WriteLine($"Directory not found: {directoryPath}");
    return 1;
}

// Parse options
string? outputMode = null;
string? reportPath = null;
var excludePatterns = new List<string>();
bool fix = false;
bool dryRun = false;
double minConfidence = 80.0;
string? fixPath = null;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--output" when i + 1 < args.Length:
            outputMode = args[++i];
            break;
        case "--report" when i + 1 < args.Length:
            reportPath = args[++i];
            break;
        case "--exclude" when i + 1 < args.Length:
            excludePatterns.Add(args[++i]);
            break;
        case "--fix":
            fix = true;
            break;
        case "--fix-path" when i + 1 < args.Length:
            fixPath = args[++i];
            break;
        case "--dry-run":
            dryRun = true;
            break;
        case "--min-confidence" when i + 1 < args.Length:
            if (double.TryParse(args[++i], out double mc))
                minConfidence = mc;
            break;
    }
}

var analyzer = new StyleAnalyzer(excludePatterns.Count > 0 ? excludePatterns : null);
var report = analyzer.Analyze(directoryPath);

var consoleReporter = new ConsoleReporter();
consoleReporter.Print(report);

if (outputMode?.Equals("editorconfig", StringComparison.OrdinalIgnoreCase) == true)
{
    Console.WriteLine();
    var generator = new EditorConfigGenerator();
    var editorConfig = generator.Generate(report);
    Console.WriteLine("Generated .editorconfig:");
    Console.WriteLine(new string('=', 60));
    Console.WriteLine(editorConfig);
}
else if (outputMode?.Equals("html", StringComparison.OrdinalIgnoreCase) == true)
{
    var htmlReporter = new HtmlReporter();
    var html = htmlReporter.Generate(report);
    var htmlPath = reportPath ?? Path.Combine(directoryPath, "style-report.html");
    File.WriteAllText(htmlPath, html);
    Console.WriteLine($"HTML report written to: {htmlPath}");
}

if (fix)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 60));
    Console.WriteLine(dryRun ? "FIX MODE (dry run — no files will be modified)" : "FIX MODE");
    Console.WriteLine(new string('=', 60));

    var configBuilder = new LayoutStyleConfigBuilder(minConfidence);
    var config = configBuilder.Build(report);

    PrintActiveRules(config);

    var targetPath = fixPath ?? directoryPath;
    var fixAnalyzer = new StyleAnalyzer(excludePatterns.Count > 0 ? excludePatterns : null);
    var csFiles = fixAnalyzer.FindCsFiles(targetPath);
    var orchestrator = new LayoutFixerOrchestrator(config, dryRun);
    var summary = orchestrator.FixDirectory(targetPath, csFiles);

    Console.WriteLine();
    Console.WriteLine($"Files processed: {summary.FilesProcessed}");
    Console.WriteLine($"Files changed:   {summary.FilesChanged}");
    Console.WriteLine($"Total changes:   {summary.TotalChanges}");

    if (summary.FileResults.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Changed files:");
        foreach (var result in summary.FileResults)
        {
            var relativePath = Path.GetRelativePath(targetPath, result.FilePath);
            var changes = string.Join(", ", result.FixerChanges.Select(kv => $"{kv.Key}: {kv.Value}"));
            Console.WriteLine($"  {relativePath} ({changes})");
        }
    }
}

return 0;

static void PrintActiveRules(LayoutStyleConfig config)
{
    Console.WriteLine();
    Console.WriteLine("Active rules:");

    Console.WriteLine("  Whitespace: always (trailing whitespace, BOM, line endings, final newline)");
    Console.WriteLine("  Blank Lines: always (collapse 2+ consecutive to 1)");

    if (config.TrailingComma != null)
        Console.WriteLine($"  Trailing Comma: {(config.TrailingComma.HasTrailingComma ? "add" : "remove")}");
    else
        Console.WriteLine("  Trailing Comma: skipped (low confidence)");

    if (config.InheritanceLayout != null)
        Console.WriteLine($"  Inheritance Layout: colon on {config.InheritanceLayout.ColonPlacement}");
    else
        Console.WriteLine("  Inheritance Layout: skipped (low confidence)");

    if (config.ParameterLayout != null)
        Console.WriteLine($"  Parameter Layout: multi-line at {config.ParameterLayout.MultilineThreshold}+ params, closing paren: {config.ParameterLayout.ClosingParen}");
    else
        Console.WriteLine("  Parameter Layout: skipped (low confidence)");

    if (config.ArrowPlacement != null)
        Console.WriteLine($"  Arrow Placement: {(config.ArrowPlacement.ArrowOnNewLine ? "new line" : "same line")}");
    else
        Console.WriteLine("  Arrow Placement: skipped (low confidence)");

    if (config.TernaryLayout != null)
        Console.WriteLine($"  Ternary Layout: threshold={config.TernaryLayout.Threshold}, pattern={config.TernaryLayout.DominantMultiLinePattern}");
    else
        Console.WriteLine("  Ternary Layout: skipped (low confidence)");

    if (config.MethodChaining != null)
        Console.WriteLine($"  Method Chaining: single-line when <={config.MethodChaining.ChainLengthThreshold} calls");
    else
        Console.WriteLine("  Method Chaining: skipped (low confidence)");

    if (config.NamespaceStyle != null)
        Console.WriteLine($"  Namespace Style: {config.NamespaceStyle.Style}");
    else
        Console.WriteLine("  Namespace Style: skipped (low confidence)");

    if (config.BlankLines != null)
    {
        Console.WriteLine($"  Blank Line Style: " +
            $"after {{: {(config.BlankLines.BlankLineAfterOpenBrace ? "add" : "remove")}, " +
            $"before }}: {(config.BlankLines.BlankLineBeforeCloseBrace ? "add" : "remove")}, " +
            $"after }}: {(config.BlankLines.BlankLineAfterCloseBrace ? "add" : "remove")}, " +
            $"after #region: {(config.BlankLines.BlankLineAfterRegion ? "add" : "remove")}, " +
            $"before #endregion: {(config.BlankLines.BlankLineBeforeEndRegion ? "add" : "remove")}");
    }
    else
        Console.WriteLine("  Blank Line Style: skipped (low confidence for brace/region rules)");

    if (config.Spacing != null)
        Console.WriteLine($"  Spacing: cast: {(config.Spacing.SpaceAfterCast ? "space" : "no space")}, keyword: {(config.Spacing.SpaceAfterKeyword ? "space" : "no space")}");
    else
        Console.WriteLine("  Spacing: skipped (low confidence)");

    if (config.NewLineKeywords != null)
        Console.WriteLine($"  Newline Before Keywords: catch: {(config.NewLineKeywords.NewLineBeforeCatch ? "new line" : "same line")}, else: {(config.NewLineKeywords.NewLineBeforeElse ? "new line" : "same line")}, finally: {(config.NewLineKeywords.NewLineBeforeFinally ? "new line" : "same line")}");
    else
        Console.WriteLine("  Newline Before Keywords: skipped (low confidence)");

    if (config.ContinuationIndent != null)
        Console.WriteLine($"  Continuation Indent: {config.ContinuationIndent.Style}");
    else
        Console.WriteLine("  Continuation Indent: skipped (low confidence)");
}
