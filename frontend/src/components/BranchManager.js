import React, { useState } from 'react';
import { api } from '../api';
import { html as diff2html } from 'diff2html';
import 'diff2html/bundles/css/diff2html.min.css';

export default function BranchManager({ repoName, repo, currentBranch, onCheckout, onRefresh }) {
  const [showCreate, setShowCreate] = useState(false);
  const [showMerge, setShowMerge] = useState(false);
  const [newBranchName, setNewBranchName] = useState('');
  const [fromBranch, setFromBranch] = useState(currentBranch);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState('');
  const [mergeSource, setMergeSource] = useState('');
  const [mergeTarget, setMergeTarget] = useState(currentBranch);
  const [mergeMsg, setMergeMsg] = useState('');
  const [merging, setMerging] = useState(false);
  const [mergeError, setMergeError] = useState('');
  const [mergeSuccess, setMergeSuccess] = useState('');

  // Compare state
  const [compareFrom, setCompareFrom] = useState('');
  const [compareTo, setCompareTo] = useState(currentBranch);
  const [compareResult, setCompareResult] = useState(null);
  const [comparing, setComparing] = useState(false);
  const [compareError, setCompareError] = useState('');

  const branches = repo.branches || [];

  const handleCreateBranch = async (e) => {
    e.preventDefault();
    setCreating(true); setCreateError('');
    try {
      await api.createBranch(repoName, newBranchName.trim(), fromBranch);
      setNewBranchName(''); setShowCreate(false);
      onRefresh();
    } catch (e) {
      setCreateError(e.message);
    } finally {
      setCreating(false);
    }
  };

  const handleCheckout = async (branch) => {
    try {
      await onCheckout(branch);
    } catch (e) {
      alert(e.message);
    }
  };

  const handleDeleteBranch = async (branch) => {
    if (branch === currentBranch) { alert("Can't delete the currently checked-out branch."); return; }
    if (!window.confirm(`Delete branch "${branch}"?`)) return;
    try {
      await api.deleteBranch(repoName, branch);
      onRefresh();
    } catch (e) {
      alert(e.message);
    }
  };

  const handleMerge = async (e) => {
    e.preventDefault();
    setMerging(true); setMergeError(''); setMergeSuccess('');
    try {
      const result = await api.mergeBranch(repoName, mergeSource, mergeTarget, mergeMsg || `Merge ${mergeSource} into ${mergeTarget}`);
      setMergeSuccess(result.message);
      setShowMerge(false);
      onRefresh();
    } catch (e) {
      setMergeError(e.message);
    } finally {
      setMerging(false);
    }
  };

  const handleCompare = async (e) => {
    e.preventDefault();
    if (!compareFrom || !compareTo || compareFrom === compareTo) {
      setCompareError('Select two different branches to compare');
      return;
    }
    setComparing(true); setCompareError(''); setCompareResult(null);
    try {
      const result = await api.getBranchDiff(repoName, compareFrom, compareTo);
      setCompareResult(result);
    } catch (e) {
      setCompareError(e.message);
    } finally {
      setComparing(false);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <div style={{ fontSize: 14, color: 'var(--text-secondary)' }}>
          {branches.length} branch{branches.length !== 1 ? 'es' : ''}
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-sm" onClick={() => setShowMerge(true)}>Merge branches</button>
          <button className="btn btn-primary btn-sm" onClick={() => setShowCreate(true)}>New branch</button>
        </div>
      </div>

      {mergeSuccess && <div className="success-banner">{mergeSuccess}</div>}

      <ul className="branch-list">
        {branches.map(branch => (
          <li key={branch} className="branch-item">
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <svg width="12" height="12" viewBox="0 0 16 16" fill="var(--text-secondary)">
                <path d="M5 3.25a.75.75 0 11-1.5 0 .75.75 0 011.5 0zm0 2.122a2.25 2.25 0 10-1.5 0v.878A2.25 2.25 0 005.75 8.5h1.5v2.128a2.251 2.251 0 101.5 0V8.5h1.5a2.25 2.25 0 002.25-2.25v-.878a2.25 2.25 0 10-1.5 0v.878a.75.75 0 01-.75.75h-4.5A.75.75 0 015 6.25v-.878zm3.75 7.378a.75.75 0 11-1.5 0 .75.75 0 011.5 0zm3-8.75a.75.75 0 11-1.5 0 .75.75 0 011.5 0z"/>
              </svg>
              <span style={{ fontWeight: 500 }}>{branch}</span>
              {branch === currentBranch && <span className="branch-current-badge">current</span>}
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              {branch !== currentBranch && (
                <button className="btn btn-sm" onClick={() => handleCheckout(branch)}>Checkout</button>
              )}
              {branch !== currentBranch && (
                <button className="btn btn-danger btn-sm" onClick={() => handleDeleteBranch(branch)}>Delete</button>
              )}
            </div>
          </li>
        ))}
      </ul>

      {/* Compare branches */}
      {branches.length >= 2 && (
        <div style={{ marginTop: 24, padding: '16px', background: 'var(--bg-canvas)', border: '1px solid var(--border)', borderRadius: 'var(--radius)' }}>
          <div style={{ fontWeight: 600, fontSize: 14, marginBottom: 12 }}>Compare branches</div>
          <form onSubmit={handleCompare} style={{ display: 'flex', gap: 8, alignItems: 'flex-end', flexWrap: 'wrap' }}>
            <div>
              <label style={{ display: 'block', fontSize: 12, color: 'var(--text-secondary)', marginBottom: 4 }}>From</label>
              <select className="form-select" style={{ minWidth: 140 }} value={compareFrom} onChange={e => setCompareFrom(e.target.value)} required>
                <option value="">— select —</option>
                {branches.map(b => <option key={b} value={b}>{b}</option>)}
              </select>
            </div>
            <div style={{ paddingBottom: 4, color: 'var(--text-secondary)' }}>→</div>
            <div>
              <label style={{ display: 'block', fontSize: 12, color: 'var(--text-secondary)', marginBottom: 4 }}>To</label>
              <select className="form-select" style={{ minWidth: 140 }} value={compareTo} onChange={e => setCompareTo(e.target.value)}>
                {branches.map(b => <option key={b} value={b}>{b}</option>)}
              </select>
            </div>
            <button type="submit" className="btn btn-primary btn-sm" disabled={comparing || !compareFrom} style={{ marginBottom: 1 }}>
              {comparing ? <><span className="spinner" /> Comparing…</> : 'Compare'}
            </button>
          </form>

          {compareError && <div className="error-banner" style={{ marginTop: 10 }}>{compareError}</div>}

          {compareResult && (
            <div style={{ marginTop: 16 }}>
              <div style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 8 }}>
                <span className="branch-badge">{compareResult.from}</span>
                {' → '}
                <span className="branch-badge">{compareResult.to}</span>
                {' — '}
                {compareResult.commits.length} commit{compareResult.commits.length !== 1 ? 's' : ''} ahead
              </div>

              {compareResult.commits.length > 0 && (
                <ul className="commit-list" style={{ marginBottom: 16 }}>
                  {compareResult.commits.map(c => (
                    <li key={c.hash} className="commit-item">
                      <div>
                        <div className="commit-msg">{c.message}</div>
                        <div className="commit-sub">{c.author} · {new Date(c.date).toLocaleDateString()}</div>
                      </div>
                      <span className="commit-hash">{c.shortHash}</span>
                    </li>
                  ))}
                </ul>
              )}

              {compareResult.diff ? (
                <div
                  className="diff2html-wrapper"
                  dangerouslySetInnerHTML={{
                    __html: diff2html(compareResult.diff, {
                      drawFileList: true,
                      matching: 'lines',
                      outputFormat: 'side-by-side',
                      renderNothingWhenEmpty: false,
                    })
                  }}
                />
              ) : (
                <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
                  No file differences between these branches.
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Create Branch Modal */}
      {showCreate && (
        <div className="modal-overlay" onClick={e => { if (e.target === e.currentTarget) setShowCreate(false); }}>
          <div className="modal">
            <div className="modal-title">Create new branch</div>
            {createError && <div className="error-banner">{createError}</div>}
            <form onSubmit={handleCreateBranch}>
              <div className="form-group">
                <label className="form-label">Branch name</label>
                <input className="form-input" value={newBranchName} onChange={e => setNewBranchName(e.target.value)} placeholder="feature/my-feature" autoFocus required />
              </div>
              <div className="form-group">
                <label className="form-label">From branch</label>
                <select className="form-select" value={fromBranch} onChange={e => setFromBranch(e.target.value)}>
                  {branches.map(b => <option key={b} value={b}>{b}</option>)}
                </select>
              </div>
              <div className="modal-actions">
                <button type="button" className="btn" onClick={() => setShowCreate(false)}>Cancel</button>
                <button type="submit" className="btn btn-primary" disabled={creating || !newBranchName.trim()}>
                  {creating ? <><span className="spinner" /> Creating…</> : 'Create branch'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Merge Modal */}
      {showMerge && (
        <div className="modal-overlay" onClick={e => { if (e.target === e.currentTarget) { setShowMerge(false); setMergeError(''); } }}>
          <div className="modal">
            <div className="modal-title">Merge branches</div>
            {mergeError && <div className="error-banner">{mergeError}</div>}
            <form onSubmit={handleMerge}>
              <div className="form-group">
                <label className="form-label">Source branch (merge FROM)</label>
                <select className="form-select" value={mergeSource} onChange={e => setMergeSource(e.target.value)} required>
                  <option value="">— select source —</option>
                  {branches.filter(b => b !== mergeTarget).map(b => <option key={b} value={b}>{b}</option>)}
                </select>
              </div>
              <div className="form-group">
                <label className="form-label">Target branch (merge INTO)</label>
                <select className="form-select" value={mergeTarget} onChange={e => setMergeTarget(e.target.value)}>
                  {branches.map(b => <option key={b} value={b}>{b}</option>)}
                </select>
              </div>
              <div className="form-group">
                <label className="form-label">Merge commit message (optional)</label>
                <input className="form-input" value={mergeMsg} onChange={e => setMergeMsg(e.target.value)} placeholder={`Merge ${mergeSource || '<source>'} into ${mergeTarget}`} />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn" onClick={() => { setShowMerge(false); setMergeError(''); }}>Cancel</button>
                <button type="submit" className="btn btn-primary" disabled={merging || !mergeSource}>
                  {merging ? <><span className="spinner" /> Merging…</> : 'Merge'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
