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

## Lo que el cliente crea desde su portal (Lote 156)

Tres cosas que la compañía enciende **por cliente** en la ficha del cliente (Clientes → perfil → «Lo que el cliente crea desde su portal»; con el módulo de almacén; apagadas por omisión). Solo las usan los accesos **ADMIN u OPERADOR** del cliente; el de solo lectura no. Todo se crea a nombre del cliente de la sesión (no se elige cliente) y con **las mismas reglas y mensajes del personal** (mismos servicios): lo que se rechaza en la pantalla del personal se rechaza igual aquí.

| Qué | Ruta | Detalle |
|---|---|---|
| Almacenes para elegir | `GET /portal/warehouse/warehouses` | los activos de la compañía; con uno solo se elige solo |
| Crear producto | `POST /portal/warehouse/products` | SKU, nombre, código de barras, marca, costo de compra opcional, por lote o no; el dueño es el cliente |
| Mis avisos de llegada | `GET/POST /portal/warehouse/arrivals`, `POST …/arrivals/{id}/cancel` | solo productos del cliente; precio unitario opcional (si el producto no tiene costo, toma el de la primera línea); cancelar solo si sigue esperado y sin recibo |
| Mis avisos de salida | `GET/POST /portal/warehouse/exits`, `GET …/exits/{id}`, `POST …/exits/{id}/cancel` | reserva en el acto lo que haya y deja lo demás pendiente; cancelar solo si sigue solicitado |

En la app, las pestañas «Llegadas» y «Salidas» y el botón «Nuevo producto» aparecen solo si la compañía lo encendió para ese cliente (`capabilities.canCreateProducts/Arrivals/Exits` de `GET /portal/me`).

Errores: 403 «Su compañía no le permite crear productos desde el portal.» / «…avisos de llegada…» / «…avisos de salida…» (bandera apagada), 403 «Su acceso es de solo lectura y no puede crear documentos.», 404 si el aviso es de otro cliente (nunca se ve ni se cancela lo ajeno), y los 400/422 del personal (producto de otro cliente, cantidad inválida, SKU repetido, etc.).

### Editar lo que el cliente creó (Lote 158)

| Qué | Ruta | Detalle |
|---|---|---|
| Editar mi aviso de llegada | `PATCH /portal/warehouse/arrivals/{id}` | almacén, referencia, fecha y líneas; solo mientras siga esperado y sin recibo abierto (mismos mensajes del personal) |
| Editar los datos de mi aviso de salida | `PATCH /portal/warehouse/exits/{publicId}` | referencia, destino, fecha requerida y notas (texto vacío las quita); solo mientras siga solicitado |
| Cambiar la cantidad de una línea | `PATCH /portal/warehouse/exits/{publicId}/lines/{lineId}` | reajusta la reserva (sube: reserva lo que haya; baja: libera primero el pendiente) |

Se necesita la misma bandera de creación del cliente; lo ajeno responde 404. En la app, cada aviso editable trae el botón «Editar». Al elegir producto en un aviso de salida se ve entre paréntesis lo disponible.
