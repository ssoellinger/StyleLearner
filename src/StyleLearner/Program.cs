using StyleLearner;
using StyleLearner.Output;

if (args.Length == 0)
{
    Console.WriteLine("Usage: StyleLearner <directory> [--output editorconfig|html] [--report <path>] [--exclude <pattern>]...");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\"");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --output editorconfig");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --output html --report report.html");
    Console.WriteLine("  StyleLearner \"C:\\MyProject\" --exclude \"**/obj/**\" --exclude \"**/*.g.cs\"");
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

return 0;
