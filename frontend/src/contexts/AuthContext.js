import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';

const AuthContext = createContext(null);

// Keys used for localStorage
const ACCESS_KEY  = 'gitxo_access_token';
const REFRESH_KEY = 'gitxo_refresh_token';

export function AuthProvider({ children }) {
  const [user, setUser]   = useState(null);
  const [loading, setLoading] = useState(true);

  // ── Boot: verify stored access token, refresh if needed ──────────────────
  useEffect(() => {
    const accessToken = localStorage.getItem(ACCESS_KEY);
    if (!accessToken) {
      setLoading(false);
      return;
    }

    fetch('/api/auth/me', { headers: { Authorization: `Bearer ${accessToken}` } })
      .then(r => {
        if (r.ok) return r.json();
        if (r.status === 401) return tryRefreshOnBoot().then(() => null);
        return null;
      })
      .then(data => { if (data) setUser(data); })
      .catch(() => clearTokens())
      .finally(() => setLoading(false));
  }, []);

  // Listen for auth expiry events dispatched by api/index.js
  useEffect(() => {
    const handler = () => { setUser(null); };
    window.addEventListener('gitxoAuthExpired', handler);
    return () => window.removeEventListener('gitxoAuthExpired', handler);
  }, []);

  async function tryRefreshOnBoot() {
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    if (!refreshToken) { clearTokens(); return; }
    try {
      const r = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken })
      });
      if (!r.ok) { clearTokens(); return; }
      const data = await r.json();
      localStorage.setItem(ACCESS_KEY,  data.accessToken);
      localStorage.setItem(REFRESH_KEY, data.refreshToken);
      setUser(data.user);
    } catch { clearTokens(); }
  }

  function clearTokens() {
    localStorage.removeItem(ACCESS_KEY);
    localStorage.removeItem(REFRESH_KEY);
    setUser(null);
  }

  // ── Login ─────────────────────────────────────────────────────────────────
  const login = useCallback(async (email, password) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Login failed');
    localStorage.setItem(ACCESS_KEY,  data.accessToken);
    localStorage.setItem(REFRESH_KEY, data.refreshToken);
    setUser(data.user);
    return data.user;
  }, []);

  // ── Logout ────────────────────────────────────────────────────────────────
  const logout = useCallback(async () => {
    const accessToken  = localStorage.getItem(ACCESS_KEY);
    const refreshToken = localStorage.getItem(REFRESH_KEY);
    try {
      if (accessToken) {
        await fetch('/api/auth/logout', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
          body: JSON.stringify({ refreshToken })
        });
      }
    } catch { /* best-effort */ }
    clearTokens();
  }, []);

  // ── Refresh user data (call after profile/settings update) ────────────────
  const refreshUser = useCallback(async () => {
    const accessToken = localStorage.getItem(ACCESS_KEY);
    if (!accessToken) return;
    try {
      const r = await fetch('/api/auth/me', { headers: { Authorization: `Bearer ${accessToken}` } });
      if (r.ok) { const data = await r.json(); setUser(data); }
    } catch { /* ignore */ }
  }, []);

  const token = localStorage.getItem(ACCESS_KEY); // convenience alias used by some components

  return (
    <AuthContext.Provider value={{ user, token, loading, login, logout, refreshUser, setUser }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
