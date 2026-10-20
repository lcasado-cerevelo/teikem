# Lote 153 — Portal del cliente de almacén: servidor (lectura) (2026-10-20)

## Qué se construyó
- **`/api/v1/portal/warehouse/*`** (módulos `CLIENT_PORTAL` y `WMS_LOTSERIAL`), solo lectura y limitado al cliente de la sesión (`cid`) además de la compañía: **`stock`** (existencias por producto, lote y almacén, con las posiciones sumadas: el cliente no ve la estantería), **`products`** (sus productos con lo que hay en total), **`movements`** (su Kárdex: entradas, salidas y ajustes, sin posición ni usuario) y **`valuation`** (el valor de su inventario con la valuación del Lote 148; es la base del seguro).
- **`GET /portal/me`** trae ahora `capabilities`: nombre de la compañía, si tiene el módulo de almacén (el menú del portal muestra Inventario, Productos y Movimientos) y si el acceso puede ver el valor. Una persona con un acceso en una compañía de almacén y otro en una de transporte ve en cada sesión solo lo que esa compañía ofrece (la sesión es de una compañía y un cliente; cambiar de acceso ya existía: `auth/switch-client`).
- **Valor del inventario:** lo ven los accesos **ADMIN** y **OPERADOR** del cliente; el de **solo lectura** recibe 403 «Su acceso no incluye ver el valor del inventario.».
- **Sin oráculo:** el inventario de otro cliente o el propio del operador no aparece, ni buscándolo. El consignatario con acceso propio sigue sin entrar (la regla del portal solo le deja `/portal/consignee`).

## Cómo se probó
- xunit `PortalWarehouseTests` (6): solo mis productos y posiciones sumadas, productos activos/inactivos, movimientos propios, valor por rol (y totales), menú según el módulo, sesión sin cliente rechazada. Suite completa .NET verde.

## Decisiones a revisar
1. **El valor lo decide el rol** (ADMIN/OPERADOR sí, solo lectura no), no un permiso propio por acceso. Si se quiere decidir persona por persona, hace falta un permiso del portal.
2. **No hay posiciones ni lotes de otros** en lo que ve el cliente; sí ve el lote y su vencimiento de lo suyo.
3. Falta (siguientes lotes): las pantallas en `app-cliente` (menú por compañía, selector de acceso), y que el cliente cree productos, órdenes de recibo y avisos de salida según los ajustes; el certificado del valor y el cierre diario quedan para cuando la aseguradora los pida.
