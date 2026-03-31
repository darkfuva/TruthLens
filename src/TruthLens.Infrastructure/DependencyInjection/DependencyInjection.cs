// src/TruthLens.Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TruthLens.Application.Repositories.Post;
using TruthLens.Application.Services.Rss;
using TruthLens.Application.Repositories.Source;
using TruthLens.Application.Repositories.External;
using TruthLens.Infrastructure.Persistence;
using TruthLens.Infrastructure.Persistence.Repositories;
using TruthLens.Infrastructure.Rss;
using TruthLens.Infrastructure.Ollama;
using Microsoft.Extensions.Options;
using TruthLens.Application.Services.Embedding;
using TruthLens.Infrastructure.Embedding;
using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Services.Clustering;
using TruthLens.Application.Services.Discovery;
using TruthLens.Application.Services.Summarization;
using TruthLens.Application.Services.Extraction;
using TruthLens.Infrastructure.Discovery;
using TruthLens.Infrastructure.Extraction;
using TruthLens.Infrastructure.Summarization;
using TruthLens.Application.Services.Scoring;

namespace TruthLens.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        services.AddDbContext<TruthLensDbContext>(options => options.UseNpgsql(conn, (o) => o.UseVector()));

        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<IRecommendedSourceRepository, RecommendedSourceRepository>();
        services.AddScoped<IExternalSourceRepository, ExternalSourceRepository>();
        services.AddScoped<IExternalEvidenceRepository, ExternalEvidenceRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<RssIngestionService>();
        services.AddScoped<EmbeddingGenerationService>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IPostEventLinkRepository, PostEventLinkRepository>();
        services.AddScoped<IEventRelationRepository, EventRelationRepository>();
        services.AddScoped<IExtractedEventCandidateRepository, ExtractedEventCandidateRepository>();
        services.AddScoped<ICosineSimilarityService, CosineSimilarityService>();
        services.AddScoped<ClusteringService>();
        services.AddScoped<EventRelationRecomputeService>();
        services.AddScoped<GraphBackfillService>();
        services.AddScoped<SourceDiscoveryService>();
        services.AddScoped<RecommendedSourcePromotionService>();
        services.AddScoped<SourceConfidenceScoringService>();
        services.AddScoped<EventConfidenceScoringService>();

        services.AddHttpClient<INewsSearchClient, BingNewsSearchClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TruthLens/0.1");
        });

        services.AddHttpClient<IFeedUrlDiscoveryClient, FeedUrlDiscoveryClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TruthLens/0.1");
        });


        services.AddHttpClient<IRssFeedClient, RssFeedClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TruthLens/0.1");
        });

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.BaseUrl), "Ollama:BaseUrl is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.EmbeddingModel), "Ollama:EmbeddingModel is required.")
            .ValidateOnStart();

        services.AddHttpClient<IEmbeddingClient, OllamaEmbeddingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        // DependencyInjection.cs (add registrations)
        services.AddScoped<EventSummarizationService>();

        services.AddHttpClient<IEventSummarizer, OllamaEventSummarizer>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddHttpClient<IEventCandidateExtractor, OllamaEventCandidateExtractor>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });


        return services;
    }
}
