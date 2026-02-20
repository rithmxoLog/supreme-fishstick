import React, { useState, useRef } from 'react';
import { api } from '../api';

const TABS = ['Files', 'Folder', 'ZIP'];

export default function PushPanel({ repoName, currentBranch, branches, onRefresh }) {
  const [tab, setTab] = useState('Files');

  // ── Shared ────────────────────────────────────────────────────────────────
  const [commitMsg, setCommitMsg]     = useState('');
  const [branch, setBranch]           = useState(currentBranch);
  const [pushing, setPushing]         = useState(false);
  const [error, setError]             = useState('');
  const [success, setSuccess]         = useState('');
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [authorName, setAuthorName]   = useState('');
  const [authorEmail, setAuthorEmail] = useState('');

  // ── Files tab ─────────────────────────────────────────────────────────────
  const [files, setFiles]           = useState([]);
  const [targetDir, setTargetDir]   = useState('');
  const [dragOver, setDragOver]     = useState(false);
  const fileInputRef                = useRef();

  // ── Folder tab ────────────────────────────────────────────────────────────
  const [folderFiles, setFolderFiles]       = useState([]);
  const [stripTopFolder, setStripTopFolder] = useState(true);
  const folderInputRef                      = useRef();

  // ── ZIP tab ───────────────────────────────────────────────────────────────
  const [zipFile, setZipFile]   = useState(null);
  const zipInputRef             = useRef();

  // ── Helpers ───────────────────────────────────────────────────────────────
  const reset = () => {
    setError(''); setSuccess('');
    setFiles([]); setFolderFiles([]); setZipFile(null);
    setCommitMsg('');
  };

  const formatSize = (bytes) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  };

  // ── Files tab helpers ─────────────────────────────────────────────────────
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
    setFiles(prev => prev.map(f => ({ ...f, targetPath: buildTargetPath(newDir, f.file.name) })));
  }

  // ── Folder tab helpers ────────────────────────────────────────────────────
  function addFolderFiles(newFiles) {
    const result = [];
    for (const file of newFiles) {
      const rel = file.webkitRelativePath || file.name;
      // Strip the top-level folder segment (e.g. "myproject/src/index.js" → "src/index.js")
      const parts = rel.split('/');
      const path  = stripTopFolder && parts.length > 1 ? parts.slice(1).join('/') : rel;
      if (path) result.push({ file, originalPath: rel, targetPath: path });
    }
    setFolderFiles(result);
  }

  function updateFolderPath(originalPath, newPath) {
    setFolderFiles(prev => prev.map(f =>
      f.originalPath === originalPath ? { ...f, targetPath: newPath } : f
    ));
  }

  function removeFolderFile(originalPath) {
    setFolderFiles(prev => prev.filter(f => f.originalPath !== originalPath));
  }

  // ── Push handlers ─────────────────────────────────────────────────────────
  const handlePushFiles = async () => {
    setError(''); setSuccess('');
    if (files.length === 0)    { setError('Select at least one file'); return; }
    if (!commitMsg.trim())     { setError('Commit message is required'); return; }
    setPushing(true);
    try {
      const result = await api.pushFiles(
        repoName, files, commitMsg.trim(), branch,
        authorName.trim() || undefined, authorEmail.trim() || undefined
      );
      setSuccess(`Pushed ${result.files.length} file(s) — commit ${result.commit.hash}: ${result.commit.message}`);
      setFiles([]); setCommitMsg('');
      onRefresh();
    } catch (e) { setError(e.message); }
    finally { setPushing(false); }
  };

  const handlePushFolder = async () => {
    setError(''); setSuccess('');
    if (folderFiles.length === 0) { setError('Select a folder first'); return; }
    if (!commitMsg.trim())        { setError('Commit message is required'); return; }
    setPushing(true);
    try {
      const mapped = folderFiles.map(f => ({ file: f.file, targetPath: f.targetPath }));
      const result = await api.pushFiles(
        repoName, mapped, commitMsg.trim(), branch,
        authorName.trim() || undefined, authorEmail.trim() || undefined
      );
      setSuccess(`Pushed ${result.files.length} file(s) — commit ${result.commit.hash}: ${result.commit.message}`);
      setFolderFiles([]); setCommitMsg('');
      onRefresh();
    } catch (e) { setError(e.message); }
    finally { setPushing(false); }
  };

  const handlePushZip = async () => {
    setError(''); setSuccess('');
    if (!zipFile)          { setError('Select a .zip file first'); return; }
    if (!commitMsg.trim()) { setError('Commit message is required'); return; }
    setPushing(true);
    try {
      const result = await api.uploadZip(repoName, zipFile, commitMsg.trim(), branch);
      const stripped = result.strippedPrefix ? ` (stripped top folder "${result.strippedPrefix}")` : '';
      setSuccess(`Extracted ${result.files.length} file(s)${stripped} — commit ${result.commit.hash}: ${result.commit.message}`);
      setZipFile(null); setCommitMsg('');
      onRefresh();
    } catch (e) { setError(e.message); }
    finally { setPushing(false); }
  };

  // ── Tab switching (clear state) ───────────────────────────────────────────
  const switchTab = (t) => { setTab(t); setError(''); setSuccess(''); };

  // ── Shared footer (branch, commit msg, advanced, submit) ──────────────────
  const sharedFooter = (onSubmit, disabled, label) => (
    <>
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
              <input className="form-input" value={authorName} onChange={e => setAuthorName(e.target.value)} placeholder="GitXO User" />
            </div>
            <div className="form-group" style={{ flex: 1, marginBottom: 0 }}>
              <label className="form-label">Author email</label>
              <input className="form-input" value={authorEmail} onChange={e => setAuthorEmail(e.target.value)} placeholder="gitxo@local" />
            </div>
          </div>
        )}
      </div>

      <button className="btn btn-primary" onClick={onSubmit} disabled={pushing || disabled}>
        {pushing ? <><span className="spinner" /> Pushing…</> : label}
      </button>
    </>
  );

  return (
    <div>
      <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 4 }}>Push to repository</h2>
      <p style={{ color: 'var(--text-secondary)', fontSize: 14, marginBottom: 16 }}>
        Upload files, a folder, or a ZIP archive — they'll be committed directly to the repo.
      </p>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 4, marginBottom: 20, borderBottom: '1px solid var(--border)', paddingBottom: 0 }}>
        {TABS.map(t => (
          <button
            key={t}
            onClick={() => switchTab(t)}
            style={{
              background: 'none', border: 'none', cursor: 'pointer',
              padding: '6px 16px', fontSize: 13, fontWeight: 500,
              color: tab === t ? 'var(--accent)' : 'var(--text-secondary)',
              borderBottom: tab === t ? '2px solid var(--accent)' : '2px solid transparent',
              marginBottom: -1,
            }}
          >
            {t === 'Files' ? '📄 Files' : t === 'Folder' ? '📁 Folder' : '🗜 ZIP'}
          </button>
        ))}
      </div>

      {error   && <div className="error-banner">{error}</div>}
      {success && <div className="success-banner">{success}</div>}

      {/* ── Files tab ─────────────────────────────────────────────────────── */}
      {tab === 'Files' && (
        <>
          <div className="form-group">
            <label className="form-label">Target directory</label>
            <input
              className="form-input"
              value={targetDir}
              onChange={e => handleTargetDirChange(e.target.value)}
              placeholder="/ (repo root) — e.g. src/components/"
            />
            <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 4 }}>
              All uploaded files go here. Override per-file below.
            </div>
          </div>

          <div
            className={`dropzone ${dragOver ? 'active' : ''}`}
            onDragOver={e => { e.preventDefault(); setDragOver(true); }}
            onDragLeave={() => setDragOver(false)}
            onDrop={e => { e.preventDefault(); setDragOver(false); addFiles(Array.from(e.dataTransfer.files)); }}
            onClick={() => fileInputRef.current.click()}
          >
            <div style={{ fontSize: 32, marginBottom: 8 }}>📄</div>
            <div className="dropzone-text">Drag & drop files here, or click to select</div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>Max 50 MB per file</div>
            <input ref={fileInputRef} type="file" multiple style={{ display: 'none' }}
              onChange={e => addFiles(Array.from(e.target.files))} />
          </div>

          {files.length > 0 && (
            <div style={{ marginTop: 12, display: 'flex', flexDirection: 'column', gap: 6 }}>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 2 }}>
                {files.length} file(s) — edit paths to control destination:
              </div>
              {files.map(f => (
                <div key={f.targetPath} style={{ display: 'flex', alignItems: 'center', gap: 8, background: 'var(--bg-overlay)', border: '1px solid var(--border)', borderRadius: 'var(--radius)', padding: '6px 10px' }}>
                  <span style={{ fontSize: 13, flexShrink: 0 }}>📄</span>
                  <input className="form-input" style={{ flex: 1, fontSize: 12, padding: '3px 8px' }}
                    value={f.targetPath} onChange={e => updateTargetPath(f.targetPath, e.target.value)} />
                  <span style={{ fontSize: 11, color: 'var(--text-secondary)', flexShrink: 0 }}>{formatSize(f.file.size)}</span>
                  <span style={{ cursor: 'pointer', color: 'var(--danger)', fontSize: 16, flexShrink: 0, lineHeight: 1 }}
                    onClick={() => removeFile(f.targetPath)}>×</span>
                </div>
              ))}
            </div>
          )}

          {sharedFooter(handlePushFiles, files.length === 0 || !commitMsg.trim(),
            `Push ${files.length > 0 ? files.length + ' file(s)' : 'files'}`)}
        </>
      )}

      {/* ── Folder tab ────────────────────────────────────────────────────── */}
      {tab === 'Folder' && (
        <>
          <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 12 }}>
            Select a folder from your computer. All files inside will be uploaded with their paths preserved.
          </p>

          <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12 }}>
            <button className="btn btn-primary" onClick={() => folderInputRef.current.click()}>
              📁 Select Folder
            </button>
            {folderFiles.length > 0 && (
              <span style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
                {folderFiles.length} file(s) from <strong>{folderFiles[0].originalPath.split('/')[0]}/</strong>
              </span>
            )}
            <input
              ref={folderInputRef}
              type="file"
              webkitdirectory="true"
              directory="true"
              multiple
              style={{ display: 'none' }}
              onChange={e => addFolderFiles(Array.from(e.target.files))}
            />
          </div>

          <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, marginBottom: 16, cursor: 'pointer' }}>
            <input type="checkbox" checked={stripTopFolder} onChange={e => {
              setStripTopFolder(e.target.checked);
              // Re-process existing files with new strip setting
              if (folderFiles.length > 0) {
                setFolderFiles(prev => prev.map(f => {
                  const parts = f.originalPath.split('/');
                  const path  = e.target.checked && parts.length > 1 ? parts.slice(1).join('/') : f.originalPath;
                  return { ...f, targetPath: path };
                }));
              }
            }} />
            Strip top-level folder name
            <span style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
              (e.g. <code>myproject/src/index.js</code> → <code>src/index.js</code>)
            </span>
          </label>

          {folderFiles.length > 0 && (
            <div style={{ maxHeight: 260, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 4, marginBottom: 8 }}>
              {folderFiles.map(f => (
                <div key={f.originalPath} style={{ display: 'flex', alignItems: 'center', gap: 8, background: 'var(--bg-overlay)', border: '1px solid var(--border)', borderRadius: 'var(--radius)', padding: '5px 10px' }}>
                  <span style={{ fontSize: 12, flexShrink: 0 }}>📄</span>
                  <input className="form-input" style={{ flex: 1, fontSize: 12, padding: '3px 8px' }}
                    value={f.targetPath} onChange={e => updateFolderPath(f.originalPath, e.target.value)} />
                  <span style={{ fontSize: 11, color: 'var(--text-secondary)', flexShrink: 0 }}>{formatSize(f.file.size)}</span>
                  <span style={{ cursor: 'pointer', color: 'var(--danger)', fontSize: 16, flexShrink: 0, lineHeight: 1 }}
                    onClick={() => removeFolderFile(f.originalPath)}>×</span>
                </div>
              ))}
            </div>
          )}

          {sharedFooter(handlePushFolder, folderFiles.length === 0 || !commitMsg.trim(),
            `Push ${folderFiles.length > 0 ? folderFiles.length + ' file(s)' : 'folder'}`)}
        </>
      )}

      {/* ── ZIP tab ───────────────────────────────────────────────────────── */}
      {tab === 'ZIP' && (
        <>
          <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 12 }}>
            Upload a <code>.zip</code> archive. The backend will extract it, preserve the folder structure,
            and commit everything in one go. If all files share a single top-level folder it will be stripped automatically.
          </p>

          <div
            className={`dropzone ${zipFile ? 'active' : ''}`}
            onClick={() => zipInputRef.current.click()}
            onDragOver={e => { e.preventDefault(); }}
            onDrop={e => {
              e.preventDefault();
              const f = e.dataTransfer.files[0];
              if (f && f.name.endsWith('.zip')) setZipFile(f);
              else setError('Only .zip files are accepted');
            }}
          >
            <div style={{ fontSize: 32, marginBottom: 8 }}>🗜</div>
            {zipFile ? (
              <>
                <div className="dropzone-text" style={{ color: 'var(--accent)' }}>{zipFile.name}</div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>{formatSize(zipFile.size)}</div>
              </>
            ) : (
              <>
                <div className="dropzone-text">Drag & drop a .zip here, or click to select</div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>Max 200 MB</div>
              </>
            )}
            <input ref={zipInputRef} type="file" accept=".zip" style={{ display: 'none' }}
              onChange={e => { if (e.target.files[0]) setZipFile(e.target.files[0]); }} />
          </div>

          {zipFile && (
            <button className="btn btn-sm" style={{ marginTop: 8, fontSize: 12 }} onClick={() => setZipFile(null)}>
              × Remove
            </button>
          )}

          {sharedFooter(handlePushZip, !zipFile || !commitMsg.trim(),
            zipFile ? `Extract & push "${zipFile.name}"` : 'Extract & push')}
        </>
      )}
    </div>
  );
}
