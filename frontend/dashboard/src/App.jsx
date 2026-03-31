import { useEffect, useMemo, useRef, useState } from "react";
import cytoscape from "cytoscape";

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

function buildLiteGraphModel(graph, selectedPostId, minRelationStrength) {
  const sourceNodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
  const sourceEdges = Array.isArray(graph?.edges) ? graph.edges : [];

  const eventNodes = sourceNodes.filter((node) => node.nodeType === "event");
  const eventNodeIds = new Set(eventNodes.map((node) => node.nodeId));

  const eventEdges = sourceEdges.filter((edge) => {
    if (edge.edgeType !== "EVENT_EVENT") return false;
    if (!eventNodeIds.has(edge.fromNodeId) || !eventNodeIds.has(edge.toNodeId)) return false;

    const strength = typeof edge.strength === "number" ? edge.strength : 0;
    return strength >= minRelationStrength;
  });

  const selectedPostNodeId = selectedPostId ? `post:${selectedPostId}` : null;
  const highlightedEventIds = new Set();

  if (selectedPostNodeId) {
    for (const edge of sourceEdges) {
      if (edge.edgeType !== "POST_EVENT") continue;

      if (edge.fromNodeId === selectedPostNodeId && eventNodeIds.has(edge.toNodeId)) {
        highlightedEventIds.add(edge.toNodeId);
      }

      if (edge.toNodeId === selectedPostNodeId && eventNodeIds.has(edge.fromNodeId)) {
        highlightedEventIds.add(edge.fromNodeId);
      }
    }
  }

  const highlightedEdgeIds = new Set();
  if (highlightedEventIds.size > 0) {
    for (const edge of eventEdges) {
      if (highlightedEventIds.has(edge.fromNodeId) || highlightedEventIds.has(edge.toNodeId)) {
        highlightedEdgeIds.add(edge.edgeId);
      }
    }
  }

  return {
    nodes: eventNodes,
    edges: eventEdges,
    highlightedEventIds,
    highlightedEdgeIds,
    hasSelection: Boolean(selectedPostNodeId),
  };
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
  const [minRelationStrength, setMinRelationStrength] = useState(0.7);

  const graphContainerRef = useRef(null);
  const cyRef = useRef(null);

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
        params.set("maxEvents", "180");
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

  const liteGraph = useMemo(
    () => buildLiteGraphModel(graph, selectedPostId, minRelationStrength),
    [graph, selectedPostId, minRelationStrength],
  );

  const cyElements = useMemo(() => {
    const nodes = liteGraph.nodes.map((node) => {
      const isHighlighted = liteGraph.highlightedEventIds.has(node.nodeId);
      const shouldFade = liteGraph.hasSelection && liteGraph.highlightedEventIds.size > 0 && !isHighlighted;

      return {
        data: {
          id: node.nodeId,
          label: truncate(node.label, 46),
          fullLabel: node.label,
        },
        classes: shouldFade ? "event-node faded" : isHighlighted ? "event-node highlighted" : "event-node",
      };
    });

    const edges = liteGraph.edges.map((edge) => {
      const isHighlighted = liteGraph.highlightedEdgeIds.has(edge.edgeId);
      const shouldFade = liteGraph.hasSelection && liteGraph.highlightedEventIds.size > 0 && !isHighlighted;

      return {
        data: {
          id: edge.edgeId,
          source: edge.fromNodeId,
          target: edge.toNodeId,
          relationType: edge.relationType ?? "RELATED",
          strength: typeof edge.strength === "number" ? edge.strength : 0,
        },
        classes: shouldFade ? "event-edge faded" : isHighlighted ? "event-edge highlighted" : "event-edge",
      };
    });

    return [...nodes, ...edges];
  }, [liteGraph]);

  useEffect(() => {
    if (!graphContainerRef.current || cyRef.current) {
      return undefined;
    }

    const cy = cytoscape({
      container: graphContainerRef.current,
      wheelSensitivity: 0.18,
      autoungrabify: true,
      style: [
        {
          selector: "node",
          style: {
            "background-color": "#5b5f66",
            label: "data(label)",
            color: "#f8fafc",
            "font-size": "12px",
            "text-wrap": "wrap",
            "text-max-width": "120px",
            "text-valign": "bottom",
            "text-halign": "center",
            "text-margin-y": "8px",
            width: 22,
            height: 22,
            opacity: 0.95,
          },
        },
        {
          selector: "node.highlighted",
          style: {
            "background-color": "#9f7aea",
            width: 28,
            height: 28,
            opacity: 1,
          },
        },
        {
          selector: "node.faded",
          style: {
            opacity: 0.24,
          },
        },
        {
          selector: "edge",
          style: {
            width: 1.2,
            "line-color": "#4b5563",
            "target-arrow-color": "#4b5563",
            "curve-style": "bezier",
            opacity: 0.6,
          },
        },
        {
          selector: "edge.highlighted",
          style: {
            width: 2,
            "line-color": "#a78bfa",
            "target-arrow-color": "#a78bfa",
            opacity: 0.95,
          },
        },
        {
          selector: "edge.faded",
          style: {
            opacity: 0.15,
          },
        },
      ],
    });

    cyRef.current = cy;

    return () => {
      cy.destroy();
      cyRef.current = null;
    };
  }, [graphLoading, graphError, liteGraph.nodes.length]);

  useEffect(() => {
    const cy = cyRef.current;
    if (!cy) {
      return;
    }

    cy.elements().remove();
    if (cyElements.length === 0) {
      return;
    }

    cy.add(cyElements);
    cy.layout({
      name: "cose",
      animate: false,
      fit: true,
      padding: 28,
      randomize: true,
      nodeRepulsion: 140000,
      idealEdgeLength: 105,
      edgeElasticity: 0.16,
      gravity: 0.28,
    }).run();
  }, [cyElements]);

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
              <h2>Event Mind Map (Lite)</h2>
              <p>Floating event title nodes. Click a post chip to highlight related events.</p>
              {selectedPostId && (
                <button type="button" onClick={() => setSelectedPostId(null)}>
                  Clear Selection
                </button>
              )}
            </div>

            <div className="graph-filters">
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

            <div className="cytoscape-wrapper">
              {graphLoading && <p className="graph-empty">Loading graph...</p>}
              {!graphLoading && graphError && <p className="status error">{graphError}</p>}
              {!graphLoading && !graphError && liteGraph.nodes.length === 0 && (
                <p className="graph-empty">No graph data for current filters.</p>
              )}
              {!graphLoading && !graphError && liteGraph.nodes.length > 0 && (
                <div ref={graphContainerRef} className="cytoscape-canvas" />
              )}
            </div>

            <div className="graph-legend">
              <span className="dot event" /> Event node
              <span className="dot highlight" /> Related to selected post
            </div>
          </aside>
        </section>
      )}
    </main>
  );
}
