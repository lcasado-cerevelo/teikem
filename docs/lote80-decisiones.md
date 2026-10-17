# Lote 80 — Pantallas de notas de crédito (P6, 2 de 5) (2026-10-16)

Segundo lote de P6: la web de las notas de crédito del Lote 79. Siguen: **81** devoluciones de venta (servidor), **82** pantallas de devoluciones y **83** la app (recibir una devolución).

## Qué se construyó
- **Pestaña «Notas de crédito»** en Facturación: lista (número, cliente, factura, motivo, estatus, total, saldo, fecha; buscador, filtro y exportación) y botón **Nueva nota de crédito** (`billing.credit`).
- **Ficha de la nota**: líneas con impuesto, totales, aplicaciones y las acciones por estatus: **Cambiar** (editor), **Aprobar** (AAL2 con la reautenticación automática del cliente del API), **Aplicar a una factura** y **Anular** con motivo (no si ya se aplicó).
- **Editor** de una nota sobre una factura (elegir la factura, líneas marcables con cantidad y monto unitario por omisión lo facturado, valor estimado) o libre (cliente, referencia externa, líneas con tipo de cargo); valida antes de enviar lo que el servidor valida (`CreditParts.tsx`, nuevo).
- **Aplicar**: facturas abiertas del mismo cliente, monto por omisión = el menor de los dos saldos, validación de topes.
- En la **ficha de la factura**: «acreditado X, saldo Y» y **Crear nota de crédito**; en **Cobros**: columna «Saldo a favor» y, en el estado de cuenta, «Saldo a favor (sin aplicar)».
- Reglas puras en `billingRules.ts`, hooks en `billingApi.ts`, textos `billing.credit.*` en es/en, estilos en `billing.css`; `openapi.json` y el cliente generado con las 5 rutas y los esquemas nuevos y cambiados.
- Manual de pantallas `docs/manual/frontend/l80-notas-de-credito.md` con 4 capturas y entrada en el README.

## Cómo se probó
- vitest: reglas (qué admite cada estatus, validación de nota libre y sobre factura, cuerpos, aplicar, estimado; 6) y la pantalla con un fetch simulado (11): lista y filtro, aprobar y las acciones que cambian, mensaje del servidor al aprobar, aplicar con tope y cuerpo, anular con motivo, nota aplicada sin anular, nueva nota sobre una factura con su cuerpo, sin líneas, nota libre sin cliente, el botón desde la factura y sin permiso (55 pruebas del módulo). `npm run check` completo.
- Playwright contra el API real (SQL Server), escritorio y móvil (360 px, sin scroll horizontal): aprobar la corrida con reautenticación, crear la nota desde la ficha de la factura (pasarse del tope da el aviso del servidor), guardar el borrador, aprobarla con reautenticación, aplicar de más (aviso), aplicar 10 y luego el resto hasta Aplicada, la factura con «acreditado» y su saldo y la lista con la nota Aplicada.

## Decisiones a revisar con Luis
1. **Pestaña propia** («Notas de crédito») en Facturación y no dentro de Cobros: crear y aprobar es trabajo de facturación; cobranza aplica.
2. **El editor sobre una factura no deja agregar líneas nuevas** (solo acreditar las de la factura); para un concepto que no está en la factura se usa una nota libre.
3. **El impuesto no se calcula en pantalla** (solo el valor de las líneas): lo calcula el servidor, para no duplicar reglas de impuestos.
4. **Aplicar es manual** y uno a uno (una factura por vez); no hay aplicar a varias facturas de un solo paso.
5. Las notas aún no salen en las plantillas de exportación ni tienen avisos; vienen con los lotes de devoluciones.

## Fuera de este lote
Devoluciones de venta (81 a 83), exportar notas con plantillas, avisos y análisis de notas, y desaplicar.
