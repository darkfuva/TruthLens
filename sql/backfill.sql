BEGIN;

-- 1) Backfill missing link from legacy Post.EventId
INSERT INTO public.post_event_links
    ("Id", "PostId", "EventId", "RelevanceScore", "IsPrimary", "RelationType", "LinkedAtUtc")
SELECT
    gen_random_uuid(),
    p."Id",
    p."EventId",
    COALESCE(p."ClusterAssignmentScore", 0.0),
    NOT EXISTS (
        SELECT 1
        FROM public.post_event_links pelp
        WHERE pelp."PostId" = p."Id" AND pelp."IsPrimary" = TRUE
    ),
    'legacy-backfill',
    NOW()
FROM public.posts p
WHERE p."EventId" IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM public.post_event_links pel
      WHERE pel."PostId" = p."Id"
        AND pel."EventId" = p."EventId"
  );

-- 2) For posts with links but no primary, promote highest relevance
WITH posts_without_primary AS (
    SELECT pel."PostId"
    FROM public.post_event_links pel
    GROUP BY pel."PostId"
    HAVING SUM(CASE WHEN pel."IsPrimary" THEN 1 ELSE 0 END) = 0
),
ranked AS (
    SELECT
        pel."Id",
        pel."PostId",
        ROW_NUMBER() OVER (
            PARTITION BY pel."PostId"
            ORDER BY pel."RelevanceScore" DESC, pel."LinkedAtUtc" ASC
        ) AS rn
    FROM public.post_event_links pel
    JOIN posts_without_primary pwp ON pwp."PostId" = pel."PostId"
)
UPDATE public.post_event_links pel
SET "IsPrimary" = TRUE
FROM ranked r
WHERE pel."Id" = r."Id"
  AND r.rn = 1;

COMMIT;
