import { useEffect, useMemo, useState } from "react";

function formatDate(value) {
  return new Date(value).toLocaleString();
}

export default function App() {
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let mounted = true;

    async function loadEvents() {
      setLoading(true);
      setError("");

      try {
        const response = await fetch("/api/events?limit=60");
        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}`);
        }

        const data = await response.json();
        if (mounted) setEvents(data);
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
  }, []);

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
          <h2>{events.length}</h2>
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

              <p className="summary">{event.summary || "Summary pending..."}</p>

              <dl>
                <div>
                  <dt>Last Seen</dt>
                  <dd>{formatDate(event.lastSeenAtUtc)}</dd>
                </div>
                <div>
                  <dt>Confidence</dt>
                  <dd>
                    {typeof event.confidenceScore === "number"
                      ? event.confidenceScore.toFixed(3)
                      : "N/A"}
                  </dd>
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
