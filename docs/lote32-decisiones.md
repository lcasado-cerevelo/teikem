# Lote 32 — Alcance de datos del usuario (`UserDataScope`), servidor (2026-10-11)

Pendiente P0.3 del plan (`docs/plan-de-desarrollo-pendiente.md`; diferido desde `lote1-decisiones.md:59` y `lote6-decisiones.md` D42). Hasta hoy
el alcance de almacén/cliente de un usuario se guardaba y se mostraba en `/me`, pero no filtraba nada.

## Decisiones del dueño que se aplicaron (2026-10-11)

- Lo que queda **fuera del alcance no aparece** (ni en listas ni por id: 404).
- **Nadie se exenta**: un administrador con alcance queda limitado igual.
- Con alcance de almacén **y** de cliente se exigen **ambos** donde la fila tiene los dos (p. ej. un ASN).
- Sin alcance de un tipo = sin restricción de ese tipo (como hasta ahora).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Contexto | `ITenantContext.ScopedWarehouseIds` / `ScopedClientIds` (`null` = sin restricción; implementación por defecto null, así los dobles de prueba no cambian) | `Infrastructure/Abstractions/ITenantContext.cs` |
| Carga | Tras autenticar, el middleware lee las filas de `UserDataScope` del usuario (una consulta pequeña por petición autenticada) y llena el contexto; el administrador de plataforma y las peticiones sin usuario no se limitan | `Api/Middleware/TenantContextMiddleware.cs` |
| Filtro | `TeikemDbContext.AddScopeFilter`: a **toda entidad con columna `WarehouseId` o `ClientId`** (`int` o `int?`) se le suma `!Scoped OR ids.Contains(col)` (más `OR col IS NULL` si es opcional) al filtro de tenant. Las entidades con la columna y sin `TenantId` (zonas, posiciones y muelles del almacén, contactos del cliente) también lo reciben. Con el filtro de tenant apagado (plataforma/sembradores) el alcance también se apaga | `Infrastructure/Persistence/TeikemDbContext.cs` |
| Pruebas | `UserDataScopeFilterTests` (6): sin alcance, solo almacén, solo cliente (con producto compartido), ambos a la vez, el tenant sigue mandando, filtro apagado | `tests/Teikem.Tests/UserDataScopeFilterTests.cs` |
| Manual | §3.5 reescrita (qué se ve con alcance, 404, nadie se exenta) y dos preguntas en la FAQ | `docs/manual/01-plataforma-y-seguridad.md`, `faq.md` |

Entidades alcanzadas por `WarehouseId`: Warehouse, WarehouseZone/Bin/Dock, StockBalance, ReceiptHeader, Asn, PickBatch, CycleCount, DamageReport,
InventoryDiscrepancy, DockAppointment, CrossDockPlan, PurchaseOrder, Rental, RentalProcess, WarehouseTask. Por `ClientId`: Client, ClientContact,
Contract, Location, SpecialService, PortalUser, TransportOrder, ImportBatch, ImportTemplate, NumberSequence, Product, Asn, Rental.

## Cómo se probó

- Pruebas del servidor: **3357 verdes** (3351 + 6 nuevas).
- De punta a punta contra el API local (SQL Server local, base nueva): un usuario con rol `TenantAdmin` y alcance a los almacenes 1 y 2 vio solo
  esos dos (el administrador sin alcance ve todos) y al pedir otro almacén por id recibió **404** (el administrador, 200).
- Humo (`scripts/smoke.sh`) en base nueva, con el cambio: **SMOKE OK**.

## Decisiones a revisar

1. **Filas hijas sin columna propia** (líneas de ASN, de recibo, de recolección): no llevan el filtro directo; se ocultan cuando se llega a
   ellas por su documento (que sí está filtrado). Pedir una línea hija por su id numérico interno, saltándose el documento, no valida alcance.
   Los endpoints exponen ids públicos de documentos, no de líneas; si se quiere blindar también eso, hay que propagar el alcance a las hijas.
2. **`StockMovement` y transferencias** (`FromWarehouseId`/`ToWarehouseId`): no se filtran por esas columnas (nombres distintos). Un usuario con
   alcance a un almacén podría ver en el Kárdex del producto el movimiento de una transferencia hacia otro almacén.
3. **Rendimiento**: una consulta extra (por `UserId`, tabla chica con índice único) en cada petición autenticada. Si pesa, pasarla a un claim del
   token o a una caché corta que se invalide con el `PUT data-scopes`.
4. **Web y app**: no cambian. La pantalla de roles y usuarios ya edita los alcances. Un usuario con alcance verá listas más cortas; lo
   sincronizado al aparato también se limita (el servidor filtra).
5. **Otros tipos de alcance**: el manual mencionaba «rutas»; hoy solo `WAREHOUSE` y `CLIENT` filtran. Otros `ScopeEntity` se guardan sin efecto.
