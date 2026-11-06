# Saldo inicial de Advance Solutions en producción (una sola vez)

Pedido de Luis, 2026-11-05. Usa el comando que ya existe (`import-legacy`, ver `docs/manual/10-migracion-de-datos.md`) con `openingBalances.source = quickbooks`: crea un ajuste `OPENING_BALANCE` por producto en la posición **PISO** del almacén **ALM-SOLUTIONS** de la compañía **Advance Solutions**. No es una importación en pantalla a propósito.

Configuración: [import.solutions-saldo-inicial.json](import.solutions-saldo-inicial.json). Fuente: `ItemList.CSV` de QuickBooks (columna `Quantity On Hand`), exportado hoy.

## Qué esperar (prueba en la base local, 2026-11-05)
36 productos con existencia, 69.530 unidades en total. Los ítems tipo Service (1000, CCTEST) y los SKU excluidos (101010, 121212, 979) no se cargan. Los SKU se normalizan sin espacios (`NECH 1001` → `NECH1001`), igual que en la migración original.
**La base local está atrasada, así que esa prueba no pudo leer lo que ya existe** (por eso decía «Creados» en compañía y productos). En producción esas filas deben salir como «Ya existían».

## Pasos (los corre Luis en el servidor; yo no tengo acceso a producción)
1. **Respaldo** de la base de producción.
2. Copiar `ItemList.CSV` y el `.json` al servidor y ajustar `sources.products` a la ruta del servidor. El `ItemList.CSV` debe ser el de HOY: la existencia que trae es la que queda.
3. En la carpeta del sitio publicado, en PowerShell como Administrador, simular: `dotnet Teikem.Api.dll import-legacy <ruta>\import.solutions-saldo-inicial.json --dry-run`
   (si no arranca con la cadena de conexión de producción, avisar; no lo he podido comprobar en el servidor).
4. Leer el reporte. Debe decir: Compañía y Almacén «Ya existían»; Productos «Ya existían» (ninguno «Creado»); **Rechazos 0**; Saldo inicial = 36. Si la compañía o el almacén salen «Creados», **parar**: el nombre o el código no coinciden.
5. Si cuadra, correr lo mismo **sin** `--dry-run`. Si se vuelve a correr, dice «El saldo inicial ya fue cargado en ALM-SOLUTIONS; no se repite».
6. Revisar en Productos e inventario que las existencias coincidan con QuickBooks.

Nota: si PISO no existe en ALM-SOLUTIONS, la herramienta la crea en la zona PCK (que ya existe en el almacén por el archivo de posiciones).
