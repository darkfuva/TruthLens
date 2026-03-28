// Services/Embedding/EmbeddingGenerationService.cs
using TruthLens.Application.Services.Post;

namespace TruthLens.Application.Services.Embedding;

public sealed class EmbeddingGenerationService
{
    private readonly IPostRepository _postRepository;
    private readonly IEmbeddingClient _embeddingClient;

    public EmbeddingGenerationService(IPostRepository postRepository, IEmbeddingClient embeddingClient)
    {
        _postRepository = postRepository;
        _embeddingClient = embeddingClient;
    }

    public async Task<int> GenerateForPendingPostsAsync(int batchSize, CancellationToken ct)
    {
        var posts = await _postRepository.GetUnembeddedBatchAsync(batchSize, ct);
        if (posts.Count == 0) return 0;

        var texts = posts
            .Select(p => $"{p.Title}\n{p.Summary ?? string.Empty}")
            .ToList();

        var vectors = await _embeddingClient.EmbedAsync(texts, ct);
        if (vectors.Count != posts.Count)
            throw new InvalidOperationException("Embedding count mismatch.");

        for (var i = 0; i < posts.Count; i++)
        {
            posts[i].Embedding = new Pgvector.Vector(vectors[i]);
            posts[i].EmbeddingModel = "local";
            posts[i].EmbeddedAtUtc = DateTimeOffset.UtcNow;
        }

        await _postRepository.SaveChangesAsync(ct);
        return posts.Count;
    }
}
