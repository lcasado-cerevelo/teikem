# Lote F8a — Menú completo de la maqueta, Sistema, Análisis y Pulso organizable (frontend)

Fecha: 2026-09-28 (v2, con las respuestas de Luis). Estado: **propuesta para aprobación de Luis**. Nada de esto está
construido.

Decisiones de Luis que fija esta versión:

- Almacén conserva la partición de F6; solo "Recepción" pasa a llamarse "Recibo".
- **Nada de Clientes ni Consignatarios** (ni Choferes ni Flota) en este lote: lo que se construye es lo de almacén y los
  **catálogos de valores**. Esas pantallas del grupo Catálogo aparecen en el menú como *pendientes*.
- **Todos los menús exactamente como la maqueta** (7 grupos, todos los ítems, orden y nombres). Lo que no tiene función
  todavía muestra una pantalla "pendiente de implementar". **Todo ítem lleva permiso y módulo** (Parte 2, P0).
- Pulso organizable en **dos niveles**: el de la compañía (lo ve todo el mundo) y el de cada usuario (lo que él escoja).
- Aparatos móviles: **el mínimo**.
- Paleta de comandos, reloj y tema: como la maqueta, en este lote.

---

## Parte 1 — Cotejo maqueta vs. aplicación (resumen; el detalle está en el historial de este archivo, commit 9f72288)

- La maqueta tiene 7 grupos: Operación, Almacén, Contabilidad, Catálogo, Análisis, Sistema, Portal de clientes. La app
  define 5, en otro orden, con "Administración" en vez de "Sistema", y como Catálogo, Análisis y Administración no tienen
  pantallas, **solo se ven 2 grupos**. Eso es lo que hace que "no se vea igual".
- Cabecera: faltan la barra "Buscar o ejecutar…" (paleta, atajo `/`), el reloj "● en vivo" y el botón de tema
  claro/oscuro (el CSS claro ya existe en `styles/tokens.css`; falta el botón).
- Pulso: en la maqueta lo que sale se decide con el switch **"Mostrar en Pulso"** de las pantallas Indicadores y Gráficos,
  que no existen; no hay orden por usuario (el backend no lo guarda); Almacén y Actividad reciente están fijos en código;
  la maqueta usa "ríos" (`.streamlabel` + `.river` + `.node`, azul cantidad / naranja dinero), la app tarjetas neutras.
- Sistema en la maqueta: Impresoras y labels, Roles y usuarios, Integraciones / API, Seguridad y auditoría, Ajustes de la
  compañía. No previó Aparatos, PIN ni Catálogos de valores (Lote 8A es posterior).

### Qué son los "ríos de Operación" (pregunta de Luis)

En la maqueta, lo primero que sale en Pulso del día son dos cadenas horizontales de nodos con número y sparkline:

1. **"Paquetes en la calle"** (azul): Entrada → En almacén → Por despachar → En ruta → Entregado. Son las **órdenes de
   transporte de hoy contadas por etapa** (cuántas entraron, cuántas están escaneadas en almacén, cuántas sin chofer, cuántas
   en ruta, cuántas entregadas).
2. **"Dinero COD de regreso"** (naranja): el efectivo cobrado en la calle en sus etapas hasta remesarlo al cliente.

Son de **Órdenes / Despacho (F3, F5) y COD (7C)**, no de almacén. Como Luis quiere ahora solo lo de almacén, **F8a no los
construye**: el Pulso de F8a arranca con "Tus indicadores" (que ya incluye los de almacén sembrados en 7A) y deja el hueco
arriba para que F3/7C solo agreguen su río. Si Luis prefiere ver desde ya el río de órdenes con los conteos reales (el
backend de Órdenes existe), se agrega como pieza aparte en F3.

---

## Parte 2 — Diseño de F8a

**Objetivo**: menú y cabecera idénticos a la maqueta, con pantalla "pendiente" y permiso en cada ítem; Pulso organizable en
dos niveles; Indicadores y Gráficos; Roles y usuarios con PIN de la app; Aparatos (mínimo); Catálogos de valores. Todo con
el API existente salvo P1.

### P0 — Menú completo de la maqueta, pantalla "pendiente" y cabecera (orden 2, agente: core)

**Grupos** (`navigation.ts`, en este orden y con estas etiquetas es/en de la maqueta): `ops` Operación · `warehouse`
Almacén · `money` Contabilidad · `catalog` Catálogo · `analytics` Análisis · `system` Sistema · `portal` Portal de
clientes. Un grupo se pinta si tiene al menos un ítem visible para el usuario (permiso + módulo), igual que hoy.

**Ítems** (todos, en el orden de la maqueta). "Pendiente" = ruta real con la pantalla `Placeholder` (ya existe en
`app/Placeholder.tsx`; se le pone el texto de la maqueta: título y subtítulo del ítem + "Esta pantalla llega en un lote
posterior" + botón "Abrir otra pantalla" que abre la paleta). Permiso y módulo con los **códigos que ya existen** en
`PermissionCatalog` y `ModuleKeys` (sin cambios de backend):

| Grupo | Ítem (maqueta) | Ruta | Estado en F8a | Permiso | Módulo |
|---|---|---|---|---|---|
| Operación | Pulso del día | `/` | existe (se rehace en P2) | — | — |
| | Órdenes | `/orders` | existe (consulta F6) | `orders.view` | `LTL_GROUND` |
| | Estación de escaneo | `/ops/scan` | pendiente (F5) | `trips.scan` | `LTL_GROUND` |
| | Sala de despacho | `/ops/dispatch` | pendiente (F5) | `trips.dispatch` | `LTL_GROUND` |
| | Monitoreo de ruta | `/ops/monitor` | pendiente (F5) | `trips.view` | `LTL_GROUND` |
| Almacén | Almacenes · Productos · Inventario · **Recibo** (antes Recepción) · Tareas de almacén · Conteo cíclico · Recolección y empaque · Proveedores · Órdenes de compra · Citas de muelle · Cruce de muelle | como hoy | existen (F6) | como hoy | como hoy |
| Contabilidad | Contabilización de compras | `/money/purchases` | pendiente (7C) | `purchasing.view` | `PURCHASING` |
| | Contabilización de despachos | `/money/dispatch` | pendiente (7C) | `billing.export` | `LTL_GROUND` |
| | Procesar entregas | `/money/cod` | pendiente (7C) | `cod.view` | `COD` |
| | Facturación | `/money/billing` | pendiente (7C) | `billing.generate` | `LTL_GROUND` |
| | Liquidación a choferes | `/money/driver-pay` | pendiente (7C) | `driverpay.view` | `LTL_GROUND` |
| Catálogo | Clientes y contratos | `/catalog/clients` | pendiente (F2) | `clients.read` | `CATALOG` |
| | Consignatarios | `/catalog/consignees` | pendiente (F2) | `locations.read` | `CATALOG` |
| | Choferes y tarifas | `/catalog/drivers` | pendiente (F4) | `fleet.view` | `CATALOG` |
| | Flota y mantenimiento | `/catalog/fleet` | pendiente (F4) | `fleet.view` | `CATALOG` |
| Análisis | Vistas e informes | `/analytics/reports` | pendiente (F8b) | `analytics.view` | `ANALYTICS` |
| | Campos personalizados | `/analytics/custom-fields` | pendiente (F8b) | `admin.customfields` | `CUSTOM_FIELDS` |
| | Indicadores | `/analytics/indicators` | **F8a P3** | `analytics.view` | `ANALYTICS` |
| | Gráficos | `/analytics/charts` | **F8a P3** | `analytics.view` | `ANALYTICS` |
| Sistema | Impresoras y labels | `/system/printers` | pendiente (sin backend) | `admin.tenant` | `SYSTEM` |
| | Roles y usuarios | `/system/users` | **F8a P4** | `admin.users` o `admin.roles` | `SYSTEM` |
| | Aparatos móviles *(nuevo, no está en la maqueta)* | `/system/devices` | **F8a P5** | `devices.manage` | `WMS_LOTSERIAL` |
| | Catálogos de valores *(nuevo)* | `/system/catalogs` | **F8a P6** | `admin.catalogs` | `SYSTEM` |
| | Integraciones / API | `/system/integrations` | pendiente (sin backend) | `admin.tenant` | `SYSTEM` |
| | Seguridad y auditoría | `/system/audit` | pendiente (F8b) | `admin.audit` | `SYSTEM` |
| | Ajustes de la compañía | `/system/settings` | pendiente (F8b) | `admin.tenant` | `SYSTEM` |
| Portal de clientes | Página principal · Entrada de órdenes · Consignatarios · Configuración · Perfil | `/portal/*` | pendientes (sin backend) | `portalusers.manage` | `CLIENT_PORTAL` |

Los ítems pendientes también se buscan desde la paleta. La i18n del menú usa los textos `nav.*` de la maqueta (título y
subtítulo; el subtítulo sale en la paleta y en la pantalla pendiente).

**Cabecera** (como la maqueta): barra **"Buscar o ejecutar…"** con `kbd /` (paleta: `kernel/ui/CommandPalette.tsx`,
atajos `/` y `Ctrl/⌘+K`, ítems visibles agrupados por grupo, filtro sin acentos, flechas + Enter navega, Esc cierra; en
móvil, icono de lupa); **"● en vivo · HH:MM:SS"** (oculto bajo 600 px); selector de compañía (como hoy); **segmento tema
☀/🌙** (`data-theme` en `<html>`, `localStorage` `teikem.theme`, inicial `prefers-color-scheme`, sin desmontar nada);
idioma (como hoy); avatar con iniciales → Mi cuenta; Salir (se mantiene, la maqueta no lo resolvió).

Pruebas: `visibleNav` con los 7 grupos y los ítems pendientes por permiso/módulo; paleta filtra y navega; tema persiste;
`Placeholder` muestra título/subtítulo del ítem. `KIT.md` documenta la paleta y cómo declarar un ítem pendiente.

### P1 — Backend mínimo: Pulso en dos niveles (orden 1, agente: implementer)

Hoy el backend ya guarda **por compañía** `ShowInPulse` y `SortOrder` en cada definición de indicador/gráfico
(`analytics.manage`) y **por usuario** `ShowInPulse` y rango en `UserAnalyticsPreference`. Falta el orden por usuario y los
paneles fijos. Cambio aditivo:

1. `Diseño/logistica-db-estructura.sql`: columna `PulseSortOrder INT NULL` en `dbo.UserAnalyticsPreference`. Tabla nueva
   `dbo.PulsePanelSetting (PulsePanelSettingId INT IDENTITY PK, TenantId INT NOT NULL FK Tenant, UserId INT NULL FK
   AspNetUsers, PanelKey NVARCHAR(40) NOT NULL, IsVisible BIT NOT NULL DEFAULT 1, SortOrder INT NOT NULL DEFAULT 0,
   UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME())` con índice único filtrado por (TenantId, PanelKey) cuando
   `UserId IS NULL` (nivel compañía) y por (TenantId, UserId, PanelKey) cuando no (nivel usuario). `ITenantScoped`. Sin seed.
2. Claves de panel en `Teikem.Domain`: `WAREHOUSE`, `ACTIVITY` (los lotes futuros agregan `ORDERS_RIVER`, `COD_RIVER`…).
3. `GET /api/v1/analytics/pulse` → `PulseDto(Indicators, Charts, Panels, HasPersonalLayout)`. Regla de resolución por
   elemento: **preferencia del usuario si existe; si no, la de la compañía** (definición para indicadores/gráficos; fila con
   `UserId NULL` para paneles; sin fila = visible, al final). Orden efectivo: `PulseSortOrder` del usuario → `SortOrder` de la
   compañía → nombre. Cada elemento lleva `sortOrder` efectivo y `source: "user" | "company"`.
4. `PUT /api/v1/analytics/pulse/layout?scope=mine|company`. Cuerpo `{ items: [{ kind: "indicator"|"chart", id, sortOrder,
   showInPulse }], panels: [{ key, isVisible, sortOrder }] }`. `mine` (`analytics.view`): escribe `UserAnalyticsPreference`
   y filas de `PulsePanelSetting` del usuario. `company` (`analytics.manage`): escribe `ShowInPulse`/`SortOrder` de las
   definiciones y filas con `UserId NULL`. Clave de panel desconocida → 400 "Panel de Pulso desconocido: {key}"; indicador que
   el usuario no puede ver → 404. Devuelve el `PulseDto` nuevo.
5. `DELETE /api/v1/analytics/pulse/layout/mine` (`analytics.view`): borra las preferencias de orden/ocultos del usuario
   (no el rango) → vuelve al de la compañía.
6. Pruebas xunit (usuario con orden parcial, sin orden, panel oculto por compañía y mostrado por usuario, restablecer);
   `scripts/smoke.sh` con los tres endpoints; manual de análisis + FAQ ("¿Por qué mi Pulso no se ve como el de otro
   usuario?", "¿Cómo vuelvo al Pulso de la compañía?").

### P2 — Pulso del día a la maqueta + Organizar (orden 3, agente: core)

- Título = **fecha del día** en el idioma activo (capitalizada), saludo como subtítulo. Botones a la derecha: **"Organizar mi
  Pulso"** (`analytics.view`) y **"Organizar el de la compañía"** (`analytics.manage`).
- Secciones en el orden efectivo que devuelve `GET /pulse`, solo si tienen contenido: *(hueco para los ríos de Operación)*
  → **"Tus indicadores"** como río (`.streamlabel` + `.river` + `.node flow|money` según `isMoney`; nodo con icono del
  módulo, nombre, valor, rango; botón "Rango"; clic → Indicadores) → **"Tus gráficos"** (grilla 2 columnas / 1 en móvil,
  panel compacto, título → Gráficos) → **Almacén** (F7A tal cual) → **Actividad reciente** (F7A tal cual). Los paneles se
  intercalan según su `sortOrder`.
- Chip discreto bajo el título: "Pulso personal" (con enlace "Volver al de la compañía" → `DELETE layout/mine` +
  `ConfirmDialog`) o "Pulso de la compañía".
- **Modo Organizar** (los dos niveles usan la misma interfaz, cambia el `scope`): cada nodo, gráfico y panel muestra ▲ ▼ y
  ojo (ocultar); arrastrar con el ratón en escritorio (HTML5 drag, sin librería), solo botones en táctil; ocultos al final
  en gris con "Mostrar". "Listo" → un solo `PUT layout`; "Cancelar" descarta. Toast "Pulso guardado". En modo compañía, un
  aviso arriba: "Esto lo verá todo el mundo que no tenga un Pulso personal".
- Vacío: "Aún no tienes indicadores ni gráficos en tu Pulso" con enlace a Indicadores; Almacén y Actividad siguen saliendo.
- Responsive como la maqueta: río envuelve bajo 980 px, una columna a 360 px, sin scroll horizontal.
- Pruebas: orden efectivo, ocultar/mostrar, cuerpo del `PUT` por `scope`, restablecer, vacío.

### P3 — Indicadores y Gráficos (grupo Análisis) (orden 3, agente: pantalla)

Como la maqueta (`indicadoresScreen`/`graficosScreen`): cabecera con "Nuevo indicador"/"Nuevo gráfico" (`analytics.manage`);
paneles **por módulo de negocio** (`businessModule`, etiqueta del catálogo `BusinessModule`) con contador; tarjetas en 3
columnas (1 en móvil) con nombre, valor actual (`GET indicators/{id}/value` / Recharts con `charts/{id}/data`), rango, **switch
"Mostrar en Pulso"** (`PUT …/my-pulse`, por usuario; con `analytics.manage` un segundo switch "…en el de la compañía" →
`PUT` de la definición), botón "Rango" (`RangeModal`), chip de visibilidad, editar y eliminar (`analytics.manage`,
`ConfirmDialog`). **Editor** (modal del kit): nombre es/en, fuente (`GET data-sources`), campo y agregación, filtro con filas
campo · operador · valor (mismo JSON que valida el API; DSL de `kernel/dsl`), módulo de negocio, es dinero, visibilidad,
rango por defecto, orden en el Pulso de la compañía, mostrar en Pulso por defecto; gráficos añaden agrupar por y tipo
(barra, dona, línea). Errores por campo al formulario. Nota al pie de la maqueta.

### P4 — Roles y usuarios + PIN de la app (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/users` (`admin.users` o `admin.roles`; el segmento **Roles | Usuarios** muestra solo lo permitido).
Exactamente la maqueta (`usuariosScreen`):

- **Roles**: tabla ordenable Rol · Permisos (n / total) · Usuarios · editar/eliminar; chips "sistema"/"plantilla"; eliminar
  bloqueado con usuarios (mensaje del API). Modal Nuevo/Editar rol: nombre y descripción es/en, casillas de permisos
  **agrupadas por categoría** (`PermissionDto.category`, icono del grupo del menú, "marcar grupo"). `PUT/DELETE` con reauth
  AAL2 del kit. Esto es lo que controla los menús: un usuario solo ve los ítems cuyo permiso tiene.
- **Usuarios**: `GET users` paginado con `QBox`. Nombre · Correo · Roles (chips → modal multi-selección, `PUT {id}/roles`,
  AAL2) · Permisos extra ("+n"/"Añadir" → modal con los del rol bloqueados "(del rol)", `PUT {id}/permissions`, AAL2) ·
  Estado (switch Activo/Suspendido → `PUT {id}/membership`) · MFA · Último acceso · **PIN app** (solo con módulo
  `WMS_LOTSERIAL` y `devices.manage` o `admin.users`): chip Sí/No (`hasPin`), "Asignar/Restablecer PIN" (modal con PIN y
  confirmación → `PUT {id}/pin`, AAL2, mensajes exactos del API, p. ej. "El PIN no puede ser una secuencia trivial."), "Quitar
  PIN" (`DELETE {id}/pin`, `ConfirmDialog`). "Cerrar sesiones" (`DELETE {id}/sessions`). **"Nuevo usuario"** (correo, nombre,
  roles, contraseña opcional → `POST users`); editar nombre/activo (`PUT {id}`).
- **Mi cuenta**: pestaña **"PIN de la app"** (`GET/PUT/DELETE /me/pin`, pide la contraseña actual) solo con `WMS_LOTSERIAL`.
- Fuera: alcances de datos (`data-scopes`) → F8b.

### P5 — Aparatos móviles, mínimo (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/devices` (`devices.manage`, `WMS_LOTSERIAL`). Una pantalla: tabla ordenable Código · Nombre · Almacén por
defecto · Estado · Último latido (`GET devices`, `QBox`); botón **"Nuevo aparato"** (nombre, almacén por defecto) → al
guardar, modal **"Código de registro"** con el código en grande (mono), botón copiar y el texto "Escríbelo en la pantalla
Registrar de Teikem Almacén" (solo se muestra una vez); acciones por fila: **desactivar / reactivar** (`ConfirmDialog`) y
**"Nuevo código de registro"** (`POST …/enroll-code`, mismo modal). Sin edición de modelo/tema ni ficha (queda para cuando
haga falta).

### P6 — Catálogos de valores (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/catalogs` (`admin.catalogs`). Maestro-detalle con el lenguaje de la maqueta (`.panel/.ph2/.pb`, tabla
`.lst`): izquierda lista de dominios (`GET catalogs/domains`, `QBox`, chip sistema/propio, "Nueva lista" → `POST
catalogs/lists`, eliminar lista propia); derecha tabla ordenable Código · Etiqueta (es) · Etiqueta (en) · Orden ·
Habilitado · Origen (Sistema / Ajustado / Propio). Valor de sistema: "Ajustar" (etiquetas, habilitado, orden → `PUT
{entity}/{code}/override`) y "Restaurar" (`DELETE …/override`). Valor propio: crear, editar, desactivar (soft), restaurar.
Al guardar, invalida la caché de `kernel/catalogs` (el resto de la app ve la etiqueta nueva sin recargar). Móvil: dominios
como selector arriba, valores en tarjetas.

### Recorrido Playwright (`web-app/e2e/f8a.spec.ts`)

1. `teikem+admin@cerevelo.com` → el menú muestra los 7 grupos con todos los ítems de la maqueta; "Sala de despacho" abre la
   pantalla pendiente con su título; `/` abre la paleta; "usu" + Enter lleva a Roles y usuarios; el tema cambia y persiste.
2. `teikem+dispatch@cerevelo.com` → no ve Sistema (sin `admin.*`) ni Aparatos.
3. Análisis → Indicadores: apagar "Mostrar en Pulso" (mío) de "Productos bajo mínimo" → Pulso no lo muestra; encender → vuelve.
4. Pulso → "Organizar mi Pulso": mover Actividad reciente arriba, ocultar Almacén, Listo; recargar: se conserva;
   `teikem+dispatch` no lo ve así. "Volver al de la compañía" → orden original. "Organizar el de la compañía": ocultar un
   gráfico → `dispatch` tampoco lo ve.
5. Sistema → Roles y usuarios: rol "Auditor E2E" con 2 permisos (reauth); usuario `e2e+{ts}@cerevelo.com` con ese rol;
   PIN `2846` (reauth) → Sí; `1234` → "El PIN no puede ser una secuencia trivial."; quitar → No; suspender → Suspendido.
   Entrar con ese usuario: el menú solo muestra lo que sus 2 permisos permiten.
6. Sistema → Aparatos: "Tablet E2E" → código de registro; "Nuevo código" → otro; desactivar/reactivar.
7. Sistema → Catálogos: ajustar la etiqueta española de un `WarehouseTaskType`; el panel Almacén del Pulso la muestra sin
   recargar; restaurar.
8. Mi cuenta → PIN de la app: fijar con contraseña; quitar.
9. Proyecto móvil (Pixel 7): Pulso, Roles y usuarios y Catálogos sin scroll horizontal; paleta desde la lupa.

### Cierre

`npm run check` y Playwright verdes en CI; `docs/frontend/loteF8a-decisiones.md`; capítulo
`docs/manual/frontend/f8a-menu-sistema-analisis-y-pulso.md` con capturas; FAQ con cada mensaje de error del lote.

---

## Parte 3 — Lo que queda para después (sin retroceder nada)

- **F8b**: Vistas e informes, Campos personalizados, Estatus (overrides, capacidades, entradas laterales), Seguridad y
  auditoría, Ajustes de la compañía (módulos, settings, feriados), alcances de datos, Administración de plataforma.
- **En sus lotes**: F2 Clientes/Consignatarios, F3 Órdenes (+ río "Paquetes en la calle"), F4 Flota/Choferes, F5
  Despacho/Monitoreo/Escaneo; 7B y 7C (+ río COD); prueba de campo de la app 8A. Cada uno **sustituye una pantalla
  pendiente por la real** sin tocar el menú.
- **Sin backend**: Impresoras y labels, Integraciones / API, colores y logo, Portal de clientes.

---

## Parte 4 — Lo que queda por confirmar

1. **Ríos de Operación** (explicados en la Parte 1): fuera de F8a, llegan con F3 y 7C. Si Luis los quiere ya, el de órdenes
   se puede hacer con el backend actual como pieza aparte.
2. **Dónde va "Catálogos de valores"**: propuesto en **Sistema** (son configuración: tipos de tarea, motivos de ajuste,
   unidades…). El grupo **Catálogo** de la maqueta queda con sus 4 ítems pendientes (clientes, consignatarios, choferes,
   flota). Si Luis prefiere ver "Catálogos de valores" dentro del grupo Catálogo, es un cambio de una línea.
3. **Mini-mock** de las tres pantallas sin maqueta (Aparatos, PIN, Catálogos de valores) antes de codificar: lo hago salvo
   que Luis diga que no hace falta.
