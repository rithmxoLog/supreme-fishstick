import React, { useEffect, useState } from 'react';
import { api } from '../api';
import { html as diff2html } from 'diff2html';
import 'diff2html/bundles/css/diff2html.min.css';

export default function CommitHistory({ repoName, currentBranch }) {
  const [commits, setCommits] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [expandedHash, setExpandedHash] = useState(null);
  const [commitDetail, setCommitDetail] = useState({});
  const [detailLoading, setDetailLoading] = useState(false);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const data = await api.listCommits(repoName, currentBranch);
        setCommits(data);
      } catch (e) {
        setError(e.message);
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [repoName, currentBranch]);

  const toggleCommit = async (hash) => {
    if (expandedHash === hash) {
      setExpandedHash(null);
      return;
    }
    setExpandedHash(hash);
    if (!commitDetail[hash]) {
      setDetailLoading(true);
      try {
        const data = await api.getCommit(repoName, hash);
        setCommitDetail(prev => ({ ...prev, [hash]: data.diff || '' }));
      } catch (e) {
        setCommitDetail(prev => ({ ...prev, [hash]: '' }));
      } finally {
        setDetailLoading(false);
      }
    }
  };

  if (loading) return <div className="loading-page"><span className="spinner" /></div>;
  if (error) return <div className="error-banner">{error}</div>;

  return (
    <div>
      <div style={{ fontSize: 14, color: 'var(--text-secondary)', marginBottom: 12 }}>
        {commits.length} commit{commits.length !== 1 ? 's' : ''} on{' '}
        <span className="branch-badge">{currentBranch}</span>
      </div>

      {commits.length === 0 ? (
        <div className="empty-state">
          <div className="empty-title">No commits yet</div>
        </div>
      ) : (
        <ul className="commit-list">
          {commits.map(commit => (
            <li key={commit.hash} className="commit-item" style={{ flexDirection: 'column', alignItems: 'stretch' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <div style={{ flex: 1 }}>
                  <div className="commit-msg">{commit.message}</div>
                  <div className="commit-sub">
                    {commit.author} · {formatDate(commit.date)}
                  </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span className="commit-hash">{commit.shortHash}</span>
                  <button
                    className="btn btn-sm"
                    onClick={() => toggleCommit(commit.hash)}
                    style={{ minWidth: 80 }}
                  >
                    {expandedHash === commit.hash ? 'Hide diff' : 'View diff'}
                  </button>
                </div>
              </div>

              {expandedHash === commit.hash && (
                <div style={{ marginTop: 12 }}>
                  {detailLoading && !commitDetail[commit.hash] ? (
                    <span className="spinner" />
                  ) : commitDetail[commit.hash] ? (
                    <div
                      className="diff2html-wrapper"
                      dangerouslySetInnerHTML={{
                        __html: diff2html(commitDetail[commit.hash], {
                          drawFileList: true,
                          matching: 'lines',
                          outputFormat: 'side-by-side',
                          renderNothingWhenEmpty: false,
                        })
                      }}
                    />
                  ) : (
                    <div style={{ color: 'var(--text-secondary)', fontSize: 13, padding: '8px 0' }}>
                      No diff available for this commit.
                    </div>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}
