namespace TruthLens.Api.Contracts;

public sealed record EventGraphResponse(
    IReadOnlyList<EventGraphNodeResponse> Nodes,
    IReadOnlyList<EventGraphEdgeResponse> Edges
);
