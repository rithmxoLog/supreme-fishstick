const BASE = '/api';

const ACCESS_KEY  = 'gitxo_access_token';
const REFRESH_KEY = 'gitxo_refresh_token';

function getToken() { return localStorage.getItem(ACCESS_KEY); }

// Attempt to get a new access token using the stored refresh token.
// Updates localStorage on success. Dispatches 'gitxoAuthExpired' on failure.
async function tryRefresh() {
  const refreshToken = localStorage.getItem(REFRESH_KEY);
  if (!refreshToken) {
    window.dispatchEvent(new Event('gitxoAuthExpired'));
    return false;
  }
  try {
    const r = await fetch(`${BASE}/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken })
    });
    if (!r.ok) {
      localStorage.removeItem(ACCESS_KEY);
      localStorage.removeItem(REFRESH_KEY);
      window.dispatchEvent(new Event('gitxoAuthExpired'));
      return false;
    }
    const data = await r.json();
    localStorage.setItem(ACCESS_KEY,  data.accessToken);
    localStorage.setItem(REFRESH_KEY, data.refreshToken);
    return true;
  } catch {
    window.dispatchEvent(new Event('gitxoAuthExpired'));
    return false;
  }
}

async function request(method, path, body, isFormData = false, _retry = true) {
  const opts = { method, headers: {} };

  const token = getToken();
  if (token) opts.headers['Authorization'] = `Bearer ${token}`;

  if (body && !isFormData) {
    opts.headers['Content-Type'] = 'application/json';
    opts.body = JSON.stringify(body);
  } else if (isFormData) {
    opts.body = body;
  }

  const res = await fetch(`${BASE}${path}`, opts);

  // Auto-refresh on 401
  if (res.status === 401 && _retry) {
    const refreshed = await tryRefresh();
    if (refreshed) return request(method, path, body, isFormData, false);
    throw new Error('Session expired. Please sign in again.');
  }

  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
  return data;
}

export const api = {
  // ── Auth ──────────────────────────────────────────────────
  login: (email, password) =>
    request('POST', '/auth/login', { email, password }),
  register: (username, email, password) =>
    request('POST', '/auth/register', { username, email, password }),
  refresh: (refreshToken) =>
    request('POST', '/auth/refresh', { refreshToken }),
  logout: (refreshToken) =>
    request('POST', '/auth/logout', { refreshToken }),
  getMe: () => request('GET', '/auth/me'),

  // ── Profile & Settings ────────────────────────────────────
  updateProfile: (displayName, bio, avatarUrl) =>
    request('PUT', '/auth/profile', { displayName, bio, avatarUrl }),
  changePassword: (currentPassword, newPassword) =>
    request('PUT', '/auth/password', { currentPassword, newPassword }),
  changeEmail: (currentPassword, newEmail) =>
    request('PUT', '/auth/email', { currentPassword, newEmail }),
  getSettings: () => request('GET', '/auth/settings'),
  updateSettings: (settings) => request('PUT', '/auth/settings', settings),

  // ── Sessions ──────────────────────────────────────────────
  getSessions: () => request('GET', '/auth/sessions'),
  revokeSession: (id) => request('DELETE', `/auth/sessions/${id}`),

  // ── Admin: User management ────────────────────────────────
  listUsers: () => request('GET', '/auth/users'),
  deleteUser: (id) => request('DELETE', `/auth/users/${id}`),

  // ── Repos ─────────────────────────────────────────────────
  listRepos: (search, publicOnly) => {
    const p = new URLSearchParams();
    if (search) p.set('search', search);
    if (publicOnly) p.set('publicOnly', 'true');
    return request('GET', `/repos${p.toString() ? `?${p}` : ''}`);
  },
  getRepo: (name) => request('GET', `/repos/${name}`),
  createRepo: (name, description, isPublic = true) =>
    request('POST', '/repos', { name, description, isPublic }),
  deleteRepo: (name) => request('DELETE', `/repos/${name}`),

  // ── Files ─────────────────────────────────────────────────
  listFiles: (repo, path = '') =>
    request('GET', `/repos/${repo}/files${path ? `?path=${encodeURIComponent(path)}` : ''}`),
  getFile: (repo, path) =>
    request('GET', `/repos/${repo}/file?path=${encodeURIComponent(path)}`),
  saveFile: (repo, filePath, content, message, branch) =>
    request('POST', `/repos/${repo}/file`, { filePath, content, message, branch }),
  deleteFile: (repo, filePath, message) =>
    request('DELETE', `/repos/${repo}/file`, { filePath, message }),

  // ── Push ─────────────────────────────────────────────────
  pushFiles: (repo, files, message, branch, authorName, authorEmail) => {
    const form = new FormData();
    form.append('message', message);
    if (branch) form.append('branch', branch);
    if (authorName) form.append('authorName', authorName);
    if (authorEmail) form.append('authorEmail', authorEmail);
    for (const f of files) form.append('files', f.file, f.targetPath);
    return request('POST', `/repos/${repo}/push`, form, true);
  },
  uploadZip: (repo, zipFile, message, branch) => {
    const form = new FormData();
    form.append('message', message);
    if (branch) form.append('branch', branch);
    form.append('file', zipFile, zipFile.name);
    return request('POST', `/repos/${repo}/upload-zip`, form, true);
  },

  // XHR-based upload with real-time progress callbacks.
  // onProgress(percent: 0-100) is called during the HTTP upload phase.
  uploadZipWithProgress: (repo, zipBlob, message, branch, onProgress, authorName, authorEmail) => {
    return new Promise((resolve, reject) => {
      const doRequest = (isRetry = false) => {
        const form = new FormData();
        form.append('message', message);
        if (branch) form.append('branch', branch);
        if (authorName) form.append('authorName', authorName);
        if (authorEmail) form.append('authorEmail', authorEmail);
        form.append('file', zipBlob, 'upload.zip');

        const xhr = new XMLHttpRequest();
        xhr.open('POST', `${BASE}/repos/${repo}/upload-zip`);

        const token = getToken();
        if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);

        if (onProgress) {
          xhr.upload.onprogress = (e) => {
            if (e.lengthComputable) onProgress(Math.round((e.loaded / e.total) * 100));
          };
        }

        xhr.onload = async () => {
          if (xhr.status === 401 && !isRetry) {
            const refreshed = await tryRefresh();
            if (refreshed) { doRequest(true); return; }
            reject(new Error('Session expired. Please sign in again.'));
            return;
          }
          try {
            const data = JSON.parse(xhr.responseText);
            if (xhr.status >= 200 && xhr.status < 300) resolve(data);
            else reject(new Error(data.error || `HTTP ${xhr.status}`));
          } catch {
            reject(new Error(`HTTP ${xhr.status}`));
          }
        };

        xhr.onerror = () => reject(new Error('Network error during upload'));
        xhr.send(form);
      };
      doRequest();
    });
  },

  // ── Branches ──────────────────────────────────────────────
  listBranches: (repo) => request('GET', `/repos/${repo}/branches`),
  createBranch: (repo, branchName, fromBranch) =>
    request('POST', `/repos/${repo}/branches`, { branchName, fromBranch }),
  checkoutBranch: (repo, branchName) =>
    request('POST', `/repos/${repo}/checkout`, { branchName }),
  mergeBranch: (repo, sourceBranch, targetBranch, message) =>
    request('POST', `/repos/${repo}/merge`, { sourceBranch, targetBranch, message }),
  deleteBranch: (repo, branch) =>
    request('DELETE', `/repos/${repo}/branches/${branch}`),

  // ── Commits ───────────────────────────────────────────────
  listCommits: (repo, branch, limit = 50) =>
    request('GET', `/repos/${repo}/commits?${branch ? `branch=${encodeURIComponent(branch)}&` : ''}limit=${limit}`),
  getCommit: (repo, hash) =>
    request('GET', `/repos/${repo}/commits/${hash}`),
  getBranchDiff: (repo, from, to) =>
    request('GET', `/repos/${repo}/diff?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`),

  // ── Issues ────────────────────────────────────────────────
  listIssues: (repo, status) =>
    request('GET', `/repos/${repo}/issues${status ? `?status=${status}` : ''}`),
  getIssue: (repo, number) =>
    request('GET', `/repos/${repo}/issues/${number}`),
  createIssue: (repo, title, body) =>
    request('POST', `/repos/${repo}/issues`, { title, body }),
  updateIssue: (repo, number, updates) =>
    request('PATCH', `/repos/${repo}/issues/${number}`, updates),
  addComment: (repo, number, body) =>
    request('POST', `/repos/${repo}/issues/${number}/comments`, { body }),

  // ── Activity logs ─────────────────────────────────────────
  getLogs: ({ repo, event_type, from, to, limit = 50, offset = 0 } = {}) => {
    const params = new URLSearchParams();
    if (repo)       params.set('repo', repo);
    if (event_type) params.set('event_type', event_type);
    if (from)       params.set('from', from);
    if (to)         params.set('to', to);
    params.set('limit', limit);
    params.set('offset', offset);
    return request('GET', `/logs?${params.toString()}`);
  },
  getLogEventTypes: () => request('GET', '/logs/event-types'),

  // ── Download URLs ─────────────────────────────────────────
  getFileDownloadUrl: (repo, filePath, branch) =>
    `/api/repos/${repo}/download/file?path=${encodeURIComponent(filePath)}${branch ? `&branch=${encodeURIComponent(branch)}` : ''}`,
  getRepoDownloadUrl: (repo, branch) =>
    `/api/repos/${repo}/download${branch ? `?branch=${encodeURIComponent(branch)}` : ''}`,
  getFolderDownloadUrl: (repo, folderPath, branch) =>
    `/api/repos/${repo}/download/folder?path=${encodeURIComponent(folderPath)}${branch ? `&branch=${encodeURIComponent(branch)}` : ''}`,
};
