# Lote 220 — Datos de la compañía y «Nueva compañía» desde Ajustes (2026-10-26)

## Pedido de Luis
- El **botón «Nueva»** en la misma pantalla de información de la compañía (no una pantalla aparte), **solo para el administrador de la plataforma** (no para el de la compañía).
- La pantalla (nueva o editando) lleva lo que no tenía: **nombre, dominio, activar/desactivar, domicilio**. El domicilio es **una localización como cualquier otra** y además **dirección postal** (shipping y billing), con textos en **español e inglés**.
- Un **teléfono** de la compañía (para cartas y documentos).
- **Quitar de todos lados** (base de datos, API, todo) lo que no se usa: los **días de sesión**. «No quiero basura por ahí en ninguna parte.»
- Al crear: una **ventana modal con la información base** (nombre, teléfono, domicilio, dominio, subdominio); al aceptar **sale con esa compañía marcada**, con los **módulos base** (ni almacén, ni transporte, ni facturación), y la plataforma termina de configurarla ahí mismo.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base de datos (`logistica-db-update.sql`, sección 2026-10-26) | `Tenant.PhoneNumber`, `CorporateLocationId`, `BillingLocationId` (FK a `Location`); tipos de localización `COMPANY` y `COMPANY_POSTAL` (los `CORPORATE`/`BILLING` son de un cliente y no pueden ser compartidos); se **copian los días de sesión a minutos** donde estaban sin configurar y se **borran** `SessionDays`, `DeviceSessionDays`, `TrackingSlug` (con su índice y defaults). Idempotente. |
| Servidor | Teléfono y direcciones en `GET/PUT /tenant/settings` (`phoneNumber`, `address`, `postalAddress`, `postalSameAsAddress`); nombre solo para plataforma (403) y sin repetir (409); `PUT /platform/tenants/{id}/active` (desactivar cierra las sesiones de las demás personas y la API responde 403 «La compañía está inactiva.»); `POST /platform/tenants` pide teléfono y domicilio, nace con **módulos base** (`SYSTEM`, `CATALOG`, `ANALYTICS`, `CUSTOM_FIELDS`) y el administrador inicial es opcional; la plataforma puede entrar a una compañía desactivada (selector «(desactivada)»). Sesiones: solo minutos (30 días sin configurar). |
| Web | Ajustes → General: nombre (editable para plataforma), teléfono, domicilio, postal (misma/otra), panel «Solo la plataforma» (dominio, subdominio, activar/desactivar con confirmación); botón **Nueva compañía** + ventana; al crear entra a la compañía nueva. es/en. |
| Humo y pruebas | 17 pruebas nuevas del servidor, 6 de la pantalla, un bloque nuevo en `smoke.sh` y el recorrido Playwright `compania-nueva.spec.ts`. |

## Decisiones tomadas (revisar)
1. **Tipos de localización propios** (`COMPANY`, `COMPANY_POSTAL`): las reglas de `Location` obligan a que `CORPORATE`/`BILLING` tengan cliente; así el domicilio de la compañía no se confunde con los de un cliente ni sale como almacén, recogida o consignatario.
2. **Teléfono obligatorio solo al crear** (las compañías de antes no lo tienen y no se bloquea guardar otros ajustes). 7 a 15 dígitos, texto libre con espacios, guiones, paréntesis, `+` y `ext`.
3. **Módulos base = Sistema, Catálogo, Análisis y Campos personalizados.** Transporte (`LTL_GROUND`) deja de encenderse solo; es «núcleo» (no se puede apagar una vez encendido) pero una compañía nueva no lo trae hasta que la plataforma lo enciende. **Las importaciones (Colavora, legado) y la compañía demo siguen como siempre** (con transporte y sin teléfono/domicilio): usan `fromScreen: false`.
4. **Administrador inicial opcional**: la plataforma opera cualquier compañía; los usuarios se agregan después.
5. **Desactivar no borra nada**; cierra las sesiones de **las demás personas** (no la de quien lo hace, para poder reactivar) y la API corta a quien ya tenía sesión (memoria de 15 s por servidor; al activar o desactivar se olvida al instante en ese servidor).
6. **`TrackingSlug` también se quitó** (estaba muerto desde el Lote 116: nadie lo leía ni lo escribía).
7. **Cambiar el nombre** actualiza también el nombre de sus dos localizaciones.

## Cómo se probó
- **Servidor** (xunit, 17 nuevas en `TenantCompanyTests`): reglas de teléfono y dirección con sus mensajes, domicilio y postal (crear, corregir la misma fila, quitar sin borrar), nombre solo plataforma y sin repetir (y las localizaciones toman el nombre nuevo), validaciones del alta antes de tocar nada, módulos base, desactivar/activar (sesiones, memoria).
- **Humo corto contra el API y SQL Server reales** (el bloque nuevo de `smoke.sh`): la compañía nace sin transporte y con sus datos, editar teléfono/postal, 400 con campo, 403 del nombre, alta sin teléfono o sin domicilio 400, desactivar → 403 «La compañía está inactiva.», login 403, reactivar.
- **Web** (vitest, 6 nuevas): nombre bloqueado sin plataforma, guardar nombre/teléfono/domicilio/postal, `postalSameAsAddress`, error 400 del teléfono, dominio, desactivar con confirmación y reactivar, «Nueva compañía» (obligatorios, cuerpo del alta, entra a la compañía).
- **Playwright con el API real**: ver `compania-nueva.spec.ts` (alta, entrada automática, edición, desactivar/activar, selector «(desactivada)»; capturas en `docs/manual/frontend/img/compania-*.png`).
- **No probado**: el correo de un administrador inicial (no se crea uno en el flujo de la pantalla); el selector de compañías con muchas compañías; la app móvil/portal (no leen estos campos).

## Pendiente / para decidir
- ¿La pantalla debería ofrecer «copiar la configuración de otra compañía» al crear? (hoy nace en blanco).
- ¿Se agrega un administrador inicial opcional en la ventana? (el API ya lo acepta).
- Los datos de producción: correr `db-update` (con simulación y respaldo) antes de usar esta versión; la sección **borra tres columnas** (`SessionDays`, `DeviceSessionDays`, `TrackingSlug`).
