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
        [FromQuery] bool includeProvisional,
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

        var totalCount = await _eventRepository.CountForDashboardAsync(minConfidence, includeProvisional, ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);

        var events = await _eventRepository.GetPageForDashboardAsync(
            resolvedPage,
            resolvedPageSize,
            normalizedSort,
            minConfidence,
            includeProvisional,
            ct);

        var items = events.Select(e => new EventListItemResponse(
            e.Id,
            e.Title,
            e.Summary,
            e.Status,
            e.ConfidenceScore,
            e.FirstSeenAtUtc,
            e.LastSeenAtUtc,
            e.Posts.Count,
            e.ExternalEvidencePosts.Count,
            e.Posts.Count + e.ExternalEvidencePosts.Count,
            e.Posts
                .OrderByDescending(p => p.PublishedAtUtc)
                .Select(p => p.Title)
                .FirstOrDefault(),
            e.Posts
                .OrderByDescending(p => p.PublishedAtUtc)
                .Select(p => new EventPostItemResponse(
                    p.Id,
                    p.Title,
                    p.Url,
                    p.PublishedAtUtc,
                    p.SourceId,
                    p.Source?.Name))
                .ToList()
        )).ToList();

        var graph = BuildGraph(events);

        var response = new PagedEventsResponse(
            resolvedPage,
            resolvedPageSize,
            totalCount,
            totalPages,
            items,
            graph
        );

        return Ok(response);
    }

    private static EventGraphResponse BuildGraph(IReadOnlyList<Event> events)
    {
        var nodes = new Dictionary<string, EventGraphNodeResponse>(StringComparer.Ordinal);
        var edges = new List<EventGraphEdgeResponse>();
        var directedEdgeKeys = new HashSet<string>(StringComparer.Ordinal);
        var undirectedEdgeKeys = new HashSet<string>(StringComparer.Ordinal);

        void AddNode(EventGraphNodeResponse node)
        {
            nodes.TryAdd(node.NodeId, node);
        }

        void AddDirectedEdge(string edgeType, string fromNodeId, string toNodeId)
        {
            var key = $"{edgeType}|{fromNodeId}|{toNodeId}";
            if (!directedEdgeKeys.Add(key))
            {
                return;
            }

            edges.Add(new EventGraphEdgeResponse(key, edgeType, fromNodeId, toNodeId));
        }

        void AddUndirectedEdge(string edgeType, string leftNodeId, string rightNodeId)
        {
            var first = string.CompareOrdinal(leftNodeId, rightNodeId) <= 0 ? leftNodeId : rightNodeId;
            var second = first == leftNodeId ? rightNodeId : leftNodeId;
            var key = $"{edgeType}|{first}|{second}";
            if (!undirectedEdgeKeys.Add(key))
            {
                return;
            }

            edges.Add(new EventGraphEdgeResponse(key, edgeType, first, second));
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

            foreach (var post in evt.Posts.OrderByDescending(p => p.PublishedAtUtc))
            {
                var postNodeId = $"post:{post.Id}";
                AddNode(new EventGraphNodeResponse(
                    postNodeId,
                    "post",
                    TruncateLabel(post.Title, 92),
                    evt.Id,
                    post.Id,
                    null));
                AddDirectedEdge("contains", eventNodeId, postNodeId);

                var sourceName = post.Source?.Name;
                if (!string.IsNullOrWhiteSpace(sourceName))
                {
                    var sourceNodeId = $"source:{post.SourceId}";
                    AddNode(new EventGraphNodeResponse(
                        sourceNodeId,
                        "source",
                        TruncateLabel(sourceName, 52),
                        null,
                        null,
                        post.SourceId));
                    AddDirectedEdge("published_by", postNodeId, sourceNodeId);
                }
            }
        }

        const double relatedThreshold = 0.9;
        for (var i = 0; i < events.Count; i++)
        {
            var leftEmbedding = events[i].CentroidEmbedding;
            if (leftEmbedding is null)
            {
                continue;
            }

            for (var j = i + 1; j < events.Count; j++)
            {
                var rightEmbedding = events[j].CentroidEmbedding;
                if (rightEmbedding is null)
                {
                    continue;
                }

                var similarity = CosineSimilarity(leftEmbedding.ToArray(), rightEmbedding.ToArray());
                if (similarity >= relatedThreshold)
                {
                    AddUndirectedEdge(
                        "related_event",
                        $"event:{events[i].Id}",
                        $"event:{events[j].Id}");
                }
            }
        }

        return new EventGraphResponse(nodes.Values.ToList(), edges);
    }

    private static string TruncateLabel(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "N/A";
        }

        return value.Length <= maxLength ? value : $"{value[..(maxLength - 1)]}...";
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            return -1;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return -1;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}
