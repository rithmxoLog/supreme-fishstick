import React, { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../contexts/AuthContext';
import FileBrowser from './FileBrowser';
import CommitHistory from './CommitHistory';
import BranchManager from './BranchManager';
import PushPanel from './PushPanel';
import IssuesPage from '../pages/IssuesPage';

const TABS = ['Code', 'Commits', 'Branches', 'Issues', 'Push'];

export default function RepoView() {
  const { repoName } = useParams();
  const { user } = useAuth();
  const [repo, setRepo] = useState(null);
  const [tab, setTab] = useState('Code');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [currentBranch, setCurrentBranch] = useState('');
  const [cloneCopied, setCloneCopied] = useState(false);

  const load = async () => {
    setLoading(true);
    try {
      const data = await api.getRepo(repoName);
      setRepo(data);
      setCurrentBranch(data.currentBranch || 'main');
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [repoName]);

  const handleCheckout = async (branch) => {
    try {
      await api.checkoutBranch(repoName, branch);
      setCurrentBranch(branch);
      load();
    } catch (e) {
      alert(e.message);
    }
  };

  const cloneUrl = `${window.location.origin}/api/git/${repoName}.git`;

  const copyCloneUrl = () => {
    navigator.clipboard.writeText(`git clone ${cloneUrl}`).then(() => {
      setCloneCopied(true);
      setTimeout(() => setCloneCopied(false), 2000);
    });
  };

  if (loading) return <div className="loading-page"><span className="spinner" /> Loading…</div>;
  if (error) return (
    <div>
      <div className="error-banner">{error}</div>
      <Link to="/" className="btn">← Back to repositories</Link>
    </div>
  );

  return (
    <div>
      <div className="repo-view-header">
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <Link to="/" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: 14 }}>
            Repositories
          </Link>
          <span style={{ color: 'var(--text-secondary)' }}>/</span>
          <span className="repo-view-title">{repo.name}</span>
          {repo.owner && (
            <span style={{ color: 'var(--text-secondary)', fontSize: 13 }}>by {repo.owner}</span>
          )}
          <span className={`visibility-badge ${repo.isPublic ? 'public' : 'private'}`}>
            {repo.isPublic ? 'Public' : 'Private'}
          </span>
        </div>
        {repo.description && <div className="repo-view-subtitle">{repo.description}</div>}

        {/* Clone URL row */}
        <div className="clone-row">
          <span className="clone-label">Clone</span>
          <code className="clone-url">{cloneUrl}</code>
          <button className="btn btn-sm" onClick={copyCloneUrl}>
            {cloneCopied ? '✓ Copied' : 'Copy'}
          </button>
          <a
            className="btn btn-sm"
            href={api.getRepoDownloadUrl(repoName, currentBranch)}
            download
          >
            ↓ Download ZIP
          </a>
        </div>
      </div>

      <div className="repo-tabs">
        {TABS.map(t => (
          <button
            key={t}
            className={`repo-tab ${tab === t ? 'active' : ''}`}
            onClick={() => setTab(t)}
          >
            {t}
          </button>
        ))}
      </div>

      {tab === 'Code' && (
        <FileBrowser
          repoName={repoName}
          currentBranch={currentBranch}
          branches={repo.branches || []}
          onCheckout={handleCheckout}
          onRefresh={load}
        />
      )}
      {tab === 'Commits' && (
        <CommitHistory repoName={repoName} currentBranch={currentBranch} />
      )}
      {tab === 'Branches' && (
        <BranchManager
          repoName={repoName}
          repo={repo}
          currentBranch={currentBranch}
          onCheckout={handleCheckout}
          onRefresh={load}
        />
      )}
      {tab === 'Issues' && (
        <IssuesPage repoName={repoName} />
      )}
      {tab === 'Push' && (
        <PushPanel
          repoName={repoName}
          currentBranch={currentBranch}
          branches={repo.branches || []}
          onRefresh={load}
        />
      )}
    </div>
  );
}
