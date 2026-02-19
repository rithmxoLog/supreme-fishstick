import React, { useState, useRef } from 'react';
import { api } from '../api';

export default function PushPanel({ repoName, currentBranch, branches, onRefresh }) {
  const [files, setFiles] = useState([]);       // [{ file: File, targetPath: string }]
  const [targetDir, setTargetDir] = useState('');
  const [commitMsg, setCommitMsg] = useState('');
  const [branch, setBranch] = useState(currentBranch);
  const [pushing, setPushing] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [dragOver, setDragOver] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [authorName, setAuthorName] = useState('');
  const [authorEmail, setAuthorEmail] = useState('');
  const fileInputRef = useRef();

  function buildTargetPath(dir, fileName) {
    const cleanDir = dir.replace(/^\//, '').replace(/\/*$/, '');
    return cleanDir ? `${cleanDir}/${fileName}` : fileName;
  }

  function addFiles(newFiles) {
    setFiles(prev => {
      const map = new Map(prev.map(f => [f.targetPath, f]));
      for (const file of newFiles) {
        const tp = buildTargetPath(targetDir, file.name);
        map.set(tp, { file, targetPath: tp });
      }
      return Array.from(map.values());
    });
  }

  function updateTargetPath(oldPath, newPath) {
    setFiles(prev => prev.map(f => f.targetPath === oldPath ? { ...f, targetPath: newPath } : f));
  }

  function removeFile(targetPath) {
    setFiles(prev => prev.filter(f => f.targetPath !== targetPath));
  }

  function handleTargetDirChange(newDir) {
    setTargetDir(newDir);
    setFiles(prev => prev.map(f => ({
      ...f,
      targetPath: buildTargetPath(newDir, f.file.name)
    })));
  }

  const handleDrop = (e) => {
    e.preventDefault();
    setDragOver(false);
    addFiles(Array.from(e.dataTransfer.files));
  };

  const handlePush = async () => {
    setError(''); setSuccess('');
    if (files.length === 0) { setError('Select at least one file to push'); return; }
    if (!commitMsg.trim()) { setError('Commit message is required'); return; }
    setPushing(true);
    try {
      const result = await api.pushFiles(
        repoName,
        files,
        commitMsg.trim(),
        branch,
        authorName.trim() || undefined,
        authorEmail.trim() || undefined
      );
      setSuccess(`Pushed ${result.files.length} file(s) — commit ${result.commit.hash}: ${result.commit.message}`);
      setFiles([]);
      setCommitMsg('');
      onRefresh();
    } catch (e) {
      setError(e.message);
    } finally {
      setPushing(false);
    }
  };

  const formatSize = (bytes) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  };

  return (
    <div>
      <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 16 }}>Push files to repository</h2>
      <p style={{ color: 'var(--text-secondary)', fontSize: 14, marginBottom: 20 }}>
        Upload files from your computer. They will be written to the repository and committed.
      </p>

      {error && <div className="error-banner">{error}</div>}
      {success && <div className="success-banner">{success}</div>}

      {/* Target directory */}
      <div className="form-group">
        <label className="form-label">Target directory</label>
        <input
          className="form-input"
          value={targetDir}
          onChange={e => handleTargetDirChange(e.target.value)}
          placeholder="/ (repo root) — e.g. src/components/"
        />
        <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 4 }}>
          All uploaded files will be placed under this path. You can override per-file below.
        </div>
      </div>

      {/* Dropzone */}
      <div
        className={`dropzone ${dragOver ? 'active' : ''}`}
        onDragOver={e => { e.preventDefault(); setDragOver(true); }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current.click()}
      >
        <div style={{ fontSize: 32, marginBottom: 8 }}>📂</div>
        <div className="dropzone-text">Drag & drop files here, or click to select</div>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>Max 10 MB per file</div>
        <input
          ref={fileInputRef}
          type="file"
          multiple
          style={{ display: 'none' }}
          onChange={e => addFiles(Array.from(e.target.files))}
        />
      </div>

      {/* File list with editable paths */}
      {files.length > 0 && (
        <div style={{ marginTop: 12, display: 'flex', flexDirection: 'column', gap: 6 }}>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 2 }}>
            {files.length} file(s) selected — edit paths to control destination:
          </div>
          {files.map(f => (
            <div
              key={f.targetPath}
              style={{ display: 'flex', alignItems: 'center', gap: 8, background: 'var(--bg-overlay)', border: '1px solid var(--border)', borderRadius: 'var(--radius)', padding: '6px 10px' }}
            >
              <span style={{ fontSize: 13, flexShrink: 0 }}>📄</span>
              <input
                className="form-input"
                style={{ flex: 1, fontSize: 12, padding: '3px 8px' }}
                value={f.targetPath}
                onChange={e => updateTargetPath(f.targetPath, e.target.value)}
              />
              <span style={{ fontSize: 11, color: 'var(--text-secondary)', flexShrink: 0 }}>
                {formatSize(f.file.size)}
              </span>
              <span
                style={{ cursor: 'pointer', color: 'var(--danger)', fontSize: 16, flexShrink: 0, lineHeight: 1 }}
                onClick={() => removeFile(f.targetPath)}
              >×</span>
            </div>
          ))}
        </div>
      )}

      {/* Branch & commit message */}
      <div className="form-group mt-16">
        <label className="form-label">Target branch</label>
        <select className="form-select" value={branch} onChange={e => setBranch(e.target.value)}>
          {branches.map(b => <option key={b} value={b}>{b}</option>)}
        </select>
      </div>

      <div className="form-group">
        <label className="form-label">Commit message *</label>
        <input
          className="form-input"
          value={commitMsg}
          onChange={e => setCommitMsg(e.target.value)}
          placeholder="Add files via GitXO push"
        />
      </div>

      {/* Advanced: author fields */}
      <div style={{ marginBottom: 16 }}>
        <button
          className="btn btn-sm"
          style={{ fontSize: 12, padding: '3px 10px' }}
          onClick={() => setShowAdvanced(v => !v)}
        >
          {showAdvanced ? '▾ Hide' : '▸ Show'} advanced options
        </button>
        {showAdvanced && (
          <div style={{ marginTop: 10, display: 'flex', gap: 12 }}>
            <div className="form-group" style={{ flex: 1, marginBottom: 0 }}>
              <label className="form-label">Author name</label>
              <input
                className="form-input"
                value={authorName}
                onChange={e => setAuthorName(e.target.value)}
                placeholder="GitXO User"
              />
            </div>
            <div className="form-group" style={{ flex: 1, marginBottom: 0 }}>
              <label className="form-label">Author email</label>
              <input
                className="form-input"
                value={authorEmail}
                onChange={e => setAuthorEmail(e.target.value)}
                placeholder="gitxo@local"
              />
            </div>
          </div>
        )}
      </div>

      <button
        className="btn btn-primary"
        onClick={handlePush}
        disabled={pushing || files.length === 0 || !commitMsg.trim()}
      >
        {pushing
          ? <><span className="spinner" /> Pushing…</>
          : `Push ${files.length > 0 ? files.length + ' file(s)' : 'files'}`}
      </button>
    </div>
  );
}
