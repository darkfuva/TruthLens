using Microsoft.AspNetCore.Mvc;
using TruthLens.Api.Contracts;
using TruthLens.Application.Repositories.Source;
using TruthLens.Domain.Entities;

namespace TruthLens.Api.Controllers;

[ApiController]
[Route("api/recommended-sources")]
public sealed class RecommendedSourcesController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "pending", "approved", "rejected", "promoted" };

    private readonly IRecommendedSourceRepository _recommendedSourceRepository;
    private readonly ISourceRepository _sourceRepository;

    public RecommendedSourcesController(
        IRecommendedSourceRepository recommendedSourceRepository,
        ISourceRepository sourceRepository)
    {
        _recommendedSourceRepository = recommendedSourceRepository;
        _sourceRepository = sourceRepository;
    }

    [HttpGet]
    public async Task<ActionResult<PagedRecommendedSourcesResponse>> GetAsync(
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var normalizedStatus = NormalizeStatusOrNull(status);
        if (normalizedStatus is not null && !AllowedStatuses.Contains(normalizedStatus))
        {
            return BadRequest("status must be one of: pending, approved, rejected, promoted.");
        }

        var resolvedPage = Math.Max(page ?? 1, 1);
        var resolvedPageSize = Math.Clamp(pageSize ?? 25, 1, 200);

        var totalCount = await _recommendedSourceRepository.CountAsync(normalizedStatus, ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var items = await _recommendedSourceRepository.GetPageAsync(normalizedStatus, resolvedPage, resolvedPageSize, ct);

        return Ok(new PagedRecommendedSourcesResponse(
            resolvedPage,
            resolvedPageSize,
            totalCount,
            totalPages,
            items.Select(MapToResponse).ToList()));
    }

    [HttpPost]
    public async Task<ActionResult<RecommendedSourceListItemResponse>> CreateAsync(
        [FromBody] CreateRecommendedSourceRequest request,
        CancellationToken ct)
    {
        if (request.ConfidenceScore is < 0 or > 1)
        {
            return BadRequest("confidenceScore must be between 0 and 1.");
        }

        if (request.SamplePostCount is < 0)
        {
            return BadRequest("samplePostCount must be >= 0.");
        }

        var normalizedFeedUrl = request.FeedUrl.Trim();
        if (!Uri.TryCreate(normalizedFeedUrl, UriKind.Absolute, out var parsedUrl))
        {
            return BadRequest("feedUrl must be an absolute URL.");
        }

        if (await _recommendedSourceRepository.ExistsByFeedUrlAsync(normalizedFeedUrl, ct))
        {
            return Conflict("A recommended source with this feedUrl already exists.");
        }

        var domain = string.IsNullOrWhiteSpace(request.Domain)
            ? parsedUrl.Host.ToLowerInvariant()
            : request.Domain.Trim().ToLowerInvariant();

        var now = DateTimeOffset.UtcNow;
        var recommendedSource = new RecommendedSource
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Domain = domain,
            FeedUrl = normalizedFeedUrl,
            Topic = request.Topic?.Trim(),
            DiscoveryMethod = string.IsNullOrWhiteSpace(request.DiscoveryMethod)
                ? "manual"
                : request.DiscoveryMethod.Trim().ToLowerInvariant(),
            Status = "pending",
            ConfidenceScore = request.ConfidenceScore,
            SamplePostCount = request.SamplePostCount ?? 0,
            DiscoveredAtUtc = now,
            LastSeenAtUtc = now
        };

        await _recommendedSourceRepository.AddAsync(recommendedSource, ct);
        await _recommendedSourceRepository.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetAsync),
            new { status = "pending", page = 1, pageSize = 25 },
            MapToResponse(recommendedSource));
    }

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<RecommendedSourceListItemResponse>> ApproveAsync(
        Guid id,
        [FromBody] ReviewRecommendedSourceRequest? request,
        CancellationToken ct)
    {
        return UpdateStatusAsync(id, "approved", request?.Note, ct);
    }

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<RecommendedSourceListItemResponse>> RejectAsync(
        Guid id,
        [FromBody] ReviewRecommendedSourceRequest? request,
        CancellationToken ct)
    {
        return UpdateStatusAsync(id, "rejected", request?.Note, ct);
    }

    [HttpPost("{id:guid}/promote")]
    public async Task<ActionResult<RecommendedSourceListItemResponse>> PromoteAsync(
        Guid id,
        [FromBody] ReviewRecommendedSourceRequest? request,
        CancellationToken ct)
    {
        var candidate = await _recommendedSourceRepository.GetByIdAsync(id, ct);
        if (candidate is null) return NotFound();

        var exists = await _sourceRepository.ExistsByFeedUrlAsync(candidate.FeedUrl, ct);
        if (!exists)
        {
            var source = new Source
            {
                Id = Guid.NewGuid(),
                Name = candidate.Name,
                FeedUrl = candidate.FeedUrl,
                IsActive = true,
                ConfidenceScore = candidate.ConfidenceScore,
                ConfidenceUpdatedAtUtc = DateTimeOffset.UtcNow,
                ConfidenceModelVersion = "phase-a-manual"
            };

            await _sourceRepository.AddAsync(source, ct);
        }

        candidate.Status = "promoted";
        candidate.ReviewedAtUtc = DateTimeOffset.UtcNow;
        candidate.ReviewNote = request?.Note?.Trim();

        await _recommendedSourceRepository.SaveChangesAsync(ct);
        return Ok(MapToResponse(candidate));
    }

    private async Task<ActionResult<RecommendedSourceListItemResponse>> UpdateStatusAsync(
        Guid id,
        string status,
        string? reviewNote,
        CancellationToken ct)
    {
        var candidate = await _recommendedSourceRepository.GetByIdAsync(id, ct);
        if (candidate is null) return NotFound();

        candidate.Status = status;
        candidate.ReviewedAtUtc = DateTimeOffset.UtcNow;
        candidate.ReviewNote = reviewNote?.Trim();
        await _recommendedSourceRepository.SaveChangesAsync(ct);

        return Ok(MapToResponse(candidate));
    }

    private static string? NormalizeStatusOrNull(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();

    private static RecommendedSourceListItemResponse MapToResponse(RecommendedSource source)
    {
        return new RecommendedSourceListItemResponse(
            source.Id,
            source.Name,
            source.Domain,
            source.FeedUrl,
            source.Topic,
            source.DiscoveryMethod,
            source.Status,
            source.ConfidenceScore,
            source.SamplePostCount,
            source.DiscoveredAtUtc,
            source.LastSeenAtUtc,
            source.ReviewedAtUtc,
            source.ReviewNote);
    }
}
