# Capítulo 10 — Migración de datos heredados (Lote 10)

Qué hace: un comando de línea de comandos, fuera del sitio web, que aprovisiona una compañía nueva y la puebla desde
las exportaciones de **QuickBooks Desktop** (CSV) y, cuando corresponde, desde la base del WMS heredado **MSWM**.
Escribe siempre a través de los mismos servicios que usa el sitio (productos, clientes, consignatarios, contactos,
proveedores, almacenes y el ledger de inventario), así que queda auditoría, historial de estatus y conciliación de
inventario como en cualquier alta desde pantalla. No agrega permisos, endpoints ni pantallas nuevas: lo opera quien
tiene acceso al servidor, no un usuario del sitio.

Quién puede: se ejecuta desde la línea de comandos del servidor, no desde el sitio; no hay un permiso asociado. El
plan de la migración y las decisiones de qué se trae de cada compañía están en
[`docs/migracion-depot-solutions-plan.md`](../migracion-depot-solutions-plan.md).

Cómo se usa:

```
dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]
dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]
```

## 1. Simular y cargar

- `import-legacy <config.json> --dry-run`: lee todas las fuentes, arma el plan con las mismas reglas que usarían los
  servicios y escribe un reporte (`.md` + un CSV por sección) **sin escribir nada en la base**. Revisar este reporte
  antes de la carga real es obligatorio.
- `import-legacy <config.json>`: la carga real. Orden: compañía → categorías → almacén, zonas y posiciones →
  proveedores → productos → campos personalizados → clientes con consignatarios y contactos → saldo inicial
  (`ADJUSTMENT` con motivo `OPENING_BALANCE`) → baja de los productos que en QuickBooks están inactivos → conciliación
  del inventario.
- Es **idempotente**: se puede repetir. La compañía se busca por nombre, la categoría por nombre, el producto por
  SKU, el proveedor por nombre, el cliente por su "Código QuickBooks", la posición por código; lo que ya existe se
  cuenta como "Ya existían" y no se toca. El saldo inicial no se repite si ya hay un asiento `OPENING_BALANCE` hacia
  ese almacén.
- Un rechazo de una fila (SKU repetido, dirección sin ciudad, término de pago desconocido) se anota con su mensaje
  exacto en el reporte y la carga sigue con la fila siguiente. Un **rechazo grave** (error de configuración, una
  fuente que no se pudo leer, la compañía que no se pudo aprovisionar, o la conciliación del inventario con
  diferencias) hace que el comando termine con código de salida `1`.

## 2. Refrescar sin empezar de cero (`--update`)

Qué hace: además de agregar lo que falta, actualiza desde QuickBooks los productos, proveedores, clientes y
consignatarios que ya existen en Teikem. Pensado para cuando los datos de QuickBooks cambian después de la carga
inicial (un precio, una dirección, un cliente nuevo) y no hace falta repetir todo.

- `import-legacy <config.json> --update`: agrega lo nuevo y actualiza lo existente.
- `import-legacy <config.json> --dry-run --update`: simula el refresco (qué cambiaría) sin escribir nada.
- **El saldo inicial nunca se toca en `--update`.** Una vez que la compañía opera en Teikem, el inventario lo mueven
  los recibos, despachos y ajustes de Teikem, no QuickBooks; el reporte informa *"El saldo inicial no se toca en modo
  --update: el inventario lo mueve Teikem."* y cuenta esas filas como omitidas.
- Un valor vacío en QuickBooks **nunca borra** un dato ya capturado en Teikem: solo se actualiza un campo cuando
  QuickBooks trae un valor y ese valor es distinto del actual.
- Campos que sí se actualizan: en productos, nombre (salvo que quede igual al SKU por no tener descripción),
  categoría, costo, precio y código de barras — nunca el SKU, la unidad de medida base ni el tipo de seguimiento
  (son inmutables una vez que el producto tiene movimientos); en proveedores, contacto, teléfono y notas; en
  clientes, nombre legal, término de pago y límite de crédito — el nombre y el código del cliente no cambian; en
  consignatarios, la dirección de la localización de entrega o de facturación ya existente.
- El reporte suma una columna **Actualizados** al resumen y una sección **Actualizaciones** (entidad, clave, campo,
  valor anterior y nuevo).
- Para un refresco típico basta con volver a exportar de QuickBooks los archivos de **Items** y **Customers** de la
  compañía (y **Vendors** solo si hay proveedores nuevos); no hace falta tocar el WMS.

## 3. Recrear la base en blanco (`db-reset`)

Qué hace: borra por completo la base de datos configurada y la vuelve a crear exactamente como `db-init` sobre un
servidor limpio (estructura, seed, seeders y el tenant demo). Pensado para repetir la migración desde cero cuantas
veces haga falta durante las pruebas, sin arrastrar datos de una carga anterior.

```
dotnet run --project src/Teikem.Api -- db-reset --yes
```

- **Es irreversible**, por eso exige `--yes`. Sin esa bandera, el comando rehúsa con el mensaje exacto
  *"db-reset borra la base de datos completa; confirme con --yes."* y termina con código `2`, sin tocar nada.
- **Solo actúa sobre un servidor local** (`localhost`, `127.0.0.1`, `(local)`, `.`, `::1`, `(localdb)\…`, con o sin
  puerto o instancia). Contra cualquier otro servidor rehúsa con *"La cadena de conexión no apunta a un servidor
  local; use --allow-remote si de verdad quiere borrar esa base."*; se fuerza con `--allow-remote`.
- **Nunca borra una base que empiece por `MSWM`**: el WMS heredado está protegido aparte, con el mensaje
  *"db-reset no toca la base del WMS heredado."*, aunque alguien apunte por error `ConnectionStrings:Teikem` ahí.

El ciclo completo para volver a migrar desde cero:

```
dotnet run --project src/Teikem.Api -- db-reset --yes
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json
```

### Validaciones y mensajes exactos

| Caso | Mensaje exacto | Código de salida |
|---|---|---|
| `import-legacy` sin ruta de configuración, o con opciones que no son `--dry-run`/`--update` | `Uso: dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]` | 2 |
| `import-legacy` con un archivo de configuración que no existe o le falta un dato obligatorio | (mensaje de la validación, por ejemplo `El archivo no existe: {ruta}.`) | 1 |
| El motivo `OPENING_BALANCE` no está en el catálogo (falta correr `db-init` con el seed del lote) | `El motivo de ajuste OPENING_BALANCE no existe en el catálogo; ejecute db-init antes de la carga.` | 1 |
| Un término de pago mapeado de QuickBooks no está en el catálogo | `El término de pago {código} no existe en el catálogo; ejecute db-init antes de la carga.` | 1 |
| Ajuste manual de inventario con motivo `OPENING_BALANCE` (desde el sitio o la API) | `El motivo OPENING_BALANCE lo asigna el sistema.` | 400 |
| `db-reset` sin `--yes` | `db-reset borra la base de datos completa; confirme con --yes.` | 2 |
| `db-reset --yes` contra un servidor que no es local, sin `--allow-remote` | `La cadena de conexión no apunta a un servidor local; use --allow-remote si de verdad quiere borrar esa base.` | 2 |
| `db-reset --yes` contra una base que empieza por `MSWM` | `db-reset no toca la base del WMS heredado.` | 2 |
| `db-reset` con sintaxis distinta a `--yes [--allow-remote]` | `Uso: dotnet run --project src/Teikem.Api -- db-reset --yes [--allow-remote]` | 2 |

## Preguntas frecuentes

Ver la sección "Lote 10 — Migración de datos" en [faq.md](faq.md).
