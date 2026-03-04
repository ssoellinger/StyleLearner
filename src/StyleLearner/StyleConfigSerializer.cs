using System.Text.Json;
using StyleLearner.Fixers;

namespace StyleLearner;

public static class StyleConfigSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void Save(LayoutStyleConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(path, json);
    }

    public static LayoutStyleConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LayoutStyleConfig>(json, Options)
            ?? throw new InvalidOperationException($"Failed to deserialize style config from {path}");
    }
}
