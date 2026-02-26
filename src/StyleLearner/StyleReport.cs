using StyleLearner.Detectors;

namespace StyleLearner;

public class StyleReport
{
    public string AnalyzedPath { get; init; } = "";
    public int TotalFiles { get; init; }
    public int TotalLines { get; init; }
    public List<DetectorResult> Results { get; init; } = new();

    /// <summary>
    /// Overall consistency score: weighted average of detector confidences (weighted by sample count).
    /// </summary>
    public double ConsistencyScore
    {
        get
        {
            var scored = Results.Where(r => r.SampleCount > 0).ToList();
            if (scored.Count == 0) return 0;
            var totalSamples = scored.Sum(r => (double)r.SampleCount);
            return scored.Sum(r => r.Confidence * r.SampleCount / totalSamples);
        }
    }
}
