import { useEffect, useMemo, useState } from "react";
import ReactFlow, { Background, Controls, MiniMap } from "reactflow";
import "reactflow/dist/style.css";

const EMPTY_GRAPH = { nodes: [], edges: [] };
const EDGE_TYPE_OPTIONS = ["POST_EVENT", "EVENT_EVENT", "POST_SOURCE"];

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

function getNodeColor(nodeType) {
  if (nodeType === "event") return "#f59e0b";
  if (nodeType === "post") return "#2563eb";
  if (nodeType === "source") return "#16a34a";
  return "#64748b";
}

function getEdgeColor(edgeType, relationType) {
  if (edgeType === "POST_EVENT") return relationType === "PRIMARY" ? "#1d4ed8" : "#60a5fa";
  if (edgeType === "POST_SOURCE") return "#16a34a";
  if (edgeType === "EVENT_EVENT") {
    if (relationType === "FOLLOWUP") return "#a855f7";
    if (relationType === "SUBEVENT_OF") return "#ec4899";
    return "#f59e0b";
  }

  return "#94a3b8";
}

function buildFlowGraph(graph, selectedPostId, edgeTypeFilter, minRelationStrength) {
  const sourceNodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
  const sourceEdges = Array.isArray(graph?.edges) ? graph.edges : [];

  const filteredEdges = sourceEdges.filter((edge) => {
    if (!edgeTypeFilter.has(edge.edgeType)) return false;
    if (edge.edgeType === "EVENT_EVENT") {
      const strength = typeof edge.strength === "number" ? edge.strength : 0;
      if (strength < minRelationStrength) return false;
    }

    return true;
  });

  const nodeMap = new Map(sourceNodes.map((node) => [node.nodeId, node]));
  const connectedNodeIds = new Set();

  for (const edge of filteredEdges) {
    connectedNodeIds.add(edge.fromNodeId);
    connectedNodeIds.add(edge.toNodeId);
  }

  const selectedNodeId = selectedPostId ? `post:${selectedPostId}` : null;
  const highlightNodes = new Set();
  const highlightEdges = new Set();

  if (selectedNodeId) {
    highlightNodes.add(selectedNodeId);
    for (const edge of filteredEdges) {
      if (edge.fromNodeId === selectedNodeId || edge.toNodeId === selectedNodeId) {
        highlightEdges.add(edge.edgeId);
        highlightNodes.add(edge.fromNodeId);
        highlightNodes.add(edge.toNodeId);
      }
    }
  }

  const buckets = {
    event: [],
    post: [],
    source: [],
    other: [],
  };

  for (const node of sourceNodes) {
    if (!connectedNodeIds.has(node.nodeId)) continue;

    if (node.nodeType === "event") buckets.event.push(node);
    else if (node.nodeType === "post") buckets.post.push(node);
    else if (node.nodeType === "source") buckets.source.push(node);
    else buckets.other.push(node);
  }

  for (const key of Object.keys(buckets)) {
    buckets[key].sort((a, b) => a.label.localeCompare(b.label));
  }

  const columnX = { event: 120, post: 440, source: 760, other: 760 };
  const verticalGap = 70;
  const startY = 60;
  const positions = new Map();

  for (const [type, items] of Object.entries(buckets)) {
    items.forEach((node, index) => {
      positions.set(node.nodeId, { x: columnX[type], y: startY + index * verticalGap });
    });
  }

  const nodes = [...buckets.event, ...buckets.post, ...buckets.source, ...buckets.other].map((node) => {
    const position = positions.get(node.nodeId) ?? { x: 50, y: 50 };
    const isHighlighted = !selectedNodeId || highlightNodes.has(node.nodeId);

    return {
      id: node.nodeId,
      data: { label: truncate(node.label, 44), nodeType: node.nodeType, postId: node.postId },
      position,
      draggable: false,
      selectable: true,
      style: {
        borderRadius: node.nodeType === "event" ? "14px" : "10px",
        border: `2px solid ${getNodeColor(node.nodeType)}`,
        background: node.nodeType === "event" ? "#fffbeb" : "#f8fafc",
        color: "#0f172a",
        fontSize: 12,
        width: node.nodeType === "event" ? 240 : 220,
        opacity: isHighlighted ? 1 : 0.25,
      },
    };
  });

  const edges = filteredEdges
    .filter((edge) => nodeMap.has(edge.fromNodeId) && nodeMap.has(edge.toNodeId))
    .map((edge) => {
      const isHighlighted = !selectedNodeId || highlightEdges.has(edge.edgeId);
      const label =
        edge.edgeType === "EVENT_EVENT"
          ? `${edge.relationType ?? "RELATED"} ${typeof edge.strength === "number" ? `(${edge.strength.toFixed(2)})` : ""}`.trim()
          : edge.edgeType;

      return {
        id: edge.edgeId,
        source: edge.fromNodeId,
        target: edge.toNodeId,
        type: "smoothstep",
        animated: edge.edgeType === "EVENT_EVENT" && isHighlighted,
        label,
        style: {
          stroke: getEdgeColor(edge.edgeType, edge.relationType),
          strokeWidth: edge.edgeType === "EVENT_EVENT" ? 2.2 : 1.8,
          opacity: isHighlighted ? 0.95 : 0.18,
        },
        labelStyle: { fontSize: 10, fill: "#334155" },
      };
    });

  return { nodes, edges };
}

export default function App() {
  const [events, setEvents] = useState([]);
  const [graph, setGraph] = useState(EMPTY_GRAPH);
  const [loading, setLoading] = useState(true);
  const [graphLoading, setGraphLoading] = useState(true);
  const [error, setError] = useState("");
  const [graphError, setGraphError] = useState("");
  const [sort, setSort] = useState("recent");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [minConfidence, setMinConfidence] = useState("");
  const [selectedPostId, setSelectedPostId] = useState(null);
  const [edgeTypeFilter, setEdgeTypeFilter] = useState(new Set(EDGE_TYPE_OPTIONS));
  const [minRelationStrength, setMinRelationStrength] = useState(0.7);

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
        params.set("includeProvisional", "true");
        if (minConfidence !== "") params.set("minConfidence", minConfidence);

        const response = await fetch(`/api/events?${params.toString()}`);
        if (!response.ok) throw new Error(`Request failed with status ${response.status}`);

        const data = await response.json();
        if (mounted) {
          setEvents(data.items ?? []);
          setTotalCount(data.totalCount ?? 0);
          setTotalPages(data.totalPages ?? 0);
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

  useEffect(() => {
    let mounted = true;

    async function loadGraph() {
      setGraphLoading(true);
      setGraphError("");

      try {
        const params = new URLSearchParams();
        params.set("sort", sort);
        params.set("maxEvents", "220");
        params.set("includeProvisional", "true");
        if (minConfidence !== "") params.set("minConfidence", minConfidence);

        const response = await fetch(`/api/events/graph?${params.toString()}`);
        if (!response.ok) throw new Error(`Graph request failed with status ${response.status}`);

        const data = await response.json();
        if (mounted) {
          setGraph(data ?? EMPTY_GRAPH);

          if (selectedPostId) {
            const postNodeId = `post:${selectedPostId}`;
            const exists = (data?.nodes ?? []).some((node) => node.nodeId === postNodeId);
            if (!exists) setSelectedPostId(null);
          }
        }
      } catch (err) {
        if (mounted) setGraphError(err.message || "Failed to load graph");
      } finally {
        if (mounted) setGraphLoading(false);
      }
    }

    loadGraph();
    return () => {
      mounted = false;
    };
  }, [minConfidence, sort, selectedPostId]);

  const stats = useMemo(() => {
    const withSummary = events.filter((x) => x.summary).length;
    const totalPosts = events.reduce((sum, x) => sum + x.postCount, 0);
    return { withSummary, totalPosts };
  }, [events]);

  const flowGraph = useMemo(
    () => buildFlowGraph(graph, selectedPostId, edgeTypeFilter, minRelationStrength),
    [graph, selectedPostId, edgeTypeFilter, minRelationStrength],
  );

  return (
    <main className="page">
      <header className="hero">
        <h1>TruthLens Event Dashboard</h1>
        <p>Live clustered news events with AI summaries and graph-native linking.</p>
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
        <span>Page {page} of {Math.max(totalPages, 1)}</span>
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
                  Confidence: {typeof event.confidenceScore === "number" ? event.confidenceScore.toFixed(3) : "N/A"}
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
              <h2>Event Graph (React Flow)</h2>
              <p>Click post chips to highlight related nodes and edges.</p>
              {selectedPostId && (
                <button type="button" onClick={() => setSelectedPostId(null)}>
                  Clear Selection
                </button>
              )}
            </div>

            <div className="graph-filters">
              {EDGE_TYPE_OPTIONS.map((edgeType) => (
                <label key={edgeType}>
                  <input
                    type="checkbox"
                    checked={edgeTypeFilter.has(edgeType)}
                    onChange={(e) => {
                      const next = new Set(edgeTypeFilter);
                      if (e.target.checked) next.add(edgeType);
                      else next.delete(edgeType);
                      setEdgeTypeFilter(next);
                    }}
                  />
                  {edgeType}
                </label>
              ))}
              <label>
                Min Event Relation Strength
                <input
                  type="number"
                  min="0"
                  max="1"
                  step="0.05"
                  value={minRelationStrength}
                  onChange={(e) => setMinRelationStrength(Math.max(0, Math.min(1, Number(e.target.value) || 0)))}
                />
              </label>
            </div>

            <div className="reactflow-wrapper">
              {graphLoading && <p className="graph-empty">Loading graph...</p>}
              {!graphLoading && graphError && <p className="status error">{graphError}</p>}
              {!graphLoading && !graphError && flowGraph.nodes.length === 0 && (
                <p className="graph-empty">No graph data for current filters.</p>
              )}
              {!graphLoading && !graphError && flowGraph.nodes.length > 0 && (
                <ReactFlow
                  nodes={flowGraph.nodes}
                  edges={flowGraph.edges}
                  fitView
                  attributionPosition="bottom-left"
                  onNodeClick={(_, node) => {
                    if (node?.data?.postId) {
                      setSelectedPostId(node.data.postId);
                    }
                  }}
                >
                  <MiniMap nodeStrokeWidth={3} />
                  <Controls />
                  <Background gap={18} size={1} />
                </ReactFlow>
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
