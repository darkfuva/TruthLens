// TruthLens.Infrastructure/Embedding/OllamaOptions.cs
namespace TruthLens.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
    public int TimeoutSeconds { get; init; } = 60;
    public string SummarizationModel { get; init; } = "llama3.2:3b";
}
