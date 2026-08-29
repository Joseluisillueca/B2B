// Accesos al portal: alta (con contraseña o activación por email), reset y borrado.
import { api } from '../api.js';
import { icons } from '../icons.js';
import { esc, fkOptions, flash } from '../util.js';

const ROLES = [['client-admin', 'Usuario de cliente'], ['admin', 'Administrador'], ['agent', 'Comercial'], ['integration', 'Integración']];
const roleLabel = r => (ROLES.find(x => x[0] === r) || [r, r])[1];

export default async function users(main) {
  const [list, clients] = await Promise.all([api.users(), fkOptions('client')]);
  const items = list.items || [];

  main.innerHTML = `
    <div class="mng-page-head">
      <div>
        <p class="crumbs">Comercial</p>
        <h1 class="title">Accesos</h1>
        <p class="lead">Quién entra al portal: usuarios de cliente, administradores del back-office y comerciales.</p>
      </div>
    </div>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.plus(20)}Nuevo acceso</h2></header>
      <div class="biz-card">
        <form class="mng-form" id="nf" novalidate>
          <div class="biz-grid">
            <p class="acc-field"><label><span>Email *</span><input type="email" id="email" maxlength="200"></label></p>
            <p class="acc-field"><label><span>Rol *</span><select id="role">${ROLES.map(([v, l]) => `<option value="${v}">${esc(l)}</option>`).join('')}</select></label></p>
            <p class="acc-field"><label><span>Nombre</span><input type="text" id="name" maxlength="120"></label></p>
            <p class="acc-field" id="clientWrap"><label><span>Cliente</span>
              <select id="client"><option value="">—</option>${clients.map(o => `<option value="${esc(o.value)}">${esc(o.label)}</option>`).join('')}</select></label></p>
            <p class="acc-field"><label><span>Contraseña</span><input type="text" id="pw" autocomplete="off" placeholder="Vacío = enviar email de activación"></label>
              <span class="acc-hint">Si la dejas vacía, se envía un correo para que el usuario la cree.</span></p>
          </div>
          <div class="acc-actions"><button type="submit" class="btn-primary">Crear acceso</button></div>
        </form>
      </div>
    </section>

    <section class="biz-section">
      <header class="acc-head biz-head"><h2>${icons.key(20)}Accesos (${items.length})</h2></header>
      <div class="grid-scroll">
        <table class="grid">
          <thead><tr><th>Email</th><th>Rol</th><th>Nombre</th><th>Contraseña</th><th>Estado</th><th class="grid-actions"></th></tr></thead>
          <tbody id="rows">${items.map(rowHtml).join('') || `<tr class="grid-empty"><td colspan="6">Todavía no hay accesos.</td></tr>`}</tbody>
        </table>
      </div>
    </section>`;

  const nf = main.querySelector('#nf');
  const roleSel = nf.querySelector('#role');
  const clientWrap = nf.querySelector('#clientWrap');
  const syncClient = () => { clientWrap.style.display = roleSel.value === 'client-admin' ? '' : 'none'; };
  roleSel.onchange = syncClient; syncClient();

  nf.onsubmit = async event => {
    event.preventDefault();
    const email = nf.querySelector('#email').value.trim();
    if (!email) return flash('Indica el email.', 'err');
    const pw = nf.querySelector('#pw').value.trim();
    const body = {
      email, role: roleSel.value,
      name: nf.querySelector('#name').value.trim() || null,
      clientExternalId: roleSel.value === 'client-admin' ? (nf.querySelector('#client').value || null) : null,
      password: pw || null, sendActivation: !pw,
    };
    try {
      await api.createUser(body);
      flash(pw ? 'Acceso creado con contraseña.' : 'Acceso creado; correo de activación enviado.');
      users(main);
    } catch (e) { flash(e.body?.error || e.message, 'err'); }
  };

  main.querySelectorAll('#rows tr[data-id]').forEach(tr => {
    const uid = tr.dataset.id;
    tr.querySelector('[data-act=pwd]').onclick = async () => {
      const pw = prompt('Nueva contraseña para este acceso:');
      if (!pw) return;
      try { await api.updateUser(uid, { password: pw }); flash('Contraseña actualizada.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
    tr.querySelector('[data-act=activate]').onclick = async () => {
      try { await api.updateUser(uid, { sendActivation: true }); flash('Correo de activación enviado.'); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
    tr.querySelector('[data-act=del]').onclick = async () => {
      if (!confirm('¿Eliminar este acceso? El usuario ya no podrá entrar.')) return;
      try { await api.delUser(uid); flash('Acceso eliminado.'); users(main); }
      catch (e) { flash(e.body?.error || e.message, 'err'); }
    };
  });
}

function rowHtml(u) {
  return `<tr data-id="${u.id}">
    <td class="grid-link">${esc(u.email)}</td>
    <td><span class="grid-chip">${esc(roleLabel(u.role))}</span></td>
    <td>${esc(u.name || '—')}</td>
    <td>${u.hasPassword ? '<span class="grid-chip ok">Sí</span>' : '<span class="grid-chip warn">Pendiente</span>'}</td>
    <td>${u.isActive ? '<span class="grid-chip ok">Activo</span>' : '<span class="grid-chip off">Inactivo</span>'}</td>
    <td class="grid-actions" style="white-space:nowrap">
      <button class="btn-ghost" data-act="pwd" title="Cambiar contraseña" style="padding:.35rem .6rem">${icons.lock(15)}</button>
      <button class="btn-ghost" data-act="activate" title="Enviar activación" style="padding:.35rem .6rem">${icons.send(15)}</button>
      <button class="btn-ghost" data-act="del" title="Eliminar" style="padding:.35rem .6rem;color:var(--out)">${icons.trash(15)}</button>
    </td>
  </tr>`;
}
