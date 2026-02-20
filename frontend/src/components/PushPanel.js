import React, { useState, useRef } from 'react';
import JSZip from 'jszip';
import { api } from '../api';

// ── Tree helpers ───────────────────────────────────────────────────────────────

function buildTree(files) {
  const root = { name: '', fullPath: '', type: 'dir', children: [] };

  for (const entry of files) {
    const parts = entry.targetPath.split('/').filter(Boolean);
    let node = root;
    let currentPath = '';

    for (let i = 0; i < parts.length - 1; i++) {
      currentPath = currentPath ? `${currentPath}/${parts[i]}` : parts[i];
      let child = node.children.find(c => c.type === 'dir' && c.name === parts[i]);
      if (!child) {
        child = { name: parts[i], fullPath: currentPath, type: 'dir', children: [] };
        node.children.push(child);
      }
      node = child;
    }

    node.children.push({
      name: parts[parts.length - 1],
      fullPath: entry.targetPath,
      type: 'file',
      fileEntry: entry,
    });
  }

  const sort = (n) => {
    n.children.sort((a, b) => {
      if (a.type !== b.type) return a.type === 'dir' ? -1 : 1;
      return a.name.localeCompare(b.name);
    });
    n.children.forEach(c => c.type === 'dir' && sort(c));
  };
  sort(root);
  return root;
}

function countFiles(node) {
  if (node.type === 'file') return 1;
  return node.children.reduce((acc, c) => acc + countFiles(c), 0);
}

function TreeNode({ node, depth, expanded, onToggle, onRemove, formatSize }) {
  const indent = { paddingLeft: 8 + depth * 16 };

  if (node.type === 'file') {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '3px 8px', ...indent }}>
        <span style={{ fontSize: 11, flexShrink: 0 }}>📄</span>
        <span style={{ flex: 1, fontSize: 12 }}>{node.name}</span>
        <span style={{ fontSize: 11, color: 'var(--text-secondary)', flexShrink: 0 }}>
          {formatSize(node.fileEntry.file.size)}
        </span>
        <span
          style={{ cursor: 'pointer', color: 'var(--danger)', fontSize: 15, flexShrink: 0, paddingLeft: 4, lineHeight: 1 }}
          onClick={() => onRemove(node.fullPath)}
        >×</span>
      </div>
    );
  }

  const isExpanded = expanded.has(node.fullPath);
  const fc = countFiles(node);

  return (
    <div>
      <div
        style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '3px 8px', cursor: 'pointer', ...indent }}
        onClick={() => onToggle(node.fullPath)}
      >
        <span style={{ fontSize: 10, color: 'var(--text-secondary)', width: 10, flexShrink: 0 }}>
          {isExpanded ? '▾' : '▸'}
        </span>
        <span style={{ fontSize: 11, flexShrink: 0 }}>📁</span>
        <span style={{ fontSize: 12, fontWeight: 500, flex: 1 }}>{node.name}</span>
        <span style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
          {fc} file{fc !== 1 ? 's' : ''}
        </span>
      </div>
      {isExpanded && node.children.map(child => (
        <TreeNode
          key={child.fullPath}
          node={child}
          depth={depth + 1}
          expanded={expanded}
          onToggle={onToggle}
          onRemove={onRemove}
          formatSize={formatSize}
        />
      ))}
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

export default function PushPanel({ repoName, currentBranch, branches, onRefresh }) {
  const [stagedFiles, setStagedFiles]       = useState([]);
  const [directZip, setDirectZip]           = useState(null);
  const [commitMsg, setCommitMsg]           = useState('');
  const [branch, setBranch]                 = useState(currentBranch);
  const [authorName, setAuthorName]         = useState('');
  const [authorEmail, setAuthorEmail]       = useState('');
  const [showAdvanced, setShowAdvanced]     = useState(false);
  const [dragOver, setDragOver]             = useState(false);
  const [pushing, setPushing]               = useState(false);
  const [progress, setProgress]             = useState(null); // { label, percent }
  const [error, setError]                   = useState('');
  const [success, setSuccess]               = useState('');
  const [expandedDirs, setExpandedDirs]     = useState(new Set());

  const fileInputRef   = useRef();
  const folderInputRef = useRef();
  const zipInputRef    = useRef();

  const formatSize = (bytes) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1048576).toFixed(1)} MB`;
  };

  const totalSize = stagedFiles.reduce((s, f) => s + f.file.size, 0);

  // ── File ingestion ─────────────────────────────────────────────────────────

  const ingestFiles = (rawFiles, stripTopLevel = false) => {
    const entries = [];
    for (const file of rawFiles) {
      const rel = file.webkitRelativePath || file.name;
      const parts = rel.split('/').filter(Boolean);
      if (parts.some(p => p.toLowerCase() === 'node_modules')) continue;
      const targetPath = stripTopLevel && parts.length > 1 ? parts.slice(1).join('/') : rel;
      if (targetPath) entries.push({ file, targetPath });
    }
    setStagedFiles(prev => {
      const map = new Map(prev.map(f => [f.targetPath, f]));
      for (const e of entries) map.set(e.targetPath, e);
      return Array.from(map.values());
    });
    setDirectZip(null);
    setError('');
  };

  const handleDrop = (e) => {
    e.preventDefault();
    setDragOver(false);
    setError('');
    const files = Array.from(e.dataTransfer.files);
    if (files.length === 1 && files[0].name.toLowerCase().endsWith('.zip')) {
      setStagedFiles([]);
      setDirectZip(files[0]);
      return;
    }
    ingestFiles(files, false);
  };

  const handleFileInput   = (e) => { ingestFiles(Array.from(e.target.files), false); e.target.value = ''; };
  const handleFolderInput = (e) => { ingestFiles(Array.from(e.target.files), true);  e.target.value = ''; };
  const handleZipInput    = (e) => {
    const f = e.target.files[0];
    if (f) { setStagedFiles([]); setDirectZip(f); }
    e.target.value = '';
  };

  const removeFile = (targetPath) =>
    setStagedFiles(prev => prev.filter(f => f.targetPath !== targetPath));

  const toggleDir = (fullPath) =>
    setExpandedDirs(prev => {
      const next = new Set(prev);
      if (next.has(fullPath)) next.delete(fullPath); else next.add(fullPath);
      return next;
    });

  // ── Push ──────────────────────────────────────────────────────────────────

  const handlePush = async () => {
    if (!directZip && stagedFiles.length === 0) { setError('Add at least one file'); return; }
    if (!commitMsg.trim()) { setError('Commit message is required'); return; }

    setError(''); setSuccess('');
    setPushing(true);
    setProgress({ label: 'Preparing…', percent: 0 });

    try {
      let zipBlob;

      if (directZip) {
        zipBlob = directZip;
        setProgress({ label: 'Uploading…', percent: 0 });
      } else {
        // Phase 1: client-side compression
        setProgress({ label: 'Compressing…', percent: 0 });
        const zip = new JSZip();
        for (const { file, targetPath } of stagedFiles) zip.file(targetPath, file);
        zipBlob = await zip.generateAsync(
          { type: 'blob', compression: 'DEFLATE', compressionOptions: { level: 6 } },
          (meta) => setProgress({ label: 'Compressing…', percent: Math.round(meta.percent * 0.5) })
        );
        setProgress({ label: 'Uploading…', percent: 50 });
      }

      // Phase 2: upload with XHR progress
      const uploadOffset = directZip ? 0 : 50;
      const uploadScale  = directZip ? 100 : 50;

      const result = await api.uploadZipWithProgress(
        repoName, zipBlob, commitMsg.trim(), branch,
        (pct) => setProgress({ label: 'Uploading…', percent: uploadOffset + Math.round(pct * uploadScale / 100) }),
        authorName.trim() || undefined,
        authorEmail.trim() || undefined,
      );

      const stripped = result.strippedPrefix ? ` (stripped "${result.strippedPrefix}")` : '';
      setSuccess(`Pushed ${result.files.length} file(s)${stripped} — commit ${result.commit.hash}: ${result.commit.message}`);
      setStagedFiles([]);
      setDirectZip(null);
      setCommitMsg('');
      onRefresh();
    } catch (e) {
      setError(e.message);
    } finally {
      setPushing(false);
      setProgress(null);
    }
  };

  const tree    = buildTree(stagedFiles);
  const hasSomething = directZip || stagedFiles.length > 0;
  const canPush = hasSomething && commitMsg.trim() && !pushing;

  return (
    <div>
      {/* Always-present hidden file inputs */}
      <input ref={fileInputRef}   type="file" multiple              style={{ display: 'none' }} onChange={handleFileInput}   />
      <input ref={folderInputRef} type="file" webkitdirectory="true" multiple style={{ display: 'none' }} onChange={handleFolderInput} />
      <input ref={zipInputRef}    type="file" accept=".zip"          style={{ display: 'none' }} onChange={handleZipInput}    />

      <h2 style={{ fontSize: 16, fontWeight: 600, marginBottom: 4 }}>Push to repository</h2>
      <p style={{ color: 'var(--text-secondary)', fontSize: 14, marginBottom: 16 }}>
        Drag &amp; drop files, a folder, or a .zip — they'll be committed directly to the repo.
        Folders are compressed in-browser before upload for speed.
      </p>

      {error   && <div className="error-banner">{error}</div>}
      {success && <div className="success-banner">{success}</div>}

      {/* ── Drop zone (shown only when nothing staged) ── */}
      {!hasSomething && (
        <div
          className={`dropzone ${dragOver ? 'active' : ''}`}
          onDragOver={e => { e.preventDefault(); setDragOver(true); }}
          onDragLeave={() => setDragOver(false)}
          onDrop={handleDrop}
        >
          <div style={{ fontSize: 40, marginBottom: 8 }}>📂</div>
          <div className="dropzone-text">Drop files, a folder, or a .zip here</div>
          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 6, marginBottom: 14 }}>— or —</div>
          <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
            <button className="btn btn-sm" onClick={() => fileInputRef.current.click()}>📄 Select Files</button>
            <button className="btn btn-sm" onClick={() => folderInputRef.current.click()}>📁 Select Folder</button>
            <button className="btn btn-sm" onClick={() => zipInputRef.current.click()}>🗜 Select ZIP</button>
          </div>
        </div>
      )}

      {/* ── Direct ZIP display ── */}
      {directZip && (
        <div style={{
          background: 'var(--bg-overlay)', border: '1px solid var(--border)',
          borderRadius: 'var(--radius)', padding: '12px 16px', marginBottom: 16,
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ fontSize: 28 }}>🗜</span>
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 13, fontWeight: 500 }}>{directZip.name}</div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                {formatSize(directZip.size)} · will be extracted server-side
              </div>
            </div>
            <button className="btn btn-sm" onClick={() => setDirectZip(null)}>× Remove</button>
          </div>
        </div>
      )}

      {/* ── Staged files tree ── */}
      {stagedFiles.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
            <span style={{ fontSize: 13, fontWeight: 500 }}>
              {stagedFiles.length} file{stagedFiles.length !== 1 ? 's' : ''} staged
              <span style={{ fontWeight: 400, color: 'var(--text-secondary)', marginLeft: 8 }}>
                ({formatSize(totalSize)} total)
              </span>
            </span>
            <div style={{ display: 'flex', gap: 6 }}>
              <button className="btn btn-sm" style={{ fontSize: 11 }} onClick={() => fileInputRef.current.click()}>+ Files</button>
              <button className="btn btn-sm" style={{ fontSize: 11 }} onClick={() => folderInputRef.current.click()}>+ Folder</button>
              <button className="btn btn-sm btn-danger" style={{ fontSize: 11 }} onClick={() => setStagedFiles([])}>Clear all</button>
            </div>
          </div>

          <div style={{
            background: 'var(--bg-overlay)', border: '1px solid var(--border)',
            borderRadius: 'var(--radius)', padding: '6px 0',
            maxHeight: 320, overflowY: 'auto',
          }}>
            {tree.children.map(child => (
              <TreeNode
                key={child.fullPath}
                node={child}
                depth={0}
                expanded={expandedDirs}
                onToggle={toggleDir}
                onRemove={removeFile}
                formatSize={formatSize}
              />
            ))}
          </div>
        </div>
      )}

      {/* ── Commit form (shown once something is staged) ── */}
      {hasSomething && (
        <>
          <div className="form-group">
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

          {/* Progress bar */}
          {progress && (
            <div style={{ marginBottom: 16 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 4 }}>
                <span style={{ color: 'var(--text-secondary)' }}>{progress.label}</span>
                <span style={{ color: 'var(--text-secondary)' }}>{progress.percent}%</span>
              </div>
              <div style={{ background: 'var(--border)', borderRadius: 4, height: 6, overflow: 'hidden' }}>
                <div style={{
                  background: 'var(--accent)', height: '100%',
                  width: `${progress.percent}%`,
                  transition: 'width 0.1s ease', borderRadius: 4,
                }} />
              </div>
            </div>
          )}

          <button className="btn btn-primary" onClick={handlePush} disabled={!canPush}>
            {pushing
              ? <><span className="spinner" /> {progress?.label ?? 'Working…'}</>
              : directZip
                ? `Extract & push "${directZip.name}"`
                : `Push ${stagedFiles.length} file${stagedFiles.length !== 1 ? 's' : ''}`
            }
          </button>
        </>
      )}
    </div>
  );
}
