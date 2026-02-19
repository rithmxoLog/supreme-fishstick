const BASE = '/api';

function getToken() {
  return localStorage.getItem('gitxo_token');
}

async function request(method, path, body, isFormData = false) {
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
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
  return data;
}

export const api = {
  // ── Auth ──────────────────────────────────────────────────
  register: (username, email, password) =>
    request('POST', '/auth/register', { username, email, password }),
  login: (email, password) =>
    request('POST', '/auth/login', { email, password }),
  getMe: () => request('GET', '/auth/me'),

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

  // ── Push (multipart upload) ───────────────────────────────
  pushFiles: (repo, files, message, branch, authorName, authorEmail) => {
    const form = new FormData();
    form.append('message', message);
    if (branch) form.append('branch', branch);
    if (authorName) form.append('authorName', authorName);
    if (authorEmail) form.append('authorEmail', authorEmail);
    for (const f of files) form.append('files', f.file, f.targetPath);
    return request('POST', `/repos/${repo}/push`, form, true);
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

  // ── Download URL helpers ──────────────────────────────────
  getFileDownloadUrl: (repo, filePath, branch) =>
    `/api/repos/${repo}/download/file?path=${encodeURIComponent(filePath)}${branch ? `&branch=${encodeURIComponent(branch)}` : ''}`,
  getRepoDownloadUrl: (repo, branch) =>
    `/api/repos/${repo}/download${branch ? `?branch=${encodeURIComponent(branch)}` : ''}`,
};
