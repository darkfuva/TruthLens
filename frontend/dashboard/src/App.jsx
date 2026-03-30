import { useEffect, useMemo, useState } from "react";

const EMPTY_GRAPH = { nodes: [], edges: [] };

function formatDate(value) {
  return new Date(value).toLocaleString();
}

function getConfidenceClass(score) {
  if (typeof score !== "number") return "badge neutral";
  if (score >= 0.8) return "badge high";
  if (score >= 0.6) return "badge medium";
  return "badge low";
}

function truncate(value, maxLength) {
  if (!value) return "N/A";
  if (value.length <= maxLength) return value;
  return `${value.slice(0, maxLength - 1)}...`;
}

function buildGraphRenderState(graph, selectedPostId) {
  const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
  const edges = Array.isArray(graph?.edges) ? graph.edges : [];

  const byType = {
    event: [],
    post: [],
    source: [],
    other: [],
  };

  for (const node of nodes) {
    if (node.nodeType === "event") byType.event.push(node);
    else if (node.nodeType === "post") byType.post.push(node);
    else if (node.nodeType === "source") byType.source.push(node);
    else byType.other.push(node);
  }

  byType.event.sort((a, b) => a.label.localeCompare(b.label));
  byType.post.sort((a, b) => a.label.localeCompare(b.label));
  byType.source.sort((a, b) => a.label.localeCompare(b.label));
  byType.other.sort((a, b) => a.label.localeCompare(b.label));

  const columnX = {
    event: 130,
    post: 430,
    source: 730,
    other: 730,
  };

  const nodePositions = {};
  let maxY = 80;

  for (const [type, bucket] of Object.entries(byType)) {
    for (let i = 0; i < bucket.length; i += 1) {
      const y = 70 + i * 68;
      nodePositions[bucket[i].nodeId] = { x: columnX[type], y };
      maxY = Math.max(maxY, y);
    }
  }

  const selectedNodeId = selectedPostId ? `post:${selectedPostId}` : null;
  const highlightedNodeIds = new Set();
  const highlightedEdgeIds = new Set();

  if (selectedNodeId) {
    highlightedNodeIds.add(selectedNodeId);
    for (const edge of edges) {
      if (edge.fromNodeId === selectedNodeId || edge.toNodeId === selectedNodeId) {
        highlightedEdgeIds.add(edge.edgeId);
        highlightedNodeIds.add(edge.fromNodeId);
        highlightedNodeIds.add(edge.toNodeId);
      }
    }
  }

  return {
    nodes,
    edges,
    nodePositions,
    highlightedNodeIds,
    highlightedEdgeIds,
    width: 860,
    height: Math.max(maxY + 80, 320),
  };
}

export default function App() {
  const [events, setEvents] = useState([]);
  const [graph, setGraph] = useState(EMPTY_GRAPH);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [sort, setSort] = useState("recent");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [minConfidence, setMinConfidence] = useState("");
  const [selectedPostId, setSelectedPostId] = useState(null);

  useEffect(() => {
    let mounted = true;

    async function loadEvents() {
      setLoading(true);
      setError("");

      try {
        const params = new URLSearchParams();
        params.set("page", String(page));
        params.set("pageSize", String(pageSize));
        params.set("sort", sort);
        if (minConfidence !== "") {
          params.set("minConfidence", minConfidence);
        }

        const response = await fetch(`/api/events?${params.toString()}`);
        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}`);
        }

        const data = await response.json();
        if (mounted) {
          const items = data.items ?? [];
          const nextGraph = data.graph ?? EMPTY_GRAPH;

          setEvents(items);
          setGraph(nextGraph);
          setTotalCount(data.totalCount ?? 0);
          setTotalPages(data.totalPages ?? 0);

          if (selectedPostId) {
            const postNodeId = `post:${selectedPostId}`;
            const stillExists = (nextGraph.nodes ?? []).some((x) => x.nodeId === postNodeId);
            if (!stillExists) {
              setSelectedPostId(null);
            }
          }
        }
      } catch (err) {
        if (mounted) setError(err.message || "Failed to load events");
      } finally {
        if (mounted) setLoading(false);
      }
    }

    loadEvents();
    return () => {
      mounted = false;
    };
  }, [minConfidence, page, pageSize, sort]);

  const stats = useMemo(() => {
    const withSummary = events.filter((x) => x.summary).length;
    const totalPosts = events.reduce((sum, x) => sum + x.postCount, 0);
    return { withSummary, totalPosts };
  }, [events]);

  const graphState = useMemo(
    () => buildGraphRenderState(graph, selectedPostId),
    [graph, selectedPostId],
  );

  return (
    <main className="page">
      <header className="hero">
        <h1>TruthLens Event Dashboard</h1>
        <p>Live clustered news events with AI summaries.</p>
      </header>

      <section className="stats">
        <article>
          <h2>{totalCount}</h2>
          <p>Events Loaded</p>
        </article>
        <article>
          <h2>{stats.totalPosts}</h2>
          <p>Posts in Scope</p>
        </article>
        <article>
          <h2>{stats.withSummary}</h2>
          <p>Summarized Events</p>
        </article>
      </section>

      <section className="controls">
        <label>
          Sort
          <select value={sort} onChange={(e) => { setSort(e.target.value); setPage(1); }}>
            <option value="recent">Recent</option>
            <option value="confidence">Confidence</option>
          </select>
        </label>

        <label>
          Page Size
          <input
            type="number"
            min="1"
            max="200"
            value={pageSize}
            onChange={(e) => {
              setPageSize(Math.max(1, Math.min(200, Number(e.target.value) || 1)));
              setPage(1);
            }}
          />
        </label>

        <label>
          Min Confidence
          <input
            type="number"
            min="0"
            max="1"
            step="0.05"
            placeholder="optional"
            value={minConfidence}
            onChange={(e) => {
              setMinConfidence(e.target.value);
              setPage(1);
            }}
          />
        </label>

        <button type="button" onClick={() => { setMinConfidence("0.7"); setPage(1); }}>
          High Confidence Only
        </button>
        <button type="button" onClick={() => { setMinConfidence(""); setPage(1); }}>
          Clear Filter
        </button>
      </section>

      <section className="pagination">
        <button type="button" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>
          Previous
        </button>
        <span>
          Page {page} of {Math.max(totalPages, 1)}
        </span>
        <button
          type="button"
          onClick={() => setPage((p) => (totalPages > 0 ? Math.min(totalPages, p + 1) : p + 1))}
          disabled={totalPages === 0 || page >= totalPages}
        >
          Next
        </button>
      </section>

      {loading && <p className="status">Loading events...</p>}
      {error && <p className="status error">{error}</p>}

      {!loading && !error && (
        <section className="layout">
          <section className="grid">
            {events.map((event) => (
              <article key={event.id} className="card">
                <div className="card-head">
                  <h3>{event.title}</h3>
                  <span>{event.postCount} posts</span>
                </div>
                <div className={getConfidenceClass(event.confidenceScore)}>
                  Confidence:{" "}
                  {typeof event.confidenceScore === "number"
                    ? event.confidenceScore.toFixed(3)
                    : "N/A"}
                </div>

                <p className="summary">{event.summary || "Summary pending..."}</p>

                <dl>
                  <div>
                    <dt>Last Seen</dt>
                    <dd>{formatDate(event.lastSeenAtUtc)}</dd>
                  </div>
                  <div>
                    <dt>Confidence</dt>
                    <dd>{typeof event.confidenceScore === "number" ? event.confidenceScore.toFixed(3) : "N/A"}</dd>
                  </div>
                  <div>
                    <dt>Latest Post</dt>
                    <dd>{event.latestPostTitle || "N/A"}</dd>
                  </div>
                </dl>

                <div className="post-list">
                  {(event.posts ?? []).slice(0, 6).map((post) => (
                    <button
                      key={post.id}
                      type="button"
                      className={`post-chip ${selectedPostId === post.id ? "active" : ""}`}
                      onClick={() => setSelectedPostId(post.id)}
                      title={post.title}
                    >
                      {truncate(post.title, 72)}
                    </button>
                  ))}
                  {(event.posts?.length ?? 0) > 6 && (
                    <span className="more-posts">+{event.posts.length - 6} more posts</span>
                  )}
                </div>
              </article>
            ))}
          </section>

          <aside className="graph-panel">
            <div className="graph-head">
              <h2>Evidence Graph</h2>
              <p>Click a post chip to highlight its graph node and links.</p>
              {selectedPostId && (
                <button type="button" onClick={() => setSelectedPostId(null)}>
                  Clear Selection
                </button>
              )}
            </div>

            <div className="graph-shell">
              {graphState.nodes.length === 0 ? (
                <p className="graph-empty">No graph nodes for this page yet.</p>
              ) : (
                <svg
                  className="graph-svg"
                  viewBox={`0 0 ${graphState.width} ${graphState.height}`}
                  role="img"
                  aria-label="Event-post-source graph"
                >
                  {graphState.edges.map((edge) => {
                    const from = graphState.nodePositions[edge.fromNodeId];
                    const to = graphState.nodePositions[edge.toNodeId];
                    if (!from || !to) return null;

                    const highlighted =
                      !selectedPostId || graphState.highlightedEdgeIds.has(edge.edgeId);

                    return (
                      <line
                        key={edge.edgeId}
                        x1={from.x}
                        y1={from.y}
                        x2={to.x}
                        y2={to.y}
                        className={`graph-edge ${highlighted ? "on" : "off"}`}
                      />
                    );
                  })}

                  {graphState.nodes.map((node) => {
                    const position = graphState.nodePositions[node.nodeId];
                    if (!position) return null;

                    const highlighted =
                      !selectedPostId || graphState.highlightedNodeIds.has(node.nodeId);
                    const radius =
                      node.nodeType === "event" ? 16 : node.nodeType === "post" ? 12 : 10;

                    return (
                      <g
                        key={node.nodeId}
                        transform={`translate(${position.x}, ${position.y})`}
                        className={`graph-node ${node.nodeType} ${highlighted ? "on" : "off"}`}
                        onClick={() => {
                          if (node.nodeType === "post" && node.postId) {
                            setSelectedPostId(node.postId);
                          }
                        }}
                      >
                        <circle r={radius} />
                        <text x={18} y={5}>
                          {truncate(node.label, 42)}
                        </text>
                      </g>
                    );
                  })}
                </svg>
              )}
            </div>

            <div className="graph-legend">
              <span className="dot event" /> Event
              <span className="dot post" /> Post
              <span className="dot source" /> Source
            </div>
          </aside>
        </section>
      )}
    </main>
  );
}
