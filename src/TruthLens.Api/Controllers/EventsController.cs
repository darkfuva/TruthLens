using Microsoft.AspNetCore.Mvc;
using TruthLens.Api.Contracts;
using TruthLens.Application.Repositories.Event;

namespace TruthLens.Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly IEventRepository _eventRepository;

    public EventsController(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventListItemResponse>>> GetRecentEventsAsync(
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var maxCount = Math.Clamp(limit ?? 50, 1, 200);
        var events = await _eventRepository.GetRecentForDashboardAsync(maxCount, ct);

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
        )).ToList();

        return Ok(response);
    }
}
