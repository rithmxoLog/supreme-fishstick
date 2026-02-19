import React, { useEffect, useState } from 'react';
import { api } from '../api';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism';

const EXT_TO_LANG = {
  js: 'javascript', jsx: 'jsx', ts: 'typescript', tsx: 'tsx',
  py: 'python', rb: 'ruby', go: 'go', rs: 'rust', java: 'java',
  cs: 'csharp', cpp: 'cpp', c: 'c', h: 'c', php: 'php',
  html: 'html', css: 'css', scss: 'scss', json: 'json',
  md: 'markdown', yml: 'yaml', yaml: 'yaml', sh: 'bash',
  bash: 'bash', ps1: 'powershell', sql: 'sql', xml: 'xml',
  toml: 'toml', tf: 'hcl',
};

function getLanguage(filename) {
  if (!filename) return 'text';
  if (filename.toLowerCase() === 'dockerfile') return 'docker';
  const ext = filename.split('.').pop().toLowerCase();
  return EXT_TO_LANG[ext] || 'text';
}

export default function FileViewer({ repoName, filePath, currentBranch, onBack, onRefresh }) {
  const [file, setFile] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [editing, setEditing] = useState(false);
  const [editContent, setEditContent] = useState('');
  const [commitMsg, setCommitMsg] = useState('');
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState('');
  const [saveSuccess, setSaveSuccess] = useState('');

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const data = await api.getFile(repoName, filePath);
        setFile(data);
        setEditContent(data.content || '');
      } catch (e) {
        setError(e.message);
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [repoName, filePath]);

  const fileName = filePath.split('/').pop();
  const language = getLanguage(fileName);

  const handleSave = async () => {
    if (!commitMsg.trim()) { setSaveError('Commit message required'); return; }
    setSaving(true); setSaveError(''); setSaveSuccess('');
    try {
      const result = await api.saveFile(repoName, filePath, editContent, commitMsg.trim(), currentBranch);
      setSaveSuccess(`Committed: ${result.commit.hash} — ${result.commit.message}`);
      setCommitMsg('');
      setEditing(false);
      setFile(prev => ({ ...prev, content: editContent }));
      onRefresh();
    } catch (e) {
      setSaveError(e.message);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    const msg = window.prompt(`Commit message for deleting "${fileName}":`, `Delete ${fileName}`);
    if (!msg) return;
    try {
      await api.deleteFile(repoName, filePath, msg);
      onRefresh();
      onBack();
    } catch (e) {
      alert(e.message);
    }
  };

  if (loading) return <div className="loading-page"><span className="spinner" /></div>;
  if (error) return <div className="error-banner">{error}</div>;

  return (
    <div>
      <div className="breadcrumb">
        <a onClick={onBack}>← Back</a>
        <span className="breadcrumb-sep">/</span>
        {filePath.split('/').map((part, i, arr) => (
          <React.Fragment key={i}>
            {i > 0 && <span className="breadcrumb-sep">/</span>}
            <span style={{
              color: i === arr.length - 1 ? 'var(--text-primary)' : 'var(--text-link)',
              cursor: i < arr.length - 1 ? 'pointer' : 'default'
            }}>
              {part}
            </span>
          </React.Fragment>
        ))}
      </div>

      {saveSuccess && <div className="success-banner">{saveSuccess}</div>}
      {saveError && <div className="error-banner">{saveError}</div>}

      <div className="file-viewer-header">
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span className="file-viewer-name">{fileName}</span>
          {!file.isBinary && language !== 'text' && (
            <span className="tag" style={{ fontSize: 11 }}>{language}</span>
          )}
        </div>
        <div className="flex gap-8">
          {!file.isBinary && !editing && (
            <button className="btn btn-sm" onClick={() => setEditing(true)}>Edit</button>
          )}
          {editing && (
            <>
              <button className="btn btn-sm" onClick={() => { setEditing(false); setSaveError(''); }}>Cancel</button>
              <button className="btn btn-primary btn-sm" onClick={handleSave} disabled={saving}>
                {saving ? <><span className="spinner" /> Saving…</> : 'Commit changes'}
              </button>
            </>
          )}
          <a
            className="btn btn-sm"
            href={api.getFileDownloadUrl(repoName, filePath, currentBranch)}
            download
          >
            Download
          </a>
          <button className="btn btn-danger btn-sm" onClick={handleDelete}>Delete</button>
        </div>
      </div>

      {editing ? (
        <div>
          <textarea
            className="editor-textarea"
            value={editContent}
            onChange={e => setEditContent(e.target.value)}
            spellCheck={false}
          />
          <div className="form-group mt-8">
            <label className="form-label">Commit message</label>
            <input
              className="form-input"
              value={commitMsg}
              onChange={e => setCommitMsg(e.target.value)}
              placeholder={`Update ${fileName}`}
            />
          </div>
        </div>
      ) : (
        <div className="file-content">
          {file.isBinary ? (
            <pre style={{ color: 'var(--text-secondary)', padding: 16 }}>
              Binary file ({file.size} bytes) — cannot display
            </pre>
          ) : (
            <SyntaxHighlighter
              language={language}
              style={vscDarkPlus}
              showLineNumbers
              customStyle={{
                margin: 0,
                borderRadius: '0 0 6px 6px',
                fontSize: '13px',
                background: 'var(--bg-canvas)',
              }}
              lineNumberStyle={{ color: 'var(--text-secondary)', minWidth: '3em' }}
            >
              {file.content || ''}
            </SyntaxHighlighter>
          )}
        </div>
      )}
    </div>
  );
}
