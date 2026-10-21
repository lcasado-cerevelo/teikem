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
