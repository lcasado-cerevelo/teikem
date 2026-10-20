# 41 — Portal del cliente de almacén (servidor)

Lote 153 (2026-10-20). Sesión del portal (`/api/v1/portal/*`): la cuenta entra a **una compañía y un cliente** a la vez; si tiene accesos en varias (por ejemplo, almacén en una compañía y transporte en otra), cambia con «Cambiar de acceso» (`POST /portal/auth/switch-client`) sin otra contraseña ni segundo paso. El menú sale de lo que esa compañía ofrece (`GET /portal/me` → `capabilities`).

| Qué | Ruta | Quién | Detalle |
|---|---|---|---|
| Mis existencias | `GET /portal/warehouse/stock` | cualquier acceso del cliente con almacén | por producto, lote y almacén; buscador y almacén; máximo 100 por página |
| Mis productos | `GET /portal/warehouse/products` | igual | con lo que hay en total; inactivos con `includeInactive` |
| Mis movimientos | `GET /portal/warehouse/movements` | igual | `from`/`to` (días de la compañía) y buscador; sin posición ni usuario |
| Valor de mi inventario | `GET /portal/warehouse/valuation` | **ADMIN u OPERADOR** del cliente | valuación de la compañía (promedio o por capas) |

Errores: 403 «El usuario no tiene acceso activo al portal.» (sesión sin cliente), 403 «Su acceso no incluye ver el valor del inventario.» (acceso de solo lectura), 403 con el aviso de módulo apagado si la compañía no tiene almacén. Un producto o movimiento de otro cliente nunca aparece.

## Pantallas del portal (app del cliente)

Lote 154 (2026-10-20). La barra de pestañas de `app-cliente` sigue a la **compañía activa** (`capabilities` de `GET /portal/me`):

| La compañía ofrece | Pestañas | Se entra por |
|---|---|---|
| Solo transporte | Inicio, Órdenes, Avisos, Mensajes, Ajustes | Inicio |
| Solo almacén | Almacén, Avisos, Mensajes, Ajustes | Almacén |
| Ambos | Todas | Inicio |

La pestaña **Almacén** tiene cuatro secciones: **Existencias** (por producto, lote y almacén), **Productos**, **Movimientos** y **Valor** (solo si el acceso es ADMIN u OPERADOR). Cada lista tiene buscador y «Cargar más». Las cantidades y los precios van en la moneda de la compañía. En Ajustes, quien tiene accesos en varias compañías ve «Cliente · Compañía» y al cambiar, el menú se rehace con lo que ofrece la nueva compañía.
