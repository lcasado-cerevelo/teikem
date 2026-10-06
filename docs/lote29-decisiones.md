# Lote 29 — Rentas R3: reportes, indicadores y aviso de rentas vencidas o por vencer (2026-10-05)

Cuarto bloque del submódulo **Rentas** (`docs/rentas-plan-de-ejecucion.md`, bloque R3, sección 4.7). Sobre las rentas (R1), las
devoluciones y los procesos (R2), agrega las **fuentes de datos** de Análisis, el aviso de **rentas vencidas o por vencer** en "Necesita tu
atención" y el **contenido de sistema** (indicadores, gráfico y reportes mínimos). Sin cambios de esquema, de permisos ni de contrato del API.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Fuente `RENTAL` | Una fila por renta. DateField **StartDate** (día de calendario; rango por días locales). Campos: número, cliente, localidad, almacén, estatus, **IsOpen** (Programada o En renta), fechas de inicio, **recogido** y pactada, **DaysToPickup**, **IsOverdue** (con el día de la compañía, `ITenantClock`), extensiones y días extendidos, **Units** (activos), **UnitsOnRent** (despachados sin devolver), devueltos, contrato, transporte estimado (dinero) y moneda, fechas de despacho, cierre y alta. Relaciones Client, Location, Warehouse. Campos personalizados de RENTAL | `Analytics/RentalDataSources.cs` |
| Fuente `RENTAL_RETURN` | Una fila por devolución. DateField **ReturnedOn**. Motivo y código, **IsEarly** (antes del recogido vigente, mismo cálculo que la lista de R2) y días de anticipación, **Condition** (resumen de las condiciones de sus equipos en orden Buena, Dañado, Incompleto) y código, HasDamage, equipos por condición, con proceso y procesos abiertos, recogido estimado y moneda. Relaciones Rental, Client | `Analytics/RentalDataSources.cs` |
| Fuente `RENTAL_PROCESS` | Una fila por proceso. DateField **StartedAtUtc**. Serie, producto, almacén, posición, estatus, **IsOpen** (misma regla que la cola: `RentalRules.IsProcessFinished`), devolución, renta, cliente, condición al volver, inicio, fin y **DaysInProcess** (días de la compañía). Relaciones Product, Warehouse, Return, Rental | `Analytics/RentalDataSources.cs` |
| Lectura por módulo | `IDataSource.TenantModule` (miembro nuevo con valor por defecto `null`): las tres fuentes declaran `RENTAL_EQUIPMENT`. `AnalyticsService.CanReadSource` exige ese módulo encendido además del permiso del EntityType (`rental.view`, ya en `OwnerReadPermission`). Con Rentas apagado no se listan las fuentes ni sus vistas, indicadores y gráficos, y leerlos responde 404 (como una fuente sin permiso) | `Analytics/DataSources.cs`, `Services/AnalyticsService.cs` |
| Aviso `RENTAL_DUE` | `RentalDueAttentionProvider` (módulo `RENTAL_EQUIPMENT`, `rental.view`): una fila por renta abierta **vencida** o **por vencer en ≤ 7 días** (recogido ≤ hoy + 7), las de recogido más antiguo primero; tono `danger` (vencida) o `warn`; params (número, cliente, localidad, almacén, recogido, días, overdue, equipos sin devolver, estatus); "Revisar" `/warehouse/rentals?rental={publicId}`; "Ver todos" `/warehouse/rentals?dueWithinDays=7&overdue=true`; `SinceUtc` = 00:00 locales del día de recogido | `Services/Attention/RentalDueAttentionProvider.cs` |
| Contenido de sistema | Vistas "Equipos en renta por cliente" (En renta, agrupada por cliente: rentas y suma de equipos en el cliente, con totales), "Rentas por vencer (7 días)", "Rentas vencidas", "Devoluciones de renta por motivo" (agrupada por motivo: devoluciones y equipos, con totales) y "Equipos en proceso" (abiertos); indicadores "Rentas por vencer (7 días)" y "Rentas vencidas" (conteo, rango ALL, módulo Almacén, apagados en el Pulso); gráfico "Devoluciones de renta por motivo" (dona, últimos 30 días, apagado en el Pulso). Idempotente por nombre | `Seeding/SystemAnalyticsSeeder.cs` |
| Compañías ya creadas | Bloque "Lote 29" del seed: el mismo contenido para toda compañía con contenido de análisis sembrado (Depot, Solutions), idempotente por nombre, como el bloque del Lote 15 | `Diseño/logistica-db-seed.sql` |
| Reglas puras | Ventana de 7 días, nombres y filtros sembrados, `NeedsAttention`, días de anticipación, días en proceso y resumen de condiciones | `Domain/Wms/RentalAnalyticsRules.cs` |
| DI | Las tres fuentes y el proveedor del aviso | `DependencyInjection.cs` |

## Cómo se probó

- `dotnet test tests/Teikem.Tests -o .tmp-testout`: **3145 pruebas, 0 fallas** (antes 3133 en `0656645`; +12). Nuevas:
  `RentalAnalyticsTests` (7: fuente RENTAL con **reloj fijo** —vencidas y por vencer con el día de la compañía, un reloj 8 días después
  cambia el resultado, las 22:00 de Puerto Rico siguen siendo "hoy" aunque en UTC ya sea mañana—, filtros sembrados, equipos y
  extensiones, rango por StartDate, Ids y **aislamiento por compañía**; devoluciones con motivo, condición resumida, anticipada y rango;
  cola de procesos con días en proceso y aislamiento; forma de las tres fuentes; reglas puras; el **aviso** con orden, tonos, params,
  "Ver todos" y aislamiento; sin `rental.view` o con Rentas apagado no aporta; **AnalyticsService** con el módulo apagado oculta fuentes,
  indicadores, gráficos y vistas (404) y sin `rental.view` también), `AnalyticsSeedFieldsTests` (+4: contenido sembrado y su forma, cada
  campo existe en su fuente, **resembrar es idempotente y no depende del módulo**, el bloque SQL tiene el mismo contenido que el seeder),
  `OwnedEntityResolverCoverageTests` (+1: fuentes y aviso desde el contenedor real, claves únicas).
- SQL Server 2022 local: `db-init` **dos veces** sobre la base **nueva** `TeikemR3Smoke` (la segunda: scripts omitidos por hash, 68
  permisos, 0 nuevos, tenant demo "ya existe" y contenido de análisis verificado).
- `scripts/smoke.sh http://localhost:5180` (API de `.tmp-testout`, `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`
  con una envoltura del build de `.tmp-testout`): **SMOKE OK, 137 pasos, 139 `ok`**, a la primera. Paso nuevo "reportes, indicadores y
  avisos de rentas (Lote 29, Rentas R3)": fuentes con su campo de fecha; una renta Programada **vencida** y una En renta que **vence en 3
  días**; "Necesita tu atención" con el grupo `RENTAL_DUE` (2, "Ver todos" con los filtros de la lista) y la vencida en `danger`; Solo
  lectura (sin `rental.view`) no ve el aviso; indicadores 1 y 1; vistas "Rentas vencidas", "Rentas por vencer (7 días)", "Equipos en renta
  por cliente" (1 cliente, 1 equipo, totales), "Devoluciones de renta por motivo" (3 motivos de las devoluciones de R2) y "Equipos en
  proceso" (0: los de R2 terminaron); gráfico por motivo (3 puntos); otra compañía sin aviso ni filas en la fuente; con Rentas **apagado**
  sin fuentes, sin indicadores (404 al leer) y sin aviso, y al encenderlo vuelve.
- **Bloque SQL del Lote 29 sobre una compañía "anterior al lote"**: en `TeikemR3Smoke`, después del smoke, se borró el contenido de rentas
  de la compañía 1 y se aplicó el bloque con `sqlcmd -I`: insertó 2 indicadores, 1 gráfico y 5 vistas; aplicado otra vez, 0 filas (la
  compañía 2, que ya lo tenía, tampoco duplicó). Por el API, el contenido insertado por SQL se lee igual (indicadores 1 y 1, vista por
  cliente y gráfico por motivo con los 3 motivos).
- Contrato: el `swagger.json` del API nuevo es **idéntico** a `web-app/openapi.json` (sin cambios de rutas ni esquemas): no se
  regeneraron tipos ni se tocó la web ni la app.

## Decisiones a revisar

1. **Se siembra en toda compañía y se oculta con el módulo apagado** (regla de lectura de Análisis por `IDataSource.TenantModule`), en
   lugar de sembrar solo donde Rentas está encendido. Al encender Rentas aparece todo sin resembrar; la regla también oculta las vistas,
   indicadores y gráficos que la compañía haya creado sobre rentas.
2. **Indicadores y gráfico apagados en el Pulso**, como "Descuadres pendientes" (D16): "Necesita tu atención" ya muestra cada renta
   vencida o por vencer. Cada usuario los enciende.
3. **Ventana fija de 7 días** (aviso, indicador y vista), no configurable por compañía.
4. **Abiertas = Programadas o En renta** (decisión 14 de R1): una Programada con el recogido pasado es vencida y sale en el aviso.
5. **Aviso**: una fila por renta (no una fila resumen), tono rojo si está vencida y ámbar si está por vencer; `SinceUtc` = medianoche
   (hora de la compañía) del día de recogido, así las vencidas quedan entre los avisos más antiguos y las por vencer después. Ruta
   `/warehouse/rentals` con `rental` y "Ver todos" con `dueWithinDays=7&overdue=true` (los filtros de la lista de R1): la pantalla llega con
   F-R1; hasta entonces la web muestra el código `RENTAL_DUE` (no se tocó la web).
6. **Condición de una devolución resumida** (condiciones distintas de sus equipos y conteo por condición): la fuente es por devolución
   (EntityType `RENTAL_RETURN`, campos personalizados del encabezado). Alternativa: una fuente por equipo devuelto.
7. **"Anticipada" contra la fecha de recogido vigente** (como la lista de devoluciones de R2), no contra la que tenía el día de la devolución.
8. **"Equipos en renta por cliente" = rentas En renta** (no Programadas) y suma de equipos despachados sin devolver.
9. **Etiqueta de estatus** en las fuentes: la del catálogo (como el resto de las fuentes de Análisis), sin el nombre que la compañía le haya
   puesto al estatus.
10. **Bloque SQL para las compañías ya creadas** (Depot y Solutions) en `logistica-db-seed.sql`, con el criterio del Lote 15 (solo compañías
    con contenido de análisis, por nombre). Sin él, solo las compañías nuevas y la demo recibirían el contenido.
11. **Días en proceso** en días de calendario de la compañía (inicio a fin, o a hoy si sigue abierto).

## No probado

- Pantallas (F-R1 y F-R2): el texto del aviso `RENTAL_DUE` y la pantalla `/warehouse/rentals` no existen todavía en la web; sin Playwright.
- El bloque SQL del Lote 29 sobre la base REAL de Depot (se probó sobre la base del smoke simulando una compañía anterior al lote).
- Rendimiento de las fuentes con muchas rentas reales (tope de 20 000 filas, consultas por lote sin N+1).
