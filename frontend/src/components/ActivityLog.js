import React, { useState, useEffect, useCallback, useRef } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

const WRITE_EVENTS = new Set([
  'REPO_CREATED', 'REPO_DELETED',
  'FILE_CREATED', 'FILE_UPDATED', 'FILE_DELETED', 'FILES_PUSHED', 'FILE_DOWNLOADED', 'REPO_DOWNLOADED',
  'BRANCH_CREATED', 'BRANCH_DELETED', 'BRANCH_SWITCHED', 'BRANCH_MERGED',
]);

function formatTimestamp(ts) {
  const d = new Date(ts);
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString();
}

function summarizeDetails(details) {
  if (!details || typeof details !== 'object') return '—';
  const entries = Object.entries(details);
  if (entries.length === 0) return '—';
  return entries.slice(0, 3).map(([k, v]) => {
    const val = Array.isArray(v) ? `[${v.length}]` : String(v);
    return `${k}: ${val}`;
  }).join(' · ');
}

export default function ActivityLog() {
  const [logs, setLogs] = useState([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [eventTypes, setEventTypes] = useState([]);
  const [expandedId, setExpandedId] = useState(null);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [filters, setFilters] = useState({ repo: '', event_type: '', from: '', to: '' });
  const [limit, setLimit] = useState(50);
  const [offset, setOffset] = useState(0);
  const intervalRef = useRef(null);

  const fetchLogs = useCallback(async () => {
    try {
      setLoading(true);
      setError('');
      const result = await api.getLogs({
        repo: filters.repo || undefined,
        event_type: filters.event_type || undefined,
        from: filters.from || undefined,
        to: filters.to || undefined,
        limit,
        offset,
      });
      setLogs(result.logs);
      setTotal(result.total);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  }, [filters, limit, offset]);

  // Load event types once
  useEffect(() => {
    api.getLogEventTypes().then(setEventTypes).catch(() => {});
  }, []);

  // Fetch logs on filter/pagination change
  useEffect(() => {
    fetchLogs();
  }, [fetchLogs]);

  // Auto-refresh interval
  useEffect(() => {
    if (autoRefresh) {
      intervalRef.current = setInterval(fetchLogs, 10000);
    } else {
      clearInterval(intervalRef.current);
    }
    return () => clearInterval(intervalRef.current);
  }, [autoRefresh, fetchLogs]);

  function handleFilterChange(key, value) {
    setFilters(prev => ({ ...prev, [key]: value }));
    setOffset(0);
  }

  function clearFilters() {
    setFilters({ repo: '', event_type: '', from: '', to: '' });
    setLimit(50);
    setOffset(0);
  }

  const totalPages = Math.max(1, Math.ceil(total / limit));
  const currentPage = Math.floor(offset / limit) + 1;
  const showingFrom = total === 0 ? 0 : offset + 1;
  const showingTo = Math.min(offset + limit, total);

  return (
    <div className="container" style={{ paddingTop: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20 }}>
        <h1 style={{ fontSize: 20, fontWeight: 700, margin: 0 }}>Activity Log</h1>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            className={`btn btn-sm ${autoRefresh ? 'btn-primary' : ''}`}
            onClick={() => setAutoRefresh(v => !v)}
            title="Toggle 10-second auto-refresh"
          >
            {autoRefresh ? 'Auto-refresh: ON' : 'Auto-refresh: OFF'}
          </button>
          <button className="btn btn-sm" onClick={fetchLogs} disabled={loading}>
            {loading ? 'Loading…' : 'Refresh'}
          </button>
        </div>
      </div>

      {/* Filter bar */}
      <div className="log-filters">
        <div className="form-group">
          <label className="form-label">Repository</label>
          <input
            className="form-input"
            placeholder="All repositories"
            value={filters.repo}
            onChange={e => handleFilterChange('repo', e.target.value)}
          />
        </div>
        <div className="form-group">
          <label className="form-label">Event type</label>
          <select
            className="form-select"
            value={filters.event_type}
            onChange={e => handleFilterChange('event_type', e.target.value)}
          >
            <option value="">All event types</option>
            {eventTypes.map(et => <option key={et} value={et}>{et}</option>)}
          </select>
        </div>
        <div className="form-group">
          <label className="form-label">From date</label>
          <input
            type="date"
            className="form-input"
            value={filters.from}
            onChange={e => handleFilterChange('from', e.target.value)}
          />
        </div>
        <div className="form-group">
          <label className="form-label">To date</label>
          <input
            type="date"
            className="form-input"
            value={filters.to}
            onChange={e => handleFilterChange('to', e.target.value)}
          />
        </div>
        <div className="form-group">
          <label className="form-label">Per page</label>
          <select
            className="form-select"
            value={limit}
            onChange={e => { setLimit(Number(e.target.value)); setOffset(0); }}
          >
            {[25, 50, 100, 500].map(n => <option key={n} value={n}>{n}</option>)}
          </select>
        </div>
        <button className="btn btn-sm" onClick={clearFilters} style={{ alignSelf: 'flex-end' }}>
          Clear filters
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {/* Results summary */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, fontSize: 13, color: 'var(--text-secondary)' }}>
        <span>
          {total === 0
            ? 'No events found'
            : `Showing ${showingFrom}–${showingTo} of ${total} events`}
        </span>
      </div>

      {/* Table */}
      <div style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius)', overflow: 'hidden' }}>
        <table className="log-table">
          <thead>
            <tr>
              <th style={{ width: 170 }}>Timestamp</th>
              <th style={{ width: 190 }}>Event type</th>
              <th style={{ width: 150 }}>Repository</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            {logs.length === 0 && !loading && (
              <tr>
                <td colSpan={4} style={{ textAlign: 'center', padding: '40px 0', color: 'var(--text-secondary)' }}>
                  <div style={{ fontSize: 28, marginBottom: 8 }}>🕐</div>
                  <div>No activity logged yet</div>
                  <div style={{ fontSize: 12, marginTop: 4 }}>Events will appear here once you start using GitRipp.</div>
                </td>
              </tr>
            )}
            {logs.map(log => (
              <tr key={log.id}>
                <td style={{ fontSize: 12, color: 'var(--text-secondary)', whiteSpace: 'nowrap' }}>
                  {formatTimestamp(log.created_at)}
                </td>
                <td>
                  <span className={`event-badge ${WRITE_EVENTS.has(log.event_type) ? 'write' : 'read'}`}>
                    {log.event_type}
                  </span>
                </td>
                <td>
                  {log.repo_name
                    ? <Link to={`/repos/${log.repo_name}`} style={{ color: 'var(--link)' }}>{log.repo_name}</Link>
                    : <span style={{ color: 'var(--text-secondary)' }}>—</span>}
                </td>
                <td
                  className="details-cell"
                  onClick={() => setExpandedId(expandedId === log.id ? null : log.id)}
                  title="Click to expand"
                >
                  {expandedId === log.id
                    ? <pre>{JSON.stringify(log.details, null, 2)}</pre>
                    : <span style={{ color: 'var(--text-secondary)', fontSize: 12 }}>{summarizeDetails(log.details)}</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {total > limit && (
        <div className="pagination-bar">
          <button
            className="btn btn-sm"
            disabled={offset === 0}
            onClick={() => setOffset(Math.max(0, offset - limit))}
          >
            ← Prev
          </button>
          <span>Page {currentPage} of {totalPages}</span>
          <button
            className="btn btn-sm"
            disabled={offset + limit >= total}
            onClick={() => setOffset(offset + limit)}
          >
            Next →
          </button>
        </div>
      )}
    </div>
  );
}
