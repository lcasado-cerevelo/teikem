# Migración de datos heredados (Lote 10): Advance Depot y Advance Solutions

Comando CLI del API que aprovisiona una compañía (tenant) y la puebla desde las exportaciones de **QuickBooks Desktop**
(CSV) y, para Advance Depot, desde la base **MSWM** del WMS actual. Escribe **siempre** a través de los servicios de
Teikem (productos, clientes, consignatarios, contactos, proveedores, almacén, zonas, posiciones y el `InventoryLedger`
para el saldo inicial): no hay scripts T-SQL de datos ni `INSERT` directos, así que quedan auditoría, historial de
estatus, `PublicId` y conciliación del inventario como en cualquier alta desde la pantalla.

Plan aprobado y mapeo campo a campo: [`docs/migracion-depot-solutions-plan.md`](../migracion-depot-solutions-plan.md).

```
dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run]
```

| Código de salida | Significado |
|---|---|
| `0` | Terminó sin rechazos graves (puede haber rechazos por fila y advertencias: revise el reporte). |
| `1` | Rechazo grave: error de configuración, falla al leer una fuente, compañía que no se pudo aprovisionar o conciliación del inventario con diferencias. |
| `2` | Sintaxis incorrecta: `Uso: dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]` |

`--dry-run` y `--update` se pueden combinar (`--dry-run --update`) para simular un refresco antes de aplicarlo.

## Los datos reales viven fuera del repositorio

Los CSV de QuickBooks contienen correos y teléfonos de clientes: **no se suben a git**. Viven en `F:\Download\TeikemMigracion\`
(`Depot Products.csv`, `Depot Customers.csv`, `Depot Vendor.csv`, `Solutions Items.csv`, `Solutions Customers.csv`,
`Solutions Vendors.csv`). El repositorio solo lleva:

| Archivo | Para qué |
|---|---|
| `import.depot.json` | Advance Depot: QuickBooks Depot + WMS MSWM (almacén `Main`), categorías **AxisCare** y **CARTONES**, almacén `ALM-DEPOT` con 6 zonas. |
| `import.solutions.json` | Advance Solutions: QuickBooks Solutions (+ el SKU `00050-7` de Depot), categoría = fabricante del ítem (columna `MANUFACTERS`; sin fabricante, `PRODUCTOS`; opción `products.categoryFromManufacturer`), 6 proveedores, almacén `ALM-SOL` con la posición única `GENERAL`. |
| `sample/` | Muestra **sintética** (datos inventados, dominios `example.com` y teléfonos 555) con el formato exacto de QuickBooks; la usan el smoke y las pruebas. |

Si los archivos están en otra carpeta, edite las rutas de `sources` (pueden ser absolutas o relativas al JSON).

## Preparar la base MSWM (solo Advance Depot)

1. Restaurar `MSWM` en el SQL Server local, una sola vez: `sqlcmd -S localhost,1433 -E -i F:\Download\TeikemMigracion\MSWM.sql`
   (el script es UTF-16 y pesa ~400 MB; tarda varios minutos).
2. Comprobar: `SELECT COUNT(*) FROM MSWM.dbo.Item`, `dbo.Inventory` y `dbo.Location`.
3. La cadena `ConnectionStrings:LegacyMswm` de `appsettings.json` usa *Integrated Security*
   (`Server=localhost,1433;Database=MSWM;Integrated Security=True;…`). El usuario `teikem_user` no tiene acceso a MSWM.
   Si necesita otra cadena (por ejemplo `sa`), créela en `src/Teikem.Api/appsettings.Development.local.json`
   (está en `.gitignore`; el API la carga después de los `appsettings` versionados):

   ```json
   { "ConnectionStrings": { "LegacyMswm": "Server=localhost,1433;Database=MSWM;User Id=sa;Password=…;TrustServerCertificate=True;Encrypt=False" } }
   ```
4. Para leer directamente el **MSWM de producción** (sin restaurarlo), defina la variable de entorno de usuario
   `ConnectionStrings__LegacyMswm` (doble guion bajo); pisa a los `appsettings`. Conviene `ApplicationIntent=ReadOnly`
   y un usuario de solo lectura. Las terminales y Visual Studio abiertos antes de crearla no la ven: ábralos de nuevo.

El importador solo ejecuta `SELECT` parametrizados por `@warehouseId` sobre `dbo.Item`, `dbo.Location`,
`dbo.Inventory` (`OnHandQuantity <> 0`) y `dbo.ItemUPC`, más el historial por posición para estimar el cupo
(`dbo.Inventory`, `dbo.Inventory_Old`, `dbo.CycleCountInventory`, `dbo.CycleCountHistory` y `dbo.PutAwayHistory`, sumados por foto). **Nunca escribe en MSWM** y solo lee el almacén configurado
(`Main`): `TrussPR` no se migra.

## Antes de la carga

- `db-init` con el seed del Lote 10 (agrega los términos de pago de QuickBooks `CHEQUE`, `CASH`, `ACH`, `NET20`,
  `NET45`, `CONSIGNMENT`, `PK_BY_REP` y el motivo de ajuste `OPENING_BALANCE`):
  `dotnet run --project src/Teikem.Api -- db-init`. Sin el motivo, la carga real se detiene con
  *"El motivo de ajuste OPENING_BALANCE no existe en el catálogo; ejecute db-init antes de la carga."*
- El administrador `teikem+admin@cerevelo.com` (el de Advance Logistics) recibe membresía en cada compañía nueva.

## 1. Simulación (`--dry-run`)

```
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json --dry-run
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json --dry-run
```

Lee todas las fuentes, arma el plan con las mismas reglas que los servicios y escribe el reporte **sin tocar la base**
(solo lee para saber qué ya existe; si la compañía no existe, todo se informa como "se crearía"). Revise el reporte
línea por línea antes de la carga real.

## 2. Carga real

```
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json
```

Orden de carga: compañía → categorías → almacén, zonas y posiciones → proveedores → productos → campos personalizados
(`qb_code` "Código QuickBooks" y `sales_rep` "Representante" en Cliente) → clientes con consignatarios (DELIVERY y, si la
dirección de facturación es distinta, BILLING) y contactos → saldo inicial (`ADJUSTMENT` con motivo `OPENING_BALANCE`, en
lotes de 200) → baja de los ítems inactivos en QuickBooks sin existencia → conciliación del inventario (con diferencias,
el comando termina con código 1).

**Es idempotente**: se puede repetir. La compañía se busca por nombre, la categoría por nombre, el producto por SKU, el
proveedor por nombre, el cliente por su "Código QuickBooks", la posición por código y el consignatario por tipo; lo que
ya existe se cuenta como "Ya existían". El saldo inicial **no se repite** si ya hay un asiento `OPENING_BALANCE` hacia
ese almacén (*"El saldo inicial ya fue cargado en ALM-DEPOT; no se repite."*). Una fila que un servicio rechaza se anota
con el mensaje exacto y la carga sigue con la siguiente.

## Recrear la base en blanco y volver a cargar (pedido de Luis, 2026-09-29)

> **Forma recomendada:** `scripts\recrear-base.ps1` hace todo el ciclo (con el MSWM de producción y validaciones previas).
> Ver [recrear-base.md](recrear-base.md).

La migración no es un trabajo de una sola vez: QuickBooks y el WMS cambian, y a veces conviene empezar de cero en vez de
apilar cargas. Para eso existe un verbo hermano de `db-init`:

```
dotnet run --project src/Teikem.Api -- db-reset --yes
```

Borra por completo la base de `ConnectionStrings:Teikem` (`ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE` +
`DROP DATABASE`) y la vuelve a crear exactamente como `db-init` sobre un servidor limpio: estructura, seed y seeders
(incluido el tenant demo Advance Logistics). El ciclo completo para repetir la migración desde cero queda en tres
comandos:

```
dotnet run --project src/Teikem.Api -- db-reset --yes
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json
```

Dos seguros, porque es una acción irreversible:

- **Sin `--yes` rehúsa**, con el mensaje exacto *"db-reset borra la base de datos completa; confirme con --yes."* y
  código de salida `2`.
- **Solo servidores locales** (`localhost`, `127.0.0.1`, `(local)`, `.`, `::1`, `(localdb)\…`, con o sin puerto o
  instancia). Contra cualquier otro host rehúsa con *"La cadena de conexión no apunta a un servidor local; use
  --allow-remote si de verdad quiere borrar esa base."*; se puede forzar con `--allow-remote`.
- **Nunca toca una base que empiece por `MSWM`** (el WMS heredado): *"db-reset no toca la base del WMS heredado."*

El usuario de `ConnectionStrings:Teikem` debe poder crear y borrar bases (`sa` o el que use `db-init` hoy); esto no
cambia nada del comando ni de la cadena existente.

## Refrescar datos sin empezar de cero (modo `--update`)

Entre una recreación completa y otra, QuickBooks sigue cambiando: nuevos productos, un cliente que corrigió su
dirección, un precio distinto. Para eso, `import-legacy` acepta `--update`:

```
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json --update
```

Sin `--update` (el comportamiento de siempre) el importador **solo agrega** lo que falta; lo que ya existe se cuenta
como "Ya existían" y no se toca. **Con `--update`**, además de agregar, actualiza desde QuickBooks las filas que ya
existen:

- **Productos** (por SKU): nombre (salvo que quede igual al SKU por no tener descripción), categoría, costo, precio y
  código de barras. Nunca el SKU, la unidad base ni el seguimiento (son inmutables una vez que el producto tiene
  movimientos), ni el estado activo/inactivo.
- **Proveedores** (por nombre): contacto, teléfono y notas.
- **Clientes** (por su "Código QuickBooks"): nombre legal, término de pago y límite de crédito. El nombre y el código
  del cliente no cambian.
- **Consignatarios**: la dirección de la `Location` DELIVERY/BILLING existente del cliente se actualiza con la de
  QuickBooks.
- **Contactos**: se agregan los correos y teléfonos nuevos; los que ya no vienen en QuickBooks no se borran.

**El saldo inicial nunca se toca en `--update`**: una vez que una compañía opera en Teikem, el inventario lo mueven
los recibos, despachos y ajustes de Teikem, no QuickBooks. El reporte lo dice explícitamente (*"El saldo inicial no se
toca en modo --update: el inventario lo mueve Teikem."*) y cuenta esas filas como omitidas, no como rechazo.

Un vacío de QuickBooks nunca borra un dato ya capturado en Teikem: `--update` solo cambia un campo cuando QuickBooks
trae un valor y ese valor es distinto del actual. El reporte suma una columna **Actualizados** al resumen y una
sección **Actualizaciones** (entidad, clave, campo, valor anterior y nuevo) para revisar exactamente qué cambió.

**Para un refresco típico solo hacen falta dos archivos por compañía** (Items y Customers de QuickBooks; Vendors solo
si agregaron proveedores). No hace falta re-exportar ni volver a montar el WMS para actualizar maestros.

## Dónde queda el reporte

En `report.outputDir` o, si no se indica, en la carpeta del primer archivo fuente (para los JSON reales:
`F:\Download\TeikemMigracion\`, fuera del repositorio, porque el reporte cita nombres, correos y teléfonos). Archivos:

- `{prefix}-{yyyyMMdd-HHmm}.md`: título con **SIMULACIÓN (dry-run)** o **CARGA REAL**, tabla de resumen (leídos,
  creados, ya existían, omitidos, rechazados y los totales esperados del plan), rechazos, advertencias, mapeos (SKU
  normalizados, términos de pago, posiciones pasadas a mayúsculas, la zona destino de cada posición del WMS con su
  descripción, productos adicionales), el conteo de posiciones por zona en el encabezado y el saldo inicial por SKU y
  posición.
- Un CSV por sección: `-resumen`, `-rechazos`, `-advertencias`, `-mapeos`, `-saldo-inicial`.
- Solo si hubo cupos estimados (almacén que viene de MSWM): `-cupos` (posición, zona, pasillo, máximo histórico, cupo,
  origen y resultado) y la sección **Cupos de posición estimados** del `.md`.

La muestra sintética escribe en `TestResults/migracion/` (ignorado por git).

## Reglas que conviene conocer

- SKU: se quitan todos los espacios y se pasa a mayúsculas (`NECH 1001` → `NECH1001`, `171-ac-426-b` → `171-AC-426-B`);
  cada cambio se lista. Sin descripción, el nombre es el SKU. Seguimiento `NONE` y unidad `UN` para todo.
- Depot: Category vacía → **AxisCare**; `CARTONES` → **CARTONES**; `SOLUTIONS` y `NATIONAL GUARD` no van a Depot. Los
  SKU del WMS con existencia que no están en QuickBooks se crean en AxisCare y se marcan en el reporte.
- Posiciones del WMS: `01-a-24` → `01-A-24` (pasillo 01, nivel A, posición 24); si dos ids coinciden al pasarlos a
  mayúsculas se conserva el primero. `W1`, `Z1`, `01` y `R-1` no se migran; una posición sin zona destino se omite.
- Código de barras: el primer UPC del WMS; si el mismo UPC está en varios productos, no se asigna a ninguno.
- Cupo de posición (solo almacenes de MSWM): el mayor total histórico de la posición en `Inventory`, `Inventory_Old`,
  `CycleCountInventory`, `CycleCountHistory` y `PutAwayHistory` (acomodos por día), redondeado hacia arriba a la decena
  (origen `HISTORIAL`); sin historial, la mediana de su pasillo (`PASILLO`), de su zona (`ZONA`) o del almacén (`ALMACEN`);
  la mediana de un pasillo o de una zona solo cuenta si sale de al menos 5 posiciones con historial.
  Una posición nueva nace con su cupo; una existente con cupo nunca se pisa; una existente sin cupo solo se llena con
  `--update`. Detalle y resultado de la validación contra MSWM en la sección 7 del plan.
- Solutions: el saldo inicial es la *Quantity On Hand* de QuickBooks en la posición `GENERAL`; las existencias negativas
  no se cargan y se informan. Las cajas `101010`, `121212` y `979` solo existen en Depot.
- Clientes de prueba descartados: `1`, `Alec` (Solutions) y `prueba2` (Depot).
- `OPENING_BALANCE` es un motivo reservado al sistema: un ajuste manual con él recibe 400
  *"El motivo OPENING_BALANCE lo asigna el sistema."*
