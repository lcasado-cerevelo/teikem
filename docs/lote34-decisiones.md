# Lote 34 — Rotación del inventario FEFO / FIFO configurable, servidor (2026-10-11)

Pendiente P0.4 del plan (`docs/plan-de-desarrollo-pendiente.md`; anotado en `lote15-decisiones.md` y `lote16-decisiones.md`: «hoy FEFO fijo»). Este lote construye
**solo el servidor** (modelo, SQL, asignación, API, pruebas, contrato y manual); el selector en la web y en la app queda para su fase.

## Decisiones del dueño que se aplicaron (2026-10-11)

- Se configura **por compañía + excepción por producto**; métodos **FEFO y FIFO** (sin LIFO); **FEFO** para todas las compañías existentes.
- La fecha que manda en FIFO es la de **primera entrada guardada por saldo** (una columna nueva), rellenada desde el Kárdex para lo que ya existe.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Reglas | `RotationMethods` (FEFO/FIFO), `RotationRules.Parse/Resolve` (producto → compañía → FEFO) y la clave FIFO (sin fecha al final) | `Domain/Wms/RotationRules.cs` |
| Asignación | `PickBatchRules.Eligible/Allocate`, `ReplenishmentRules.EligibleSources/Plan` y `StockAllocator.Allocate` reciben el método (por omisión FEFO: el orden de antes no cambia). FIFO = fecha de primera entrada, luego vencimiento, luego zona/posición/lote | `Domain/Wms/PickBatchRules.cs`, `ReplenishmentRules.cs`, `StockAllocator.cs` |
| Modelo | `StockBalance.FirstReceivedAtUtc`, `Product.RotationMethod` (NULL = la compañía), `Tenant.RotationMethod` (FEFO) | `Domain/Wms/Stock.cs`, `Product.cs`, `Domain/Tenancy/Tenant.cs` + configuraciones EF |
| Ledger | Fija la fecha al crearse el saldo y cuando un saldo vacío vuelve a llenarse; **una transferencia hereda la del origen**; la reconstrucción de saldo usa la primera entrada del Kárdex si faltaba | `Infrastructure/Wms/InventoryLedger.cs` |
| Servicios | Recolección (`PickBatchService`), despacho manual (misma ruta), orden de salida del aparato (`StockExitService`) y reabasto (`ReplenishmentService`) leen el método (producto → compañía) y la fecha del saldo | `Infrastructure/Services/` |
| API | `rotationMethod` en `GET`/`PUT /tenant/settings` y en el alta, el `PATCH` y la ficha de producto (vacío en el `PATCH` = quitar la excepción); 400 `El método de rotación debe ser FEFO o FIFO.` | `TenantService`, `ProductService`, `Contracts/` |
| SQL | Sección **2026-10-11 (e)** de `Diseño/logistica-db-update.sql`: 3 columnas, 2 `CHECK`, y el relleno único de `FirstReceivedAtUtc` desde el Kárdex (idempotente) | `Diseño/logistica-db-update.sql` |
| Contrato | `web-app/openapi.json` (solo agregados de campos) y `schema.d.ts` de la web y de la app | `web-app/`, `app-almacen/` |
| Pruebas | `RotationTests` (18: reglas, orden FEFO/FIFO, desempates, cuarentena, reabasto, asignador), `RotationServiceTests` (4: recolección y orden de salida con compañía FIFO, excepción del producto en las dos direcciones), 4 de ledger (fecha, no se pisa, transferencia hereda, vacío→lleno reinicia); actualizadas las que congelan las firmas de los contratos | `tests/Teikem.Tests/` |
| Manual | §2.2 de `docs/manual/06-inventario-y-almacen.md`, nota en §1.6 y 4 preguntas en `faq.md` | `docs/manual/` |

## Cómo se probó

- Pruebas del servidor: **3394 verdes**.
- Base nueva con `db-init` (el camino del CI): la sección SQL se aplica y `db-update` justo después da «0 cambio(s)». Sobre una base con datos,
  `db-update` agregó las columnas, los `CHECK` y rellenó los 30 saldos con existencia (0 sin fecha); una segunda corrida, 0 cambios.
- Contra el API local: `GET /tenant/settings` → `FEFO`; `PUT` con `fifo` → `FIFO`; `LIFO` → 400 con el mensaje exacto; guardar otro campo no toca la rotación;
  alta de producto con `FIFO` y con `LIFO` (400); `PATCH` `fefo`, vacío (quita) y sin el campo (no cambia).
- Humo (`scripts/smoke.sh`) en base nueva (con `db-init`): **SMOKE OK**; al terminar, los 40 saldos con existencia tenían fecha de primera entrada y 0 no (el camino real de SQL del ledger).

## Decisiones a revisar

1. **FIFO manda sobre el vencimiento** (el vencimiento solo desempata). Para un producto que vence, una compañía FIFO podría sacar un lote que vence después; la
   salida es dejar ese producto como FEFO con su excepción (documentado en el manual). Si se prefiere «FIFO pero nunca sacar lo vencido/por vencer antes», es otra regla.
2. **La fecha es por saldo, no por unidad:** si llegan más unidades a un saldo con existencia, conserva la fecha más vieja. Con muchas entradas pequeñas a la misma
   posición/lote, FIFO rota entre posiciones y lotes, no unidad por unidad dentro de una misma posición.
3. **Relleno de lo existente:** para el inventario que ya estaba, la fecha es la primera entrada del Kárdex a esa posición/lote (o la última actualización del saldo).
   Es una aproximación razonable, no un dato histórico exacto de cada unidad.
4. **Falta la web y la app:** el selector de método en Ajustes de la compañía → Operación y en la ficha del producto cambia pantallas existentes: no se tocó (pide tu
   visto bueno). Por ahora solo por la API. Las pantallas de salida ya respetan el método porque usan el orden del servidor.
5. **Lotes listados por producto** (`GET /products/{id}/lots`) siguen ordenados por vencimiento (informativo); no es la asignación.
