namespace TruthLens.Application.Services.Discovery;

public sealed record SourceDiscoveryRunResult(
    int EventsProcessed,
    int PostsProcessed,
    int PostsMeetingTarget,
    int CandidatesAdded,
    int CandidatesUpdated,
    int MinFeedsTarget
);
