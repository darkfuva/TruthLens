namespace TruthLens.Api.Contracts;

public sealed record EventGraphEdgeResponse(
    string EdgeId,
    string EdgeType,
    string FromNodeId,
    string ToNodeId,
    string? RelationType,
    double? Strength
);
