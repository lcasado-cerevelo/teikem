# Lote F8a — Control de punta a punta por permisos: menú completo de la maqueta, Pulso por paneles, Sistema, Análisis y marca

Fecha: 2026-09-28 (v3, diseño de detalle). Estado: **aprobado en principio por Luis; pendiente de su "ejecuta"**. Nada de esto está
construido. Se ejecutará en una sesión nueva, en local, con el texto de arranque de `docs/frontend/loteF8-arranque.md`.

## 0. Principio rector: yo decido qué ve cada usuario, de punta a punta

Luis quiere sacar **almacén** al frente ya, pero con una solución **final y escalable**: que todo lo que se ve en la aplicación
(grupos y elementos del menú, pantallas, y cada parte del Pulso del día) esté gobernado por permisos y módulos, para poder
configurar, por ejemplo, un usuario de almacén que **solo vea lo de almacén**. La cadena de control queda así, y cada eslabón ya
existe o lo construye este lote:

| Eslabón | Qué controla | Dónde se configura | Estado |
|---|---|---|---|
| 1. Módulos del tenant | Qué funcionalidad está encendida para la compañía (`WMS_LOTSERIAL`, `LTL_GROUND`, `COD`, `ANALYTICS`, `SYSTEM`…). Un módulo apagado oculta sus elementos del menú y sus paneles del Pulso a **todos**. | `PUT /modules/{key}` (`admin.tenant`, AAL2) — pantalla en F8b; hoy por API/seed. | existe |
| 2. Roles | Conjuntos de permisos reutilizables (`recurso.accion`). Seis plantillas de sistema + los que cree la compañía. | **F8a P4** (Roles y usuarios). | backend existe; pantalla en F8a |
| 3. Permisos extra por usuario | Excepción puntual sin crear un rol. | **F8a P4**. | backend existe; pantalla en F8a |
| 4. Menú y pantallas | Cada elemento del menú y cada ruta declara `perm` + `module`; sin permiso no se pinta ni se entra (`Sin permiso`), con módulo apagado, `Módulo apagado`. | `routes.tsx` (**F8a P0** completa la tabla con todos los ítems de la maqueta). | existe el mecanismo; F8a lo aplica a todo |
| 5. Pulso del día por paneles | **Cada panel del Pulso tiene su propio permiso** (`pulse.*`) y módulo; cada indicador o gráfico exige además poder leer su fuente de datos. El servidor solo devuelve lo que el usuario puede ver. | **F8a P1** (registro de paneles + permisos) y **P2** (pantalla). | nuevo |
| 6. Orden y visibilidad del Pulso | Orden y ocultos **por compañía** (`pulse.organize_company`) y **por usuario** (cada quien el suyo). | **F8a P1 + P2**. | nuevo |
| 7. Visibilidad de cada indicador/gráfico | Privado / compartido con personas / toda la compañía, más "Mostrar en Pulso" por defecto y por usuario. | **F8a P3** (Indicadores y Gráficos). | backend existe; pantalla en F8a |

**Receta "usuario que solo ve almacén"** (lo que Luis podrá hacer al cerrar F8a, sin tocar código):

1. Sistema → Roles y usuarios → Roles: usar la plantilla **Operador de almacén** (o crear "Almacén solo lectura") y dejarle solo
   permisos de las categorías Almacén (`inventory.view`, `warehouse.receive`, `warehouse.pick`, `warehouse.count`…) y del
   Pulso (`pulse.warehouse`, `pulse.indicators`, `pulse.charts`, `pulse.activity`) y `analytics.view` (decisión de Luis en la
   ejecución: sin él, la política de `/analytics/activity` bloquea el panel Actividad reciente antes de llegar al filtro por
   módulo — la plantilla Operador de almacén ya lo trae). Quitar `orders.view`, `trips.*`, `cod.*`.
2. Usuarios → Nuevo usuario → correo, nombre, ese rol; opcionalmente PIN de la app.
3. Resultado: el menú muestra Operación → Pulso del día y el grupo Almacén; nada de Contabilidad, Catálogo, Análisis, Sistema
   ni Portal (sus ítems exigen permisos que no tiene). El Pulso muestra el panel Almacén, Actividad reciente (solo eventos de
   almacén, como ya hace 7A), y los indicadores y gráficos **cuya fuente de datos sea de almacén** (`STOCK_BALANCE`,
   `INVENTORY_TRANSACTION`, `RECEIPT`…); un indicador sobre órdenes no aparece aunque esté marcado para el Pulso de la
   compañía, porque exige `orders.view`.

Todo lote posterior (F2 a F5, 7C, F8b) **se enchufa a esta cadena sin diseñarla de nuevo**: un ítem de menú = una fila en
`routes.tsx` con su permiso; un panel del Pulso = una entrada en el registro de paneles con su permiso `pulse.*`.

---

## 1. Cotejo maqueta vs. aplicación (resumen; el detalle está en el historial de este archivo, commit 9f72288)

- La maqueta tiene 7 grupos (Operación, Almacén, Contabilidad, Catálogo, Análisis, Sistema, Portal de clientes). La app define
  5, en otro orden, "Administración" en vez de "Sistema", y como Catálogo, Análisis y Administración no tienen pantallas, solo se
  ven 2 grupos. Eso es lo que hace que "no se vea igual".
- Cabecera: faltan la barra "Buscar o ejecutar…" (paleta, atajo `/`), el reloj "● en vivo" y el botón de tema claro/oscuro (el CSS
  claro ya existe en `styles/tokens.css`; falta el botón). La barra lateral muestra una "T" en CSS en vez del logo de Teikem.
- Pulso: en la maqueta lo que sale se decide con el switch "Mostrar en Pulso" de Indicadores y Gráficos (pantallas que no
  existen); no hay orden por usuario; Almacén y Actividad reciente están fijos en código; la maqueta usa "ríos"
  (`.streamlabel` + `.river` + `.node`, azul cantidad / naranja dinero) y la app tarjetas neutras.
- **Ríos de Operación** (pregunta de Luis): las dos cadenas de arriba del Pulso de la maqueta. "Paquetes en la calle" = órdenes
  de transporte de hoy por etapa (Entrada → En almacén → Por despachar → En ruta → Entregado); "Dinero COD de regreso" = el
  efectivo cobrado en la calle hasta remesarlo. Son de Órdenes/Despacho (F3, F5) y COD (7C); **fuera de F8a**, pero el registro
  de paneles ya les deja el sitio (§2.3).

---

## 2. Modelo de permisos del Pulso (lo nuevo de este lote)

### 2.1 Permisos nuevos (categoría `PULSE`, "Pulso del día")

Se agregan a `PermissionCatalog` (fuente de verdad, sembrada por `PermissionSeeder`) y se espejan en
`Diseño/logistica-db-seed.sql` (bloque `#P`, categoría nueva `PULSE` en el catálogo `PermissionCategory`):

| Código | Categoría | Etiqueta es / en | Qué abre |
|---|---|---|---|
| `pulse.indicators` | PULSE | Ver indicadores en el Pulso / See indicators on the Pulse | Sección "Tus indicadores" (cada indicador exige además leer su fuente, §2.2) |
| `pulse.charts` | PULSE | Ver gráficos en el Pulso / See charts on the Pulse | Sección "Tus gráficos" (ídem) |
| `pulse.warehouse` | PULSE | Ver el panel Almacén del Pulso / See the Warehouse panel | Panel Almacén (F7A). Datos: `inventory.view`; módulo `WMS_LOTSERIAL` |
| `pulse.activity` | PULSE | Ver Actividad reciente en el Pulso / See Recent activity | Panel Actividad reciente (F7A). Datos: `analytics.view`; módulo `ANALYTICS` |
| `pulse.organize_company` | PULSE | Organizar el Pulso de la compañía / Organize the company Pulse | Botón "Organizar el de la compañía" y `PUT …/layout?scope=company` |

Organizar **mi** Pulso no exige permiso (son preferencias propias). No se crea `pulse.view`: la pantalla de inicio siempre
existe; si el usuario no tiene ningún panel, ve la bienvenida.

Plantillas de rol (`PermissionCatalog.RoleTemplates` + bloque `#RP` del seed): **TenantAdmin** todos; **WarehouseOperator**
`pulse.warehouse`, `pulse.indicators`, `pulse.charts`, `pulse.activity`; **Dispatcher**, **Billing**, **ReadOnly**
`pulse.indicators`, `pulse.charts`, `pulse.activity`; **Driver** ninguno. (`PermissionSeeder` ya agrega a las plantillas
existentes los códigos nuevos de cada versión.)

Recipe para un panel futuro (F3, F5, 7C): una constante en `PulsePanels`, una fila en `PermissionCatalog.All` (+ plantillas),
una línea en el seed, una entrada en el registro del frontend. Nada más.

### 2.2 Regla de lectura de indicadores y gráficos

Un indicador o gráfico entra en el Pulso de un usuario solo si, **todas a la vez**:

1. tiene `pulse.indicators` (o `pulse.charts`);
2. puede verlo por visibilidad (privado suyo, compartido con él, o de toda la compañía) — regla que ya aplica `GetIndicatorsAsync`;
3. **puede leer su fuente de datos**: el `EntityType` del `IDataSource` se traduce al permiso de lectura con el diccionario
   EntityType → permiso de lectura que `PermissionCatalog` ya usa para los resolvers de pertenencia (p. ej. `STOCK_BALANCE`,
   `INVENTORY_TRANSACTION`, `PRODUCT`, `RECEIPT` → `inventory.view`; `TRANSPORT_ORDER` → `orders.view`; `TRIP` → `trips.view`;
   `PURCHASE_ORDER` → `purchasing.view`). Una fuente sin `EntityType` (no debería existir) se trata como visible.
4. su módulo de negocio (`BusinessModule`: `WAREHOUSE` → `WMS_LOTSERIAL`, `OPERATIONS` → `LTL_GROUND`, `ACCOUNTING` → `COD`
   o `LTL_GROUND`) está encendido — se reutiliza el mismo mapa que ya usa `visibleModules` de Actividad reciente (7A).

La misma regla se aplica en las pantallas Indicadores y Gráficos (P3): lo que el usuario no puede leer, no se lista.

### 2.3 Registro de paneles (`PulsePanels`, en `Teikem.Domain`)

```
Key          Permiso                Permisos de datos   Módulo          Orden por defecto   Lote
INDICATORS   pulse.indicators       (por elemento)      ANALYTICS       20                  F8a
CHARTS       pulse.charts           (por elemento)      ANALYTICS       30                  F8a
WAREHOUSE    pulse.warehouse        inventory.view      WMS_LOTSERIAL   40                  F8a
ACTIVITY     pulse.activity         analytics.view      ANALYTICS       50                  F8a
-- reservados (no se declaran hasta su lote):
ORDERS_RIVER (10, F3)  COD_RIVER (11, 7C)  DECISIONS (15, F3/F5)  RADAR (60, F3)
```

"Tus indicadores" y "Tus gráficos" son **paneles** del registro (se ordenan y ocultan como bloque) y, dentro, cada indicador o
gráfico se ordena y oculta individualmente. Así el modelo es uno solo: paneles → elementos.

---

## 3. Piezas

### P1 — Backend: permisos del Pulso, registro de paneles y orden en dos niveles (orden 1, agente: implementer)

**Esquema** (`Diseño/logistica-db-estructura.sql`, capa de análisis, después de `UserAnalyticsPreference`):

```sql
ALTER TABLE dbo.UserAnalyticsPreference ADD PulseSortOrder INT NULL;   -- se edita en el CREATE TABLE, no como ALTER
CREATE TABLE dbo.PulsePanelSetting (
    PulsePanelSettingId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    UserId       INT NULL REFERENCES dbo.AspNetUsers(Id),          -- NULL = nivel compañía
    PanelKey     NVARCHAR(40) NOT NULL,
    IsVisible    BIT NOT NULL DEFAULT 1,
    SortOrder    INT NOT NULL DEFAULT 0,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UQ_PulsePanelSetting_Company ON dbo.PulsePanelSetting(TenantId, PanelKey) WHERE UserId IS NULL;
CREATE UNIQUE INDEX UQ_PulsePanelSetting_User    ON dbo.PulsePanelSetting(TenantId, UserId, PanelKey) WHERE UserId IS NOT NULL;
```

Seed (`logistica-db-seed.sql`): valor `PULSE` en `PermissionCategory` (`{"es":"Pulso del día","en":"Day pulse"}`); las 5 filas
de `#P`; las filas de `#RP` de §2.1. Sin filas de `PulsePanelSetting` (sin fila = visible, en el orden por defecto del registro).

**Dominio**: entidad `PulsePanelSetting` (`ITenantScoped`, `[AuditEntity("PULSE_PANEL_SETTING")]` → nuevo `EntityType`
`PULSE_PANEL_SETTING` en `EntityTypes` y en el seed); `PulsePanels` (registro de §2.3: `record PulsePanelDef(string Key, string
Permission, string[] DataPermissions, string? Module, int DefaultSortOrder)`); constantes nuevas en `PermissionCatalog` con su
`PermissionDef` y plantillas.

**Contratos** (`AnalyticsContracts.cs`):

```csharp
public sealed record PulsePanelDto(string Key, bool IsVisible, int SortOrder, string Source);          // Source: "user" | "company" | "default"
public sealed record IndicatorValueDto(int Id, string Name, decimal? Value, bool IsMoney, string? DateRangeMode,
    DateTime? FromUtc, DateTime? ToUtc, string BusinessModule, int SortOrder, bool IsVisible, string Source);
public sealed record ChartDataDto(…igual + BusinessModule, SortOrder, IsVisible, Source);
public sealed record PulseDto(IReadOnlyList<IndicatorValueDto> Indicators, IReadOnlyList<ChartDataDto> Charts,
    IReadOnlyList<PulsePanelDto> Panels, bool HasPersonalLayout, bool CanOrganizeCompany);
public sealed record PulseLayoutItem(string Kind, int Id, int SortOrder, bool IsVisible);                // Kind: "indicator" | "chart"
public sealed record PulseLayoutPanel(string Key, int SortOrder, bool IsVisible);
public sealed record PulseLayoutRequest(IList<PulseLayoutItem>? Items, IList<PulseLayoutPanel>? Panels);
```

`GET /api/v1/analytics/pulse` (sin permiso propio; módulo `ANALYTICS` no se exige para no romper el inicio: sin el módulo, la
lista de paneles sale vacía salvo `WAREHOUSE`): devuelve **solo** los paneles cuyo permiso + permisos de datos tiene el usuario y
cuyo módulo está encendido; dentro de `INDICATORS`/`CHARTS`, solo los elementos que pasan §2.2. Se incluyen los ocultos
(`IsVisible=false`) para que el modo Organizar pueda mostrarlos de nuevo; la pantalla no los pinta. Resolución por elemento:
preferencia del usuario si existe (`Source="user"`), si no la de la compañía (`"company"`), si no el registro/definición
(`"default"`). Orden: `SortOrder` efectivo, luego nombre. `HasPersonalLayout` = el usuario tiene alguna fila propia de orden u
ocultos. `CanOrganizeCompany` = tiene `pulse.organize_company`.

`PUT /api/v1/analytics/pulse/layout?scope=mine|company` (`scope` obligatorio; `mine` sin permiso; `company` exige
`pulse.organize_company`): idempotente; escribe solo lo que viene en el cuerpo (un panel o elemento ausente no se toca).
Validaciones (`ValidationException`, 400, campo → mensaje): `scope` → "Alcance inválido: use mine o company."; `panels[i].key`
→ "Panel de Pulso desconocido: {key}."; `items[i].kind` → "Tipo inválido: use indicator o chart."; `items[i].id` que el usuario
no puede ver → `NotFoundException` 404 "Indicador"/"Gráfico"; panel cuyo permiso no tiene el usuario → 404 (no se revela).
`mine` escribe `UserAnalyticsPreference.PulseSortOrder/ShowInPulse` y `PulsePanelSetting(UserId=yo)`; `company` escribe
`IndicatorDefinition/ChartDefinition.SortOrder/ShowInPulse` y `PulsePanelSetting(UserId=NULL)`. Devuelve el `PulseDto` nuevo.
Auditoría: las filas de `PulsePanelSetting` quedan en `AuditLog`; el cambio de compañía además genera un `SecurityEvent`? No:
basta la auditoría de entidad.

`DELETE /api/v1/analytics/pulse/layout/mine`: borra `PulseSortOrder` (y `ShowInPulse` propio) de `UserAnalyticsPreference` y
las filas propias de `PulsePanelSetting`; conserva el rango de fecha propio. 204.

`my-pulse` (existente) se mantiene y se documenta como atajo de un solo elemento.

**Servicio** (`AnalyticsService`): `GetPulseAsync` reescrito con el registro; método privado `CanReadSource(IDataSource)` para
§2.2 (usa `ICurrentUser`/permisos efectivos y el diccionario de lectura por `EntityType`); `SaveLayoutAsync(scope, req)`;
`ResetMyLayoutAsync()`. `GetIndicatorsAsync`/`GetChartsAsync` aplican también §2.2.3 y .4 (fuente legible y módulo).

**Pruebas xunit** (`tests/Teikem.Tests/Analytics/PulseLayoutTests.cs`): (1) usuario con `pulse.warehouse` sin
`inventory.view` no recibe el panel; (2) indicador sobre `TRANSPORT_ORDER` no sale para quien no tiene `orders.view` aunque esté
en el Pulso de la compañía; (3) resolución usuario > compañía > defecto y `Source`; (4) orden parcial (un elemento con
`PulseSortOrder` y el resto sin); (5) `company` sin `pulse.organize_company` → 403; (6) clave desconocida → 400 con el mensaje
exacto; (7) `DELETE mine` conserva el rango; (8) `PermissionSeeder` agrega los `pulse.*` a las plantillas.
`scripts/smoke.sh`: `PUT layout?scope=mine`, `GET pulse` (comprueba orden y `hasPersonalLayout=true`), `DELETE mine`,
`PUT layout?scope=company` con admin y `GET pulse` con `teikem+dispatch` (comprueba que el orden de compañía le aplica).
Manual: capítulo de análisis (`docs/manual/`) sección "Pulso del día: paneles, permisos y orden" con la tabla de §2.1 y §2.3, y
FAQ: "¿Por qué no veo el panel Almacén?", "¿Por qué mi Pulso no se ve como el de otro usuario?", "¿Cómo vuelvo al Pulso de la
compañía?", "Marqué un indicador para el Pulso y un usuario no lo ve".

### P0 — Menú completo de la maqueta, pantalla "pendiente", cabecera (orden 2, agente: core)

**Grupos** (`navigation.ts`), orden y etiquetas es/en de la maqueta: `ops` Operación/Operations · `warehouse` Almacén/Warehouse ·
`money` Contabilidad/Accounting · `catalog` Catálogo/Catalog · `analytics` Análisis/Analytics · `system` Sistema/System ·
`portal` Portal de clientes/Client portal. Iconos: los de la maqueta (`box`, `layers`, `cash`, `users`, `chart`, `gear`, `users`)
portados a `app/icons.tsx`. Un grupo se pinta si tiene al menos un ítem visible. `admin` → `system` en el código.

**Ítems** (`routes.tsx`, todos, en el orden de la maqueta; `nav.order` = posición × 10). "Pendiente" = ruta real con `Placeholder`
(título y subtítulo del ítem desde `nav.<key>.title/.subtitle`, texto `shell.placeholder.body` = "Esta pantalla llega en un lote
posterior.", botón "Abrir otra pantalla" que abre la paleta; marca pequeña de Teikem arriba). Permiso y módulo con los **códigos que
ya existen**:

| Grupo | Ítem | Ruta | Estado | Permiso | Módulo |
|---|---|---|---|---|---|
| Operación | Pulso del día | `/` | P2 | — | — |
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
| | Indicadores | `/analytics/indicators` | **P3** | `analytics.view` | `ANALYTICS` |
| | Gráficos | `/analytics/charts` | **P3** | `analytics.view` | `ANALYTICS` |
| Sistema | Impresoras y labels | `/system/printers` | pendiente (sin backend) | `admin.tenant` | `SYSTEM` |
| | Roles y usuarios | `/system/users` | **P4** | `admin.users` o `admin.roles` | `SYSTEM` |
| | Aparatos móviles *(nuevo)* | `/system/devices` | **P5** | `devices.manage` | `WMS_LOTSERIAL` |
| | Catálogos de valores *(nuevo)* | `/system/catalogs` | **P6** | `admin.catalogs` | `SYSTEM` |
| | Integraciones / API | `/system/integrations` | pendiente (sin backend) | `admin.tenant` | `SYSTEM` |
| | Seguridad y auditoría | `/system/audit` | pendiente (F8b) | `admin.audit` | `SYSTEM` |
| | Ajustes de la compañía | `/system/settings` | pendiente (F8b) | `admin.tenant` | `SYSTEM` |
| Portal de clientes | Página principal · Entrada de órdenes · Consignatarios · Configuración · Perfil | `/portal/home` `/portal/orders` `/portal/consignees` `/portal/settings` `/portal/profile` | pendientes (sin backend) | `portalusers.manage` | `CLIENT_PORTAL` |

`AppRoute.perm` admite `"a|b"` (cualquiera de los dos), igual que `[RequirePermission("a|b")]` del API: `RouteGate` y `visibleNav`
lo dividen por `|`. `useCan(...perms)` ya es "todos"; se agrega `useCanAny(...perms)`.

**Cabecera** (`AppShell.tsx`, como la maqueta): barra "Buscar o ejecutar…" con `kbd /` → paleta (`kernel/ui/CommandPalette.tsx`:
atajos `/` y `Ctrl/⌘+K` fuera de un campo de texto; lista de ítems visibles agrupados por grupo, con título y subtítulo; filtro sin
acentos por título y subtítulo; ↑ ↓ Enter navega, Esc cierra; en móvil, icono de lupa que abre la misma paleta a pantalla completa);
"● en vivo · HH:MM:SS" (`Intl.DateTimeFormat` del idioma; se actualiza cada segundo; oculto bajo 600 px); selector de compañía
(como hoy); segmento tema ☀ / 🌙 (`data-theme` en `<html>`; `localStorage` `teikem.theme`; inicial `prefers-color-scheme`; cambiar
tema no desmonta nada); idioma (como hoy); avatar con iniciales → Mi cuenta; Salir.

**i18n** (`es.json`/`en.json`): `nav.groups.*` (7), `nav.<key>.title` y `.subtitle` para los 41 ítems (textos `nav.*` de la
maqueta), `shell.palette.{placeholder,empty,hint}`, `shell.live`, `shell.theme.{light,dark}`, `shell.placeholder.{body,another}`.

**Pruebas**: `navigation.test.ts` (7 grupos; ítems pendientes por permiso y módulo; `a|b`); `CommandPalette.test.tsx` (filtra,
navega con Enter, Esc cierra); `AppShell.test.tsx` (tema persiste y cambia `data-theme`; reloj presente); `Placeholder` muestra
título/subtítulo. `KIT.md`: sección "Menú y pantallas pendientes" y "Paleta de comandos".

### P2 — Pulso del día por paneles, a la maqueta, con Organizar (orden 3, agente: core)

**Datos**: `usePulse()` → `GET /pulse`; `useSaveLayout(scope)` → `PUT`; `useResetMyLayout()` → `DELETE`. Invalidación de
`PULSE_QUERY_KEY` tras guardar.

**Registro del frontend** (`features/analytics/pulsePanels.tsx`): `{ key, titleKey, render: (ctx) => ReactNode }` para
`INDICATORS`, `CHARTS`, `WAREHOUSE`, `ACTIVITY`. Un panel que el API devuelve y el frontend no conoce se ignora (compatibilidad
hacia adelante); uno que el frontend conoce y el API no devuelve, no se pinta (permiso/módulo).

**Pantalla** (`Pulse.tsx`):

- Cabecera: `<h1>` fecha del día (`toLocaleDateString(lang, {weekday:'long', day:'numeric', month:'long'})`, solo la primera
  letra en mayúscula), subtítulo = saludo (`analytics.pulse.subtitle`); a la derecha "Organizar mi Pulso" y, si
  `canOrganizeCompany`, "Organizar el de la compañía". Chip bajo el título: "Pulso personal · Volver al de la compañía" si
  `hasPersonalLayout`, si no "Pulso de la compañía".
- Secciones = `panels` ordenados por `sortOrder` y `isVisible`, cada una con su `render`:
  - `INDICATORS`: `.streamlabel` "Tus indicadores" + `.river` de `.node flow|money` (icono del `businessModule`, nombre, valor
    `formatValue`, subtítulo = rango o módulo, botón "Rango" existente); clic en el nodo → `/analytics/indicators`. Solo los
    `isVisible`. Vacío → no se pinta.
  - `CHARTS`: `.streamlabel` "Tus gráficos" + grilla `repeat(auto-fill, minmax(min(100%, 380px), 1fr))`, `align-items:start`,
    altura mínima común 300 px; cada panel `Panel` con el gráfico Recharts actual; con ≤ 3 puntos, lista "etiqueta · valor" en vez
    de gráfico; dona con el total al centro; etiquetas ya vienen resueltas por el API.
  - `WAREHOUSE`: `WarehousePulsePanel` (F7A, sin cambios).
  - `ACTIVITY`: `ActivityPanel` (F7A, sin cambios).
- Sin paneles (`panels` vacío): `EmptyState` bienvenida (`analytics.pulse.welcomeTitle/Body`). Paneles pero sin elementos en
  INDICATORS/CHARTS: la sección no se pinta; si además WAREHOUSE/ACTIVITY no están: "Aún no tienes indicadores ni gráficos en tu
  Pulso" con enlace a Indicadores.

**Modo Organizar** (`PulseOrganizer.tsx`, mismo componente para `mine` y `company`): copia local de `panels`, `indicators`,
`charts`; barra superior fija: título "Organizando mi Pulso" / "Organizando el Pulso de la compañía (lo verá todo el que no tenga
uno personal)", botones "Listo" y "Cancelar". Cada panel: cabecera con asa, ▲ ▼, ojo (ocultar/mostrar). Dentro de INDICATORS y
CHARTS, cada elemento igual (▲ ▼ ojo). Arrastrar con el ratón en escritorio (HTML5 drag & drop nativo, sin librería); en táctil
solo botones. Ocultos: se quedan en su sitio, atenuados, con "Mostrar". "Listo" → un `PUT layout?scope=…` con **todos** los
paneles y elementos (orden = índice × 10); toast "Pulso guardado"; error → `title` del ProblemDetails. "Cancelar" descarta.
Teclado: con foco en el asa, ↑/↓ mueven.

**Responsive**: río con `flex-wrap` bajo 980 px, nodos `flex: 1 1 30%`; una columna a 360 px; sin scroll horizontal.

**Pruebas** (`Pulse.test.tsx`, `PulseOrganizer.test.tsx`): orden de paneles por `sortOrder`; panel desconocido ignorado;
`isVisible=false` no se pinta; mover y ocultar generan el cuerpo esperado del `PUT` (con scope); "Volver al de la compañía" llama
al `DELETE` tras confirmar; sin `canOrganizeCompany` no aparece el botón; vacíos.

### P3 — Indicadores y Gráficos (grupo Análisis) (orden 3, agente: pantalla)

Rutas `/analytics/indicators` y `/analytics/charts` (`analytics.view`, `ANALYTICS`). `useIndicators()` → `GET indicators`;
`useCharts()` → `GET charts`; `useDataSources()` → `GET data-sources`; valor de tarjeta `GET indicators/{id}/value` /
`charts/{id}/data` (consultas independientes, con spinner por tarjeta).

**Pantalla** (como `indicadoresScreen`): cabecera título/subtítulo de la maqueta (`analytics.indicators.title/sub`), botón "Nuevo
indicador" (`analytics.manage`). Paneles por módulo de negocio (`businessModule`, etiqueta del catálogo `BusinessModule`, icono
del grupo) con contador; tarjetas `repeat(auto-fill, minmax(min(100%, 300px), 1fr))`: nombre, chip "Por defecto" si `isSystem`,
valor, rango (texto), switch **"Mostrar en Pulso del día"** (mío: `PUT …/my-pulse`; con `analytics.manage` o dueño, un segundo
switch "en el de la compañía": `PUT` de la definición con `showInPulse`), botón "Rango" (`RangeModal`, solo si
`canChangeDate`), chip de visibilidad (Todos / Privado / Compartido (n)), "Creado por {name}", editar y eliminar solo si `canEdit`
(`ConfirmDialog` "¿Eliminar el indicador {name}?"). Nota al pie `analytics.indicators.note`. Vacío: "Todavía no hay indicadores."

**Editor** (modal del kit, `DefinitionEditor.tsx`, compartido con gráficos): Nombre (obligatorio; "Ponle un nombre al indicador"),
Descripción es/en, Fuente de datos (select de `data-sources`; al cambiar se limpian campo y filtros), Cálculo (`AggregateFn`:
COUNT/SUM/AVG/MIN/MAX del catálogo), Campo (campos numéricos de la fuente; oculto con COUNT), Filtro (filas campo · operador ·
valor sobre `fields` y `customFields` de la fuente; operadores del DSL de `kernel/dsl`; serializa al `filterJson` que valida el
API), Módulo de negocio (catálogo; por defecto `defaultBusinessModule` de la fuente), "Es dinero", Quién puede verlo (Toda la
compañía / Solo yo / Personas específicas → `shares` con buscador de usuarios `GET users` si tiene `admin.users`, si no solo por
correo), Rango de fecha por defecto (`DateRangeMode` del catálogo; Desde/Hasta con `CUSTOM`; deshabilitado con
`dateRangeApplies=false` y el texto `noDateField` de la maqueta), Orden en el Pulso de la compañía (`sortOrder`), "Mostrar en
Pulso por defecto". Gráficos añaden **Agrupar por** (campos de la fuente) y **Tipo** (`ReportChartType`: barra, dona, línea).
Vista previa (botón: calcula con `POST reports/{entity}/preview`? no: para indicadores se guarda y se lee; la vista previa queda
fuera). Errores del servidor por campo al formulario; `title` general en toast.

**Pruebas**: agrupación por módulo; solo se listan los legibles (el API filtra; la pantalla no re-filtra); switch mío llama a
`my-pulse` e invalida `PULSE_QUERY_KEY`; el editor serializa el filtro y envía el cuerpo `IndicatorUpsertRequest`/`ChartUpsertRequest`
correcto; sin `analytics.manage` no hay "Nuevo" ni editar.

### P4 — Roles y usuarios + PIN de la app (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/users` (`admin.users|admin.roles`, `SYSTEM`). Segmento **Roles | Usuarios**: solo las pestañas permitidas; sin
`admin.roles` se abre en Usuarios.

**Roles** (`GET roles`, `GET permissions`): tabla ordenable Rol · Permisos (n / total) · Usuarios (`userCount`) · acciones; chips
"sistema" (`isSystem`) y "plantilla" (`isTemplate`); "Nuevo rol" (`admin.roles`). Modal Nuevo/Editar (`RoleUpsertRequest`): Nombre
(obligatorio → "El nombre es obligatorio."), Descripción es/en, permisos en casillas **agrupadas por `category`** (etiqueta del
catálogo `PermissionCategory`, icono del grupo del menú; casilla "todo el grupo"); guardar `POST roles` / `PUT roles/{id}` (AAL2 →
modal de reauth del kit y reintento); eliminar `DELETE roles/{id}` (AAL2; `ConfirmDialog`; error del API "El rol tiene usuarios
asignados; reasígnelos antes de eliminarlo." en toast); "Permisos desconocidos: …" imposible desde la interfaz, pero se muestra
si llega. Nota al pie `system.roles.note` (texto `rolesNote` de la maqueta, adaptado a los nombres reales de las plantillas).

**Usuarios** (`GET users` paginado, `QBox` por nombre/correo, filtro "incluir suspendidos"): columnas Nombre · Correo · Roles
(chips) · Permisos extra ("+n" / "Añadir") · Estado (chip `membershipStatus`: Activo / Suspendido / Invitado + switch) · MFA (sí/no)
· Último acceso · **PIN app** (solo con módulo `WMS_LOTSERIAL` y `devices.manage|admin.users`: chip Sí/No por `hasPin`) · acciones.
Acciones por fila:
- Editar (nombre, activo): `PUT users/{id}` (`UserUpdateRequest`). Error "No puede desactivarse a sí mismo." → toast.
- Roles: modal multi-selección (`GET roles`) → `PUT users/{id}/roles` (AAL2). "Roles desconocidos: …" → toast.
- Permisos extra: modal "Permisos de {name}" con los del rol marcados y bloqueados "(del rol)" y los extra togglables →
  `PUT users/{id}/permissions` (AAL2). Texto `userPermHint` de la maqueta.
- Estado: switch Activo/Suspendido → `PUT users/{id}/membership` `{status: "ACTIVE"|"SUSPENDED"}`; "No puede cambiar su propia
  membresía." → toast y el switch vuelve.
- Cerrar sesiones: `DELETE users/{id}/sessions` (`ConfirmDialog`).
- PIN: "Asignar PIN" / "Restablecer PIN" → modal con PIN y confirmación (numérico, 4 a 6 dígitos según `PinService`; los dos
  iguales → "Los PIN no coinciden." en cliente) → `PUT users/{id}/pin` (`PinAdminSetRequest`, AAL2); errores del API por campo
  (`pin`), p. ej. "El PIN no puede ser una secuencia trivial." (la lista completa está en el manual 08). "Quitar PIN" →
  `DELETE users/{id}/pin` (`ConfirmDialog`).
- "Nuevo usuario": modal (`UserCreateRequest`): Correo (obligatorio → "El correo es obligatorio."), Nombre, Roles (multi), Contraseña
  (opcional; si va vacía el usuario queda invitado y define la contraseña por el flujo que ya exista; errores de Identity en
  `password`), Tipo = interno (el de portal muestra "Los usuarios de portal se administran desde el expediente del cliente (Lote
  2)." si se intenta). "El usuario ya pertenece a esta compañía." (409) → toast.

**Mi cuenta** (`account/PinTab.tsx`, pestaña "PIN de la app", solo con `WMS_LOTSERIAL`): estado (`GET /me/pin`: tiene PIN,
actualizado, bloqueado hasta), "Fijar/Cambiar PIN" (contraseña actual + PIN + confirmación → `PUT /me/pin`,
`PinSetRequest`; error `currentPassword` → mensaje del API), "Quitar PIN" (`DELETE /me/pin` con contraseña actual).

**Pruebas**: segmento por permisos; roles agrupados por categoría; el `PUT roles` muestra el reauth cuando el API responde
`aal2_required`; switch de estado revierte con error; columna PIN solo con módulo y permiso; el modal de PIN valida
coincidencia en cliente y muestra el mensaje del API.

### P5 — Aparatos móviles, mínimo (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/devices` (`devices.manage`, `WMS_LOTSERIAL`). `GET devices` (`QBox`, filtro "incluir inactivos"). Tabla ordenable
Código · Nombre · Almacén por defecto · Estado (chip activo/inactivo) · Último latido (relativo) · Versión de la app. "Nuevo
aparato": modal con Nombre (obligatorio) y Almacén por defecto (`useWarehouses`, opcional) → `POST devices`
(`DeviceCreateRequest`; el API genera el código) → modal "Código de registro" (`DeviceCreatedDto.enrollCode` en grande, mono,
botón Copiar con toast "Copiado", texto "Escríbelo en la pantalla Registrar de Teikem Almacén. Solo se muestra una vez."). Por
fila: "Desactivar" / "Reactivar" (`POST …/deactivate|reactivate`, `ConfirmDialog`), "Nuevo código de registro" (`POST
…/enroll-code`, mismo modal). Sin ficha ni edición de modelo/tema.

### P6 — Catálogos de valores (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/catalogs` (`admin.catalogs`, `SYSTEM`). Maestro-detalle. Izquierda: `GET catalogs/domains` con `QBox`, chip
"sistema"/"propia" (`isSystem`), "Nueva lista" (modal `CatalogListCreateRequest`: Nombre es (obligatorio → "El nombre es
obligatorio." / "Nombre inválido."), Nombre en, Descripción, valores iniciales código+etiqueta) → `POST catalogs/lists`; "Eliminar
lista" (solo propias; `DELETE lists/{domainKey}`; errores "Las listas de sistema no se eliminan.", "La lista está en uso por un
campo personalizado; desactívela en vez de eliminarla."). Derecha: `GET catalogs/{entity}` (incluye deshabilitados): tabla
ordenable Código · Etiqueta (es) · Etiqueta (en) · Orden · Habilitado · Origen (Sistema / Ajustado (`isOverridden`) / Propia).
Acciones: valor de sistema → "Ajustar" (modal `LookupOverrideRequest`: etiquetas es/en, habilitado, orden → `PUT
{entity}/{code}/override`) y "Restaurar" (`DELETE …/override`); valor propio → "Editar" (`LookupCodeUpsertRequest`: código
obligatorio "El código es obligatorio." / "Código inválido (máx. 40 caracteres alfanuméricos)."; etiqueta es obligatoria "La
etiqueta es obligatoria."; `PUT {entity}/{code}`), "Nuevo valor" (`POST {entity}`), "Desactivar" (`DELETE`, soft) y
"Restaurar" (`POST …/restore`). Mensajes de dominio global: "Los catálogos globales solo los edita el administrador de
plataforma; use overrides o cree una lista propia." y "Un valor de sistema no se desactiva globalmente; use el override del tenant
(IsEnabled=false)." → toast. Tras guardar: invalidar la caché de `kernel/catalogs` (`queryClient.invalidateQueries` del prefijo
`/api/v1/catalogs`). Móvil: dominios como `select` arriba, valores en tarjetas.

### P7 — Marca Teikem en la web (orden 2, agente: core)

Paquete en `Logos/` (mismos archivos que la landing `web/assets/img/logo-*.svg`). Regla de la maqueta (`brandLogoFor`): tema oscuro
→ variante `-inv`; claro → normal; el idioma elige el lema. Copiar a `web-app/public/brand/`:
`teikem-1b-horizontal-tagline-es(.svg|-inv.svg)`, `teikem-1a-horizontal-tagline-en(…)`, `teikem-2-horizontal-notagline(…)`,
`teikem-8-favicon-256.png`, `favicon.ico`, `teikem-8-appicon-180.png`; crear `Logos/teikem-symbol.svg` (hexágono + T, mismas
rutas que `teikemLogo()` de la maqueta) y copiarlo. `kernel/ui/Brand.tsx`: `<BrandLockup>` (`<img>` por idioma y tema,
`alt="Teikem"`) y `<BrandMark size>`. Barra lateral: lockup a todo el ancho con `filter: drop-shadow(0 4px 14px
var(--brand-glow))`; marca de 44 px al colapsar. Login, MFA, selección de compañía y `Splash` a pantalla completa: lockup
centrado. `Placeholder`: marca pequeña. `index.html`: `<link rel="icon" href="/brand/favicon.ico">`, PNG 256,
`apple-touch-icon` 180, `<title>Teikem</title>`, `theme-color #0B2C66`. Pruebas: el `src` cambia con idioma y tema sin recargar; la
marca aparece al colapsar. `KIT.md`: "Marca".

### P8 — Marca Teikem en la app móvil (orden 2, agente: implementer; fuera de `fe-implementar`)

`scripts/brand-icons.mjs` (`sharp`, ejecutable en CI y en local) genera desde `Logos/teikem-symbol.svg`: `assets/icon.png` 1024
(símbolo sobre `#0B2C66`), `android-icon-foreground.png` (símbolo con margen de seguridad del 20 %),
`android-icon-background.png` (`#0B2C66` liso), `android-icon-monochrome.png` (símbolo blanco), `splash-icon.png` (símbolo;
`backgroundColor` `#0B2C66` en `app.config.ts`), `favicon.png`, y el lockup `-inv` es/en a PNG @2x/@3x. Pantallas Registrar y
Entrar: lockup arriba del formulario (por idioma). `adaptiveIcon.backgroundColor` → `#0B2C66`. Verificación: `npm run check` de la
app, APK del job `android`, captura del icono para `docs/manual/09-app-almacen.md`.

---

## 4. Recorrido Playwright (`web-app/e2e/f8a.spec.ts`)

Datos propios por corrida (`STAMP`), limpieza en `afterAll`, capturas `docs/manual/frontend/img/f8a-*.png`.

1. **Marca y shell** (`teikem+admin`): lockup es/oscuro en la barra; cambiar a inglés y tema claro cambia el archivo sin recargar;
   colapsar → marca; pestaña con favicon; `/` abre la paleta, "usu" + Enter → Roles y usuarios; el tema persiste al recargar.
2. **Menú completo**: los 7 grupos y todos los ítems de la maqueta; "Sala de despacho" abre la pantalla pendiente con su
   título y subtítulo.
3. **Usuario solo almacén** (la receta de §0): crear rol "Almacén E2E {STAMP}" con `inventory.view`, `warehouse.receive`,
   `pulse.warehouse`, `pulse.indicators`, `pulse.activity` (reauth); crear usuario `e2e+{STAMP}@cerevelo.com` con ese rol y
   contraseña; entrar con él: el menú muestra solo Operación → Pulso y el grupo Almacén (Almacenes, Productos, Inventario,
   Recibo…), ningún otro grupo; el Pulso muestra el panel Almacén y Actividad reciente, **no** "Tus gráficos", y en "Tus
   indicadores" solo los de almacén; `/system/users` directo → "Sin permiso".
4. **Indicadores**: como admin, apagar "Mostrar en Pulso" (mío) de "Productos bajo mínimo" → Pulso no lo muestra; encender → vuelve.
5. **Organizar**: "Organizar mi Pulso": mover Actividad reciente arriba, ocultar Almacén, Listo; recargar: se conserva; el
   usuario E2E no lo ve así. "Volver al de la compañía" → orden original. "Organizar el de la compañía": ocultar un gráfico → el
   usuario E2E (con `pulse.charts` agregado al rol) tampoco lo ve.
6. **PIN**: al usuario E2E, PIN `2846` (reauth) → Sí; `1234` → "El PIN no puede ser una secuencia trivial."; quitar → No;
   suspender → Suspendido y ya no puede entrar.
7. **Aparatos**: "Tablet E2E" → código de registro; "Nuevo código" → otro distinto; desactivar y reactivar.
8. **Catálogos**: ajustar la etiqueta española de un `WarehouseTaskType`; en el Pulso (panel Almacén) se ve sin recargar;
   restaurar.
9. **Mi cuenta → PIN de la app**: fijar con contraseña; quitar.
10. **Móvil (Pixel 7, 360 px)**: Pulso, Roles y usuarios y Catálogos sin scroll horizontal; paleta desde la lupa.

## 5. Cierre

`dotnet build && dotnet test`, `db-init` en BD limpia, `scripts/smoke.sh`; `npm run check`; Playwright (`escritorio` + `movil`);
CI verde; `docs/frontend/loteF8a-decisiones.md`; `docs/manual/frontend/f8a-menu-sistema-analisis-y-pulso.md` con capturas;
capítulo de análisis del manual (P1) y FAQ; `docs/manual/README.md` actualizado. Orden de ejecución: P1 y P8 y el mini-mock
(Aparatos, PIN, Catálogos) en paralelo → OK de Luis al mini-mock → regenerar `web-app/openapi.json` → `fe-implementar` con P0,
P7, P2, P3, P4, P5, P6.

---

## 6. Lo que queda para después (sin retroceder nada)

- **F8b**: Vistas e informes, Campos personalizados, Estatus, Seguridad y auditoría, Ajustes de la compañía (módulos, settings,
  feriados), alcances de datos, Administración de plataforma. Cada una sustituye su pantalla pendiente.
- **En sus lotes**: F2, F3 (+ río "Paquetes en la calle", "Necesita tu decisión", Radar como paneles `pulse.*`), F4, F5, 7B, 7C (+
  río COD), prueba de campo de la app 8A.
- **Sin backend**: Impresoras y labels, Integraciones / API, colores y logo por compañía, Portal de clientes, asistente.

## 7. Opinión sobre el dashboard de la maqueta (pedida por Luis)

Se conserva: los dos ríos cuentan la historia y el azul/naranja se sostiene en toda la página; cifras en monoespaciada con
subtítulo corto y sparkline; "Necesita tu decisión" y el Radar son lo más útil. Cambios que recoge el plan: (1) lo más accionable
queda bajo el pliegue → con el orden por compañía se sube cuando exista; (2) un tenant solo de almacén tendría los ríos en cero →
los paneles sin permiso/módulo/datos no se pintan; (3) gráficos con ≤ 3 puntos → lista; dona con total al centro; etiquetas del
catálogo; (4) el subtítulo de cifras es fijo en la maqueta → saldrá de los ríos; (5) "28 De Septiembre" → solo primera letra en
mayúscula; (6) altura mínima común en las tarjetas de gráficos; (7) la mascota/asistente no se dibuja (sin backend); (8) Almacén y
Actividad reciente (F7A) entran como paneles del mismo modelo.
