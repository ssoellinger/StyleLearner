using System.Diagnostics;

namespace StyleLearner;

public static class GitHelper
{
    public static List<string> GetChangedCsFiles(string repoPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return [];

        var files = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
                continue;

            var status = line[..2];
            var path = line[3..].Trim();

            // Skip deleted files
            if (status.Contains('D'))
                continue;

            // Handle renames: "R  old -> new"
            if (status.Contains('R'))
            {
                var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIndex >= 0)
                    path = path[(arrowIndex + 4)..];
            }

            // Remove surrounding quotes git adds for paths with special chars
            if (path.StartsWith('"') && path.EndsWith('"'))
                path = path[1..^1];

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            files.Add(Path.GetFullPath(Path.Combine(repoPath, path)));
        }

        return files;
    }
}
