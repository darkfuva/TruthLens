namespace TruthLens.Worker;

public sealed class WorkerJobsOptions
{
    public const string SectionName = "WorkerJobs";

    public IngestionOptions Ingestion { get; set; } = new();
    public DiscoveryOptions Discovery { get; set; } = new();
    public ScoringOptions Scoring { get; set; } = new();
    public SummarizationOptions Summarization { get; set; } = new();
}

public class JobOptionsBase
{
    public bool Enabled { get; set; } = true;
    public bool RunOnStart { get; set; } = true;
    public int IntervalSeconds { get; set; } = 300;
}

public sealed class IngestionOptions : JobOptionsBase
{
    public int MaxIterations { get; set; } = 20;
    public int EmbeddingBatchSize { get; set; } = 64;
    public int ClusteringBatchSize { get; set; } = 100;
    public double ClusteringThreshold { get; set; } = 0.82;
}

public sealed class DiscoveryOptions : JobOptionsBase
{
    public int MaxEvents { get; set; } = 20;
    public int MinFeedsPerPost { get; set; } = 5;
    public double AutoPromoteMinConfidence { get; set; } = 0.65;
    public int AutoPromoteMinSamplePostCount { get; set; } = 2;
    public int AutoPromoteMaxCount { get; set; } = 25;
    public int PostPromotionMaxCatchupIterations { get; set; } = 5;
}

public sealed class ScoringOptions : JobOptionsBase
{
    public int MaxSources { get; set; } = 250;
    public int MaxRecommended { get; set; } = 250;
    public int MaxEvents { get; set; } = 200;
    public int LookbackDays { get; set; } = 30;
}

public sealed class SummarizationOptions : JobOptionsBase
{
    public int BatchSize { get; set; } = 10;
    public int MaxIterations { get; set; } = 20;
}
