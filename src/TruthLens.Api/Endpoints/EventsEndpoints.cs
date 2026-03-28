using TruthLens.Api.Contracts;
using TruthLens.Application.Repositories.Event;

namespace TruthLens.Api.Endpoints;

public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", GetRecentEventsAsync)
            .WithName("GetRecentEvents")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> GetRecentEventsAsync(
        IEventRepository eventRepository,
        int? limit,
        CancellationToken ct)
    {
        // Clamp limit so callers cannot accidentally request unbounded data.
        var maxCount = Math.Clamp(limit ?? 50, 1, 200);
        var events = await eventRepository.GetRecentForDashboardAsync(maxCount, ct);

        var response = events.Select(e => new EventListItemResponse(
            e.Id,
            e.Title,
            e.Summary,
            e.ConfidenceScore,
            e.FirstSeenAtUtc,
            e.LastSeenAtUtc,
            e.Posts.Count,
            e.Posts
                .OrderByDescending(p => p.PublishedAtUtc)
                .Select(p => p.Title)
                .FirstOrDefault()
        ));

        return Results.Ok(response);
    }
}
