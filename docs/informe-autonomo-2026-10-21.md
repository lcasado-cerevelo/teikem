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

### Menores — parte 7: el portal edita sus productos ✔ (adjuntar documentos queda aparte)
- **Nuevo `PATCH /portal/warehouse/products/{publicId}`**: el cliente edita nombre, código de barras, marca, modelo, peso, volumen y costo de SUS productos (mismo permiso que crearlos: bandera de la compañía + rol ADMIN/OPERADOR; producto de otro cliente → 404; sin cambios → 400). Delega en `ProductService.UpdateAsync` (mismas reglas y mensajes que el personal). SKU, seguimiento y unidad no se tocan.
- App del cliente: botón «Editar» en cada producto de la pestaña Productos y formulario `EditProductForm` (parte de lo que ya tiene; costo vacío = se conserva). Tipos de `schema.d.ts` agregados a mano porque `web-app/openapi.json` no incluye los endpoints del portal de almacén.
- Pruebas: 2 xunit nuevas (edita lo propio / 404 lo ajeno / cuerpo vacío; bandera apagada y solo lectura) + 2 jest. Manual 41 y FAQ al día.
- **Adjuntar documentos (NO hecho):** no existe almacenamiento de archivos para avisos ni órdenes del cliente (solo metadatos en documentos de vehículos/conductores), así que hacerlo es un lote propio (tabla, almacenamiento, endpoints, pantallas en portal y web). **Decisión de Luis:** qué documentos se adjuntan (¿lista de empaque, factura, guía?), a qué (aviso de llegada, de salida), tamaño máximo y dónde se guardan.

### Menores — parte 8: endurecer los endpoints con los permisos de acceso del menú ⏸ (no hecho, a propósito)
- Hoy el permiso de **acceso** (`kardex.view`, `products.view`…) gobierna el menú y la ruta de la web, y los endpoints siguen con los permisos de **datos** (`inventory.view`…). Exigir también el de acceso en el servidor **rompería** a quien hoy tiene el permiso de datos sin el de acceso: los aparatos (la app de almacén lee con `inventory.view`), las integraciones y los roles ya clonados. Es un cambio de seguridad con riesgo de bloquear la operación en producción, y no hay una regla clara de qué endpoint pertenece a qué ítem de menú. **Decisión de Luis:** ¿se quiere de verdad (y se acepta que los aparatos pidan el permiso nuevo), o basta con la puerta de la web?

## H1 — Contabilización de compras y de despachos ✔ (Lote 181)
- **Construido completo** (servidor, web, manual, pruebas, humo en el CI): ver `docs/lote181-decisiones.md`. Regla: por **dueño del inventario** (lo propio sí, lo de un cliente no), sin importar el tipo de documento, con el **costo ya congelado** de cada línea; corridas `CMP-#####` / `DSP-#####` Generada → Aceptada → Exportada (+ Cancelada) con plantillas de exportación; una línea nunca en dos corridas vivas.
- **Encontró y arregló** (aparte de lo propio del lote): el contador real rechazaba los tipos nuevos; el aviso de diferencia de precio del Lote 180 nunca avisaba (leía `purchaseCost` del nivel equivocado) y estaban viejos los tests de contratos y el mapa de auditoría.
- **Por confirmar con Luis:** la regla de dueño/costo, que un recibo cuenta al cerrarse, que lo eliminado después de contabilizar no se corrige solo y que no lleva AAL2.

## C4 — Recepción maestra («Recibir juntos») ✔ (Lotes 182-183)
- **Hecho con la misma regla del despacho** (un solo dueño y almacén, se arma al abrir, sin sumar después): ver `docs/lote182-decisiones.md`. Es un **envoltorio** `RCM-#####` sobre los recibos de siempre (cada aviso conserva su `REC-#####`): se abren juntos con el mismo transporte y referencia y se confirman juntos (todo o nada).
- Servidor, SQL, pruebas (incluida una con SQL Server real), humo contra el API real en el CI, pantalla web (pestaña «Maestros», «Recibir juntos», ficha, confirmar todo, eliminar), manual 44 + L183 + FAQ y recorrido Playwright (escritorio y móvil).
- **No hecho:** la app de almacén, elegir órdenes de compra en la pantalla y capturar una sola vez para todo el camión repartiendo entre los avisos (cambiar el modelo del recibo era demasiado riesgo: `AsnId` único en 100+ lugares). **Decide Luis** si el envoltorio basta o si se quiere la captura única.
