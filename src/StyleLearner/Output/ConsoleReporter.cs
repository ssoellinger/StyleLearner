using StyleLearner.Detectors;

namespace StyleLearner.Output;

public class ConsoleReporter
{
    private string _basePath = "";

    public void Print(StyleReport report)
    {
        _basePath = report.AnalyzedPath;

        Console.WriteLine();
        WriteColored("StyleLearner Analysis Report", ConsoleColor.Cyan);
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"  Path:        {report.AnalyzedPath}");
        Console.WriteLine($"  Files:       {report.TotalFiles:N0}");
        Console.WriteLine($"  Lines:       {report.TotalLines:N0}");
        Console.WriteLine(new string('=', 60));

        var score = report.ConsistencyScore;
        var scoreColor = score >= 90 ? ConsoleColor.Green : score >= 70 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.Write("  Consistency: ");
        WriteColored($"{score:F1}%", scoreColor);
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        foreach (var result in report.Results)
        {
            PrintDetectorResult(result);
        }
    }

    private void PrintDetectorResult(DetectorResult result)
    {
        var confidenceColor = result.Confidence switch
        {
            >= 90 => ConsoleColor.Green,
            >= 70 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };

        WriteColored($"  {result.DetectorName}", ConsoleColor.White);
        Console.WriteLine(new string('-', 56));

        Console.Write("    Pattern:    ");
        WriteColored(result.DominantPattern, ConsoleColor.Cyan);

        Console.Write("    Confidence: ");
        WriteColored($"{result.Confidence:F1}%", confidenceColor);

        Console.WriteLine($"    Samples:    {result.SampleCount:N0}");

        // Print key details
        if (result.Details.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var (key, value) in result.Details)
            {
                Console.WriteLine($"      {key}: {value}");
            }

            Console.ResetColor();
        }

        // Print examples
        if (result.Examples.Count > 0)
        {
            Console.WriteLine();
            PrintExamples(result.Examples);
        }

        Console.WriteLine();
    }

    private void PrintExamples(List<StyleExample> examples)
    {
        // Show conforming first, then non-conforming
        var conforming = examples.Where(e => e.IsConforming).ToList();
        var nonConforming = examples.Where(e => !e.IsConforming).ToList();

        if (conforming.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    Conforming:");
            Console.ResetColor();

            foreach (var ex in conforming)
            {
                PrintExample(ex, ConsoleColor.Green);
            }
        }

        if (nonConforming.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("    Non-conforming:");
            Console.ResetColor();

            foreach (var ex in nonConforming)
            {
                PrintExample(ex, ConsoleColor.Red);
            }
        }
    }

    private void PrintExample(StyleExample example, ConsoleColor markerColor)
    {
        var fileName = ShortenPath(example.FilePath);
        var marker = markerColor == ConsoleColor.Green ? "+" : "-";

        Console.Write("      ");
        Console.ForegroundColor = markerColor;
        Console.Write(marker);
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" {fileName}:{example.LineNumber}");
        Console.ResetColor();
        Console.Write($" — ");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine(example.Label);
        Console.ResetColor();

        // Print snippet
        Console.ForegroundColor = ConsoleColor.DarkGray;
        foreach (var line in example.Snippet.Split('\n'))
        {
            Console.WriteLine($"        {line}");
        }

        Console.ResetColor();
    }

    private string ShortenPath(string path)
    {
        if (!string.IsNullOrEmpty(_basePath))
        {
            var relative = Path.GetRelativePath(_basePath, path).Replace('\\', '/');
            // Show up to 2 segments for context
            var parts = relative.Split('/');
            if (parts.Length > 2)
                return string.Join("/", parts[^2..]);
            return relative;
        }

        var allParts = path.Replace('\\', '/').Split('/');
        return allParts.Length > 2 ? string.Join("/", allParts[^2..]) : allParts[^1];
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }
}
