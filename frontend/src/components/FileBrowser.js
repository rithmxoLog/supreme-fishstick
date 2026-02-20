import React, { useEffect, useState } from 'react';
import { api } from '../api';
import FileViewer from './FileViewer';

export default function FileBrowser({ repoName, currentBranch, branches, onCheckout, onRefresh }) {
  const [path, setPath] = useState('');
  const [files, setFiles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [selectedFile, setSelectedFile] = useState(null);

  const loadFiles = async (p = '') => {
    setLoading(true);
    setError('');
    try {
      const data = await api.listFiles(repoName, p);
      setFiles(data.files);
      setPath(p);
      setSelectedFile(null);
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadFiles(''); }, [repoName, currentBranch]);

  const navigateTo = (filePath, type) => {
    if (type === 'directory') {
      loadFiles(filePath);
    } else {
      setSelectedFile(filePath);
    }
  };

  const breadcrumbs = () => {
    if (!path) return [];
    return path.split('/').filter(Boolean);
  };

  if (selectedFile) {
    return (
      <FileViewer
        repoName={repoName}
        filePath={selectedFile}
        currentBranch={currentBranch}
        onBack={() => { setSelectedFile(null); }}
        onRefresh={() => { loadFiles(path); onRefresh(); }}
      />
    );
  }

  return (
    <div>
      <div className="branch-selector" style={{ justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <select
            className="form-select"
            style={{ width: 'auto' }}
            value={currentBranch}
            onChange={e => onCheckout(e.target.value)}
          >
            {branches.map(b => <option key={b} value={b}>{b}</option>)}
          </select>
          <span className="branch-badge">
            <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
              <path d="M5 3.25a.75.75 0 11-1.5 0 .75.75 0 011.5 0zm0 2.122a2.25 2.25 0 10-1.5 0v.878A2.25 2.25 0 005.75 8.5h1.5v2.128a2.251 2.251 0 101.5 0V8.5h1.5a2.25 2.25 0 002.25-2.25v-.878a2.25 2.25 0 10-1.5 0v.878a.75.75 0 01-.75.75h-4.5A.75.75 0 015 6.25v-.878zm3.75 7.378a.75.75 0 11-1.5 0 .75.75 0 011.5 0zm3-8.75a.75.75 0 11-1.5 0 .75.75 0 011.5 0z"/>
            </svg>
            {currentBranch}
          </span>
        </div>
        <a
          className="btn btn-sm"
          href={api.getFolderDownloadUrl(repoName, path, currentBranch)}
          download
          title={path ? `Download "${path.split('/').pop()}" as ZIP` : 'Download repository as ZIP'}
        >
          ↓ {path ? 'Folder ZIP' : 'ZIP'}
        </a>
      </div>

      {/* Breadcrumb */}
      <div className="breadcrumb">
        <a onClick={() => loadFiles('')}>{repoName}</a>
        {breadcrumbs().map((crumb, i) => {
          const crumbPath = breadcrumbs().slice(0, i + 1).join('/');
          return (
            <React.Fragment key={crumbPath}>
              <span className="breadcrumb-sep">/</span>
              <a onClick={() => loadFiles(crumbPath)}>{crumb}</a>
            </React.Fragment>
          );
        })}
      </div>

      {error && <div className="error-banner">{error}</div>}

      {loading ? (
        <div className="loading-page"><span className="spinner" /></div>
      ) : files.length === 0 ? (
        <div className="empty-state">
          <div className="empty-title">Empty directory</div>
        </div>
      ) : (
        <table className="file-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Last commit</th>
              <th style={{ textAlign: 'right' }}>Date</th>
              <th style={{ width: 40 }}></th>
            </tr>
          </thead>
          <tbody>
            {path && (
              <tr>
                <td colSpan={4}>
                  <span
                    className="file-link dir-link"
                    onClick={() => {
                      const parts = path.split('/').filter(Boolean);
                      parts.pop();
                      loadFiles(parts.join('/'));
                    }}
                  >
                    <span className="file-icon">📁</span>..
                  </span>
                </td>
              </tr>
            )}
            {files.map(file => (
              <tr key={file.path}>
                <td>
                  <span
                    className={`file-link ${file.type === 'directory' ? 'dir-link' : ''}`}
                    onClick={() => navigateTo(file.path, file.type)}
                  >
                    <span className="file-icon">{file.type === 'directory' ? '📁' : getFileIcon(file.name)}</span>
                    {file.name}
                  </span>
                </td>
                <td className="commit-msg-cell">
                  {file.lastCommit ? file.lastCommit.message : '—'}
                </td>
                <td className="date-cell">
                  {file.lastCommit ? formatDate(file.lastCommit.date) : '—'}
                </td>
                <td style={{ textAlign: 'right', paddingRight: 8 }}>
                  {file.type === 'directory' && (
                    <a
                      className="btn btn-sm"
                      href={api.getFolderDownloadUrl(repoName, file.path, currentBranch)}
                      download
                      title={`Download "${file.name}" as ZIP`}
                      onClick={e => e.stopPropagation()}
                      style={{ fontSize: 11, padding: '2px 7px' }}
                    >
                      ↓
                    </a>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

function getFileIcon(name) {
  const ext = name.split('.').pop().toLowerCase();
  const icons = { js: '📄', ts: '📄', jsx: '⚛️', tsx: '⚛️', py: '🐍', json: '{}', md: '📝', css: '🎨', html: '🌐', sh: '⚙️', yml: '⚙️', yaml: '⚙️', gitignore: '🚫' };
  return icons[ext] || '📄';
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const d = new Date(dateStr);
  const now = new Date();
  const diff = (now - d) / 1000;
  if (diff < 60) return 'just now';
  if (diff < 3600) return `${Math.floor(diff/60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff/3600)}h ago`;
  if (diff < 86400 * 30) return `${Math.floor(diff/86400)}d ago`;
  return d.toLocaleDateString();
}
