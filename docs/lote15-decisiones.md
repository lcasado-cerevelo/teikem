# Lote 15 — Cambios de Almacén, tanda 5: Pulso del día (franja "Almacén hoy", gráficos de la compañía, indicadores por fila, filas fijas y días de Puerto Rico); qué se construyó y decisiones a revisar

> **Equivalencia:** este documento = Lote 5 del plan de cambios (plan 0+1 = 11, plan 2 = 12, plan 3 = 13, plan 4 = 14, plan 5 = 15).
> El repositorio ya tenía lote1…lote14, así que el cierre de cada lote del plan usa el siguiente número libre.

Fecha: 2026-09-30. Origen: el documento de cambios del dueño del producto sobre las pantallas de Almacén
(`F:\Download\Cambios.pdf`, la parte del Pulso) y las decisiones del dueño del 2026-09-30 (D1 a D16; ver más abajo). **D1 la cambió el
dueño respecto del plan: 7 barritas, fijas, contando hoy** (el plan decía 5). Cierra el backend y el frontend web de estas piezas.

Como los lotes 11 a 14, este documento cubre también el frontend web: no hay un `docs/frontend/loteF…-decisiones.md` aparte. El
manual funcional está en [`docs/manual/07-pulso-y-actividad.md`](manual/07-pulso-y-actividad.md) (sección 5, "Pulso del día"); el de
pantallas, en [`docs/manual/frontend/f7a-pulso-almacen-y-actividad.md`](manual/frontend/f7a-pulso-almacen-y-actividad.md) (con avisos
en [F1](manual/frontend/f1-nucleo-y-mi-cuenta.md) y [F8a](manual/frontend/f8a-menu-sistema-analisis-y-marca.md)); el mensaje de error
nuevo y las preguntas, en [`docs/manual/faq.md`](manual/faq.md) (sección "Lote 15").

## Nota sobre Advance Depot y Advance Solutions

No se escribió nada en Advance Depot ni en Advance Solutions. En la base local ambas compañías **nacieron con el código nuevo**
(`db-reset` + `import-legacy`, 0 rechazos), así que ya tienen los gráficos de la compañía y "Descuadres pendientes" sembrados. Solo se
leyeron con `SELECT`. La conversión de gráficos de una compañía **ya creada antes** del lote se probó en la demo Advance Logistics con
filas simuladas (ver "Cómo se prueba" y la decisión 9).

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| Esquema | **`dbo.ChartDefinition.SeedKey`** `NVARCHAR(40) NULL` (con `IF COL_LENGTH`, y en el `CREATE TABLE` para bases nuevas) e índice único filtrado **`UQ_ChartDefinition_SeedKey (TenantId, SeedKey) WHERE SeedKey IS NOT NULL`** (con `IF NOT EXISTS sys.indexes`). No hay más tablas ni columnas | `Diseño/logistica-db-estructura.sql`, `ChartDefinition.SeedKey` (`Analytics.cs`, `[NotAudited]`), `ExtensibilityConfigurations.cs` | — |
| Seed | Bloque nuevo "Lote 15" (idempotente, en `logistica-db-seed.sql`) para **las compañías ya creadas** con contenido de análisis sembrado: (1) convierte "Valor de inventario por categoría" (barras de sistema) en **dona de la compañía** con `SeedKey = INVENTORY_VALUE`; (2) convierte "Movimientos de inventario por tipo" en barras de la compañía con campo `Units` y `SeedKey = MOVEMENTS_BY_TYPE`, conservando el rango que la compañía haya puesto; (3) crea los que falten (ni `SeedKey` ni nombre, activos o borrados); (4) crea el indicador de sistema **"Descuadres pendientes"**. Órdenes 1 y 2 solo si siguen en los de fábrica (90 y 96) y la compañía no organizó sus gráficos. Al ser SQL no deja `AuditLog` (como el bloque 5b) | `Diseño/logistica-db-seed.sql`, `ChartSeedKeys` (`CatalogDomains.cs`), `SystemAnalyticsSeeder.cs` (compañías nuevas y la demo: mismo criterio, con `ConvertAsync`; helper `Chart()` con `isSystem` y `seedKey`; `Indicator("Descuadres pendientes", …)`) | — |
| Dominio | (sin tablas) | `WarehousePulseRules` (nueva, pura: 7 días locales con hoy, `IndexOf`, `ReceivedUnits`, `OutboundUnits`, `Series` que rellena días vacíos, tono naranja), `PulseDay`, `PulsePanels.WarehouseDay` (`WAREHOUSE_DAY`, orden −10; reusa `pulse.warehouse` + `inventory.view` + `WMS_LOTSERIAL`: no hay permiso nuevo) | — |
| API — franja | (usa `InventoryTransaction`, `CycleCount` y la consulta de productos bajo mínimo) | `WarehousePulseService` (una consulta agrupada en SQL por día local, tipo y motivo; conteos con diferencia repartidos por día en memoria; bajo mínimo con `ProductService.ListAsync(BelowMin, Take 1)`, la misma consulta del panel Almacén), `WarehousePulseContracts.cs` (`WarehousePulseQuery`, `WarehousePulseDayDto`, `WarehousePulseDaysDto`), registro en `DependencyInjection.cs` | `GET /api/v1/inventory/pulse/days?warehousePublicIds=&days=7` (`inventory.view`; módulo `WMS_LOTSERIAL` de la clase; `days` 1 a 14) |
| API — panel | (sin tablas) | Panel `WAREHOUSE_DAY` en `PulsePanels.All` (orden −10: primero también en los Pulsos ya organizados, porque Organizar guarda `índice × 10` desde 0) | `GET /api/v1/analytics/pulse` (trae un panel más: 6 para el administrador) |
| Motor de gráficos | (sin tablas) | `AnalyticsEngine.FoldOthers` ("Otras"/"Others", clave `$others`, en barras, dona y pastel con SUM o COUNT pasado el 8.º grupo); campo **`Units`** (`|Quantity|`, "Unidades movidas") en `InventoryTransactionDataSource`; `AnalyticsService.IsCompanyOwned` (gráfico sin dueño y no de sistema: lo edita o borra quien tenga `analytics.manage`; el rango por defecto también con `analytics.dates`) | `PUT` y `DELETE /api/v1/analytics/charts/{id}` (sin cambio de ruta; ahora aceptan los gráficos de la compañía) |
| Días de Puerto Rico | (sin tablas) | `DateRangeResolver` reescrito (días locales → instantes UTC con `ITenantClock`; `DataQuery.FromDay` y `ToDayExclusive`), `AnalyticsEngine.GroupKey` (agrupa por día local), campo `Date` de movimientos = día local (`DateOnly`), `TripDataSource` y `ContractDataSource` filtran su día de calendario con el rango local, `ClientDataSourceHelpers.Today()` y `FleetDocumentDataSource` usan el "hoy" local, `ActivityFeedService` (ventana `today` desde 00:00 PR). Se eliminó `ActivityRules.TenantZone` | Todos los que calculan indicadores, gráficos, vistas y exportaciones con rango `LAST7`, `LAST30`, `THIS_MONTH` o `CUSTOM` |
| Indicador | (usa `IndicatorDefinition`) | "Descuadres pendientes" (fuente `INVENTORY_DISCREPANCY`, estatus `OPEN`, rango `ALL`, orden 99, módulo Almacén, **apagado en el Pulso**) en el seeder y en el seed SQL | `GET /api/v1/analytics/indicators` (lo lista) |
| Contratos web y móvil | Sin tablas | `web-app/openapi.json`, `web-app/src/kernel/api/schema.d.ts` y `app-almacen/src/kernel/api/schema.d.ts` regenerados | — |
| Kit (web) | Sin tablas | `useElementHeight` (`kernel/ui/useElementWidth.ts`), `tenantZone.ts` (`TENANT_TIME_ZONE`, `zonedInputFromUtc`, `utcFromZonedInput`, `localDayOf`, `tenantToday`; `countView.ts` la importa), `KIT.md` | — |
| Frontend — gráficos | — | `ChartVisual.tsx`: **siempre se dibuja gráfico** (se quitó `CHART_LIST_MAX_POINTS` y la lista), línea con 12 puntos o menos pinta los puntos, tooltip con fecha larga, `role="img"` con `aria-label`, tamaño `mini` y color por punto, dona con el total al centro (compacto `$1.2M` si pasa de 10 caracteres). `ChartCard` del Pulso usa `ChartVisual`; se borró `.pulse-pts`. `chartPreview.ts` con "Otras" y días locales. `ChartsPage.tsx`: chip "De la compañía" y confirmación "no vuelve". 2 por fila en el Pulso (`PULSE_CHART_COLUMNS`, estilo en línea) | — |
| Frontend — franja | — | `WarehouseDayBand.tsx`, `warehouseDay.ts` (tarjetas, enlaces, rango), `warehouseFilterStore.ts` (almacén compartido con el panel Almacén: `useSyncExternalStore` sobre la misma clave de `localStorage`), `useWarehousePulseDays` (`staleTime` 60 s, refresco cada 5 min; `pulseDays` se invalida con las mutaciones de inventario), entrada `WAREHOUSE_DAY` con `pinnable` en `pulsePanels.tsx` | — |
| Frontend — filas fijas | — | `Pulse.tsx` (cabecera partida: `.pulse-pin-head` fija con fecha y "Organizar"; `.pulse-greet` se desplaza; `data-pinned` en la primera sección si es fijable y no se organiza; alturas medidas con `useElementHeight`), `pinnedPanelKey`, `pulse.css` (`.pulse-home`) | — |
| Frontend — indicadores por fila | — | `indicatorLines` (Operación, Almacén, Contabilidad), `IndicatorsRiver` (h2 "Tus indicadores" y un `div.pulse-line` con h3 por fila), `PulseOrganizer.tsx` (los indicadores se ordenan solo dentro de su fila; ayuda `linesHint` y `pinHint`) | — |
| Frontend — enlaces | — | Conteo cíclico lee la URL (`countFiltersFromUrl`: `warehousePublicIds`, `status`, `origins`, `from`, `to`); Productos e inventario lee `?warehousePublicIds=` (`kpi=low` ya existía); el Kárdex ya leía `types`, `from`, `to` y `warehousePublicIds` (Lote 14) | — |
| Humo | — | `scripts/smoke.sh`: bloque "Lote 15" tras el del Lote 14 (franja, 400, recibo, recolección y conteo, gráficos de la compañía, panel, "Descuadres pendientes" y, con `SMOKE_SQL`, un movimiento a las 23:59 de ayer en hora de PR); la lista de paneles del administrador de F8a pasa a 6 | — |
| Pruebas | — | Backend: `WarehousePulseRulesTests`, `WarehousePulseServiceTests`, `AnalyticsCompanyChartTests` (nuevas); ajustadas `AnalyticsSeedFieldsTests`, `PulseLayoutTests`, `ActivityRulesTests`, `TotpAndTextTests` (bordes de `DateRangeResolver` 03:59Z y 04:00Z) y `WmsControllerSecurityTests` (121 acciones). Web: `ChartVisual.test.tsx`, `warehouseDay.test.ts`, `tenantZone.test.ts`, `useElementHeight.test.tsx` nuevas; `Pulse`, `PulseOrganizer`, `WarehouseFilter`, `ChartsPage`, `DefinitionEditor`, `chartPreview`, `countView`, `CycleCountScreen` y `ProductListScreen` ajustadas. e2e: `lote15.spec.ts` (nuevo, 9 pruebas) y ajustes a `loteF1.spec.ts` y `f8a.spec.ts` | — |

Todo bajo el filtro de tenant existente. La franja no tiene tabla propia: cuenta lo que ya existe. Los gráficos de la compañía son
`ChartDefinition` (lleva `TenantId`) con `IsSystem = 0` y `OwnerUserId = NULL`, y responden igual que cualquier gráfico (visibilidad,
permiso de lectura de la fuente y módulo de negocio). La prueba de seguridad de controladores (`WmsControllerSecurityTests`) ya declara la
acción nueva (de 120 a 121).

## Decisiones del dueño del producto (2026-09-30)

Mandan sobre las opciones que había propuesto el arquitecto.

| # | Tema | Elegido | Cómo quedó |
|---|---|---|---|
| D1 | Días de las barritas | **7 barritas = los últimos 7 días, contando hoy; fijo, sin selector** (el plan decía 5) | La última barrita es hoy y crece en el día; un día sin movimiento es una barrita vacía. El número grande es lo de **hoy** y en letra pequeña va "7 días: N". "Hoy" empieza a la medianoche de Puerto Rico. El API acepta `days` de 1 a 14 (por defecto 7) para pruebas; la pantalla siempre pide 7 |
| D2 | Unidades de salida | **A:** recolección + cruce de muelle; eliminar una recolección resta ese día | `ISSUE` y `CROSSDOCK` suman su cantidad sin signo; el ajuste `PICK_BATCH_REVERSAL` resta. No cuenta daños, pérdidas, vencidos, conteos ni transferencias. "Unidades recibidas" = lo que de verdad llegó (`RECEIPT` más los ajustes `RECEIPT_VARIANCE`, el mismo neto del indicador). "Conteos con diferencia" = conteos cerrados ese día en `RECONCILED_VARIANCE` |
| D3 | Color | **A:** violeta; naranja en "Conteos con diferencia" si hoy hubo y en "Productos bajo mínimo" si hay | El servidor manda `countsAlert` y `belowMinAlert`; la barrita de hoy de Conteos también va naranja |
| D4 | Clic en la tarjeta | **A:** abre el detalle ya filtrado | Recibidas → Kárdex `?types=RECEIPT&from&to`; Salida → Kárdex `?types=ISSUE&types=CROSSDOCK&from&to`; Conteos → `/warehouse/cycle-counts?status=RECONCILED_VARIANCE`; Bajo mínimo → `/warehouse/products?kpi=low`. Todos con `&warehousePublicIds=` si hay almacén elegido. Las barritas solo muestran (tooltip con la fecha larga y la cantidad) |
| D5 | Almacén | **A:** el mismo del panel "Almacén", sincronizado y recordado por usuario | Selector en la franja (muestra el código; el nombre va en el `title`). Cambiarlo en un lado lo cambia en el otro |
| D6 | Filas fijas | **A:** fecha + franja debajo; si se oculta o se mueve la franja, solo la fecha | "Necesita tu atención" va debajo de la franja y se desplaza. Solo en el Pulso del día. Al organizar no hay filas fijas (la barra de Organizar ya es fija) |
| D7 | Celular | **A:** fijas y compactas 2×2; acostado solo la fecha | Bajo 720 px la franja va 2×2 con número y barritas, sin el texto pequeño. Con alto menor de 560 px solo queda fija la fecha |
| D8 | Orden de las filas de indicadores | **A:** el del menú (Operación, Almacén, Contabilidad si hay) | Dentro de cada fila, el orden de siempre. Organizar ordena solo dentro de su fila. "Cambios registrados", "Accesos fallidos" y "Usuarios activos" están marcados como Operación y salen en esa fila |
| D9 | Los 2 gráficos | **A:** convertir los existentes de fábrica y llevarlos a todas las compañías | Conservan el nombre; dejan de ser de sistema; llegan con `db-init` a las ya creadas |
| D10 | Tipo del valor de inventario | **A:** dona con el total al centro | `PIE` no se habilitó en el API ni en el editor |
| D11 | Reparto | **A:** por categoría del producto; 7 mayores + "Otras" | El "Otras" lo arma el motor (ver decisión 1 abajo) |
| D12 | Movimientos por tipo | **A:** unidades en positivo, últimos 7 días, rango editable | Campo `Units` = `|Quantity|`; cada usuario cambia el período con "Rango"; no hay opción de "5 días" |
| D13 | Posición | **A:** primera fila juntos (valor a la izquierda, movimientos a la derecha), respetando órdenes ya organizados | Órdenes 1 y 2 solo si siguen en 90 y 96 y la compañía no organizó (decisión 5). Nunca más de 2 por fila; en celular, uno debajo del otro |
| D14 | Permisos | **A:** quien tenga "Crear vistas, indicadores y gráficos" (`analytics.manage`); si se borran, no vuelven | Un borrado es lógico y conserva su `SeedKey` y su nombre, que bloquean cualquier siembra posterior |
| D15 | Repetidos | **A:** no se toca nada | La franja dice "hoy"; los indicadores viejos siguen con su período. Quien quiera los oculta con Organizar |
| D16 | "Descuadres pendientes" | **A:** se crea, apagado en el Pulso | Aparece en Análisis → Indicadores; "Necesita tu atención" ya muestra los descuadres |

## Qué se construyó

### Backend

- **Franja "Almacén hoy".** `GET /api/v1/inventory/pulse/days` devuelve la zona (`America/Puerto_Rico`), la fecha de hoy, el rango UTC
  cubierto, una fila por día (de la más vieja a hoy; siempre `days` filas) con `receivedUnits`, `receivedMovements`, `outboundUnits`,
  `outboundMovements` y `countsWithVariance`, los números de hoy y los totales (`receivedToday`, `receivedTotal`, `outboundToday`,
  `outboundTotal`, `countsWithVarianceToday`, `countsWithVarianceTotal`), `belowMinProducts` y las banderas `countsAlert` y
  `belowMinAlert`. Las cifras salen de una sola consulta agrupada en SQL (un `CASE` anidado sobre las medianoches locales en UTC): aguanta
  días de 23 o 25 horas y no pasa por el motor de gráficos (que trabaja en memoria con tope de filas). El almacén se resuelve por
  `PublicId` bajo el filtro de tenant: uno que no existe o es de otra compañía da todo en cero, nunca cae en "todos". Cifras con escala 3
  en JSON (por ejemplo `12.000`).
- **Panel `WAREHOUSE_DAY`.** Orden −10, así que queda primero en todos los Pulsos, también en los ya organizados. Sin permiso nuevo:
  `pulse.warehouse` + `inventory.view` + `WMS_LOTSERIAL`.
- **Gráficos de la compañía.** `ChartDefinition.SeedKey` marca al gráfico sembrado sin depender de su nombre. Un gráfico sin dueño y no
  de sistema lo edita o borra quien tenga `analytics.manage`. El seeder (compañías nuevas y demo) y el bloque SQL (compañías ya creadas)
  convierten solo la fila de sistema que sigue exactamente como se sembró y solo si la compañía no tiene ya esa clave. Un gráfico
  renombrado o borrado no se vuelve a crear.
- **"Otras".** En barras, dona y pastel con SUM o COUNT, con más de 8 grupos el motor devuelve los 7 mayores y un punto "Otras"/"Others"
  (idioma del usuario) con la suma exacta del resto. Con AVG, MIN o MAX siguen los 8 mayores.
- **Días de Puerto Rico en todo el análisis** (decisión del dueño: "hoy" es en hora local en todo lo que dependa del día). Qué cambió de
  comportamiento:
  - Los rangos `LAST7` (hoy y los 6 días anteriores), `LAST30`, `THIS_MONTH` y `CUSTOM` son **días locales** de Puerto Rico convertidos a
    instantes UTC: desde las 00:00 locales del primer día (inclusivo) hasta las 00:00 locales del día siguiente al último (exclusivo).
    Antes eran días UTC. Un movimiento de las 20:00 a las 24:00 locales ahora cuenta en su día local.
  - Agrupar por un campo de fecha agrupa por el **día local** del instante.
  - El campo `Date` de la fuente de movimientos es el día local y sale como `"2026-09-30"` (antes `"2026-09-30T00:00:00"` en UTC).
  - Las fuentes de rutas y de contratos filtran su día de calendario con el rango local.
  - "Hoy" en los vencimientos (documentos de flota, choferes, contratos vigentes) es el de Puerto Rico.
  - La ventana `today` de Actividad reciente empieza a las 00:00 de Puerto Rico.
  - **Sigue en UTC:** la validación de mínimo y máximo de fechas de campos personalizados (`Dsl/RuleEvaluator.cs`), `ClientService.Today()`,
    `DriverDocumentService.Today()` y `DriverRateService`.
- **"Descuadres pendientes".** Indicador de sistema sobre `INVENTORY_DISCREPANCY` (estatus `OPEN`), rango `ALL`, apagado en el Pulso.

### Frontend web

- **Siempre gráfico.** Ninguna tarjeta de gráfico cae a una lista de puntos. Una dona de una sola rebanada y una línea de un solo día se
  dibujan. Sin puntos, el aviso "Este gráfico no tiene datos en el rango configurado." (Pulso) o "Este gráfico no tiene datos con los
  filtros y el rango actuales." (Análisis → Gráficos).
- **2 por fila.** "Tus gráficos" pone como máximo 2 tarjetas por fila (una columna bajo 776 px). Análisis → Gráficos no cambió.
- **Vista previa alineada.** La vista previa del editor de gráficos (`chartPreview.ts`) hace lo mismo que el servidor: días locales de
  Puerto Rico y "Otras" con SUM o COUNT.
- **Indicadores por fila.** "Tus indicadores" muestra una fila por módulo con su etiqueta (h3); "Contabilidad" solo si hay algún
  indicador (por ejemplo, "COD por cobrar"). En Organizar, los indicadores se listan por fila y solo se mueven dentro de la suya.
- **Franja "Almacén hoy · últimos 7 días".** Cuatro tarjetas con tubería punteada violeta. Las tres primeras tienen número de hoy,
  "7 días: N" y 7 barritas; "Productos bajo mínimo" va al final, "en este momento" y sin gráfico. Mientras carga muestra "…"; con un 403
  muestra "—" sin sacar al usuario del Pulso.
- **Almacén compartido.** El selector de la franja y el del panel "Almacén" son el mismo estado.
- **Filas fijas.** Ver D6 y D7.
- **Enlaces.** Conteo cíclico y Productos e inventario leen su filtro de la URL (solo al abrir la pantalla).

## Cómo se prueba

Corridas del 2026-09-30 sobre el código de este lote.

1. `dotnet build Teikem.sln && dotnet test Teikem.sln` — **2.687 pruebas en verde**; compilación sin avisos (0). Pruebas nuevas y
   ajustadas: ver la fila "Pruebas" del mapa (incluye la traducción a `GROUP BY CASE` con `ToQueryString` y los bordes 03:59Z y 04:00Z
   del rango de fechas).
2. Web (`cd web-app && npm run check`): `tsc` limpio, oxlint sin errores, vitest **93 archivos / 850 pruebas en verde** y build OK.
3. App móvil (`cd app-almacen`): `tsc` limpio con el `schema.d.ts` regenerado.
4. Recorridos Playwright (`web-app/e2e`, contra el API real): **57 pasaron, 0 fallaron**: escritorio 39 (7 omitidos), móvil 8 (49
   omitidos) y escritorio-f8a 10 (1 omitido).
   - `lote15.spec.ts` (nuevo, 9 pruebas): (1) franja: 4 tarjetas en orden, 7 barritas en las tres primeras, ninguna en "Productos bajo
     mínimo", primera sección bajo la fecha y tooltip con "Hoy" y la fecha larga; (2) un recibo de 5 y una recolección de 2 hechos por
     API suben "Unidades recibidas" y "Unidades de salida" (la recolección se elimina al final); (3) el clic en cada tarjeta lleva a su
     pantalla con sus filtros (Kárdex con tipos y siete días, Conteo en Diferencia, Productos con `kpi=low`); (4) el almacén elegido en la
     franja cambia el del panel y al revés, viaja en los enlaces y se recuerda al recargar; (5) al desplazar, la fecha y la franja siguen a
     la vista y "Necesita tu atención" se va; (6) "Tus gráficos": siempre `svg`, sin `.pulse-pts`, máximo 2 por fila, valor a la izquierda
     y movimientos a la derecha en la primera fila, dona con total; (7) Análisis → Gráficos: los dos son "De la compañía" y tienen Editar;
     (8) "Tus indicadores" en filas con h3 (Operación y Almacén primero); (9) móvil (360 px): franja 2×2 compacta, fija al desplazar y sin
     scroll horizontal. Genera las capturas `docs/manual/frontend/img/l15-*.png`.
   - `loteF1.spec.ts` (la franja `.wh-river` va 2×2 en celular, no apilada) y `f8a.spec.ts` ("Ocultar Almacén" y "Mostrar Almacén" con
     `exact: true`, porque chocaban con "Almacén hoy") ajustados.
5. **Base desde cero, repetible:** `db-reset` y luego `import-legacy` de Depot (0 rechazos, 1.310 asientos de saldo inicial) y de
   Solutions (0 rechazos, 32 asientos); `db-init` dos veces sin cambios en la segunda; seed forzado tres veces sobre la demo (conversión de
   filas viejas simuladas, compañía organizada que conserva sus órdenes, gráfico renombrado o borrado que no se recrea). Depot y Solutions
   solo por `SELECT`.
6. **Prueba en vivo en la demo Advance Logistics** (por API): franja de hoy con recibido 12, salida 5 (7 menos 2 de la reversa de una
   recolección eliminada), conteos con diferencia 1 y bajo mínimo 1, con los tonos naranja; los dos gráficos convertidos se editan (PUT
   200); orden `WAREHOUSE_DAY` −10 y `ATTENTION` 5.
7. **Verificación en el navegador** (demo, a 1920, 1440 y 360 px, tema claro y oscuro): dona de una sola rebanada y línea de un solo día
   dibujadas; 2 gráficos por fila; sin scroll horizontal; la fecha y la franja fijas al desplazarse; la dona con el total al centro; barras
   reales. Medido: a 1440 px quedan fijas la fecha y la franja; a 360 px la franja 2×2 ocupa 295 px de 780; con 740×360 solo queda fija
   la fecha.
8. **Humo (`scripts/smoke.sh`, incluido el bloque "Lote 15"): `SMOKE OK`, 128 pasos en verde**, con `SMOKE_SQL` (sqlcmd local),
   sobre una base recreada desde cero igual que el CI: `db-reset --yes`, `import-legacy` de Depot (0 rechazos, 1.310 asientos) y de
   Solutions (0 rechazos, 32 asientos) y `db-init` dos veces sin errores. En esa base, los dos gráficos de la compañía
   (`INVENTORY_VALUE`, `MOVEMENTS_BY_TYPE`) quedaron en las tres compañías (Advance Logistics, Depot y Solutions), no de sistema, con
   órdenes 1 y 2 (comprobado por SELECT). El Pulso del admin trae 6 paneles, `WAREHOUSE_DAY` primero (−10).

9. Las capturas del manual: las nuevas `l15-*.png` (pulso-franja, pulso-franja-tooltip, pulso-fijas, pulso-graficos, pulso-indicadores,
   pulso-franja-movil, graficos-de-la-compania, franja-kardex-recibidas, franja-conteo-diferencia y franja-productos-bajo-minimo) y las de
   Pulso de F1, F6, F7a y F8a regeneradas por los recorridos.

### Qué no se probó

Los reportes recibidos no cubren lo siguiente; nada de esto se probó al escribir este documento:

- **El CI de GitHub Actions** con este código. La zona `America/Puerto_Rico` (con respaldo "SA Western Standard Time" y, si no, UTC−4
  fija) no se ejercitó en Linux.
- **El humo completo** (ver el marcador del punto 8) y **la base desde cero con el humo** (los pasos por `SMOKE_SQL`, entre ellos mover
  un recibo a las 23:59 de ayer en hora de PR).
- **La conversión de gráficos en una compañía real ya creada** (Depot o Solutions, que nacieron con el código nuevo). Solo se probó con
  filas simuladas en la demo (decisión 9).
- **La franja con datos de Depot** (unos 1.300 asientos migrados): el rendimiento de la consulta agrupada no se midió con esa carga. No
  se amplió el índice `IX_InvTxn_Tenant_Date` (queda para medir con datos reales).
- **El cambio de día a medianoche con la pantalla abierta**: el refresco cada 5 minutos está escrito, pero ningún recorrido cruza la
  medianoche de Puerto Rico.
- **Días de 23 o 25 horas en una zona con horario de verano**: lo cubren pruebas unitarias con Nueva York, no una compañía real (Puerto
  Rico no cambia de horario).
- **Filtros o vistas guardados que dependan del formato de texto del campo `Date` de movimientos** (ahora `"2026-09-30"`): no se
  revisaron.
- **Un lector de pantalla** sobre el `aria-label` de los gráficos y de la franja, y **el foco con teclado** sobre las filas fijas (se
  agregó `scroll-margin-top`, pero no hay una prueba de teclado).
- **El modo Organizar con arrastre** en la lista de indicadores por fila (el teclado y los botones ▲ y ▼ los cubren las pruebas).

## Decisiones a revisar

1. **"Otras" también cambia las barras existentes con más de 8 grupos.** El plan lo pedía solo para la dona. Se hizo en el motor para
   barras, dona y pastel con SUM o COUNT, así que un gráfico de barras como "Cambios por usuario" con más de 8 grupos ahora muestra 7 más
   "Otras" (antes, los 8 mayores). Con AVG, MIN o MAX no cambia. Si el dueño prefiere que las barras sigan como estaban, hay que limitar el
   cambio a dona y pastel en `AnalyticsEngine.EvaluateChartAsync` y en `chartPreview.ts`.
2. **La salida de un día puede salir negativa.** Si un día solo se eliminaron recolecciones (la reversa resta y no hubo salidas ese día),
   "Unidades de salida" de ese día es negativa (no se probó cómo se ve la barrita). Es la consecuencia de D2: la reversa resta el
   día en que se elimina, no el día en que se creó la recolección.
3. **El enlace de "Conteos con diferencia" no lleva fechas.** El filtro de fechas de la lista de conteos es el de alta del conteo, no el
   del cierre, y dejaría fuera un conteo creado antes y cerrado dentro de la ventana. La lista muestra todos los conteos en Diferencia (con
   el almacén elegido), no solo los de los 7 días; la cifra de la tarjeta sí cuenta por día de cierre.
4. **El Kárdex puede sumar distinto que la tarjeta.**
   - **Salida:** las recolecciones eliminadas restan en la tarjeta, pero en el Kárdex son un ajuste fuera de los tipos Despacho y Cruce
     de muelle (`ISSUE` y `CROSSDOCK`), así que el Kárdex de "Salida" puede sumar más.
   - **Recibidas:** la tarjeta es el neto (incluye los ajustes por diferencia de recibo) y el enlace filtra solo el tipo Recepción
     (`RECEIPT`), así que las diferencias salen como ajustes aparte. Es la advertencia de D4 y va a la FAQ.
5. **Compañía "organizada" = algún gráfico activo con orden 0.** Organizar guarda `índice × 10` desde 0, así que un orden 0 delata que
   alguien organizó. Es una inferencia: si alguien puso 0 a mano sin organizar, sus dos gráficos nuevos no pasan a la primera fila (quedan
   en 90 y 96). Vale para el seeder y para el bloque SQL.
6. **Lo que sigue en UTC** (ver "Días de Puerto Rico"): validación de mínimo y máximo de fechas de campos personalizados,
   `ClientService.Today()`, `DriverDocumentService.Today()` y `DriverRateService`. Un vencimiento calculado por esos servicios puede
   diferir en horas del que muestra un indicador o un gráfico.
7. **`customRangeDays` (web) calcula en UTC.** En Puerto Rico da el mismo número de días; en otra zona podría diferir. Queda para cuando la
   zona sea configurable por compañía.
8. **2 por fila con estilo en línea.** `PULSE_CHART_COLUMNS` va como `style` en `ChartsGrid` y no en `pulse.css`. Se hizo así para que la
   regla no toque a Análisis → Gráficos, que comparte la clase. Es más difícil de sobrescribir desde una hoja de estilos.
9. **La conversión de filas viejas se probó solo con filas simuladas.** En la base local Depot y Solutions nacieron con el código nuevo y
   la demo ya traía los gráficos nuevos; la conversión del bloque SQL y del seeder se probó devolviendo a la demo filas con la forma
   exacta del Lote 6 y 7A (y una organizada). Los renombrados y borrados se probaron igual. No hay una compañía real con datos viejos.
10. **El bloque SQL solo actúa en compañías con contenido de análisis sembrado** (al menos un gráfico o indicador de sistema). Una
    compañía sin ninguno no recibe los gráficos ni "Descuadres pendientes" por `db-init`.
11. **La franja siempre se pinta** (sin movimiento, ceros y barritas vacías) si el usuario tiene el panel; no se oculta con "sin
    datos". Un 403 muestra "—" y no redirige.
12. **`days` se expone (1 a 14)** aunque la pantalla siempre pide 7. Sirve para pruebas y para una futura opción del usuario.
13. **Sigue vigente `db-init` no idempotente fuera de lo guardado** (decisión 18 del Lote 13): `SeedKey` y su índice llevan guarda, pero
    sobre una base existente cualquier otro cambio de estructura exige `db-reset`. Para este lote se usó `db-reset` + `import-legacy` y el
    seed forzado sobre la base poblada.
14. **"−0" en el resumen del Kárdex (corregido en este lote).** Venía del Lote 14: "Salidas (uds)" decía "−0" en rojo cuando no había
    salidas. Ahora las cantidades de entrada y salida llevan signo y color solo si no son cero (`summaryItems` en `kardexView.ts`); la
    captura `l15-franja-kardex-recibidas.png` se tomó antes del arreglo.
15. **Pantalla vacía de la dona.** El plan preveía el mensaje "Sin inventario valorado: no hay existencias o los productos no tienen costo
    de compra." para la dona sin datos. No se construyó: sale el aviso genérico de gráfico sin datos.

## Qué queda fuera de este lote (a propósito)

- **Contabilidad** en el Pulso: la fila "Contabilidad" solo aparece si hay algún indicador de ese módulo (hoy "COD por cobrar"); no hay
  franja ni gráficos de contabilidad.
- **Mini gráfico de "Productos bajo mínimo"** (una serie diaria) y **ocupación diaria** de las posiciones: necesitan una foto diaria que
  calcule el motor de trabajos programados, que no existe. Hoy la tarjeta es solo el número de ahora.
- **"Paquetes en la calle" y "Dinero COD de regreso"** con datos reales (paneles reservados `ORDERS_RIVER` y `COD_RIVER`).
- **`PIE` (pastel) como tipo de gráfico** en el API y en el editor: D10 eligió la dona.
- **Zona horaria por compañía** (sigue siendo la de Puerto Rico para todas, con el reloj ya preparado) y **selector de días** de la franja.
- **Cambios a la app móvil** más allá de regenerar `schema.d.ts`.
- **Ocultar automáticamente los indicadores y la dona repetidos** (D15: no se toca nada).
- **Corrida del CI** con este código: se confirma al hacer push.

## Pendiente anotado para un lote futuro (decisión del dueño, 2026-09-30)

- **Método de rotación configurable (FIFO / FEFO) por compañía**, con posible excepción por producto o categoría. Hoy la
  asignación de inventario está fija en FEFO (`StockAllocator.cs`, `PickBatchRules.cs`, `ReplenishmentRules.cs` y las
  sugerencias de la web): vencimiento ascendente, sin fecha al final, luego tipo de zona y código de posición. Para productos sin
  vencimiento eso **no es FIFO**: no se ordena por fecha de entrada. Falta decidir dónde se configura, qué métodos se ofrecen,
  qué fecha manda en FIFO cuando el producto no tiene lote (derivarla del Kárdex o guardar una fecha de primera entrada por
  saldo) y qué método tienen por defecto las compañías existentes. El dueño lo dejó para después; no está diseñado.
