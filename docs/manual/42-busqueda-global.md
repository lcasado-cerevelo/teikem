# 42 — Búsqueda global («Buscar o ejecutar»)

Lote 167 (2026-10-21, dictado por Luis). El campo **Buscar o ejecutar…** de la barra de arriba (atajo `/` o `Ctrl/⌘+K`) ahora, además de abrir pantallas del menú, **busca registros por su número o su nombre** y lleva a la pantalla que corresponde.

## Qué hace
- Se escribe un número o un nombre (desde 2 letras). Los resultados salen **arriba**, agrupados por tipo (Envíos, Clientes, Almacenes…); debajo siguen las pantallas del menú que coinciden.
- **Si hay un solo resultado y es exacto** (por ejemplo, se tecleó el número completo de un envío, de 4 letras en adelante), **se abre solo**. Si hay uno exacto y otros parecidos, o varios, o uno solo parecido, **se muestran para escoger** (↑↓ y Enter, o clic).
- Los números se buscan por **cómo empiezan** (`ORD-10` encuentra `ORD-100` y `ORD-1000`; lo exacto sale primero). Los nombres de clientes, almacenes, productos y proveedores se buscan **dentro** del nombre. Es rápida a propósito: cada tipo trae pocos resultados.
- Cada tipo se busca **solo si el usuario puede verlo** (el permiso de la pantalla correspondiente y el módulo encendido). Quien no tiene `orders.view` no ve envíos en los resultados, aunque exista el número.

## Qué se busca y a dónde lleva
| Se escribe | Encuentra | Abre |
|---|---|---|
| Número de envío, conduce, empaque (EMP-), número de factura del cliente, número de paquete o pieza, referencia del cliente | Envíos (`orders.view`, módulo Transporte) | La ficha del envío |
| Número de ruta (p. ej. `2026-0001` o `M-0011…`) | Rutas (`trips.view`) | Monitoreo de la ruta |
| Número de reclamación (`RCL-`) | Reclamaciones (`claims.view`) | La lista de reclamaciones filtrada por ese número |
| Número o nombre del cliente | Clientes (`clients.read`) | La ficha del cliente |
| Código o nombre de almacén, zona, posición | Almacenes, zonas y posiciones (`inventory.view`, módulo Almacén) | El almacén / la lista de posiciones de su zona |
| SKU (número de parte), código de barras, nombre o parte del nombre del producto | Productos | La ficha del producto |
| Número de recibo (`REC-`), de recolección o empaque (`EMP-`, `DMA-`), de aviso de salida (`SAL-`) | Documentos de almacén | Su pantalla |
| Nombre del proveedor | Proveedores (`purchasing.view`) | La lista de proveedores filtrada por ese nombre |
| Número de orden de compra (`PO-`) / de venta (`OV-`) / de factura (`FAC-`) / de renta (`REN-`) | Compras, ventas, facturación, rentas | Su ficha (la factura, la lista filtrada) |
| **Teléfono** (cualquier formato: `(787) 555-0142`, `+1 787 555 0142`, `7875550142`) | El dueño del contacto: cliente, persona del cliente, consignatario, envío, o proveedor | Su pantalla (si el usuario puede verla) |

Las listas que se abren **filtradas** (reclamaciones, facturas, proveedores, consignatarios) leen el filtro de la dirección (`?q=`).

## Cómo funciona (soporte)
- `GET /api/v1/search?q=texto` (cualquier usuario autenticado; sin permiso propio). Responde `{ query, hits[] }`: cada resultado trae `type`, `id`, `title`, `subtitle`, `matchedBy` (por qué campo salió), `exact` y `keys` extra. A dónde lleva cada tipo lo decide la web (`kernel/search/searchRules.ts`).
- Menos de 2 letras: respuesta vacía. Máximo 40 resultados, 6 por tipo, lo exacto primero.
- Índices que la hacen rápida: los de cada número (ya existían) y dos nuevos en `Diseño/logistica-db-update.sql` (Lote 167): `IX_ContactPoint_Value` (teléfonos) e `IX_WarehouseBin_Code` (posiciones).
- El teléfono se compara por **dígitos** (los teléfonos se guardan sin formato, Lote 162); con «+prefijo» o 11 dígitos que empiezan en 1 se quita el prefijo.

## Lo que todavía no busca
- **Avisos de llegada** (su `Number` aún no existe; lo hace otra sesión) y **números de serie / lotes** (por ahora fuera, a pedido de Luis).
- **Número de parte** es el SKU del producto (no hay otro campo).
- Los subtítulos de los resultados salen en español («Conduce…», «Empaque…»), también con la interfaz en inglés.
- Si ya se está en una lista (por ejemplo Reclamaciones) y se elige otro resultado que abre la misma lista, el filtro nuevo no se aplica hasta volver a entrar.

## Casos frecuentes
| Pregunta | Respuesta |
|---|---|
| «Tecleé el número de un envío y no sale.» | Revise que empiece igual (no se busca por el final del número) y que su usuario tenga `orders.view` y el módulo Transporte. |
| «Tecleé el número completo y no se abrió solo.» | Se abre solo con un único resultado exacto. Si hay otro envío (de otro cliente) con el mismo número, se muestran los dos para escoger. |
| «Busqué un teléfono y no aparece.» | Escríbalo completo (7 dígitos o más) y revise que el contacto esté activo y que su usuario pueda ver al dueño (cliente, consignatario, envío). |
