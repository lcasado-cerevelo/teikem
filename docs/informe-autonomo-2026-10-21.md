# Informe del trabajo autónomo (2026-10-21)

Pedido de Luis: hacer sin supervisión las partes que faltan —C4, H1, G2 y los menores—; al terminar cada parte: pull, commit y push; anotar el resultado aquí y reportar al final.

## Supuestos que tomé (a revisar)
- **C4 (recibo con varios documentos):** misma regla que el despacho: un solo dueño/cliente, se arma al empezar el recibo, no se suman documentos después.
- **H1 (contabilización):** la regla del plan (por dueño del inventario, costo congelado por línea); pendiente de visto bueno de Luis.
- **G2 (avisos de flota):** umbrales ajustables por compañía con valores por omisión razonables.
- **Menores:** donde había que elegir, elegí lo más simple y lo anoto en la parte.

## Resultados por parte

### G2 — Avisos de flota, parte 2 (Lote 179) ✔
- Cuatro avisos nuevos en el trabajo diario existente: órdenes de trabajo abiertas > 14 días, combustible fuera de rango (> 30 % del promedio de las 3 a 5 cargas anteriores), vehículos y choferes sin viaje > 14 días.
- Pruebas: `FleetAlertTests` 24 verdes (+10) y las de avisos/trabajos (203). Sin cambios de estructura.
- **Para revisar:** umbrales fijos (no ajustables por compañía); lo dejé así a propósito para no abrir cuatro ajustes sin datos reales. Detalle: `docs/lote179-decisiones.md`.

### Menores (Lote 180, en curso) — parte 1 ✔
- **Ocultar opciones de recibo a ciegas cuando se exige documento:** «Nuevo recibo» (web) ofrece solo los orígenes permitidos por «el recibo exige documento» (libre: todo; cualquier documento: aviso/orden; aviso; orden de compra) y abre con el primero permitido. `receiptSources.ts` + 3 pruebas; el servidor igual valida (409).
- **Diferencia de precio en líneas existentes de una orden de compra:** la ficha lee el costo guardado del producto de cada línea ya existente y no recibida, y muestra el aviso «El producto tiene guardado X; este precio es distinto.» con la casilla para actualizarlo (si tiene `inventory.manage`). 1 prueba nueva.
- **«Mensajes de clientes» sin menú:** ya estaba **decidido por Luis el 2026-10-20: sin ítem de menú** (el portal tiene su propia app; la pantalla sigue en `/portal/messages`). Lo cierro como decidido, sin código.

### Menores — parte 2: smoke ✔
- **`scripts/smoke.sh` está obsoleto** (no por un cambio reciente): el script de actualización renombró los estatus de las órdenes (DRAFT→SUBMITTED, INBOUND→UNASSIGNED, PLANNED→ASSIGNED y se borraron CONFIRMED, ARRIVED, ON_HOLD, PARTIAL) y el smoke, de 5 459 líneas, sigue usando los viejos en cientos de pasos desde «estatus (B)». Lo corrí contra una base nueva: falla en el primer uso de `INBOUND`. **No lo reescribí** (sería rehacerlo casi entero); decisión de Luis: ¿se rehace, se recorta o se retira del CI?
- **Nuevo `scripts/smoke-maestra.sh`** (agregado al CI antes del smoke general): recolección maestra de punta a punta contra el API real. **Pasa completo, incluido el empaque de la maestra con UN envío y la eliminación de una maestra empacada**, que era lo que había quedado sin probar en los Lotes 176-178.

### Menores — parte 3: filtro de zona en Saldos y Conciliación ✔
- `GET /inventory/balances` y `GET /inventory/discrepancies` aceptan `zoneIds` (como el Kárdex desde el Lote 159: equivale a las posiciones de esas zonas; con posiciones elegidas deben cumplirse las dos; en Conciliación el total de un producto sin posición queda fuera). La barra de filtros compartida ya los aplica a las tres pestañas.
- Pruebas: 2 xunit nuevas (saldos por zona, descuadres por zona) + 1 vitest; contrato regenerado de los dos endpoints.

### Menores — parte 4: EXIT_NOTICE como fuente de Vistas ✔
- Nueva fuente de datos `EXIT_NOTICE` (avisos de salida, una fila por aviso: cliente, almacén, referencia, destino, estatus, abierto/atrasado/con pendiente/asignado, unidades pedidas/reservadas/despachadas/pendientes, cumplimiento). Lectura con `inventory.view` y módulo de almacén con lotes/series; se puede usar en Vistas, indicadores y gráficos desde la web sin más cambios. No sembré vistas de sistema nuevas (las arma cada compañía).
- Pruebas: 2 nuevas (permiso y filas con atraso/pendiente/cumplimiento/ids) + las de análisis, permisos y seeders (425 verdes).

### Menores — parte 5: encabezado de tarjetas y paneles + recorrido Playwright ✔
- **Encabezado** (`.panel .ph2`): fondo = el **tercer color de la marca (el gris, `--money`) al 8 %** mezclado con el fondo del panel (`color-mix`; sale de la marca y del tema, igual que la barra lateral), esquinas superiores redondeadas; lo atenuado dentro del encabezado (contador y subtítulo) se oscurece con 25 % del texto para conservar AA. No toca los encabezados de las tablas. Prueba de contraste: **los 14 temas × claro y oscuro** (texto ≥ 7, atenuado ≥ 4.5) sobre el encabezado, y que `base.css` use las mismas proporciones que las constantes (`brandTheme.test.ts`, 4 pruebas nuevas). Además se vio en pantalla (capturas `l180-encabezado-claro/oscuro`).
- **Recorrido Playwright nuevo** `e2e/lote180-encabezado-maestra.spec.ts` (escritorio y móvil, **pasa**): (1) el encabezado tiene fondo propio y apenas se aparta del panel en claro y en oscuro; (2) **recolección maestra por la web contra el API real**: «Recolectar juntos» dos avisos, la ficha muestra los movimientos de las dos posiciones («de aquí, de allá»), Empacar/Eliminar disponibles, y Eliminar devuelve el inventario. Capturas del manual en `docs/manual/frontend/img/l180-*.png`.

### Menores — parte 6: `db-update` contra SQL Server (simulación y corrida real) ✔
- **Hallazgo y arreglo (real, habría fallado en producción):** `db-update` aplicaba primero el diff de esquema y después `logistica-db-update.sql`. Las secciones del script agregan columnas NOT NULL por pasos (anulable → relleno → NOT NULL); el diff las agregaba de golpe como NOT NULL sin valor por omisión y fallaba en cualquier tabla con datos («cannot be added to non-empty table», p. ej. `Asn.Number` del Lote 170). **Ahora en la corrida real el `update.sql` va primero** y el diff solo completa lo que el script no cubra; la simulación (`--dry-run`) no toca la base y sigue mostrando el plan de siempre (`DatabaseInitializer.UpdateAsync`).
- **Probado** contra SQL Server 2022 con una base «vieja» (estructura + seed congelados, un tenant, un almacén y 2 avisos de llegada cargados): la simulación no cambia nada; la corrida real termina «base de datos al día», numera los avisos existentes (`LLE-00001`, `LLE-00002`), y una segunda corrida da 0 cambios (idempotente).
