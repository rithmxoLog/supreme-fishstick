import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';

export default function RepoList() {
  const [repos, setRepos] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState('');

  const load = async () => {
    setLoading(true);
    try {
      const data = await api.listRepos();
      setRepos(data);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const handleCreate = async (e) => {
    e.preventDefault();
    setCreating(true);
    setCreateError('');
    try {
      await api.createRepo(newName.trim(), newDesc.trim());
      setNewName(''); setNewDesc('');
      setShowCreate(false);
      load();
    } catch (e) {
      setCreateError(e.message);
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (name) => {
    if (!window.confirm(`Delete repository "${name}"? This cannot be undone.`)) return;
    try {
      await api.deleteRepo(name);
      load();
    } catch (e) {
      alert(e.message);
    }
  };

  if (loading) return <div className="loading-page"><span className="spinner" /> Loading repositories…</div>;

  return (
    <div>
      <div className="page-header">
        <h1 className="page-title">Repositories</h1>
        <button className="btn btn-primary" onClick={() => setShowCreate(true)}>+ New repository</button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {repos.length === 0 ? (
        <div className="empty-state">
          <div className="empty-title">No repositories yet</div>
          <p>Create your first repository to get started.</p>
          <br />
          <button className="btn btn-primary" onClick={() => setShowCreate(true)}>Create a repository</button>
        </div>
      ) : (
        repos.map(repo => (
          <div className="repo-item" key={repo.name}>
            <div className="flex-1">
              <Link className="repo-name" to={`/repos/${repo.name}`}>{repo.name}</Link>
              {repo.description && <div className="repo-desc">{repo.description}</div>}
              <div className="repo-meta">
                <span className="tag">{repo.currentBranch || 'main'}</span>
                {' '}
                {repo.lastCommit
                  ? <>Last commit: <b>{repo.lastCommit.message}</b> · {formatDate(repo.lastCommit.date)}</>
                  : 'No commits yet'
                }
              </div>
            </div>
            <button className="btn btn-danger btn-sm" onClick={() => handleDelete(repo.name)}>Delete</button>
          </div>
        ))
      )}

      {showCreate && (
        <div className="modal-overlay" onClick={e => { if (e.target === e.currentTarget) setShowCreate(false); }}>
          <div className="modal">
            <div className="modal-title">Create a new repository</div>
            {createError && <div className="error-banner">{createError}</div>}
            <form onSubmit={handleCreate}>
              <div className="form-group">
                <label className="form-label">Repository name *</label>
                <input
                  className="form-input"
                  value={newName}
                  onChange={e => setNewName(e.target.value)}
                  placeholder="my-awesome-project"
                  required
                  autoFocus
                />
              </div>
              <div className="form-group">
                <label className="form-label">Description (optional)</label>
                <textarea
                  className="form-textarea"
                  value={newDesc}
                  onChange={e => setNewDesc(e.target.value)}
                  placeholder="Short description of this repository"
                />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn" onClick={() => setShowCreate(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary" disabled={creating || !newName.trim()}>
                  {creating ? <><span className="spinner" /> Creating…</> : 'Create repository'}
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
  if (diff < 3600) return `${Math.floor(diff/60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff/3600)}h ago`;
  return `${Math.floor(diff/86400)}d ago`;
}
