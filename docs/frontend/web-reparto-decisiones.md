# Web: reparto por posición, plan de salida y calculadora con fondo (2026-10-07)

## Qué se construyó
- Calculadora de cantidad con «Fondo» en la app y en el conteo cíclico web (`quantityCalc.ts`, `QuantityCalculator.tsx`).
- Reparto por posición en Acomodar (`SplitBinsEditor`, acción `distribute`) y en recibo directo (`ReceiptSplitModal`), con la misma regla que servidor y app (`splitPlan.ts`).
- Despacho: «Sugerir posiciones» (`planCollect` en `collectForm.ts`, acción en `CollectPanel`).

## Cómo se probó
vitest: `splitPlan`, `TaskSplit`, `ReceiptScreen` (reparto), `planCollect`; `npm run check` verde. No hay prueba de componente de «Sugerir posiciones» ni recorrido en navegador.

## Decisiones a revisar
- El reparto del recibo web son llamadas secuenciales, no atómicas (el servidor no tiene endpoint de reparto de recibo).
- Solo recibos ciegos/devolución y productos sin serie; las series no se reparten.
- El plan de salida usa saldos disponibles del almacén por el orden de salida configurado.
