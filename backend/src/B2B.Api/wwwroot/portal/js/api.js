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

  me: () => request('GET', '/api/portal/me'),

  /** Cartera de clientes del agente (contrato §agent). `params` es un URLSearchParams */
  agentClients: params => request('GET', `/api/agent/clients?${params}`),

  /** Suplanta a un cliente: devuelve { token, client }. El token opera como el cliente */
  impersonate: clientId => request('POST', '/api/agent/impersonate', { clientId }),

  /** Alta de cliente (prealta). `body` es el formulario anidado del alta */
  createClient: body => request('POST', '/api/agent/clients', body),

  /** Bandeja de solicitudes de registro del agente */
  clientRequests: () => request('GET', '/api/agent/clients/requests'),

  /** Documentos agregados de la cartera (pedidos/albaranes/facturas). `params` URLSearchParams */
  agentDocuments: params => request('GET', `/api/agent/documents?${params}`),

  /** Estadísticas agregadas de la cartera del agente */
  agentStatistics: locale => request('GET', `/api/agent/statistics?locale=${locale}`),

  /** Calendario de citas del agente */
  appointments: params => request('GET', `/api/agent/appointments${params ? `?${params}` : ''}`),
  createAppointment: body => request('POST', '/api/agent/appointments', body),
  setAppointmentStatus: (id, status) => request('POST', `/api/agent/appointments/${id}/status`, { status }),

  /** Descarga autenticada: los CSV van con Bearer, así que no valen enlaces sueltos */
  async download(path, fallbackName) {
    const response = await fetch(path, {
      headers: state.token ? { Authorization: `Bearer ${state.token}` } : {}
    });
    if (response.status === 401) {
      state.clear();
      onUnauthorized();
      throw new ApiError(401, { error: 'Unauthorized' });
    }
    if (!response.ok) throw new ApiError(response.status, null);

    const disposition = response.headers.get('content-disposition') || '';
    const named = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = url;
    link.download = named ? decodeURIComponent(named[1]) : fallbackName;
    document.body.append(link);
    link.click();
    link.remove();
    // Revocar en el mismo tick cancela la descarga en algunos navegadores
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  }
};
