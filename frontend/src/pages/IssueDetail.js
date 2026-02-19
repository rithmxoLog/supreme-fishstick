import React, { useEffect, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../contexts/AuthContext';

export default function IssueDetail({ repoName, issueNumber, onBack }) {
  const { user } = useAuth();
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [comment, setComment] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [commentError, setCommentError] = useState('');

  const load = async () => {
    setLoading(true);
    try {
      const d = await api.getIssue(repoName, issueNumber);
      setData(d);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [repoName, issueNumber]);

  const handleToggleStatus = async () => {
    const newStatus = data.issue.status === 'open' ? 'closed' : 'open';
    try {
      await api.updateIssue(repoName, issueNumber, { status: newStatus });
      load();
    } catch (e) {
      alert(e.message);
    }
  };

  const handleComment = async (e) => {
    e.preventDefault();
    if (!comment.trim()) return;
    setSubmitting(true);
    setCommentError('');
    try {
      await api.addComment(repoName, issueNumber, comment.trim());
      setComment('');
      load();
    } catch (e) {
      setCommentError(e.message);
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) return <div className="loading-page"><span className="spinner" /></div>;
  if (error) return <div className="error-banner">{error}</div>;

  const { issue, comments } = data;

  return (
    <div>
      <button className="btn btn-sm" onClick={onBack} style={{ marginBottom: 16 }}>
        ← Back to issues
      </button>

      <div className="issue-detail-header">
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
          <h2 className="issue-detail-title">{issue.title}</h2>
          <span className={`issue-status-badge ${issue.status}`}>
            {issue.status === 'open' ? '● Open' : '● Closed'}
          </span>
        </div>
        <div className="issue-meta">
          #{issue.number} · opened by <strong>{issue.author.username}</strong> · {formatDate(issue.createdAt)}
        </div>
      </div>

      {/* Issue body */}
      {issue.body && (
        <div className="issue-comment-box">
          <div className="comment-header">
            <strong>{issue.author.username}</strong>
            <span style={{ color: 'var(--text-secondary)', fontSize: 12 }}>{formatDate(issue.createdAt)}</span>
          </div>
          <div className="comment-body">{issue.body}</div>
        </div>
      )}

      {/* Comments */}
      {comments.length > 0 && (
        <div className="comments-section">
          {comments.map(c => (
            <div key={c.id} className="issue-comment-box">
              <div className="comment-header">
                <strong>{c.author.username}</strong>
                <span style={{ color: 'var(--text-secondary)', fontSize: 12 }}>{formatDate(c.createdAt)}</span>
              </div>
              <div className="comment-body">{c.body}</div>
            </div>
          ))}
        </div>
      )}

      {/* Actions */}
      {user && (
        <div className="issue-actions">
          <button
            className={`btn btn-sm ${issue.status === 'open' ? 'btn-danger' : 'btn-primary'}`}
            onClick={handleToggleStatus}
            style={{ marginBottom: 16 }}
          >
            {issue.status === 'open' ? 'Close issue' : 'Reopen issue'}
          </button>

          <form onSubmit={handleComment} className="comment-form">
            <label className="form-label">Leave a comment</label>
            {commentError && <div className="error-banner">{commentError}</div>}
            <textarea
              className="form-textarea"
              value={comment}
              onChange={e => setComment(e.target.value)}
              placeholder="Write a comment…"
              style={{ minHeight: 100 }}
            />
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
              <button type="submit" className="btn btn-primary" disabled={submitting || !comment.trim()}>
                {submitting ? <><span className="spinner" /> Posting…</> : 'Comment'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}
