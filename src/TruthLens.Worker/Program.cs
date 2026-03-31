using TruthLens.Infrastructure;
using TruthLens.Worker;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddOptions<WorkerJobsOptions>()
    .Bind(builder.Configuration.GetSection(WorkerJobsOptions.SectionName))
    .Validate(options => options.Ingestion.IntervalSeconds > 0, "WorkerJobs:Ingestion:IntervalSeconds must be > 0.")
    .Validate(options => options.Discovery.IntervalSeconds > 0, "WorkerJobs:Discovery:IntervalSeconds must be > 0.")
    .Validate(options => options.Scoring.IntervalSeconds > 0, "WorkerJobs:Scoring:IntervalSeconds must be > 0.")
    .Validate(options => options.Summarization.IntervalSeconds > 0, "WorkerJobs:Summarization:IntervalSeconds must be > 0.")
    .Validate(options => options.Backfill.IntervalSeconds > 0, "WorkerJobs:Backfill:IntervalSeconds must be > 0.")
    .ValidateOnStart();

builder.Services.AddSingleton<WorkerPipelineRunner>();

// These HostedServices run independently with their own intervals.
builder.Services.AddHostedService<IngestionEmbeddingClusteringWorker>();
builder.Services.AddHostedService<DiscoveryPromotionWorker>();
builder.Services.AddHostedService<ScoringWorker>();
builder.Services.AddHostedService<SummarizationWorker>();
builder.Services.AddHostedService<GraphBackfillWorker>();

var host = builder.Build();
host.Run();
