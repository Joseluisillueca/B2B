// Cliente HTTP del portal: añade el Bearer, normaliza errores y trata el 401
// como fin de sesión (el token caduca a las 2 h con longDuration=false).

import { state } from './state.js';

export class ApiError extends Error {
  constructor(status, body) {
    super(body?.error || `HTTP ${status}`);
    this.status = status;
    this.body = body;
  }
}

let onUnauthorized = () => {};
export const setUnauthorizedHandler = fn => { onUnauthorized = fn; };

async function request(method, path, body) {
  const headers = {};
  if (state.token) headers.Authorization = `Bearer ${state.token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  const response = await fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body)
  });

  if (response.status === 401) {
    state.clear();
    onUnauthorized();
    throw new ApiError(401, { error: 'Unauthorized' });
  }

  const text = await response.text();
  const parsed = text ? safeParse(text) : null;
  if (!response.ok) throw new ApiError(response.status, parsed);
  return parsed;
}

const safeParse = text => { try { return JSON.parse(text); } catch { return null; } };

export const api = {
  get: path => request('GET', path),
  post: (path, body) => request('POST', path, body ?? {}),
  put: (path, body) => request('PUT', path, body ?? {}),
  del: path => request('DELETE', path),

  /** Login: mismo contrato que usa el conector (contrato 01 §1) */
  async login(email, password) {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, type: 'global', longDuration: true })
    });
    if (!response.ok) throw new ApiError(response.status, await response.json().catch(() => null));
    return response.json();
  },

  me: () => request('GET', '/api/portal/me')
};
