# Lote F8a — Sistema, Análisis y Pulso organizable (frontend) + cotejo con la maqueta

Fecha: 2026-09-28. Estado: **propuesta para aprobación de Luis**. Nada de esto está construido.

Origen: Luis pidió analizar por qué no hay pantallas de configuración, catálogos ni usuarios, cómo asignar el PIN de la
app móvil, y cómo "organizar" el Pulso del día, **sin darle para atrás a los lotes pendientes** (7B Operación, 7C
Contabilidad, F2 a F5 de frontend, prueba de campo de la app). Y pidió revisar que la app siga la maqueta
`Diseño/teikem-mockups.html`, porque "no la ve igualita".

La parte 1 es el cotejo (qué dice la maqueta, qué hay hoy, qué falta y por qué). La parte 2 es el diseño de F8a. La
parte 3 es lo que se deja para F8b y para los lotes que ya estaban definidos. La parte 4 son las decisiones que necesito
de Luis antes de codificar.

---

## Parte 1 — Cotejo maqueta vs. aplicación

Fuentes: `Diseño/teikem-mockups.html` (constante `NAV`, diccionario `nav`, funciones `home`, `usuariosScreen`,
`indicadoresScreen`, `ajustesScreen`, cabecera HTML) contra `web-app/src/app/{navigation.ts,routes.tsx,AppShell.tsx}`,
`web-app/src/features/analytics/Pulse.tsx` y `web-app/src/kernel/i18n/es.json`.

### 1.1 Menú lateral: grupos

| Maqueta (orden y nombre) | App hoy | Diferencia |
|---|---|---|
| 1. Operación | 1. Operación | igual |
| 2. Almacén | 3. Almacén | **orden invertido** con Catálogo |
| 3. Contabilidad | — | no existe el grupo (backend 7C pendiente); debe existir aunque esté vacío para que 7C solo agregue rutas |
| 4. Catálogo | 2. Catálogo | orden; **sin ninguna pantalla** → el grupo no se pinta |
| 5. Análisis | 4. Análisis | **sin ninguna pantalla** → no se pinta |
| 6. Sistema | 5. **Administración** | nombre distinto; **sin ninguna pantalla** → no se pinta |
| 7. Portal de clientes | — | no existe (sin backend) |

Consecuencia: la app muestra hoy **dos grupos de siete** (Operación y Almacén). Eso, más que el estilo, es lo que hace que
"no se vea igual". El `visibleNav()` ya oculta grupos vacíos (correcto); lo que falta son las pantallas.

### 1.2 Menú lateral: pantallas por grupo

| Grupo | Maqueta | App hoy | Estado |
|---|---|---|---|
| Operación | Pulso del día · Órdenes · Estación de escaneo · Sala de despacho · Monitoreo de ruta | Pulso del día · Órdenes (consulta, solo lectura de F6) | Escaneo, Despacho y Monitoreo son **F5** (backend Lote 5 listo). Órdenes completa es **F3**. No se tocan en F8a. |
| Almacén | Almacenes · Ubicaciones · Productos e inventario · Compras · Recibo · Ajustes de inventario · Recolección y empaque · Conteo cíclico · Cruce de muelle · Kárdex de movimientos | Almacenes · Productos · Inventario · Recepción · Tareas de almacén · Conteo cíclico · Recolección y empaque · Proveedores · Órdenes de compra · Citas de muelle · Cruce de muelle | Cubierto por F6 con **otra partición**: Ubicaciones vive dentro de la ficha de Almacén; Kárdex y Ajustes son pestañas de Inventario; Compras se dividió en Proveedores y Órdenes de compra; se agregaron Tareas de almacén y Citas de muelle (no son ítems en la maqueta). Nombres distintos: "Recibo" → "Recepción", "Productos e inventario" → "Productos" + "Inventario". **Decisión 1.** |
| Contabilidad | Contabilización de compras · Contabilización de despachos · Procesar entregas (COD) · Facturación · Liquidación | — | Sin backend (7C). Fuera. |
| Catálogo | Clientes y contratos · Consignatarios · Choferes y tarifas · Flota y mantenimiento | — | Backend **listo** (Lotes 2 y 4); frontend son **F2 y F4**, pendientes. **Decisión 2.** |
| Análisis | Vistas e informes · Campos personalizados · Indicadores · Gráficos | — | Backend **listo** (capa transversal, Lote 1). Indicadores y Gráficos entran en **F8a** porque ahí vive el control del Pulso (1.4). Vistas y Campos → F8b. |
| Sistema | Impresoras y labels · Roles y usuarios · Integraciones / API · Seguridad y auditoría · Ajustes de la compañía | — | Backend listo para Roles y usuarios, Auditoría, Ajustes (módulos, settings, feriados). Sin backend: Impresoras, Integraciones. La maqueta **no previó** Aparatos móviles ni PIN (Lote 8A es posterior a la maqueta). F8a: Roles y usuarios (+PIN), Aparatos, Catálogos de valores. F8b: Auditoría, Ajustes. |
| Portal de clientes | Página principal · Entrada de órdenes · Consignatarios · Configuración · Perfil | — | Sin backend. Fuera. |

### 1.3 Cabecera y barra lateral (shell)

| Elemento de la maqueta | App hoy | Acción |
|---|---|---|
| Barra de comando "Buscar o ejecutar…" con atajo `/` (paleta: busca pantallas y navega) | no existe | **F8a P0** (barato: busca en las rutas visibles del menú) |
| "● en vivo · HH:MM:SS" | no existe | F8a P0 (trivial) |
| Selector de compañía | `select` cuando hay más de una membresía | igual |
| Interruptor de tema claro/oscuro | **no existe el botón**, aunque `styles/tokens.css` ya trae `[data-theme="light"]` | F8a P0 (solo el botón + persistencia) |
| Idioma con bandera 🇵🇷 ES / 🇺🇸 EN | `LangSelect` sin bandera | dejar como está (Decisión 6 si Luis quiere las banderas) |
| Avatar con iniciales (sin botón de salir visible) | avatar + nombre + botón Salir | dejar Salir (la maqueta no resolvió el cierre de sesión) |
| Marca: lockup completo / marca cuadrada al colapsar | "T" + "Teikem" | menor; se alinea cuando exista logo por compañía (sin backend) |
| Menú colapsable, grupos acordeón, cajón en móvil | igual | igual |

### 1.4 Pulso del día

Maqueta (`home()`), de arriba abajo:

1. Título = **fecha del día** ("lunes, 28 de septiembre") con subtítulo de cifras ("596 órdenes en movimiento · $19,960 COD").
2. Río **"Paquetes en la calle"**: Entrada → En almacén → Por despachar → En ruta → Entregado (nodos azules con sparkline).
3. Río **"Dinero COD de regreso"** (nodos naranja).
4. **"Tus indicadores"**: los indicadores que el usuario marcó "Mostrar en Pulso", como río (azul cantidad / naranja dinero, clic lleva a Indicadores). Si no hay ninguno, la sección no se pinta.
5. **"Tus gráficos"**: los gráficos marcados para Pulso, grilla de 2 columnas, panel compacto, clic lleva a Gráficos.
6. **"Necesita tu decisión"**: bandeja de 4 avisos con botón de acción.
7. **Radar de órdenes abiertas**: agrupar por pueblo/cliente, buscador.

App hoy (`Pulse.tsx`): título fijo "Pulso del día" + saludo; indicadores como tarjetas `Panel` genéricas (grilla auto-fill
220 px); gráficos como `Panel` de 260 px; panel **Almacén** (F7A, con mock aprobado por Luis); **Actividad reciente** (F7A).

Por qué se siente "regado y sin control":

- **El interruptor "Mostrar en Pulso" de la maqueta vive en las pantallas Indicadores y Gráficos, que no existen.** El
  backend sí lo tiene (`PUT /analytics/indicators/{id}/my-pulse`, `.../charts/{id}/my-pulse`, por usuario), pero no hay
  ningún lugar en la interfaz para tocarlo. Todo indicador sembrado con `ShowInPulse = 1` sale, y no hay cómo quitarlo.
- **No hay orden por usuario.** `GET /analytics/pulse` devuelve por `SortOrder` global (solo `analytics.manage` lo cambia) y
  `UserAnalyticsPreference` guarda `ShowInPulse` y rango por usuario, pero **no orden**.
- **Almacén y Actividad reciente son fijos** en el código: no se pueden ocultar ni mover.
- **Lenguaje visual distinto**: la maqueta usa ríos (`.streamlabel` + `.river` + `.node`, azul/naranja); la app usa
  tarjetas neutras. Las clases del río ya están portadas en `styles/base.css` (F1 portó el `<style>` de la maqueta).

Lo que **no** entra en F8a del Pulso: los dos ríos de Operación (dependen de Órdenes F3 y de COD 7C), la bandeja
"Necesita tu decisión" (F3/F5) y el Radar (F3). F8a deja el **orden de secciones de la maqueta** y los huecos listos para que
esos lotes solo agreguen su sección. **Decisión 4.**

### 1.5 Pantallas de Sistema en la maqueta (lo que F8a debe seguir)

- **Roles y usuarios** (`usuariosScreen`): una sola pantalla, segmento **Roles | Usuarios** arriba a la derecha, botón
  "Nuevo rol" solo en Roles. Tabla de roles: Rol · Permisos (n / total) · Usuarios · editar/eliminar. Modal de rol: nombre
  + casillas de permisos **agrupadas por categoría** con el icono del grupo. Tabla de usuarios: Nombre · Correo · Rol ·
  Permisos extra (botón "+n" o "Añadir" → modal con los del rol marcados y bloqueados, los extra togglables) · Estado
  (switch Activo/Suspendido). Nota al pie explicativa.
- **Ajustes de la compañía** (`ajustesScreen`): Módulos (tabla con dependencia y switch) · Valores por defecto · Pipeline de
  órdenes (capacidades por estatus) · Colores y logo. → F8b (colores/logo sin backend).
- **Indicadores** / **Gráficos**: cabecera + "Nuevo indicador"; paneles **por módulo de negocio** con contador; tarjetas en 3
  columnas con nombre, valor, rango, **switch "Mostrar en Pulso"**, editar/eliminar; nota al pie.
- No hay maqueta para: **Aparatos móviles**, **PIN de usuario**, **Catálogos de valores**. Se diseñan con el mismo lenguaje
  (tabla `.lst`, `.panel/.ph2/.pb`, modal del kit) y se someten a Luis en un mini-mock HTML antes de codificar, como se hizo
  con `mock-pulso-almacen.html` en F7A. **Decisión 5.**

---

## Parte 2 — Diseño de F8a

**Objetivo**: que el usuario administrador pueda, desde la web, (a) organizar su Pulso (qué sale y en qué orden, incluidos
los paneles de Almacén y Actividad), (b) crear usuarios y roles, (c) asignar el PIN de la app móvil y registrar aparatos,
(d) mantener los catálogos de valores; y que el shell y el menú queden como la maqueta (grupos, orden, nombres, paleta,
tema, reloj). Todo consumiendo el API existente salvo la pieza P1 (orden y paneles del Pulso por usuario).

**Permisos y módulos** (códigos exactos del API): `analytics.view`, `analytics.manage`, módulo `ANALYTICS`; `admin.users`,
`admin.roles`; `devices.manage` y módulo `WMS_LOTSERIAL` (aparatos y PIN); `admin.catalogs`. AAL2 (reauth del kit) en:
`PUT/DELETE roles/{id}`, `PUT users/{id}/roles`, `PUT users/{id}/permissions`, `PUT users/{id}/pin`.

### P1 — Backend mínimo: orden y paneles del Pulso por usuario (orden 1, agente: implementer)

Sin esto, "organizar" solo podría ser ocultar. Cambio pequeño y aditivo, siguiendo las convenciones del repo (un solo set
de scripts SQL, entidad + configuración EF, DTOs en `Contracts/`, permisos existentes):

1. `Diseño/logistica-db-estructura.sql`: columna `PulseSortOrder INT NULL` en `dbo.UserAnalyticsPreference` (ya es por
   usuario y por indicador o gráfico). Tabla nueva `dbo.UserPulsePanel (UserPulsePanelId INT IDENTITY PK, TenantId INT NOT
   NULL FK Tenant, UserId INT NOT NULL FK AspNetUsers, PanelKey NVARCHAR(40) NOT NULL, IsVisible BIT NOT NULL DEFAULT 1,
   SortOrder INT NOT NULL DEFAULT 0, UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(), UNIQUE (TenantId, UserId,
   PanelKey))` en la capa de análisis, `ITenantScoped`. Sin seed (sin fila = visible, al final).
2. `PulseDto(Indicators, Charts, Panels)`: `Panels` = `[{ key, isVisible, sortOrder }]` para las claves conocidas del
   servidor (`WAREHOUSE`, `ACTIVITY`; constantes en `Teikem.Domain`), completando con el valor por defecto las que no tienen
   fila. Indicadores y gráficos ordenados por `PulseSortOrder` del usuario (nulos al final) y luego `SortOrder` global y
   nombre; cada `IndicatorValueDto`/`ChartDataDto` lleva `pulseSortOrder`.
3. `PUT /api/v1/analytics/pulse/layout` (`analytics.view`, módulo `ANALYTICS`): cuerpo `{ items: [{ kind: "indicator" |
   "chart", id, sortOrder, showInPulse }], panels: [{ key, isVisible, sortOrder }] }`. Escribe/actualiza
   `UserAnalyticsPreference` (respetando el permiso de ver cada indicador: 404 si no puede verlo) y `UserPulsePanel`
   (clave desconocida → `ValidationException` "Panel de Pulso desconocido: {key}", 400). Devuelve el `PulseDto` nuevo.
   `my-pulse` sigue existiendo (lo usan las tarjetas de Indicadores/Gráficos).
4. Pruebas xunit de la ordenación (usuario con orden parcial, sin orden, panel oculto). `scripts/smoke.sh`: un `PUT layout`
   y un `GET pulse` que compruebe el orden. Manual `docs/manual/` capítulo de análisis + FAQ: "¿Por qué mi Pulso no se ve
   igual que el de otro usuario?".

### P0 — Shell y menú a la maqueta (orden 2, agente: core)

- `navigation.ts`: `NAV_GROUPS` en el orden y con las claves de la maqueta: `ops`, `warehouse`, `money`, `catalog`,
  `analytics`, `system`, `portal`; etiquetas `Operación · Almacén · Contabilidad · Catálogo · Análisis · Sistema · Portal de
  clientes` (y en inglés las de la maqueta). Los grupos vacíos siguen ocultos. `admin` → `system` en todas las rutas.
- Renombres del grupo Almacén según **Decisión 1** (por defecto: "Recepción" → "Recibo"; el resto se queda).
- **Tema**: botón claro/oscuro en la cabecera (segmento `sun`/`moon` como la maqueta); `data-theme` en `<html>`,
  persistido en `localStorage` (`teikem.theme`), valor inicial `prefers-color-scheme`. Cambiar tema no desmonta nada.
- **Reloj "● en vivo · HH:MM:SS"** en la cabecera (oculto bajo 600 px).
- **Paleta de comandos**: barra "Buscar o ejecutar…" en la cabecera (en móvil, icono de lupa); atajos `/` y `Ctrl/⌘+K`;
  lista agrupada por grupo del menú con las rutas visibles para el usuario (mismo `visibleNav`), filtro por texto sin
  acentos, flechas + Enter navega, Esc cierra. Componente `kernel/ui/CommandPalette.tsx` documentado en `KIT.md`.
- Pruebas: `visibleNav` con los grupos nuevos; paleta filtra y navega; tema persiste.

### P2 — Pulso del día a la maqueta + modo "Organizar" (orden 3, agente: core)

- Cabecera: **fecha del día** como título (`toLocaleDateString` del idioma activo, capitalizada) y el saludo como
  subtítulo. Botón **"Organizar"** a la derecha (solo con `analytics.view`).
- Secciones, en el orden de la maqueta y solo si tienen contenido: *(hueco: ríos de Operación, F3/7C)* → **"Tus
  indicadores"** como río (`.streamlabel` + `.river` + `.node flow|money` según `isMoney`; nodo = icono, nombre, valor,
  rango; clic → `/analytics/indicators`; botón "Rango" se conserva en el nodo) → **"Tus gráficos"** (grilla 2 columnas en
  escritorio, 1 en móvil; panel compacto con el gráfico Recharts actual; clic en el título → `/analytics/charts`) →
  **Almacén** (panel F7A tal cual) → **Actividad reciente** (F7A tal cual). El orden real lo dicta `GET /pulse`
  (indicadores/gráficos por `pulseSortOrder`; paneles por `panels[].sortOrder`, intercalables con las secciones: un
  panel con `sortOrder` menor que el primer indicador sube arriba).
- **Modo Organizar** (estado local hasta pulsar "Listo"): cada nodo, gráfico y panel muestra ▲ ▼ y un ojo (ocultar);
  arrastrar con el ratón en escritorio (HTML5 drag, sin librería) y solo botones en táctil; los ocultos se listan al final
  en gris ("Ocultos: …") con "Mostrar" para recuperarlos. "Listo" → un solo `PUT /analytics/pulse/layout`; "Cancelar"
  descarta. Toast "Pulso guardado". Los errores ProblemDetails con su `title`.
- Vacío: "Aún no tienes indicadores ni gráficos en tu Pulso" con enlace a Indicadores; el panel Almacén y Actividad siguen
  saliendo si aplican.
- Responsive: río envuelve (`flex-wrap`) como la maqueta bajo 980 px; una columna a 360 px; sin scroll horizontal.
- Pruebas unitarias: orden por `pulseSortOrder`, ocultar/mostrar, cuerpo del `PUT` que se envía, vacío.

### P3 — Indicadores y Gráficos (grupo Análisis) (orden 3, agente: pantalla)

Dos rutas: `/analytics/indicators` y `/analytics/charts` (`analytics.view`, módulo `ANALYTICS`), como la maqueta:

- Cabecera con título/subtítulo de la maqueta y botón "Nuevo indicador"/"Nuevo gráfico" (`analytics.manage`).
- Paneles **por módulo de negocio** (`businessModule` del DTO, etiqueta del catálogo `BusinessModule`) con contador; dentro,
  tarjetas en 3 columnas (1 en móvil): nombre, valor actual (`GET indicators/{id}/value` / gráfico Recharts con
  `charts/{id}/data`), rango (texto), **switch "Mostrar en Pulso"** (`PUT .../my-pulse`, por usuario), botón "Rango"
  (`RangeModal` existente), chip de visibilidad (privado / compartido / compañía), editar y eliminar (`analytics.manage`,
  `ConfirmDialog`).
- **Editor** (modal del kit, mismo padding del shell): nombre es/en, fuente de datos (`GET data-sources`), campo y función de
  agregación (de la fuente), filtro con filas campo · operador · valor (mismo JSON que valida el API; reutiliza el DSL de
  `kernel/dsl` de F1), módulo de negocio, "es dinero", visibilidad, rango por defecto, orden global (`sortOrder`), "Mostrar en
  Pulso por defecto". Gráficos añaden **agrupar por** y **tipo** (barra, dona, línea). Errores por campo del servidor al
  formulario.
- Nota al pie de la maqueta ("Los indicadores se calculan en el servidor con el rango…").
- Pruebas: agrupación por módulo, switch llama a `my-pulse`, editor envía el cuerpo correcto.

### P4 — Roles y usuarios + PIN de la app (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/users` (`admin.users` **o** `admin.roles`: el segmento solo muestra la pestaña permitida). Exactamente la
maqueta:

- **Roles** (`GET roles`, `GET permissions`): tabla ordenable Rol · Permisos (n / total) · Usuarios · acciones. Chips
  "sistema" / "plantilla" (`isSystem`, `isTemplate`); eliminar deshabilitado con usuarios (mensaje del API en toast).
  Modal "Nuevo rol"/"Editar rol": nombre y descripción es/en, casillas de permisos **agrupadas por `category`** con icono del
  grupo del menú y "marcar todo el grupo". `PUT/DELETE` piden AAL2 (reauth del kit).
- **Usuarios** (`GET users` paginado, `QBox`): Nombre · Correo · Roles (chips; clic → modal multi-selección, `PUT
  {id}/roles`, AAL2) · Permisos extra ("+n" / "Añadir" → modal con los del rol marcados y bloqueados "(del rol)", extras
  togglables, `PUT {id}/permissions`, AAL2) · Estado (switch Activo/Suspendido → `PUT {id}/membership`) · MFA · Último
  acceso · **PIN app** (solo con módulo `WMS_LOTSERIAL` y `devices.manage` o `admin.users`): chip "Sí/No" (`hasPin`) y
  acciones "Asignar/Restablecer PIN" (modal con PIN y confirmación; `PUT {id}/pin`, AAL2; errores exactos del API, p. ej.
  "El PIN no puede ser una secuencia trivial.") y "Quitar PIN" (`DELETE {id}/pin`, `ConfirmDialog`). Acción "Cerrar
  sesiones" (`DELETE {id}/sessions`). Botón **"Nuevo usuario"**: correo, nombre, roles, contraseña opcional (`POST users`).
  Editar nombre/activo (`PUT {id}`).
- **Mi cuenta**: pestaña nueva **"PIN de la app"** (`GET/PUT/DELETE /me/pin`, pide la contraseña actual) solo con el módulo
  `WMS_LOTSERIAL` encendido; muestra estado, bloqueo (`lockedUntilUtc`) y fecha.
- Fuera de F8a: alcances de datos (`data-scopes`) → F8b.

### P5 — Aparatos móviles (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/devices` (`devices.manage`, módulo `WMS_LOTSERIAL`). Sin maqueta: sigue el patrón lista + modal del kit y el
mini-mock de la Decisión 5.

- Tabla ordenable: Código · Nombre · Modelo · Almacén por defecto · Estado (chip) · Último latido · Versión de la app
  (`GET devices`, `QBox`). Filtro "incluir inactivos".
- "Nuevo aparato": código (opcional, el API lo genera), nombre, modelo, almacén por defecto (`useWarehouses`), tema. Al
  guardar, modal **"Código de registro"** con el código en grande (mono), botón copiar y el texto de la app: "Escríbelo en la
  pantalla Registrar de Teikem Almacén" (el código solo se muestra una vez; el API no lo devuelve después).
- Ficha/acciones: renombrar, cambiar almacén por defecto y tema (`PATCH`), desactivar/reactivar (`POST …/deactivate|reactivate`,
  `ConfirmDialog`), **"Nuevo código de registro"** (`POST …/enroll-code`, mismo modal). `rowVersion` en el `PATCH` para
  concurrencia (mensaje del API si cambió).

### P6 — Catálogos de valores (grupo Sistema) (orden 3, agente: pantalla)

Ruta `/system/catalogs` (`admin.catalogs`). Sin maqueta: mini-mock de la Decisión 5. Maestro-detalle:

- Izquierda: lista de dominios (`GET catalogs/domains`) con `QBox`, chip "sistema" / "propio", "Nueva lista"
  (`POST catalogs/lists`: nombre es/en, descripción, valores iniciales) y eliminar lista propia (`DELETE lists/{domainKey}`).
- Derecha: tabla ordenable de valores (`GET catalogs/{entity}`): Código · Etiqueta (es) · Etiqueta (en) · Orden · Habilitado
  · Origen (chip: Sistema / Ajustado / Propio). Valor de sistema: "Ajustar" (etiquetas, habilitado, orden → `PUT
  {entity}/{code}/override`) y "Restaurar" (`DELETE …/override`). Valor propio: crear (`POST {entity}`), editar (`PUT
  {entity}/{code}`), desactivar (`DELETE`, soft) y restaurar (`POST …/restore`). Al guardar, invalida la caché de
  `kernel/catalogs` para que el resto de la app vea la etiqueta nueva sin recargar.
- Móvil: dominios como selector arriba, tabla debajo en tarjetas.

### Recorrido Playwright (`web-app/e2e/f8a.spec.ts`)

1. `teikem+admin@cerevelo.com` → el menú muestra Operación, Almacén, Análisis y Sistema (Catálogo, Contabilidad y Portal no,
   por estar vacíos). `/` abre la paleta; escribir "usu" y Enter lleva a Roles y usuarios. El botón de tema cambia
   `data-theme` y persiste al recargar.
2. Análisis → Indicadores: apagar "Mostrar en Pulso" del indicador de almacén "Productos bajo mínimo"; Pulso ya no lo
   muestra; encenderlo de nuevo, aparece.
3. Pulso → Organizar: mover "Actividad reciente" arriba de "Tus indicadores", ocultar "Almacén", Listo; recargar: el orden
   se conserva y Almacén no aparece; Organizar → "Mostrar" Almacén → vuelve. `teikem+dispatch@cerevelo.com` no ve ese
   orden (es por usuario).
4. Sistema → Roles y usuarios: crear rol "Auditor E2E" con 2 permisos (pide reauth); crear usuario `e2e+{ts}@cerevelo.com`
   con ese rol; asignarle PIN `2846` (reauth) → chip "Sí"; PIN `1234` → mensaje "El PIN no puede ser una secuencia
   trivial."; quitar PIN → "No"; suspender → chip Suspendido.
5. Sistema → Aparatos: crear "Tablet E2E"; se muestra el código de registro; "Nuevo código de registro" muestra otro;
   desactivar y reactivar.
6. Sistema → Catálogos: ajustar la etiqueta española de un valor de `WarehouseTaskType`; en Pulso (panel Almacén) la lista
   de tipos muestra la etiqueta nueva sin recargar; restaurar.
7. Mi cuenta → PIN de la app: fijar con contraseña actual; quitar.
8. Proyecto móvil (Pixel 7): Pulso, Roles y usuarios y Catálogos sin scroll horizontal; paleta desde el icono de lupa.

### Cierre

`npm run check` verde, Playwright verde en CI, `docs/frontend/loteF8a-decisiones.md`, capítulo
`docs/manual/frontend/f8a-sistema-analisis-y-pulso.md` con capturas, FAQ (cada mensaje de error del lote).

---

## Parte 3 — Lo que queda para después (sin retroceder nada)

**F8b (siguiente lote de Sistema/Análisis, todo con backend listo)**: Vistas e informes; Campos personalizados
(definiciones); Estatus (overrides, capacidades por estatus, entradas laterales — la maqueta las pone en "Ajustes de la
compañía"); Seguridad y auditoría (`/audit/changes`, `security-events`, `activity` + CSV); Ajustes de la compañía (módulos
con dependencia y switch AAL2, settings, feriados y días laborables); alcances de datos por usuario; Administración de
plataforma (tenants).

**Siguen en sus lotes ya definidos**: F2 Clientes y contratos + Consignatarios (backend listo), F3 Órdenes completa, F4
Flota y choferes (backend listo), F5 Despacho, monitoreo y escaneo (backend listo); 7B Operación y 7C Contabilidad
(backend); prueba de campo de la app 8A. Los ríos de Operación, "Necesita tu decisión" y el Radar del Pulso llegan con F3/F5/7C
y solo agregan secciones al orden que P2 deja listo.

**Sin backend (fuera hasta que exista)**: Impresoras y labels, Integraciones / API, colores y logo de la compañía, Portal
de clientes, Contabilidad.

---

## Parte 4 — Decisiones que necesito de Luis

1. **Nombres del grupo Almacén.** Propongo renombrar solo "Recepción" → "Recibo" (1:1 con la maqueta) y conservar la
   partición de F6 (Ubicaciones dentro de Almacén; Kárdex y Ajustes dentro de Inventario; Proveedores y Órdenes de compra
   separados; Tareas de almacén y Citas de muelle como ítems). ¿De acuerdo, o quieres el menú exactamente como la maqueta
   (10 ítems con esos nombres) aunque cambie la navegación de F6?
2. **Qué son "los catálogos".** F8a incluye **Catálogos de valores** (listas: tipos, motivos, unidades…). El grupo
   **Catálogo** de la maqueta (Clientes y contratos, Consignatarios, Choferes, Flota) es F2 + F4, con backend listo. Propongo
   hacer **F2 inmediatamente después de F8a**. ¿Bien así, o prefieres F2 antes que F8a?
3. **Organizar el Pulso por usuario** (cada quien su orden y sus ocultos; el `SortOrder` global de `analytics.manage` es
   el punto de partida). Alternativa: un solo orden para toda la compañía. Recomiendo por usuario (es lo que ya hace el
   backend con "Mostrar en Pulso" y el rango).
4. **Ríos de Operación, "Necesita tu decisión" y Radar** quedan para F3/F5/7C. Confirmar.
5. **Mini-mock antes de codificar** para las tres pantallas sin maqueta (Aparatos, PIN en Usuarios y Mi cuenta, Catálogos de
   valores) en `docs/frontend/mock-f8a-sistema.html`, como se hizo en F7A. Confirmar (un día menos de sorpresas).
6. **Detalles del shell**: paleta de comandos, reloj y tema en F8a (recomendado, son baratos). ¿Banderas 🇵🇷/🇺🇸 en el
   selector de idioma como la maqueta? (Por defecto, no.)
