// Entrada al back-office — mismo "login split" del portal. Solo rol admin: tras el
// login se comprueba el acceso a /api/admin/* antes de dejar pasar.
import { api, auth } from '../api.js';
import { brandMark } from '/portal/js/branding.js';

export default function login(app) {
  app.setAttribute('aria-busy', 'false');
  app.innerHTML = `
    <div class="login-split">
      <div class="login-hero">
        <a class="brand" href="#/login" style="text-decoration:none">${brandMark()}</a>
        <div>
          <p class="hero-kicker">Back-office</p>
          <h1 class="login-display">Gestión<br>de maestros</h1>
        </div>
        <p style="color:rgba(255,255,255,.82); max-width:26rem; margin:0">
          Catálogo, clientes y pedidos del portal B2B, en un solo sitio.</p>
      </div>
      <div class="login-panel">
        <form class="login-card" id="lf" novalidate>
          <h1 class="login-h">Entrar</h1>
          <input class="field" id="email" type="email" placeholder="Email" autocomplete="username" required>
          <input class="field" id="pw" type="password" placeholder="Contraseña" autocomplete="current-password" required>
          <button class="submit" type="submit">Entrar</button>
          <p class="err" id="err" role="alert"></p>
          <p class="mng-login-note">Acceso reservado a administradores del portal.</p>
        </form>
      </div>
    </div>`;

  const form = app.querySelector('#lf');
  const err = app.querySelector('#err');
  form.onsubmit = async event => {
    event.preventDefault();
    const btn = form.querySelector('.submit');
    btn.disabled = true; err.textContent = '';
    const email = app.querySelector('#email').value.trim();
    try {
      const res = await api.login(email, app.querySelector('#pw').value);
      auth.token = res.token; auth.who = email;
      // Verifica que es admin (el endpoint es RequireAdmin → 403 si no lo es)
      await api.summary();
      // El hash puede seguir siendo #/dashboard (lo fija boot.js al cargar), así que
      // no basta con reasignarlo: se resuelve la ruta a mano para pintar el shell.
      if (location.hash !== '#/dashboard') location.hash = '#/dashboard';
      const { resolve } = await import('../router.js');
      resolve();
    } catch (failure) {
      btn.disabled = false;
      auth.clear();
      err.textContent = failure.status === 403
        ? 'Este usuario no tiene permisos de administración.'
        : failure.status === 401 ? 'Credenciales incorrectas.'
        : (failure.message || 'No se pudo iniciar sesión.');
    }
  };
}
