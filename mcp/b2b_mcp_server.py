"""
Servidor MCP "lejan-b2b" — consulta en lenguaje natural de los datos del portal B2B.

Expone como herramientas MCP las consultas típicas que un cliente hace sobre SU
propia actividad ("¿cuánto he vendido este mes?", "¿qué pedidos he hecho?",
"¿cuánto debo?"). Se autentica contra la API del portal con las credenciales del
cliente, así que el token filtra por cliente y nunca se ven datos de otros.

Conéctalo a un chat (p. ej. Claude Desktop) — ver README.md.

Configuración por variables de entorno:
  B2B_BASE_URL   URL del portal            (por defecto http://localhost:5198)
  B2B_EMAIL      email del cliente         (obligatorio)
  B2B_PASSWORD   contraseña del cliente    (obligatorio)

Ejecutar:  python b2b_mcp_server.py        (transporte stdio)
"""

from __future__ import annotations

import os
from datetime import date, datetime
from typing import Any

import httpx

# FastMCP: el SDK oficial lo expone en mcp.server.fastmcp; el paquete standalone
# `fastmcp` (v2) lo expone en la raíz. Se admite cualquiera de los dos.
try:
    from mcp.server.fastmcp import FastMCP
except ImportError:  # pragma: no cover
    from fastmcp import FastMCP

# ── Configuración ──────────────────────────────────────────────────────────
BASE_URL = os.environ.get("B2B_BASE_URL", "http://localhost:5198").rstrip("/")
EMAIL = os.environ.get("B2B_EMAIL", "")
PASSWORD = os.environ.get("B2B_PASSWORD", "")
CHARACTER_LIMIT = 25_000

mcp = FastMCP("lejan-b2b")

# ── Traducciones de estado/tipo (la API los da como códigos) ───────────────
ORDER_STATUS = {
    "open": "Abierto", "partially-shipped": "Envío parcial", "shipped": "Enviado",
    "invoiced": "Facturado", "canceled": "Cancelado",
}
INVOICE_STATUS = {
    "overdue": "Vencida", "paid": "Cobrada", "partial": "Parcial",
    "credit": "A crédito", "pending": "Pendiente",
}
ORDER_TYPE = {
    "SCHEDULED": "Programación", "REPLENISHMENT": "Reposición",
    "REORDER": "Reposición", "NOT_DEFINED": "—", "": "—",
}

# ── Estado de sesión (token cacheado) ──────────────────────────────────────
_token: str | None = None


class B2BError(Exception):
    """Error de negocio con mensaje accionable para el chat."""


def _login(client: httpx.Client) -> str:
    if not EMAIL or not PASSWORD:
        raise B2BError(
            "Faltan credenciales. Define las variables de entorno B2B_EMAIL y "
            "B2B_PASSWORD con el email y la contraseña del cliente del portal."
        )
    r = client.post(
        f"{BASE_URL}/api/auth/login",
        json={"email": EMAIL, "password": PASSWORD, "longDuration": True},
        timeout=20,
    )
    if r.status_code == 401:
        raise B2BError("Credenciales incorrectas (B2B_EMAIL / B2B_PASSWORD).")
    if r.status_code == 429:
        raise B2BError("El portal ha limitado los intentos de inicio de sesión; espera un minuto y reintenta.")
    r.raise_for_status()
    return r.json()["token"]


def _get(path: str, params: dict[str, Any] | None = None) -> Any:
    """GET autenticado contra /api/portal/*, con re-login transparente si el token caduca."""
    global _token
    with httpx.Client() as client:
        if _token is None:
            _token = _login(client)
        for _ in range(2):
            r = client.get(
                f"{BASE_URL}{path}",
                params={k: v for k, v in (params or {}).items() if v not in (None, "")},
                headers={"Authorization": f"Bearer {_token}"},
                timeout=25,
            )
            if r.status_code == 401:
                _token = _login(client)  # el token caducó: renueva y reintenta una vez
                continue
            if r.status_code == 404:
                raise B2BError(f"No encontrado: {path}")
            r.raise_for_status()
            return r.json()
    raise B2BError("No se pudo autenticar contra el portal.")


# ── Utilidades de formato ──────────────────────────────────────────────────
def _eur(value: float | int | None) -> str:
    n = float(value or 0)
    s = f"{n:,.2f}"  # 1,234.56
    s = s.replace(",", "@").replace(".", ",").replace("@", ".")  # → 1.234,56
    return f"{s} €"


def _fecha(iso: str | None) -> str:
    if not iso:
        return "—"
    try:
        return datetime.fromisoformat(iso.replace("Z", "+00:00")).strftime("%d/%m/%Y")
    except ValueError:
        return iso[:10]


def _mes_actual() -> tuple[str, str]:
    hoy = date.today()
    desde = hoy.replace(day=1)
    return desde.isoformat(), hoy.isoformat()


def _rango(desde: str | None, hasta: str | None) -> tuple[str, str]:
    """Normaliza un rango; si falta, usa el mes en curso."""
    if not desde and not hasta:
        return _mes_actual()
    d = desde or "2000-01-01"
    h = hasta or date.today().isoformat()
    return d, h


# ── Herramientas ───────────────────────────────────────────────────────────
@mcp.tool(annotations={"readOnlyHint": True, "openWorldHint": True})
def resumen_ventas(desde: str | None = None, hasta: str | None = None) -> str:
    """Resumen de la actividad del cliente en un periodo: cuánto ha facturado, cuántos
    pedidos ha hecho y cuánto debe. Es la respuesta a "¿cuánto he vendido este mes?".

    Parámetros:
      desde: fecha inicial ISO (YYYY-MM-DD). Opcional.
      hasta: fecha final ISO (YYYY-MM-DD). Opcional.
    Si no se indican fechas, usa el MES EN CURSO.

    Devuelve un resumen en texto con: total facturado, nº de facturas, nº de pedidos y
    unidades del periodo, y la deuda pendiente total (todas las facturas sin cobrar).
    """
    d, h = _rango(desde, hasta)
    stats = _get("/api/portal/statistics", {"from": d, "to": h})
    pedidos = _get("/api/portal/orders", {"from": d, "to": h, "take": 200})
    facturas = _get("/api/portal/invoices", {"take": 500})

    unidades = sum(int(i.get("units") or 0) for i in pedidos.get("items", []))
    deuda = sum(float(i.get("debt") or 0) for i in facturas.get("items", []))
    vencidas = facturas.get("counts", {}).get("overdue", 0)

    return (
        f"**Resumen del {_fecha(d + 'T00:00:00')} al {_fecha(h + 'T00:00:00')}**\n\n"
        f"- Facturado: **{_eur(stats.get('total'))}** en {stats.get('count', 0)} factura(s)\n"
        f"- Pedidos realizados: **{pedidos.get('total', 0)}** ({unidades} unidades)\n"
        f"- Deuda pendiente total: **{_eur(deuda)}**"
        + (f" · {vencidas} factura(s) vencida(s)" if vencidas else "")
    )


@mcp.tool(annotations={"readOnlyHint": True, "openWorldHint": True})
def pedidos(
    desde: str | None = None,
    hasta: str | None = None,
    estado: str | None = None,
    buscar: str | None = None,
    limite: int = 20,
) -> str:
    """Lista los pedidos del cliente. Responde a "¿qué pedidos he hecho este mes?".

    Parámetros:
      desde/hasta: rango de fechas ISO (YYYY-MM-DD). Si faltan ambos, usa el MES EN CURSO.
      estado: filtra por estado. Uno de: abierto, envio-parcial, enviado, facturado, cancelado.
      buscar: texto por número de pedido o referencia de cliente.
      limite: máximo de pedidos a devolver (por defecto 20, máx 100).

    Devuelve una tabla con número, fecha, tipo, unidades, importe y estado de cada pedido.
    """
    d, h = _rango(desde, hasta)
    estado_api = {
        "abierto": "open", "envio-parcial": "partially-shipped", "enviado": "shipped",
        "facturado": "invoiced", "cancelado": "canceled",
    }.get((estado or "").lower(), "")
    data = _get("/api/portal/orders", {
        "from": d, "to": h, "status": estado_api, "search": buscar,
        "take": max(1, min(limite, 100)),
    })
    items = data.get("items", [])
    if not items:
        return f"No hay pedidos entre {_fecha(d + 'T00:00:00')} y {_fecha(h + 'T00:00:00')}" + (
            f" con estado «{estado}»" if estado else "") + "."

    filas = "\n".join(
        f"| {i['number']} | {_fecha(i.get('date'))} | {ORDER_TYPE.get(i.get('type',''), i.get('type',''))} "
        f"| {i.get('units', 0)} | {_eur(i.get('total'))} | {ORDER_STATUS.get(i.get('status',''), i.get('status',''))} |"
        for i in items
    )
    total_importe = sum(float(i.get("total") or 0) for i in items)
    cab = (f"**{data.get('total', len(items))} pedido(s)** entre {_fecha(d + 'T00:00:00')} "
           f"y {_fecha(h + 'T00:00:00')} — importe mostrado: {_eur(total_importe)}\n\n")
    tabla = ("| Pedido | Fecha | Tipo | Uds. | Importe | Estado |\n"
             "|---|---|---|---|---|---|\n" + filas)
    return (cab + tabla)[:CHARACTER_LIMIT]


@mcp.tool(annotations={"readOnlyHint": True, "openWorldHint": True})
def facturas(estado: str | None = None, limite: int = 20) -> str:
    """Lista las facturas del cliente con su deuda pendiente y vencimiento. Responde a
    "¿qué facturas tengo?", "¿cuánto debo?", "¿tengo facturas vencidas?".

    Parámetros:
      estado: filtra por estado. Uno de: vencida, cobrada, parcial, credito, pendiente.
      limite: máximo de facturas (por defecto 20, máx 100).

    Devuelve una tabla con número, fecha, forma de pago, importe, deuda y vencimiento,
    más el total de deuda pendiente de las facturas mostradas.
    """
    estado_api = {
        "vencida": "overdue", "cobrada": "paid", "parcial": "partial",
        "credito": "credit", "pendiente": "pending",
    }.get((estado or "").lower(), "")
    data = _get("/api/portal/invoices", {"status": estado_api, "take": max(1, min(limite, 100))})
    items = data.get("items", [])
    if not items:
        return "No hay facturas" + (f" con estado «{estado}»" if estado else "") + "."

    filas = "\n".join(
        f"| {i['number']} | {_fecha(i.get('date'))} | {i.get('payMethod','—')} | {_eur(i.get('total'))} "
        f"| {_eur(i.get('debt'))} | {_fecha(i.get('dueDate'))} | {INVOICE_STATUS.get(i.get('status',''), i.get('status',''))} |"
        for i in items
    )
    deuda = sum(float(i.get("debt") or 0) for i in items)
    cab = f"**{data.get('total', len(items))} factura(s)** — deuda pendiente mostrada: **{_eur(deuda)}**\n\n"
    tabla = ("| Factura | Fecha | Forma de pago | Importe | Deuda | Vence | Estado |\n"
             "|---|---|---|---|---|---|---|\n" + filas)
    return (cab + tabla)[:CHARACTER_LIMIT]


@mcp.tool(annotations={"readOnlyHint": True, "openWorldHint": True})
def ventas_por_mes(desde: str | None = None, hasta: str | None = None) -> str:
    """Serie de ventas facturadas mes a mes. Responde a "¿cómo han ido mis ventas este
    año?", "¿qué mes vendí más?".

    Parámetros:
      desde/hasta: rango ISO (YYYY-MM-DD). Si faltan, usa los últimos 12 meses.

    Devuelve una tabla mes · importe facturado · nº de facturas, con el total del periodo.
    """
    data = _get("/api/portal/statistics", {"from": desde, "to": hasta})
    meses = data.get("months", [])
    if not meses:
        return "No hay datos de ventas en el periodo."
    filas = "\n".join(f"| {m['month']} | {_eur(m.get('amount'))} | {m.get('count', 0)} |" for m in meses)
    cab = (f"**Ventas del {data.get('from','?')} al {data.get('to','?')}** — "
           f"total **{_eur(data.get('total'))}** en {data.get('count', 0)} factura(s)\n\n")
    tabla = "| Mes | Facturado | Facturas |\n|---|---|---|\n" + filas
    return cab + tabla


@mcp.tool(annotations={"readOnlyHint": True, "openWorldHint": True})
def mi_cuenta() -> str:
    """Datos del cliente y su crédito: nombre, número de cliente y límite de crédito.
    Útil para contextualizar ("¿quién soy?", "¿cuál es mi límite de crédito?").
    """
    me = _get("/api/portal/me")
    c = me.get("client") or {}
    credito = c.get("creditInfo") or {}
    limite = credito.get("value")
    return (
        f"**{c.get('name','(sin nombre)')}** (nº {c.get('number','—')})\n"
        f"- Usuario: {me.get('email','—')}\n"
        + (f"- Límite de crédito: {_eur(limite)}\n" if limite is not None else "")
    )


if __name__ == "__main__":
    mcp.run()
