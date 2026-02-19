import React, { useEffect, useState, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../contexts/AuthContext';

export default function ExplorePage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [repos, setRepos] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState('');
  const [newDesc, setNewDesc] = useState('');
  const [newPublic, setNewPublic] = useState(true);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState('');

  const load = useCallback(async (q = '') => {
    setLoading(true);
    setError('');
    try {
      const data = await api.listRepos(q || undefined, !user ? true : undefined);
      setRepos(data);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  }, [user]);

  useEffect(() => { load(); }, [load]);

  const handleSearch = (e) => {
    e.preventDefault();
    load(search);
  };

  const handleCreate = async (e) => {
    e.preventDefault();
    setCreating(true);
    setCreateError('');
    try {
      await api.createRepo(newName.trim(), newDesc.trim(), newPublic);
      setNewName(''); setNewDesc(''); setNewPublic(true);
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
      load(search);
    } catch (e) {
      alert(e.message);
    }
  };

  return (
    <div>
      <div className="explore-hero">
        <h1 className="explore-title">Explore Repositories</h1>
        <p className="explore-subtitle">Browse all public repositories or manage your own.</p>
        <form onSubmit={handleSearch} className="explore-search">
          <input
            className="form-input explore-search-input"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search repositories…"
          />
          <button type="submit" className="btn">Search</button>
          {search && (
            <button type="button" className="btn" onClick={() => { setSearch(''); load(''); }}>
              Clear
            </button>
          )}
        </form>
      </div>

      <div className="page-header">
        <div style={{ color: 'var(--text-secondary)', fontSize: 14 }}>
          {loading ? 'Loading…' : `${repos.length} repositor${repos.length !== 1 ? 'ies' : 'y'}${search ? ` matching "${search}"` : ''}`}
        </div>
        {user && (
          <button className="btn btn-primary" onClick={() => setShowCreate(true)}>
            + New repository
          </button>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}

      {!loading && repos.length === 0 ? (
        <div className="empty-state">
          <div className="empty-title">{search ? 'No matching repositories' : 'No repositories yet'}</div>
          {user
            ? <><p>Be the first to create one.</p><br /><button className="btn btn-primary" onClick={() => setShowCreate(true)}>Create a repository</button></>
            : <p>Sign in to create repositories.</p>
          }
        </div>
      ) : (
        <div className="repo-grid">
          {repos.map(repo => (
            <div className="repo-card" key={repo.name}>
              <div className="repo-card-main">
                <div className="repo-card-header">
                  <Link className="repo-name" to={`/repos/${repo.name}`}>{repo.name}</Link>
                  <span className={`visibility-badge ${repo.isPublic ? 'public' : 'private'}`}>
                    {repo.isPublic ? 'Public' : 'Private'}
                  </span>
                </div>
                {repo.owner && (
                  <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
                    by {repo.owner}
                  </div>
                )}
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
              {user && (repo.owner === user.username || user.isAdmin) && (
                <button
                  className="btn btn-danger btn-sm"
                  style={{ alignSelf: 'flex-start', marginTop: 8 }}
                  onClick={() => handleDelete(repo.name)}
                >
                  Delete
                </button>
              )}
            </div>
          ))}
        </div>
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
              <div className="form-group">
                <label className="form-label">Visibility</label>
                <div className="visibility-options">
                  <label className="visibility-option">
                    <input
                      type="radio"
                      name="visibility"
                      checked={newPublic}
                      onChange={() => setNewPublic(true)}
                    />
                    <span>
                      <strong>Public</strong> — anyone can see this repository
                    </span>
                  </label>
                  <label className="visibility-option">
                    <input
                      type="radio"
                      name="visibility"
                      checked={!newPublic}
                      onChange={() => setNewPublic(false)}
                    />
                    <span>
                      <strong>Private</strong> — only you can see this repository
                    </span>
                  </label>
                </div>
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
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
  if (diff < 86400 * 30) return `${Math.floor(diff / 86400)}d ago`;
  return d.toLocaleDateString();
}
