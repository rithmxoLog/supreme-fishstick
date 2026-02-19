import React, { useEffect, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../contexts/AuthContext';
import IssueDetail from './IssueDetail';

export default function IssuesPage({ repoName }) {
  const { user } = useAuth();
  const [issues, setIssues] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [statusFilter, setStatusFilter] = useState('open');
  const [selectedIssue, setSelectedIssue] = useState(null);
  const [showCreate, setShowCreate] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newBody, setNewBody] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState('');

  const load = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await api.listIssues(repoName, statusFilter);
      setIssues(data);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [repoName, statusFilter]);

  const handleCreate = async (e) => {
    e.preventDefault();
    setCreating(true);
    setCreateError('');
    try {
      await api.createIssue(repoName, newTitle.trim(), newBody.trim() || undefined);
      setNewTitle('');
      setNewBody('');
      setShowCreate(false);
      setStatusFilter('open');
      load();
    } catch (e) {
      setCreateError(e.message);
    } finally {
      setCreating(false);
    }
  };

  if (selectedIssue !== null) {
    return (
      <IssueDetail
        repoName={repoName}
        issueNumber={selectedIssue}
        onBack={() => { setSelectedIssue(null); load(); }}
      />
    );
  }

  return (
    <div>
      <div className="issues-header">
        <div className="issues-filter-tabs">
          <button
            className={`issues-filter-btn ${statusFilter === 'open' ? 'active' : ''}`}
            onClick={() => setStatusFilter('open')}
          >
            Open
          </button>
          <button
            className={`issues-filter-btn ${statusFilter === 'closed' ? 'active' : ''}`}
            onClick={() => setStatusFilter('closed')}
          >
            Closed
          </button>
        </div>
        {user && (
          <button className="btn btn-primary btn-sm" onClick={() => setShowCreate(true)}>
            + New issue
          </button>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}

      {loading ? (
        <div className="loading-page"><span className="spinner" /></div>
      ) : issues.length === 0 ? (
        <div className="empty-state">
          <div className="empty-title">No {statusFilter} issues</div>
          {user && statusFilter === 'open' && (
            <><p>Open an issue to report a problem or request a feature.</p><br />
            <button className="btn btn-primary" onClick={() => setShowCreate(true)}>
              Open an issue
            </button></>
          )}
        </div>
      ) : (
        <ul className="issues-list">
          {issues.map(issue => (
            <li key={issue.id} className="issue-item" onClick={() => setSelectedIssue(issue.number)}>
              <div className="issue-icon">
                {issue.status === 'open'
                  ? <span style={{ color: '#3fb950' }}>●</span>
                  : <span style={{ color: '#8b949e' }}>●</span>
                }
              </div>
              <div className="issue-body">
                <div className="issue-title">{issue.title}</div>
                <div className="issue-meta">
                  #{issue.number} opened {formatDate(issue.createdAt)} by {issue.author.username}
                  {issue.commentCount > 0 && (
                    <span style={{ marginLeft: 12 }}>
                      💬 {issue.commentCount}
                    </span>
                  )}
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}

      {showCreate && (
        <div className="modal-overlay" onClick={e => { if (e.target === e.currentTarget) setShowCreate(false); }}>
          <div className="modal" style={{ width: 600 }}>
            <div className="modal-title">Open a new issue</div>
            {createError && <div className="error-banner">{createError}</div>}
            <form onSubmit={handleCreate}>
              <div className="form-group">
                <label className="form-label">Title *</label>
                <input
                  className="form-input"
                  value={newTitle}
                  onChange={e => setNewTitle(e.target.value)}
                  placeholder="Brief description of the issue"
                  required
                  autoFocus
                />
              </div>
              <div className="form-group">
                <label className="form-label">Description (optional)</label>
                <textarea
                  className="form-textarea"
                  value={newBody}
                  onChange={e => setNewBody(e.target.value)}
                  placeholder="Provide more details about the issue…"
                  style={{ minHeight: 120 }}
                />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn" onClick={() => setShowCreate(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary" disabled={creating || !newTitle.trim()}>
                  {creating ? <><span className="spinner" /> Submitting…</> : 'Submit issue'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  const now = new Date();
  const diff = (now - d) / 1000;
  if (diff < 60) return 'just now';
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
  if (diff < 86400 * 30) return `${Math.floor(diff / 86400)}d ago`;
  return d.toLocaleDateString();
}
