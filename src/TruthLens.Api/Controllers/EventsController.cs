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
    public async Task<ActionResult<PagedEventsResponse>> GetRecentEventsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] int? limit,
        [FromQuery] string? sort,
        [FromQuery] double? minConfidence,
        CancellationToken ct)
    {
        // Keep backward compatibility with old `limit` query by treating it as pageSize.
        var resolvedPage = Math.Max(page ?? 1, 1);
        var requestedPageSize = pageSize ?? limit ?? 50;
        var resolvedPageSize = Math.Clamp(requestedPageSize, 1, 200);
        var normalizedSort = (sort ?? "recent").Trim().ToLowerInvariant();

        if (normalizedSort is not ("recent" or "confidence"))
        {
            return BadRequest("sort must be either 'recent' or 'confidence'.");
        }

        if (minConfidence is < 0 or > 1)
        {
            return BadRequest("minConfidence must be between 0 and 1.");
        }

        var totalCount = await _eventRepository.CountForDashboardAsync(minConfidence, ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);

        var events = await _eventRepository.GetPageForDashboardAsync(
            resolvedPage,
            resolvedPageSize,
            normalizedSort,
            minConfidence,
            ct);

        var items = events.Select(e => new EventListItemResponse(
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

        var response = new PagedEventsResponse(
            resolvedPage,
            resolvedPageSize,
            totalCount,
            totalPages,
            items
        );

        return Ok(response);
    }
}
