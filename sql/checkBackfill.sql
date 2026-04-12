-- embedded posts without primary link
SELECT COUNT(*) AS embedded_without_primary
FROM public.posts p
WHERE p."Embedding" IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM public.post_event_links pel
      WHERE pel."PostId" = p."Id" AND pel."IsPrimary" = TRUE
  );

-- posts having >1 primary link (must be 0)
SELECT COUNT(*) AS posts_with_multiple_primary
FROM (
  SELECT "PostId"
  FROM public.post_event_links
  WHERE "IsPrimary" = TRUE
  GROUP BY "PostId"
  HAVING COUNT(*) > 1
) x;

-- legacy parity check: Post.EventId vs primary link EventId
SELECT COUNT(*) AS legacy_mismatch_count
FROM public.posts p
JOIN public.post_event_links pel
  ON pel."PostId" = p."Id" AND pel."IsPrimary" = TRUE
WHERE p."EventId" IS NOT NULL
  AND p."EventId" <> pel."EventId";
