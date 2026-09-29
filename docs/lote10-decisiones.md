# Lote 10 — Migración de datos heredados: qué se construyó y decisiones a revisar

Fecha: 2026-09-29. Plan aprobado: [`docs/lote10-plan.json`](lote10-plan.json). Diseño: [`docs/migracion-depot-solutions-plan.md`](migracion-depot-solutions-plan.md)
(análisis de los datos de QuickBooks Depot/Solutions y del WMS MSWM, mapeo campo a campo, once decisiones cerradas por
Luis el 2026-09-28). Este lote no toca ningún módulo de negocio: agrega un comando CLI fuera del pipeline HTTP y dos
catálogos globales.

## Mapa de lo construido

| Pieza | Archivos principales | Qué hace |
|---|---|---|
| P1 — reglas puras | `src/Teikem.Domain/Migration/LegacyImportRules.cs`, `LegacyImportModels.cs` | Normaliza SKU, parsea direcciones de QuickBooks, mapea términos de pago, deriva código/pasillo/nivel/posición de un id del WMS, resuelve la categoría de Depot; sin EF ni IO. |
| P2 — lectores y reporte | `src/Teikem.Infrastructure/Migration/QuickBooksCsvReader.cs`, `MswmReader.cs`, `LegacyImportConfig.cs`, `LegacyImportReport.cs` | Parser CSV propio (UTF-8 o Windows-1252) para las listas de QuickBooks Desktop; `MswmReader` solo hace `SELECT` sobre `dbo.Item/Location/Inventory/ItemUPC` de una base externa; el reporte en Markdown + CSV con las secciones Resumen, Rechazos, Advertencias, Mapeos, Saldo inicial y (nuevo) Actualizaciones. |
| P0 — integración | `LegacyImportService.cs`, `LegacyImportRunner.cs`, `Program.cs`, `DependencyInjection.cs`, seed, `docs/migracion/*` | Orquesta la migración de una compañía a través de los servicios existentes (`ProvisioningService`, `ProductService`, `ClientService`, `LocationService`, `ContactPointService`, `SupplierService`, `WarehouseService`, `WarehouseLayoutService`, `InventoryLedger`); comando `import-legacy`; los dos JSON reales (Depot, Solutions) y una muestra sintética para pruebas y smoke. |
| **P3 — `db-reset`** (agregado a mano el 2026-09-29, pedido de Luis: *"debo tener algo para recrear la base en blanco y volver a pasarle los datos"*) | `SqlScriptRunner.DropDatabaseAsync`, `DatabaseInitializer.ResetAsync`, `DbResetRules.cs`, `Program.cs` | Verbo `db-reset --yes [--allow-remote]`: borra la base de `ConnectionStrings:Teikem` y la reinicializa como `db-init` sobre servidor limpio. Reglas puras en `DbResetRules` (confirmación, servidor local, nunca `MSWM*`) con pruebas. |
| **P4 — `--update`** (agregado a mano el 2026-09-29, pedido de Luis: refrescar sin volver a mandar todo) | `LegacyImportService.cs` (`UpdateProductAsync`, `UpdateSupplierAsync`, `UpdateClientAsync`, `UpdateLocationAsync`), `LegacyImportRunner.cs`, `LegacyImportReport.cs` | Bandera `--update` de `import-legacy`: además de agregar, actualiza desde QuickBooks los productos, proveedores, clientes y consignatarios que ya existen (nunca el saldo inicial). |

## Por qué P3 y P4 se hicieron a mano y no con el workflow de piezas en paralelo

El plan original (`docs/lote10-plan.json`) solo tenía P1/P2/P0. El workflow `lote-implementar` las construyó, compiló
y entró en la fase de verificación (4 lentes + refutación adversarial); Luis pidió cortarla porque estaba generando
demasiadas rondas de refutación para lo que hacía falta revisar. Se detuvo el workflow, se verificó a mano lo que
había (compiló, 2 287 pruebas en verde) y P3/P4 se añadieron directamente al código, sin pasar por el bucle de
revisión de 4 lentes. Antes de cortarlo, el workflow ya había corregido en su primera ronda tres hallazgos reales
(confirmados por los verificadores adversariales) que se conservan:

- `appsettings.json`: `ConnectionStrings:Teikem` había quedado con `User Id=teikem_user` en vez de `sa` (rompía CI y
  el smoke); se corrigió a `sa`, dejando solo el agregado `LegacyMswm`.
- `OPENING_BALANCE` es un motivo de ajuste de sistema en el API pero no estaba oculto en el selector de la web
  (`web-app/src/features/warehouse/InventoryAdjustModal.tsx`); se agregó a `SYSTEM_RESERVED_REASONS`.
- El reporte no decía a qué zona iba cada posición del WMS; ahora `Mapeos` incluye una fila "Zona de posición" por
  cada posición creada.

## Cómo se prueba

1. `dotnet build Teikem.sln` — compila sin errores (verificado en este entorno, .NET 8.0.425).
2. `dotnet test Teikem.sln` — **2 321 pruebas, 0 fallidas** (verificado en este entorno). Del lote:
   `LegacyImportRulesTests`, `LegacyImportReadersTests`, `LegacyImportCatalogTests`, `LegacyImportServiceTests`
   (incluye el dry-run completo del servicio contra EF InMemory) y `DbResetRulesTests` (confirmación, servidor
   local/remoto, nunca `MSWM*`, sintaxis, códigos de salida).
3. Verificación manual con el binario real (`dotnet run --project src/Teikem.Api`) contra el SQL Server local, sin
   escribir en la base:
   - `db-reset` sin `--yes` → *"db-reset borra la base de datos completa; confirme con --yes."*, código `2`.
   - `import-legacy` sin argumentos → mensaje de uso con `[--dry-run] [--update]`, código `2`.
   - `import-legacy docs/migracion/sample/import.sample.json --dry-run --update` → título con "— modo --update",
     sección "Saldo inicial" informa *"El saldo inicial no se toca en modo --update"*, reporte con 7 archivos
     (se agregó `-actualizaciones.csv`).
   - Ese mismo dry-run, corrido contra la base de desarrollo real (no InMemory), **detectó correctamente** que el
     término de pago `CHEQUE` de la muestra no estaba en el catálogo de esa base (el seed del Lote 10 no se había
     aplicado aún): confirma que la verificación de catálogo global funciona también sin tenant, tal como pedía el
     hallazgo de la primera ronda del workflow ("el dry-run de una compañía nueva no anticipa los rechazos de la
     carga real").
4. Se agregaron tres pasos a `scripts/smoke.sh`: dry-run de la muestra (código 0, reporte con 6 CSV), dry-run
   `--update` (marca el modo, informa que el saldo inicial no se toca) y `db-reset` sin `--yes` (código 2, no toca
   la base). No se corrió el smoke completo en esta sesión (requiere el API arriba, JWT y un recorrido largo de
   endpoints ajenos a este lote); queda para CI.

## Hallazgo fuera de este lote (a revisar aparte)

Al correr `db-init` contra la base de desarrollo real para verificar el seed nuevo, falló con
`There is already an object named 'Tenant' in the database` porque `Diseño/logistica-db-estructura.sql` tiene un
cambio local **sin commitear** (2 líneas, del trabajo en curso del Lote F8a) que cambió el hash del archivo: el
verificador de idempotencia de `SqlScriptRunner` (`__SchemaVersion`, por nombre + hash SHA-256) ya no reconoce el
script como aplicado y trata de correrlo entero sobre una base que ya tiene esas tablas. **No es un defecto de este
lote** (el Lote 10 solo tocó `logistica-db-seed.sql`, no `logistica-db-estructura.sql`) y no se corrigió aquí porque
pertenece al trabajo en curso de otro lote. Es exactamente el escenario que resuelve `db-reset`, pero correrlo
sobre esa base de desarrollo borraría los datos de Advance Logistics que haya ahora mismo: queda a criterio de Luis.

## Decisiones tomadas (revisar)

Las once decisiones de fondo (modelo de una compañía por empresa, fuente de verdad de existencias, sin lotes/series,
categorías, proveedores, limpieza de datos de prueba, qué no se migra) están en
[`docs/migracion-depot-solutions-plan.md`](migracion-depot-solutions-plan.md) sección 5 y no se repiten aquí. De
implementación:

- **`OPENING_BALANCE`** es un motivo de ajuste reservado al sistema (como `RECEIPT_VARIANCE`): un ajuste manual con
  ese código recibe 400 *"El motivo OPENING_BALANCE lo asigna el sistema."*; solo lo escribe `import-legacy`.
- **Los términos de pago de QuickBooks se agregan al catálogo global** (`PaymentTerm`), no a uno propio del lote:
  los catálogos son compartidos entre compañías por diseño de la plataforma.
- **Idempotencia por clave natural**: compañía por nombre, categoría por nombre, producto por SKU, proveedor por
  nombre, cliente por su campo personalizado "Código QuickBooks", posición por código, consignatario por tipo
  (DELIVERY/BILLING) por cliente.
- **`db-reset` es una acción explícita y protegida**: exige `--yes`, rehúsa servidores no locales sin
  `--allow-remote` y nunca borra una base que empiece por `MSWM` (el WMS heredado). No hay una versión "silenciosa".
- **`--update` nunca borra ni toca el saldo inicial.** Un valor vacío en QuickBooks nunca sobrescribe uno ya
  capturado en Teikem; solo actualiza cuando QuickBooks trae un valor distinto del actual.
- **La carga real de Depot y Solutions con los datos de producción queda pendiente** de que Luis apruebe correr
  `import.depot.json` e `import.solutions.json` contra una base (de prueba o real); este lote entrega el mecanismo,
  no la carga en sí.

## Qué queda fuera de este lote (a propósito)

- Migrar Truss PR (decisión explícita de Luis: no se migra nunca).
- Migrar historial de ventas, compras o libro mayor de QuickBooks (decisión explícita: solo maestros y saldo
  inicial).
- Sincronización continua o programada QuickBooks↔Teikem: `--update` es manual, a pedido.
- Corregir el desajuste de `Diseño/logistica-db-estructura.sql` descrito arriba (pertenece a otro lote en curso).
- El smoke completo de extremo a extremo con el API arriba (los tres pasos nuevos se limitan a lo que no requiere
  tenant real ni JWT).
