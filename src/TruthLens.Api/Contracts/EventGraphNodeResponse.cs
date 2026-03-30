namespace TruthLens.Api.Contracts;

public sealed record EventGraphNodeResponse(
    string NodeId,
    string NodeType,
    string Label,
    Guid? EventId,
    Guid? PostId,
    Guid? SourceId
);
