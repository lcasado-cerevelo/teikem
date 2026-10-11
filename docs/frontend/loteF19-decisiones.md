# Lote F19 — Despacho manual en la web (2026-10-11)

Consume el servidor del [Lote 31](../lote31-decisiones.md). Capítulo de pantalla: [F19](../manual/frontend/f19-despacho-manual.md).

## Decisión del dueño que cambió el diseño
**Sin pantalla ni menú nuevos.** El primer borrador tenía «Despachos manuales» (`/warehouse/manual-issues`) con su ficha; se retiró. Todo vive en
«Recolección y empaque»: ahí todo es recolección y las manuales (DMA) no tienen empaque.

## Qué se construyó
- `CollectPanel`: interruptor **Despacho manual (sin entrega)** (con `warehouse.issue`; si no hay `warehouse.pick`, el panel es manual siempre).
  Motivo (select, `GET /manual-issues/reasons`) y nota; envío a `POST /manual-issues` con `Idempotency-Key` (misma llave si el cuerpo es el mismo).
  Reutiliza la rejilla, FEFO, series y «Sugerir posiciones» sin copiar. `collectSchema(…, { manual })` agrega motivo obligatorio y nota ≤ 500.
- `PickBatchesPanel`: **sin** `kind` (ALL); columna **Tipo** (Empaque / Despacho manual · motivo), filtro **Tipo** (`kind=PACK|MANUAL`),
  estatus «Despachado»/«Cancelado» (`PickBatchStatusChip`), Eliminar según `usePickBatchCanDelete` (manual = `warehouse.issue`).
- Ficha (`PickBatchDetailBody`): motivo, dueño, nota, enlace al Kárdex, sin etapas ni Empacar; `DeletePickBatchDialog` usa
  `DELETE /manual-issues/{id}` si `isManual`.
- Lógica pura en `manualIssueView.ts`; hooks en `api.ts` (`useManualIssueReasons`, `useCreateManualIssue`, `useDeleteManualIssue`, `useManualIssues`, `useManualIssue`, `exportManualIssues`; los dos últimos hoy sin pantalla que los use).

## Cómo se probó
`npm run check` verde (152 archivos, 1525 pruebas): `PickBatchManual.test.tsx` (lista, filtro Tipo, modo manual, motivo obligatorio, POST con
llave, 409 en su fila, ficha, Eliminar, permisos), `manualIssueView.test.ts`, `pickBatchView.test.ts`, `PickBatchScreen.test.tsx` (columna Tipo).
Playwright `e2e/loteF19-despacho-manual.spec.ts` (proyectos `escritorio-f19` y `movil-f19`, después de F18) contra el API real: 409, DMA real
(existencia 5 → 3), lista, filtro, ficha, Kárdex, Eliminar (vuelve a 5, Cancelado), sin scroll horizontal a 360 px; y `lote13.spec.ts` sigue verde.

## Decisiones a revisar
1. **Filtro por motivo**: el servidor no lo trae (`kind` y `search` solamente). No se agregó; la búsqueda libre ya encuentra por motivo y nota.
2. **Nota fuera del Kárdex** y estatus «Despachado» (solo etiqueta; el código sigue `COLLECTED`).
3. El orden de campos del panel es Almacén, interruptor, Motivo, Nota (el modal ya no existe).
