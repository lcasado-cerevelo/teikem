# Frontend web de operación — diseño y modelo de agentes

Fecha: 2026-09-27. Estado: **propuesta para aprobación de Luis**. Nada de esto está construido.

El backend (Lotes 1 a 6) expone 371 endpoints en 47 controladores, con JWT, permisos `recurso.accion`, módulos por
tenant, catálogos multilingües, estatus con etapas, campos personalizados, vistas, indicadores y gráficos. El frontend
web consume ese API tal cual: no se agrega lógica de negocio en el navegador salvo la que el propio API ya define como
compartida (el evaluador de reglas del DSL, opción A aprobada). La maqueta `Diseño/teikem-mockups.html` es la
especificación visual y de flujo pantalla por pantalla; el manual funcional `docs/manual/` es la especificación de
comportamiento (validaciones, mensajes, transiciones).

---

## 1. Tecnología

| Capa | Elección | Por qué (y por qué ahorra trabajo de agentes) |
|---|---|---|
| Aplicación | **React 18 + TypeScript + Vite** | Estándar de facto; los agentes lo conocen sin explicación; compila y arranca en segundos. |
| Rutas | React Router | Una ruta por pantalla de la maqueta; carga diferida por módulo. |
| Datos del servidor | **TanStack Query** + cliente **generado desde Swagger** (`openapi-typescript` + `openapi-fetch`) | Los tipos de los 371 endpoints se generan con un comando: cero DTOs escritos a mano, cero desincronización con el API. Es el mayor ahorro de tokens del proyecto. |
| Formularios | react-hook-form + zod | Validación en cliente declarativa; los mensajes del servidor (ProblemDetails con `errors` por campo) se mapean al formulario con un solo helper. |
| Estilo | **CSS propio con variables** (portado de la maqueta) + un kit de componentes interno | La maqueta ya tiene el lenguaje visual (`.pal/.pi/.pb/.ft`, `.chip`, `qbox`). Se porta una vez en F1 y todas las pantallas lo reutilizan. Sin Tailwind: evita que los agentes generen "sopa de clases" larga y costosa. |
| Tablas | TanStack Table | Ordenamiento por columna en toda tabla (convención del documento), paginación del servidor, sin scroll horizontal. |
| Mapas | Leaflet + OpenStreetMap | Lo fija el documento maestro. Solo en F5 (monitoreo). |
| Gráficos | Recharts | Barra, dona y línea, que es lo que define el módulo de gráficos. |
| Idioma | Diccionario JSON es/en propio (`t('clave')`) | El API ya devuelve etiquetas de catálogo por `Accept-Language`; el frontend solo traduce su propio cromo. Cambiar idioma no reinicia la pantalla (convención). |
| Pruebas | Vitest + Testing Library; **Playwright** contra el API real | Playwright es el equivalente del `smoke.sh`: recorre las pantallas con datos reales creados en el momento. |
| Entrega | `npm run build` → zip estático (`dist/`) | El documento pide API y frontend en zips separados. Se sirve desde cualquier servidor estático o desde el propio Kestrel del API. |

## 2. Arquitectura de la aplicación

```
web-app/
  src/
    app/            arranque, rutas, layout (barra lateral por módulos, cabecera con tenant, idioma, usuario)
    kernel/         lo transversal, se construye UNA vez en F1:
      auth/         login, refresh con rotación, reauth AAL2 (modal de contraseña o TOTP), MFA, sesiones
      access/       <Can perm="orders.create">, <ModuleGate key="CATALOG">, menú filtrado por /me
      api/          cliente generado + interceptor de token + mapeo de ProblemDetails a errores de formulario
      catalogs/     caché de LookupCode y StatusCode por dominio (etiquetas, colores, StageKind)
      status/       <StatusPipeline> (dibuja etapas pipeline/lateral/terminal desde StatusCode) y <StatusActions>
      custom-fields/ renderizador de campos personalizados a partir de las definiciones del API
      dsl/          RuleEvaluator en TypeScript, misma semántica que el del servidor, con los mismos casos de prueba
      analytics/    widgets de vistas, indicadores, gráficos y Pulso (el motor corre en el servidor)
      ui/           kit: Panel, DataTable, Filters, QBox, Chip, Modal, Form fields, Toast, ConfirmDialog, EmptyState
      i18n/         diccionario es/en
    features/       una carpeta por módulo, espejo de los lotes del backend
      security/  clients/  orders/  fleet/  dispatch/  warehouse/
  e2e/              Playwright: un archivo por lote de frontend
```

Patrones fijos (los agentes no los reinventan; los copian):

- **Pantalla de lista**: `Filters` (dropdowns y fechas) + `QBox` (texto libre, aplicado después de los filtros) + `DataTable`
  paginada del servidor + acciones por fila con guardas de estatus (`capabilities` que ya devuelve el API).
- **Pantalla de ficha**: cabecera con `StatusPipeline` y acciones permitidas, pestañas por sección, formularios con
  `react-hook-form` y errores del servidor por campo.
- **Modal de alta** con el mismo padding y tipografía del shell (convención del documento).
- **Errores**: todo ProblemDetails se muestra con su `title`; los `errors` por campo van al formulario; `module_disabled`
  y `forbidden` tienen pantalla propia.

## 3. Lotes de frontend

Cada lote de frontend consume un módulo del backend ya terminado y se cierra con: `npm run check` (tipos, lint, pruebas
unitarias, build) en verde, Playwright en verde contra el API real en CI, capítulo del manual con capturas de pantalla, y
`docs/frontend/loteFN-decisiones.md`.

| Lote | Pantallas (nombres de la maqueta) | Depende de |
|---|---|---|
| **F1 Núcleo y plataforma** | Esqueleto, login, MFA, reauth, menú por módulos y permisos, cambio de tenant e idioma; Roles y usuarios, Seguridad y auditoría, Catálogos, Estatus (pipeline y capacidades), Campos personalizados, Vistas, Indicadores, Gráficos, Pulso del día, Módulos, Ajustes del tenant, Administración de plataforma. Kit de componentes, cliente generado, DSL en TypeScript. | Lote 1 |
| **F2 Clientes y contratos** | Clientes y contratos (ficha maestro-detalle: perfil, numeración, contactos, direcciones y punto de recogido, contratos con modelo de facturación, tarifas con historial, servicios especiales, usuarios de portal), Consignatarios. | Lote 2 |
| **F3 Órdenes** | Entrada de órdenes (rápida, detallada, especial), Órdenes (listado, ficha, estatus, cancelar, eliminar), cotización y crédito, importador en dos pasos, plantillas de importación. | Lote 3 |
| **F4 Flota y choferes** | Flota y mantenimiento (Vehículos, Documentos por vencer, Mantenimiento preventivo, Órdenes de trabajo, Combustible), Choferes y tarifas (maestro-detalle con tarifas por entrega, intento y viaje, política de pago). | Lote 4 |
| **F5 Despacho y rutas** | Sala de despacho (planificar el día, rutas, sin asignar, optimizar, reordenar, despachar), Monitoreo de rutas con mapa Leaflet y pines, Estación de escaneo (salida, con voz), Zonas de despacho. | Lote 5 |
| **F6 Almacén e inventario** | Almacenes y ubicaciones, Productos e inventario, Kárdex, Recibo, Tareas de almacén, Conteo cíclico, Recolección y empaque, Compras (proveedores y órdenes), Ajustes de inventario, Cruce de muelle. | Lote 6 |
| **F7 Cierre** | Pulso y dashboards completos, exportaciones CSV, impresoras y etiquetas (si el backend lo trae), accesibilidad, rendimiento, empaquetado del zip, manual de usuario con capturas de todo. | F1 a F6 |

Quedan fuera hasta que exista su backend: Facturación, Liquidación, Procesar entregas (COD), Portal de clientes,
Integraciones, Equipos en alquiler, Contabilización de compras y despachos.

## 4. Modelo de agentes: mínimo consumo sin perder efectividad

### Qué costó en el backend y por qué

| Lote | Agentes | De ellos, refutadores | Tokens de agentes |
|---|---|---|---|
| 2 | 150 | 127 (85 %) | 11.3 M |
| 3 | 337 | 281 (83 %) | 11.9 M |
| 4 | 172 | 141 (82 %) | 14.7 M |
| 5 | 132 | 103 (78 %) | 13.0 M |
| 6 | 189 | 157 (83 %) | 16.7 M |

Tres cosas concentraron el gasto: (1) **dos refutadores por hallazgo en cuatro rondas** (más del 80 % de los agentes);
(2) **cada agente recibía el plan completo** (90 KB de JSON) en su prompt; (3) **esfuerzo "xhigh" en todos los revisores y
refutadores**. En el backend eso estaba justificado: cada hallazgo podía ser una fuga entre tenants o un descuadre de
inventario. En el frontend la mayoría del riesgo lo atrapan herramientas deterministas gratis (compilador de TypeScript,
tipos generados del API, lint, pruebas de Playwright), así que el gasto se reparte distinto.

### Diseño propuesto

**Principio: primero lo determinista, después el agente barato, y el agente caro solo donde hay criterio.**

| Etapa | Antes (backend) | Ahora (frontend) | Ahorro |
|---|---|---|---|
| Diseño | 3 lectores + crítico + 3 arquitectos + 2 jueces + síntesis (10 agentes, ~2 M) | **F1**: igual, una sola vez, porque fija la arquitectura. **F2 a F7**: 1 lector (sonnet) que extrae de la maqueta y el manual la lista de pantallas y campos + 1 arquitecto (opus, high) + 1 crítico (sonnet). 3 agentes. | ~70 % por lote |
| Contexto | plan completo a cada agente | cada agente recibe **solo su pieza**, el contrato del kit (`docs/frontend/kit.md`, 2 páginas) y el recorte de OpenAPI de su módulo; el plan completo queda en disco y el agente lee lo que necesita | ~40 % del prompt de cada agente |
| Implementación | opus xhigh en todo | **kernel y pantallas complejas** (F1, despacho, escaneo, DSL): opus, high. **Pantallas de lista y ficha**: sonnet, medium, partiendo de la plantilla de pantalla del kit. | ~50 % |
| Compuerta determinista | ninguna antes de revisar | `npm run check` (tsc, eslint, vitest, build) **debe pasar antes de que un agente revise nada**; el implementador corrige localmente. Playwright corre al final de cada lote. | gratis; elimina rondas de revisión por errores triviales |
| Revisión | 4 lentes × 4 rondas | **2 lentes** (paridad con el API y permisos/módulos en pantalla; pruebas y convenciones de interfaz), opus high, **máximo 2 rondas** | ~65 % |
| Refutación | 2 refutadores por hallazgo, xhigh | **solo hallazgos marcados "alta"** reciben 1 refutador (opus, medium); los de severidad media y baja se corrigen directo si cuestan menos de una pantalla, o se anotan | ~85 % de los refutadores |
| Documentación | 2 escribas sonnet | igual, más capturas de pantalla automáticas de Playwright para el manual | = |

Proyección: **4 a 6 M de tokens por lote de frontend** frente a 11 a 17 M en el backend, con los mismos criterios de cierre
(compila, pruebas verdes, recorrido end-to-end verde, manual escrito desde el código real).

### Agentes

| Agente | Modelo / esfuerzo | Papel |
|---|---|---|
| `fe-reader` | sonnet / medium | Extrae de la maqueta, el manual y Swagger la lista de pantallas, campos, acciones y endpoints de un lote. Solo lee. |
| `fe-architect` | opus / high | Plan del lote: piezas por pantalla, qué reutiliza del kit, qué agrega al kit, pruebas y recorrido Playwright. |
| `fe-implementer` | sonnet / medium | Pantallas de lista y ficha a partir de la plantilla del kit. Corre `npm run check` antes de devolver. |
| `fe-implementer-core` | opus / high | Kernel, kit, DSL, pantallas con interacción compleja (despacho, escaneo, mapa). |
| `fe-reviewer` | opus / high | Dos lentes: paridad con el API (tipos generados, permisos, módulos, mensajes) y pruebas/convenciones de interfaz. |
| `fe-verifier` | opus / medium | Refuta solo hallazgos de severidad alta. |
| `scribe` (existente) | sonnet / high | Capítulo del manual de usuario con capturas, FAQ y documento de decisiones. |

### Workflows

- `fe-diseno`: F1 con panel completo (una vez); F2 a F7 con lector → arquitecto → crítico.
- `fe-implementar`: kernel primero (si el lote lo toca) → pantallas en paralelo (2 a la vez, límite de la máquina) →
  `npm run check` → revisión de 2 lentes → refutación solo de "alta" → corrección → máximo 2 rondas → Playwright →
  documentación. Reanudable como los actuales.

## 5. Verificación y cierre de cada lote

1. `npm run check`: tipos, lint, pruebas unitarias (DSL con los mismos vectores que el backend, formateadores, `Can`,
   `matchesQ`), build.
2. Playwright contra el API real (SQL Server + `db-init` + API, igual que el smoke): inicio de sesión, recorrido de cada
   pantalla del lote creando datos con sufijo de tiempo, verificación de permisos (un despachador no ve el botón que no
   puede usar) y de módulo apagado. Guarda capturas para el manual.
3. CI: un job nuevo `frontend` en `.github/workflows/ci.yml` que corre 1 y 2.
4. Documentación: `docs/manual/frontend/0N-<pantalla>.md` con capturas, FAQ, y `docs/frontend/loteFN-decisiones.md`.

## 6. Decisiones que necesita Luis antes de F1

1. **Estilo visual**: portar el CSS de la maqueta tal cual (misma apariencia que ya conoces) o rediseñar. Recomendación:
   portar tal cual; el rediseño se puede hacer después cambiando variables.
2. **Cómo se sirve**: zip estático aparte (como pide el documento) o servido por el propio API en `/app`. Recomendación:
   ambos disponibles; el zip es la entrega formal.
3. **Navegadores**: solo escritorio moderno (Chrome, Edge, Safari, Firefox actuales) o también tabletas. Recomendación:
   escritorio y tableta; el móvil del chofer es la app del Lote 7, no esta.
4. **Nombre del paquete y ruta en el repo**: `web-app/` junto a `web/` (la landing) y `src/` (el API). Recomendación: sí,
   mismo repo, misma rama, mismo CI.
