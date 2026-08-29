// Cliente HTTP del back-office de gestión. Bearer del admin; 401 → login.
// Reutiliza los endpoints ya existentes: /api/admin/* (RequireAdmin).

const TOKEN = 'mng_token', WHO = 'mng_who';

export const auth = {
  // Reutiliza también el token del CMS clásico (admin.html usa `b2b_token`) para que,
  // al venir de allí, no haya que iniciar sesión otra vez.
  get token() { return sessionStorage.getItem(TOKEN) || sessionStorage.getItem('b2b_token') || ''; },
  set token(v) { v ? sessionStorage.setItem(TOKEN, v) : sessionStorage.removeItem(TOKEN); },
  get who() { return sessionStorage.getItem(WHO) || ''; },
  set who(v) { v ? sessionStorage.setItem(WHO, v) : sessionStorage.removeItem(WHO); },
  clear() { sessionStorage.removeItem(TOKEN); sessionStorage.removeItem(WHO); },
};

export class ApiError extends Error {
  constructor(status, body) { super(body?.error || `HTTP ${status}`); this.status = status; this.body = body; }
}

let onUnauthorized = () => {};
export const setUnauthorizedHandler = fn => { onUnauthorized = fn; };

async function request(method, path, body) {
  const headers = {};
  if (auth.token) headers.Authorization = `Bearer ${auth.token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (res.status === 401) { auth.clear(); onUnauthorized(); throw new ApiError(401, { error: 'Sesión caducada' }); }
  const text = await res.text();
  const parsed = text ? safeParse(text) : null;
  if (!res.ok) throw new ApiError(res.status, parsed);
  return parsed;
}
const safeParse = t => { try { return JSON.parse(t); } catch { return null; } };

export const api = {
  get: p => request('GET', p),
  post: (p, b) => request('POST', p, b ?? {}),
  put: (p, b) => request('PUT', p, b ?? {}),
  del: p => request('DELETE', p),

  async login(email, password) {
    const res = await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, type: 'global', longDuration: true }),
    });
    if (!res.ok) throw new ApiError(res.status, await res.json().catch(() => null));
    return res.json();
  },

  summary: () => request('GET', '/api/admin/summary'),
  docs: type => request('GET', `/api/admin/sync-documents?entityType=${encodeURIComponent(type)}&take=500&includePayload=true`),
  doc: (type, id) => request('GET', `/api/admin/sync-documents/${encodeURIComponent(type)}/${encodeURIComponent(id)}`),
  saveEntity: (type, id, body, parentId) =>
    request('PUT', `/api/admin/entities/${encodeURIComponent(type)}/${encodeURIComponent(id)}`
      + (parentId ? `?parentId=${encodeURIComponent(parentId)}` : ''), body),
  delEntity: (type, id) => request('DELETE', `/api/admin/entities/${encodeURIComponent(type)}/${encodeURIComponent(id)}`),

  users: () => request('GET', '/api/admin/users'),
  createUser: b => request('POST', '/api/admin/users', b),
  updateUser: (id, b) => request('PUT', `/api/admin/users/${id}`, b),
  delUser: id => request('DELETE', `/api/admin/users/${id}`),

  orderStatus: (id, status) => request('PUT', `/api/admin/orders/${encodeURIComponent(id)}/status`, { status }),
  delOrder: id => request('DELETE', `/api/admin/orders/${encodeURIComponent(id)}`),

  // Imágenes de modelo (marketing): endpoints ya existentes
  modelImages: () => request('GET', '/api/admin/model-images'),
  setModelImage: (modelId, uri) => request('PUT', `/api/admin/model-images/${encodeURIComponent(modelId)}`, { uri }),
  delModelImage: modelId => request('DELETE', `/api/admin/model-images/${encodeURIComponent(modelId)}`),

  // Sube un fichero de imagen (multipart) y devuelve { url, name, size }. La aloja el
  // propio portal en /media/portal y la sirve como estático.
  async uploadMedia(file) {
    const form = new FormData();
    form.append('file', file);
    const res = await fetch('/api/admin/media', {
      method: 'POST', headers: auth.token ? { Authorization: `Bearer ${auth.token}` } : {}, body: form,
    });
    if (res.status === 401) { auth.clear(); onUnauthorized(); throw new ApiError(401, { error: 'Sesión caducada' }); }
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new ApiError(res.status, data);
    return data;
  },
};
