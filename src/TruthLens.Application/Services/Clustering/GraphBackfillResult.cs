namespace TruthLens.Application.Services.Clustering;

public sealed record GraphBackfillResult(
    int PostsScanned,
    int LinksAdded,
    int CandidatesAdded,
    int EventsTouched
);
