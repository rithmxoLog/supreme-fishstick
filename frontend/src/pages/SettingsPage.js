import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../api';

// ─── Sidebar item ────────────────────────────────────────────────────────────
function SidebarItem({ label, active, onClick }) {
  return (
    <button
      onClick={onClick}
      style={{
        display: 'block', width: '100%', textAlign: 'left',
        padding: '6px 16px', fontSize: 14, fontWeight: active ? 600 : 400,
        color: active ? 'var(--text-primary)' : 'var(--text-secondary)',
        background: active ? 'var(--bg-overlay)' : 'transparent',
        borderLeft: active ? '2px solid var(--text-link)' : '2px solid transparent',
        border: 'none', cursor: 'pointer', transition: 'all 0.15s',
      }}
    >
      {label}
    </button>
  );
}

// ─── Reusable form banner ────────────────────────────────────────────────────
function Banner({ type, msg }) {
  if (!msg) return null;
  const bg = type === 'success' ? '#1a7f37' : '#cf222e';
  return (
    <div style={{ background: bg, color: '#fff', borderRadius: 'var(--radius)', padding: '8px 12px', marginBottom: 16, fontSize: 14 }}>
      {msg}
    </div>
  );
}

// ─── Section card ─────────────────────────────────────────────────────────────
function SectionCard({ title, subtitle, children }) {
  return (
    <div className="card" style={{ marginBottom: 24 }}>
      <div className="card-header">
        <h2 style={{ fontSize: 18, fontWeight: 600, margin: 0 }}>{title}</h2>
        {subtitle && <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 4, marginBottom: 0 }}>{subtitle}</p>}
      </div>
      <div style={{ padding: '0 0 8px' }}>{children}</div>
    </div>
  );
}

// ─── TAB: Profile ────────────────────────────────────────────────────────────
function ProfileTab({ user, refreshUser }) {
  const [displayName, setDisplayName] = useState(user?.displayName || '');
  const [bio,         setBio]         = useState(user?.bio         || '');
  const [avatarUrl,   setAvatarUrl]   = useState(user?.avatarUrl   || '');
  const [msg,  setMsg]  = useState('');
  const [err,  setErr]  = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setDisplayName(user?.displayName || '');
    setBio(user?.bio || '');
    setAvatarUrl(user?.avatarUrl || '');
  }, [user]);

  const save = async (e) => {
    e.preventDefault();
    setMsg(''); setErr(''); setBusy(true);
    try {
      await api.updateProfile(displayName || null, bio || null, avatarUrl || null);
      await refreshUser();
      setMsg('Profile updated successfully.');
    } catch (ex) { setErr(ex.message); }
    finally { setBusy(false); }
  };

  return (
    <SectionCard title="Public profile" subtitle="This information will be displayed on your profile.">
      <Banner type="success" msg={msg} />
      <Banner type="error"   msg={err} />
      <form onSubmit={save} style={{ maxWidth: 480 }}>
        <div className="form-group">
          <label className="form-label">Name</label>
          <input className="form-input" value={displayName} onChange={e => setDisplayName(e.target.value)}
            placeholder="Your full name" maxLength={80} />
          <small style={{ color: 'var(--text-secondary)', fontSize: 12 }}>
            Your name may appear around GitXO where you contribute.
          </small>
        </div>
        <div className="form-group">
          <label className="form-label">Bio</label>
          <textarea className="form-textarea" value={bio} onChange={e => setBio(e.target.value)}
            placeholder="A brief description of yourself" rows={3} maxLength={200} />
          <small style={{ color: 'var(--text-secondary)', fontSize: 12 }}>You can @mention other users.</small>
        </div>
        <div className="form-group">
          <label className="form-label">Avatar URL</label>
          <input className="form-input" value={avatarUrl} onChange={e => setAvatarUrl(e.target.value)}
            placeholder="https://example.com/avatar.png" type="url" />
          <small style={{ color: 'var(--text-secondary)', fontSize: 12 }}>Link to a public image for your avatar.</small>
        </div>
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? <><span className="spinner" /> Saving…</> : 'Update profile'}
        </button>
      </form>
    </SectionCard>
  );
}

// ─── TAB: Account ────────────────────────────────────────────────────────────
function AccountTab({ user, logout }) {
  const navigate = useNavigate();

  // Change email
  const [newEmail,   setNewEmail]   = useState('');
  const [emailPass,  setEmailPass]  = useState('');
  const [emailMsg,   setEmailMsg]   = useState('');
  const [emailErr,   setEmailErr]   = useState('');
  const [emailBusy,  setEmailBusy]  = useState(false);

  const changeEmail = async (e) => {
    e.preventDefault();
    setEmailMsg(''); setEmailErr(''); setEmailBusy(true);
    try {
      await api.changeEmail(emailPass, newEmail);
      setEmailMsg('Email address updated.');
      setNewEmail(''); setEmailPass('');
    } catch (ex) { setEmailErr(ex.message); }
    finally { setEmailBusy(false); }
  };

  // Change password
  const [curPw,  setCurPw]  = useState('');
  const [newPw,  setNewPw]  = useState('');
  const [confPw, setConfPw] = useState('');
  const [pwMsg,  setPwMsg]  = useState('');
  const [pwErr,  setPwErr]  = useState('');
  const [pwBusy, setPwBusy] = useState(false);

  const changePassword = async (e) => {
    e.preventDefault();
    if (newPw !== confPw) { setPwErr('Passwords do not match'); return; }
    setPwMsg(''); setPwErr(''); setPwBusy(true);
    try {
      await api.changePassword(curPw, newPw);
      await logout();
      navigate('/login', { state: { message: 'Password changed. Please sign in again.' } });
    } catch (ex) { setPwErr(ex.message); }
    finally { setPwBusy(false); }
  };

  return (
    <>
      {/* Change email */}
      <SectionCard title="Change email address" subtitle={"Current: " + (user?.email || '')}>
        <Banner type="success" msg={emailMsg} />
        <Banner type="error"   msg={emailErr} />
        <form onSubmit={changeEmail} style={{ maxWidth: 480 }}>
          <div className="form-group">
            <label className="form-label">New email address</label>
            <input className="form-input" type="email" value={newEmail} onChange={e => setNewEmail(e.target.value)} required />
          </div>
          <div className="form-group">
            <label className="form-label">Confirm with password</label>
            <input className="form-input" type="password" value={emailPass} onChange={e => setEmailPass(e.target.value)} required autoComplete="current-password" />
          </div>
          <button type="submit" className="btn btn-primary" disabled={emailBusy}>
            {emailBusy ? <><span className="spinner" /> Saving…</> : 'Update email'}
          </button>
        </form>
      </SectionCard>

      {/* Change password */}
      <SectionCard title="Change password" subtitle="Use a strong password of at least 8 characters.">
        <Banner type="success" msg={pwMsg} />
        <Banner type="error"   msg={pwErr} />
        <form onSubmit={changePassword} style={{ maxWidth: 480 }}>
          <div className="form-group">
            <label className="form-label">Current password</label>
            <input className="form-input" type="password" value={curPw} onChange={e => setCurPw(e.target.value)} required autoComplete="current-password" />
          </div>
          <div className="form-group">
            <label className="form-label">New password</label>
            <input className="form-input" type="password" value={newPw} onChange={e => setNewPw(e.target.value)} required minLength={8} autoComplete="new-password" />
          </div>
          <div className="form-group">
            <label className="form-label">Confirm new password</label>
            <input className="form-input" type="password" value={confPw} onChange={e => setConfPw(e.target.value)} required minLength={8} autoComplete="new-password" />
          </div>
          <button type="submit" className="btn btn-primary" disabled={pwBusy}>
            {pwBusy ? <><span className="spinner" /> Saving…</> : 'Update password'}
          </button>
        </form>
        <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 12 }}>
          After changing your password, you will be signed out of all sessions.
        </p>
      </SectionCard>
    </>
  );
}

// ─── TAB: Security (sessions) ────────────────────────────────────────────────
function SecurityTab() {
  const [sessions, setSessions] = useState([]);
  const [loading,  setLoading]  = useState(true);
  const [err,      setErr]      = useState('');

  const loadSessions = useCallback(async () => {
    setLoading(true);
    try { setSessions(await api.getSessions()); }
    catch (ex) { setErr(ex.message); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { loadSessions(); }, [loadSessions]);

  const revoke = async (id) => {
    if (!window.confirm('Revoke this session?')) return;
    try { await api.revokeSession(id); loadSessions(); }
    catch (ex) { setErr(ex.message); }
  };

  const formatDate = (d) => d ? new Date(d).toLocaleString() : '—';

  return (
    <SectionCard title="Active sessions"
      subtitle="These are all the devices that are currently logged in to your account.">
      {err && <Banner type="error" msg={err} />}
      {loading ? (
        <p style={{ color: 'var(--text-secondary)', fontSize: 14 }}>Loading sessions…</p>
      ) : sessions.length === 0 ? (
        <p style={{ color: 'var(--text-secondary)', fontSize: 14 }}>No active sessions found.</p>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {sessions.map(s => (
            <div key={s.id} style={{
              display: 'flex', justifyContent: 'space-between', alignItems: 'center',
              padding: '12px 16px', background: 'var(--bg-overlay)',
              border: '1px solid var(--border)', borderRadius: 'var(--radius)'
            }}>
              <div>
                <div style={{ fontSize: 14, fontWeight: 500, marginBottom: 4 }}>
                  {s.userAgent || 'Unknown device'}
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                  Created: {formatDate(s.createdAt)} · Expires: {formatDate(s.expiresAt)}
                </div>
              </div>
              <button className="btn btn-danger btn-sm" onClick={() => revoke(s.id)}>
                Revoke
              </button>
            </div>
          ))}
        </div>
      )}
    </SectionCard>
  );
}

// ─── TAB: Preferences ────────────────────────────────────────────────────────
function PreferencesTab({ user }) {
  const [settings, setSettings] = useState(null);
  const [msg,  setMsg]  = useState('');
  const [err,  setErr]  = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!user) return;
    api.getSettings().then(s => setSettings(s)).catch(ex => setErr(ex.message));
  }, [user]);

  const save = async (e) => {
    e.preventDefault();
    setMsg(''); setErr(''); setBusy(true);
    try {
      const saved = await api.updateSettings(settings);
      setSettings(saved);
      setMsg('Preferences saved.');
    } catch (ex) { setErr(ex.message); }
    finally { setBusy(false); }
  };

  if (!settings) return <p style={{ color: 'var(--text-secondary)', fontSize: 14 }}>Loading preferences…</p>;

  const chk = (field) => (e) => setSettings(s => ({ ...s, [field]: e.target.checked }));
  const set = (field) => (e) => setSettings(s => ({ ...s, [field]: e.target.value }));

  return (
    <SectionCard title="Preferences" subtitle="Customize your GitXO experience.">
      <Banner type="success" msg={msg} />
      <Banner type="error"   msg={err} />
      <form onSubmit={save} style={{ maxWidth: 480 }}>
        <div className="form-group">
          <label className="form-label">Default branch name</label>
          <input className="form-input" value={settings.defaultBranch} onChange={set('defaultBranch')}
            placeholder="main" maxLength={60} />
          <small style={{ color: 'var(--text-secondary)', fontSize: 12 }}>
            New repositories will use this as the default branch.
          </small>
        </div>
        <div className="form-group">
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 14 }}>
            <input type="checkbox" checked={settings.showEmail} onChange={chk('showEmail')} />
            Keep my email address private
          </label>
          <small style={{ color: 'var(--text-secondary)', fontSize: 12, marginTop: 4, display: 'block' }}>
            When enabled, your email won't appear publicly.
          </small>
        </div>
        <div style={{ marginBottom: 16 }}>
          <div style={{ fontWeight: 500, fontSize: 14, marginBottom: 8 }}>Email notifications</div>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 14, marginBottom: 6 }}>
            <input type="checkbox" checked={settings.emailOnPush} onChange={chk('emailOnPush')} />
            Notify me about pushes to my repositories
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 14 }}>
            <input type="checkbox" checked={settings.emailOnIssue} onChange={chk('emailOnIssue')} />
            Notify me about new issues and comments
          </label>
        </div>
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? <><span className="spinner" /> Saving…</> : 'Save preferences'}
        </button>
      </form>
    </SectionCard>
  );
}

// ─── TAB: Admin — User Management ────────────────────────────────────────────
function AdminTab({ currentUser }) {
  const [users,   setUsers]   = useState([]);
  const [loading, setLoading] = useState(true);
  const [err,     setErr]     = useState('');
  const [msg,     setMsg]     = useState('');

  // Add user form
  const [username, setUsername] = useState('');
  const [email,    setEmail]    = useState('');
  const [password, setPassword] = useState('');
  const [addBusy,  setAddBusy]  = useState(false);

  const loadUsers = useCallback(async () => {
    setLoading(true);
    try { setUsers(await api.listUsers()); }
    catch (ex) { setErr(ex.message); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { loadUsers(); }, [loadUsers]);

  const addUser = async (e) => {
    e.preventDefault();
    setMsg(''); setErr(''); setAddBusy(true);
    try {
      const data = await api.register(username.trim(), email.trim(), password);
      setMsg(`User "${data.user.username}" created successfully.`);
      setUsername(''); setEmail(''); setPassword('');
      loadUsers();
    } catch (ex) { setErr(ex.message); }
    finally { setAddBusy(false); }
  };

  const deleteUser = async (id, uname) => {
    if (!window.confirm(`Delete user "${uname}"? This cannot be undone.`)) return;
    try {
      await api.deleteUser(id);
      setMsg(`User "${uname}" deleted.`);
      loadUsers();
    } catch (ex) { setErr(ex.message); }
  };

  return (
    <>
      <SectionCard title="Add user" subtitle="Create a new account. The user can sign in immediately.">
        <Banner type="success" msg={msg} />
        <Banner type="error"   msg={err} />
        <form onSubmit={addUser} style={{ maxWidth: 400 }}>
          <div className="form-group">
            <label className="form-label">Username</label>
            <input className="form-input" value={username} onChange={e => setUsername(e.target.value)}
              required placeholder="e.g. johndoe" pattern="[a-zA-Z0-9_\-]{3,30}"
              title="3–30 alphanumeric, dashes, or underscores" />
          </div>
          <div className="form-group">
            <label className="form-label">Email address</label>
            <input className="form-input" type="email" value={email} onChange={e => setEmail(e.target.value)} required autoComplete="off" />
          </div>
          <div className="form-group">
            <label className="form-label">Password</label>
            <input className="form-input" type="password" value={password} onChange={e => setPassword(e.target.value)}
              required minLength={8} autoComplete="new-password" />
          </div>
          <button type="submit" className="btn btn-primary" disabled={addBusy}>
            {addBusy ? <><span className="spinner" /> Creating…</> : 'Create user'}
          </button>
        </form>
      </SectionCard>

      <SectionCard title="All users">
        {loading ? (
          <p style={{ color: 'var(--text-secondary)', fontSize: 14 }}>Loading users…</p>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {users.map(u => (
              <div key={u.id} style={{
                display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                padding: '10px 16px', background: 'var(--bg-overlay)',
                border: '1px solid var(--border)', borderRadius: 'var(--radius)'
              }}>
                <div>
                  <span style={{ fontWeight: 600, fontSize: 14 }}>{u.username}</span>
                  {u.isAdmin && <span style={{ marginLeft: 8, fontSize: 11, background: '#9a6700', color: '#fff', borderRadius: 999, padding: '2px 6px' }}>Admin</span>}
                  <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>{u.email}</div>
                </div>
                {u.id !== currentUser?.id && (
                  <button className="btn btn-danger btn-sm" onClick={() => deleteUser(u.id, u.username)}>
                    Delete
                  </button>
                )}
                {u.id === currentUser?.id && (
                  <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>You</span>
                )}
              </div>
            ))}
          </div>
        )}
      </SectionCard>
    </>
  );
}

// ─── Main SettingsPage ────────────────────────────────────────────────────────
export default function SettingsPage() {
  const { user, logout, refreshUser } = useAuth();
  const [tab, setTab] = useState('profile');

  const tabs = [
    { id: 'profile',     label: 'Profile' },
    { id: 'account',     label: 'Account' },
    { id: 'security',    label: 'Security' },
    { id: 'preferences', label: 'Preferences' },
    ...(user?.isAdmin ? [{ id: 'admin', label: 'Admin — Users' }] : [])
  ];

  return (
    <div>
      <div className="page-header">
        <h1 className="page-title">Settings</h1>
      </div>

      <div style={{ display: 'flex', gap: 24, alignItems: 'flex-start' }}>
        {/* Sidebar */}
        <div style={{ width: 200, flexShrink: 0 }}>
          <div style={{
            background: 'var(--bg-canvas)', border: '1px solid var(--border)',
            borderRadius: 'var(--radius)', overflow: 'hidden'
          }}>
            {tabs.map(t => (
              <SidebarItem key={t.id} label={t.label} active={tab === t.id} onClick={() => setTab(t.id)} />
            ))}
          </div>
        </div>

        {/* Content */}
        <div style={{ flex: 1, minWidth: 0 }}>
          {tab === 'profile'     && <ProfileTab user={user} refreshUser={refreshUser} />}
          {tab === 'account'     && <AccountTab user={user} logout={logout} />}
          {tab === 'security'    && <SecurityTab />}
          {tab === 'preferences' && <PreferencesTab user={user} />}
          {tab === 'admin'       && user?.isAdmin && <AdminTab currentUser={user} />}
        </div>
      </div>
    </div>
  );
}
