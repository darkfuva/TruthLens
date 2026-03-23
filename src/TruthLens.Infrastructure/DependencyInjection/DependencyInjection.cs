// src/TruthLens.Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TruthLens.Application.Services.Post;
using TruthLens.Application.Services.Rss;
using TruthLens.Application.Services.Source;
using TruthLens.Infrastructure.Persistence;
using TruthLens.Infrastructure.Persistence.Repositories;
using TruthLens.Infrastructure.Rss;
using Microsoft.Extensions.Options;
using TruthLens.Application.Services.Embedding;
using TruthLens.Infrastructure.Embedding;


namespace TruthLens.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        services.AddDbContext<TruthLensDbContext>(options => options.UseNpgsql(conn));

        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<RssIngestionService>();
        services.AddScoped<EmbeddingGenerationService>();

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.BaseUrl), "Ollama:BaseUrl is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.EmbeddingModel), "Ollama:EmbeddingModel is required.")
            .ValidateOnStart();
            
        services.AddHttpClient<IRssFeedClient, RssFeedClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TruthLens/0.1");
        });

        services.AddHttpClient<IEmbeddingClient, OllamaEmbeddingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
