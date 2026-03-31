using Microsoft.AspNetCore.Mvc;
using TruthLens.Api.Contracts;
using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

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
        [FromQuery] bool? includeProvisional,
        CancellationToken ct)
    {
        var resolvedPage = Math.Max(page ?? 1, 1);
        var requestedPageSize = pageSize ?? limit ?? 50;
        var resolvedPageSize = Math.Clamp(requestedPageSize, 1, 200);
        var normalizedSort = NormalizeSort(sort);
        if (normalizedSort is null)
        {
            return BadRequest("sort must be either 'recent' or 'confidence'.");
        }

        if (minConfidence is < 0 or > 1)
        {
            return BadRequest("minConfidence must be between 0 and 1.");
        }

        var resolvedIncludeProvisional = includeProvisional ?? true;

        var totalCount = await _eventRepository.CountForDashboardAsync(minConfidence, resolvedIncludeProvisional, ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);

        var events = await _eventRepository.GetPageForDashboardAsync(
            resolvedPage,
            resolvedPageSize,
            normalizedSort,
            minConfidence,
            resolvedIncludeProvisional,
            ct);

        var items = events.Select(e =>
        {
            var linkedPosts = e.PostLinks
                .Select(x => x.Post)
                .Where(x => x is not null)
                .DistinctBy(x => x.Id)
                .OrderByDescending(x => x.PublishedAtUtc)
                .ToList();

            return new EventListItemResponse(
                e.Id,
                e.Title,
                e.Summary,
                e.Status,
                e.ConfidenceScore,
                e.FirstSeenAtUtc,
                e.LastSeenAtUtc,
                linkedPosts.Count,
                e.ExternalEvidencePosts.Count,
                linkedPosts.Count + e.ExternalEvidencePosts.Count,
                linkedPosts.Select(p => p.Title).FirstOrDefault(),
                linkedPosts
                    .Select(p => new EventPostItemResponse(
                        p.Id,
                        p.Title,
                        p.Url,
                        p.PublishedAtUtc,
                        p.SourceId,
                        p.Source?.Name))
                    .ToList());
        }).ToList();

        var graph = await BuildGraphAsync(events.Select(x => x.Id).ToList(), ct);
        var response = new PagedEventsResponse(
            resolvedPage,
            resolvedPageSize,
            totalCount,
            totalPages,
            items,
            graph);

        return Ok(response);
    }

    [HttpGet("graph")]
    public async Task<ActionResult<EventGraphResponse>> GetGraphAsync(
        [FromQuery] string? sort,
        [FromQuery] int? maxEvents,
        [FromQuery] double? minConfidence,
        [FromQuery] bool? includeProvisional,
        CancellationToken ct)
    {
        var normalizedSort = NormalizeSort(sort);
        if (normalizedSort is null)
        {
            return BadRequest("sort must be either 'recent' or 'confidence'.");
        }

        if (minConfidence is < 0 or > 1)
        {
            return BadRequest("minConfidence must be between 0 and 1.");
        }

        var resolvedIncludeProvisional = includeProvisional ?? true;

        var resolvedMaxEvents = Math.Clamp(maxEvents ?? 150, 1, 500);
        var events = await _eventRepository.GetPageForDashboardAsync(
            page: 1,
            pageSize: resolvedMaxEvents,
            sort: normalizedSort,
            minConfidence: minConfidence,
            includeProvisional: resolvedIncludeProvisional,
            ct: ct);

        var graph = await BuildGraphAsync(events.Select(x => x.Id).ToList(), ct);
        return Ok(graph);
    }

    private async Task<EventGraphResponse> BuildGraphAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        var events = await _eventRepository.GetByIdsWithGraphAsync(eventIds, ct);
        var eventIdSet = events.Select(x => x.Id).ToHashSet();

        var nodes = new Dictionary<string, EventGraphNodeResponse>(StringComparer.Ordinal);
        var edges = new Dictionary<string, EventGraphEdgeResponse>(StringComparer.Ordinal);

        void AddNode(EventGraphNodeResponse node)
        {
            nodes.TryAdd(node.NodeId, node);
        }

        void AddEdge(EventGraphEdgeResponse edge)
        {
            edges.TryAdd(edge.EdgeId, edge);
        }

        foreach (var evt in events)
        {
            var eventNodeId = $"event:{evt.Id}";
            AddNode(new EventGraphNodeResponse(
                eventNodeId,
                "event",
                TruncateLabel(evt.Title, 72),
                evt.Id,
                null,
                null));

            var links = evt.PostLinks.OrderByDescending(x => x.IsPrimary).ThenByDescending(x => x.RelevanceScore).ToList();
            if (links.Count == 0)
            {
                links.AddRange(evt.Posts.Select(p => new PostEventLink
                {
                    Id = Guid.Empty,
                    EventId = evt.Id,
                    PostId = p.Id,
                    Post = p,
                    IsPrimary = true,
                    RelationType = "PRIMARY_LEGACY",
                    RelevanceScore = p.ClusterAssignmentScore ?? 0.55
                }));
            }

            foreach (var link in links)
            {
                var post = link.Post;
                var postNodeId = $"post:{post.Id}";
                AddNode(new EventGraphNodeResponse(
                    postNodeId,
                    "post",
                    TruncateLabel(post.Title, 92),
                    evt.Id,
                    post.Id,
                    null));

                AddEdge(new EventGraphEdgeResponse(
                    EdgeId: $"POST_EVENT:{post.Id}:{evt.Id}",
                    EdgeType: "POST_EVENT",
                    FromNodeId: postNodeId,
                    ToNodeId: eventNodeId,
                    RelationType: link.RelationType,
                    Strength: link.RelevanceScore));

                if (!string.IsNullOrWhiteSpace(post.Source?.Name))
                {
                    var sourceNodeId = $"source:{post.SourceId}";
                    AddNode(new EventGraphNodeResponse(
                        sourceNodeId,
                        "source",
                        TruncateLabel(post.Source.Name, 56),
                        null,
                        null,
                        post.SourceId));
                    AddEdge(new EventGraphEdgeResponse(
                        EdgeId: $"POST_SOURCE:{post.Id}:{post.SourceId}",
                        EdgeType: "POST_SOURCE",
                        FromNodeId: postNodeId,
                        ToNodeId: sourceNodeId,
                        RelationType: null,
                        Strength: null));
                }
            }

            foreach (var relation in evt.OutgoingRelations.Where(x => eventIdSet.Contains(x.ToEventId)))
            {
                AddEdge(new EventGraphEdgeResponse(
                    EdgeId: $"EVENT_EVENT:{relation.FromEventId}:{relation.ToEventId}:{relation.RelationType}",
                    EdgeType: "EVENT_EVENT",
                    FromNodeId: $"event:{relation.FromEventId}",
                    ToNodeId: $"event:{relation.ToEventId}",
                    RelationType: relation.RelationType,
                    Strength: relation.Strength));
            }
        }

        return new EventGraphResponse(nodes.Values.ToList(), edges.Values.ToList());
    }

    private static string? NormalizeSort(string? sort)
    {
        var normalized = (sort ?? "recent").Trim().ToLowerInvariant();
        return normalized is "recent" or "confidence" ? normalized : null;
    }

    private static string TruncateLabel(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "N/A";
        }

        return value.Length <= maxLength ? value : $"{value[..(maxLength - 1)]}...";
    }
}
