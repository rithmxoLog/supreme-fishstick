import React, { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

export default function RegisterPage() {
  const { token } = useAuth();

  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setSuccess('');
    setLoading(true);
    try {
      const res = await fetch('/api/auth/register', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`
        },
        body: JSON.stringify({ username, email, password })
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || 'Failed to create user');
      setSuccess(`User "${data.user.username}" created successfully.`);
      setUsername('');
      setEmail('');
      setPassword('');
    } catch (e) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <div className="auth-card">
        <h1 className="auth-title">Add new user</h1>

        {error && <div className="error-banner">{error}</div>}
        {success && (
          <div className="error-banner" style={{ background: 'var(--success, #2da44e)', color: '#fff', borderColor: 'transparent' }}>
            {success}
          </div>
        )}

        <form onSubmit={handleSubmit} className="auth-form">
          <div className="form-group">
            <label className="form-label">Username</label>
            <input
              className="form-input"
              type="text"
              value={username}
              onChange={e => setUsername(e.target.value)}
              required
              autoFocus
              placeholder="e.g. johndoe"
              pattern="[a-zA-Z0-9_\-]{3,30}"
              title="3–30 alphanumeric characters, dashes, or underscores"
            />
            <small style={{ color: 'var(--text-secondary)', fontSize: 12 }}>
              3–30 characters. Letters, numbers, dashes, underscores.
            </small>
          </div>
          <div className="form-group">
            <label className="form-label">Email address</label>
            <input
              className="form-input"
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              required
              autoComplete="off"
            />
          </div>
          <div className="form-group">
            <label className="form-label">Password</label>
            <input
              className="form-input"
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
              minLength={8}
              autoComplete="new-password"
            />
            <small style={{ color: 'var(--text-secondary)', fontSize: 12 }}>
              Minimum 8 characters.
            </small>
          </div>
          <button type="submit" className="btn btn-primary auth-submit" disabled={loading}>
            {loading ? <><span className="spinner" /> Creating user…</> : 'Create user'}
          </button>
        </form>
      </div>
    </div>
  );
}
