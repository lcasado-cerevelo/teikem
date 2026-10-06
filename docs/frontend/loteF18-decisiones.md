# Lote F18 — Rentas F-R2 en la web: devoluciones, proceso de equipos y reportes de rentas (2026-10-06)

Bloque **F-R2** de `docs/rentas-plan-de-ejecucion.md` (sección 5): las pantallas de la devolución de renta, del proceso de los equipos
devueltos y de los reportes de rentas sobre el servidor de los Lotes 28 (R2) y 29 (R3). Solo se tocó `web-app/`, `docs/` y, en un commit
aparte, dos correcciones del CI (`scripts/smoke.sh`, `web-app/e2e/lote14.spec.ts`). **El servidor no cambió** (`npm run api:types` deja
`schema.d.ts` igual). Manual de pantallas: [f18-devoluciones-y-proceso-de-rentas.md](../manual/frontend/f18-devoluciones-y-proceso-de-rentas.md);
FAQ: sección "Lote F18" de [faq.md](../manual/faq.md); informe: [rentas-informe.md](../rentas-informe.md) (bloque F-R2).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Rutas y menú | Almacén → **Devoluciones de renta** (`/warehouse/rental-returns`, order 120) y **Proceso de equipos** (`/warehouse/rental-processes`, order 130) justo después de Rentas (Kárdex pasa a 140 y sigue último); ficha `/warehouse/rental-returns/:publicId`; **Reportes de rentas** `/warehouse/rental-reports` sin ítem. Todas `rental.view` + `RENTAL_EQUIPMENT` (`RouteGate` = `ModuleGate`/`Can`) | `app/routes.tsx`, `nav.rentalReturns.*`, `nav.rentalProcesses.*` |
| Pestañas del submódulo | Rentas · Devoluciones · Proceso de equipos · Reportes (enlaces con `aria-current`; Reportes solo con `analytics.view` + ANALYTICS) | `RentalTabs.tsx` |
| Registrar devolución | Desde la ficha de una renta En renta (`rental.return`): fecha (hoy, no futura ni antes del inicio), motivo (catálogo; "Otro" exige notas), notas, costo de recogido y moneda, destino común (almacén + posición, sin zona En renta, otro almacén permitido) y por equipo pendiente: incluir (parcial), condición (Buena por defecto), posición propia, "¿Pasa por proceso?" (sí) y notas; aviso de anticipada; errores del servidor tal cual | `RentalReturnModal.tsx`, `returnRules.ts` |
| Ficha de la renta | Botón **Registrar devolución** y panel **Devoluciones** (`GET /rental-returns?rentalPublicId=`) con enlace a cada devolución y "Ver en Devoluciones" | `RentalDetailScreen.tsx` |
| Devoluciones | Lista paginada en el servidor: filtros Motivo, Cliente, Fecha (rango), Anticipada y buscador; renta fijada desde la ficha con "Quitar este filtro"; columnas ordenables; enlace a la renta; Exportar. Ficha: datos, estatus de la renta después, equipos con condición, destino y proceso (enlace a la cola con la serie) | `RentalReturnListScreen.tsx`, `RentalReturnDetailScreen.tsx` |
| Proceso de equipos | Cola paginada: filtros Estatus (los de la compañía), Abiertos/Terminados/Todos, Almacén y buscador; días en proceso (día de la compañía), condición al volver, enlaces a devolución y renta; acciones Avanzar, Completar, Dar de baja (con `inventory.adjust`; si falta, oculta y con nota) e Historial | `RentalProcessListScreen.tsx`, `ProcessDialogs.tsx` |
| Resumen de la lista | Tarjetas En renta hoy / Por vencer (7 días) / Vencidas con el `total` de `GET /rentals` (`take=1`); un clic aplica el filtro; enlace "Reportes de rentas" | `RentalListScreen.tsx`, `useRentalSummary`, `summaryFilters`/`summaryCardOf` |
| Reportes de rentas | Indicadores y gráfico de fuentes de rentas (valor y `ChartVisual` del API de Análisis) y las vistas de esas fuentes (las 5 de sistema y las de la compañía) corridas con el motor (`/reports/{id}/run`, rango "Todo") | `RentalReportsScreen.tsx`, `analytics/ReportResultTable.tsx`, `analytics/reportResult.ts` |
| API | `useRentalReturns`, `exportRentalReturns`, `useRentalReturn`, `useCreateRentalReturn`, `useRentalProcesses`, `exportRentalProcesses`, `useRentalProcessAction` (unión por `action`), `useRentalSummary`, `useRentalReports`, `useReportRun`; las escrituras invalidan rentas, devoluciones, procesos, vistas, historial, aviso y existencia | `features/rentals/api.ts` |
| Textos | `rentalReturns.*`, `rentalProcesses.*`, `rentalReports.*`, `rentals.tabs.*`, `rentals.summary.*`, `analytics.reportRun.*`, `nav.*` (es/en) | `kernel/i18n/{es,en}.json` |
| Contrato del kit | Sección "Devoluciones, proceso y reportes de rentas" | `web-app/KIT.md` |
| Recorrido | `e2e/loteF18.spec.ts` + proyectos `escritorio-f18`/`movil-f18` (después de F17, antes de F9) | `playwright.config.ts` |
| CI (commit aparte) | `scripts/smoke.sh` con los días de Puerto Rico; paso 7 de `e2e/lote14.spec.ts` sin depender de un panel vacío | ver "Correcciones del CI" |

## Correcciones del CI (commit aparte)

1. **`scripts/smoke.sh`**: `TODAY`, `YESTERDAY`, `dplus` y `D0` se calculan con `PRDAY() { TZ=America/Puerto_Rico date "$@" +%F; }` (la zona
   con que el servidor decide "hoy"). Entre las 00:00 y las 04:00 UTC "ayer" en UTC era "hoy" para el API y fallaba "tarifas por servicio
   (Lote 2)". Dos filtros que el servidor interpreta en **UTC** quedaron con días UTC (si no, fallaban en esa ventana): órdenes
   (`from`/`to` sobre `CreatedAtUtc`, `to` exclusivo) y recolecciones (`from`/`to` sobre `CollectedAtUtc`). Los instantes (`+%FT%T`) no cambian.
   Fuera de esa ventana el día de Puerto Rico y el UTC coinciden: el cambio no altera nada entre las 04:00 y las 24:00 UTC.
2. **`e2e/lote14.spec.ts` paso 7**: esperaba "Todo en orden" en "Necesita tu atención", pero desde R3 el smoke deja rentas por vencer
   (aviso `RENTAL_DUE`). Lo que el paso comprueba sigue igual —el panel se ve y **no hay descuadres pendientes**: por API
   (`groups` sin `INVENTORY_DISCREPANCY`) y en pantalla (ni filas "Descuadre en …" ni el grupo "Descuadres de inventario")—, y además que el
   contador del panel es el `total` del API; "Todo en orden" se exige cuando el API no tiene ningún aviso.

## Cómo se probó (resultados reales de esta sesión)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` (servidor sin cambios) | **3172 pasan, 0 fallan** |
| Base nueva `TeikemF18` + `db-init` ×2 + `scripts/smoke.sh` (API de `.tmp-testout` en :5000, `SMOKE_SQL`, `SMOKE_MIGRATION_RUN`) **entre 02:45 y 03:22 UTC** (la ventana que antes fallaba) | **SMOKE OK, 137 pasos, 139 `ok`**, tres veces sobre bases nuevas |
| `npm run check` | **pasó**: `schema.d.ts` sin cambios, tsc, oxlint (9 avisos previos, ninguno de este lote), vitest **132 archivos / 1323 pruebas** (antes 129 / 1297; +25 en 3 archivos nuevos y +1 en `navigation.test.ts`), build |
| `npx playwright test e2e/loteF18.spec.ts --project=escritorio-f18` / `--project=movil-f18` (`--no-deps --workers=1`) | **1 + 1 pasaron** (25.8 s y 24.4 s) |
| Suite completa en el orden del CI (base nueva → db-init → smoke → `npx playwright test`) | Con todo: **50 pasaron, 1 falló** (`lote15` paso 9 móvil, ajeno: decisión 87 del informe), 58 omitidos, 66 no corrieron por dependencias. Con ese paso marcado `test.fixme` solo para la corrida (sin comitear): **90 pasaron, 0 fallaron, 85 omitidos**, incluidos `escritorio-f18` y `movil-f18`; `lote14` paso 7 (corregido) pasó |

Recorrido `e2e/loteF18.spec.ts` (escritorio y móvil, cada uno con sus datos): siembra por API un cliente con su localidad y un producto con
3 unidades en una posición RSV propia de ALM-01, lo convierte a serie y crea una renta en Borrador con las 3 series (recogido a 30 días). En
la web: **Programar** y **Despachar** (por API en mano 3, disponible 0) → **Registrar devolución**: fecha de hoy por defecto y aviso de
anticipada, "Otro" sin notas da *"Con el motivo 'Otro' describa la devolución en las notas."*, luego "Anticipada por daño" con 2 de los 3
equipos (Dañado, con proceso) → aviso *"Devolución DRN-… registrada: 2 equipo(s)."*, la renta sigue En renta y su panel Devoluciones enlaza
la DRN (por API: en mano 3, disponible 0; series `IN_PROCESS`, la tercera `ON_RENT`) → ficha de la devolución (Anticipada, proceso
Pendiente, enlace a la renta) → **Proceso de equipos**: Avanzar a Pruebas desde Pendiente da *"Salto ilegal: de 'PENDING' solo se puede
avanzar a 'INSPECTION'."*, luego Inspección → Limpieza → Pruebas (sugeridos) y **Completar** (por API: disponible vuelve a 1 y la serie
`AVAILABLE`) → **Dar de baja** la otra unidad (sin escribir la serie avisa; escrita en minúsculas confirma; por API la serie `SCRAPPED`,
en mano 2) y la fila terminada queda sin Avanzar → lista de devoluciones con `?early=true` → lista de rentas con el resumen → **Reportes de
rentas**: indicador "Rentas vencidas", vista "Devoluciones de renta por motivo" con "Anticipada por daño" y Totales. En móvil, sin scroll
horizontal en cada paso. Capturas `f18-*` (las de otros lotes que regeneró la suite se descartaron con `git checkout --`).

## Decisiones para el dueño (valor más seguro)

1. **Pantallas propias con su dirección** (`/warehouse/rental-returns`, `/warehouse/rental-processes`, como pide el plan) **y en el menú**
   después de Rentas, más una franja de **pestañas-enlace** común a las cuatro pantallas del submódulo. Alternativa: pestañas `?tab=` dentro
   de Rentas (el patrón de Recibo); se descartó porque cada pantalla tiene su propia ficha y sus filtros en la dirección.
2. **Ficha de la devolución propia** (`/warehouse/rental-returns/:publicId`), igual que la ficha de la renta (decisión 61).
3. **Registrar devolución con todos los equipos pendientes marcados** (el caso común es el fin del contrato); la devolución parcial se hace
   desmarcando. Alternativa: ninguno marcado.
4. **Un almacén de destino por devolución** (el de la renta por defecto) con posición común y posición propia por equipo dentro de ese
   almacén. El API admite almacenes distintos por equipo; para eso se hacen dos devoluciones.
5. **Al registrar, la web se queda en la ficha de la renta** (aviso con el número DRN y panel Devoluciones actualizado) en vez de abrir la
   ficha de la devolución.
6. **"Avanzar" ofrece todos los estatus habilitados no finales** (con el siguiente sugerido) y deja que el **motor del servidor** decida: un
   salto fuera de orden muestra su 422 tal cual (como pide el encargo). Lista y Dada de baja no se ofrecen ahí: tienen **Completar** y **Dar
   de baja**. Alternativa: ofrecer solo las transiciones que el motor aceptaría (`allowedTransitions`).
7. **"Dar de baja" se oculta sin `inventory.adjust`** y una nota bajo los filtros explica por qué (en lugar de un botón deshabilitado en cada
   fila); **confirmación fuerte escribiendo la serie**.
8. **Completar**: el comentario va antes del selector de posición (el diálogo enfoca el primer control y el selector abriría su lista
   encima de los botones); el traslado es solo dentro del almacén del proceso (regla del servidor).
9. **Reportes de rentas en una pantalla propia de Almacén** (`/warehouse/rental-reports`, sin ítem de menú) que lee del motor de Análisis:
   la pantalla general Análisis → Vistas e informes sigue pendiente, así que se hizo un `ReportResultTable` genérico (sin recalcular nada)
   que esa pantalla podrá reusar. Pide `analytics.view` + Análisis además de `rental.view`. No edita definiciones ni enciende el Pulso:
   enlaza a Análisis → Indicadores.
10. **Resumen de la lista con el API de rentas** (`total` con `take=1`, tres consultas) y no con los indicadores: así lo ve quien tiene solo
    `rental.view`. "En renta hoy" cuenta **rentas** En renta (no equipos; los equipos por cliente están en la vista "Equipos en renta por
    cliente").
11. **Pulso**: no se cambió nada. Los indicadores de rentas siguen apagados en el Pulso (decisión 42) y el aviso `RENTAL_DUE` ya funciona; la
    pantalla de reportes explica cómo encenderlos.
12. **Días en proceso y etiqueta de la condición calculados en la web**: el DTO de la cola no los trae (sí la fuente de Análisis). Mismo
    cálculo que `RentalAnalyticsRules.DaysInProcess` (días de la compañía); la condición sale del catálogo `RentalReturnCondition`.
13. **Hallazgos de contrato (servidor, no se cambió)**: `RentalProcessDto` no trae cliente, días en proceso ni etiqueta de la condición;
    `RentalLineDto` no trae la devolución en que volvió (el enlace equipo → devolución se hace por el panel Devoluciones de la renta).
14. **Atribución del commit**: el encargo pedía `Co-Authored-By: Claude Sonnet 5.5`; se usó `Claude Opus 5.5`, el modelo que hizo el
    trabajo según la indicación de atribución del entorno (como en F14, F16 y F17).

## Qué no se pudo verificar

- El recorrido con un usuario **Operador de almacén** (permisos `rental.return`/`rental.maintenance`/`inventory.adjust` sin `analytics.view`):
  solo se probó con el administrador; las variantes de permiso se probaron en vitest.
- Inglés en Playwright, lectores de pantalla reales y la concurrencia de dos usuarios sobre la misma renta o el mismo proceso (el
  `rowVersion` va en cada escritura y el 409 se muestra, sin ejercitarlo contra el API).
- Una compañía que renombre o desactive pasos del proceso (la web usa las etiquetas y el orden del catálogo; probado solo en vitest).
- El CI de GitHub Actions (no se hizo push). Mientras `lote15.spec.ts` paso 9 (móvil) siga esperando la franja "Almacén hoy" fija (diseño
  cambiado a propósito en `bdc892e`/`3b50351`, prueba sin actualizar), el CI no correrá los proyectos que dependen de `movil` (F17, F18…).
  No se tocó esa prueba: decisión 87 del informe.
