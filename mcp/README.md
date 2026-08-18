# MCP "lejan-b2b" — pregunta a tu portal en lenguaje natural

Servidor [MCP](https://modelcontextprotocol.io) que conecta un chat (Claude Desktop,
Claude Code, cualquier cliente MCP) con los datos del portal B2B. Te deja preguntar
cosas como:

- «¿Cuánto he vendido este mes?»
- «¿Qué pedidos he hecho en julio?»
- «¿Tengo facturas vencidas? ¿Cuánto debo?»
- «Enséñame mis ventas mes a mes este año.»

El servidor se autentica en la API del portal con **tus** credenciales, así que solo
ve los datos de tu cliente (el token filtra por cliente en el backend).

## Herramientas expuestas

| Herramienta | Para qué |
|---|---|
| `resumen_ventas(desde?, hasta?)` | Facturado + nº de pedidos/unidades + deuda del periodo (por defecto, el mes en curso) |
| `pedidos(desde?, hasta?, estado?, buscar?, limite?)` | Lista de pedidos con importe y estado |
| `facturas(estado?, limite?)` | Facturas con deuda y vencimiento |
| `ventas_por_mes(desde?, hasta?)` | Serie de ventas facturadas mes a mes |
| `mi_cuenta()` | Nombre, nº de cliente y límite de crédito |

Todas son de **solo lectura**.

## Instalación

```bash
cd mcp
python -m venv .venv
.venv\Scripts\activate            # Windows PowerShell:  .venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

## Configuración

Variables de entorno:

| Variable | Valor |
|---|---|
| `B2B_BASE_URL` | URL del portal (por defecto `http://localhost:5198`) |
| `B2B_EMAIL` | tu email del portal |
| `B2B_PASSWORD` | tu contraseña |

## Conectarlo a Claude Desktop

Edita `claude_desktop_config.json` (menú **Claude → Ajustes → Desarrollador → Editar
config**) y añade:

```json
{
  "mcpServers": {
    "lejan-b2b": {
      "command": "C:\\ruta\\a\\mcp\\.venv\\Scripts\\python.exe",
      "args": ["C:\\Users\\Usuario\\Documents\\AL\\B2BNew\\mcp\\b2b_mcp_server.py"],
      "env": {
        "B2B_BASE_URL": "http://localhost:5198",
        "B2B_EMAIL": "tu-email@dominio.com",
        "B2B_PASSWORD": "tu-contraseña"
      }
    }
  }
}
```

Reinicia Claude Desktop y ya puedes preguntar por tus datos en el chat.

## Probar sin cliente MCP

```bash
# Comprueba que arranca y lista sus herramientas (con el inspector de MCP):
mcp dev b2b_mcp_server.py
```

## Notas

- El servidor cachea el token y lo renueva solo si caduca.
- Las fechas se pasan en formato ISO `YYYY-MM-DD`; las respuestas salen en `dd/mm/aaaa`
  y con importes en euros (formato español).
- Pensado para el mismo backend `.NET` del portal; no requiere acceso directo a la BD.
