import { useEffect, useMemo, useState } from "react";

function formatDate(value) {
  return new Date(value).toLocaleString();
}

function getConfidenceClass(score) {
  if (typeof score !== "number") return "badge neutral";
  if (score >= 0.8) return "badge high";
  if (score >= 0.6) return "badge medium";
  return "badge low";
}

export default function App() {
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [sort, setSort] = useState("recent");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [minConfidence, setMinConfidence] = useState("");

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

  const stats = useMemo(() => {
    const withSummary = events.filter((x) => x.summary).length;
    const totalPosts = events.reduce((sum, x) => sum + x.postCount, 0);
    return { withSummary, totalPosts };
  }, [events]);

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
            </article>
          ))}
        </section>
      )}
    </main>
  );
}
