using System.ComponentModel.DataAnnotations;

namespace TruthLens.Api.Contracts;

public sealed record ReviewRecommendedSourceRequest(
    [property: MaxLength(1000)] string? Note
);
