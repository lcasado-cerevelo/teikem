# Lote 125 — Cierre de P12: alertas operacionales, SLA como indicador y Kemi con pantalla e inglés

## Qué se construyó
- **Cuatro avisos** de «Necesita tu atención» (`OperationalAlertProviders.cs`), todos filas agregadas y con su permiso/módulo: `ORDERS_FAILED` (intento fallido; fecha desde el último paso a ese estatus; rojo > 2 días), `CLAIMS_OPEN` (sin resolver; cuenta, la más antigua y valor declarado; rojo > 7 días), `FLEET_DOCS_DUE` (los mismos documentos del panel «Documentos por vencer», 30 días; rojo si hay vencidos) e `INVOICES_OVERDUE` (estatus Vencida; rojo). Texto es/en en la web.
- **SLA como indicador:** campo nuevo `SlaMetPct` en la fuente de órdenes (100 cumplida, 0 fuera de SLA, vacío si no aplica), indicadores «Órdenes con SLA en riesgo» (en el Pulso) y «Entregas dentro del SLA» (promedio a 30 días), gráfico «Órdenes por estado del SLA». Se siembran con el contenido de fábrica (no requiere SQL).
- **Kemi:** el contexto de la pantalla donde está la persona sube lo documentado de ella (×1,35, como Yudi) y las palabras comunes en inglés se acompañan de su equivalente en español (glosario de ~100 palabras) con aviso de que las respuestas salen en español.
- **P12 queda cerrado** en el plan único.

## Cómo se probó
- xunit: `OperationalAlertTests` (5: fallida con fecha del historial y urgencia, reclamaciones abiertas vs resueltas, facturas vencidas, documentos de flota sin documentos y permiso, y la fuente de órdenes con `SlaMetPct` 100/0 y los filtros sembrados contra los campos de la fuente). Suite completa: 4.540 pasan, 0 fallan.
- vitest: 4 pruebas de los textos de las alertas, 2 del glosario/pantalla de Kemi. Suite completa: 3.180 pasan.

## Decisiones a revisar
1. **Umbrales de urgencia** (2 días fallidas, 7 días reclamaciones; facturas siempre rojo; documentos rojos si hay vencidos): constantes fáciles de cambiar.
2. **El manual no se tradujo:** en inglés Kemi busca con un glosario y responde en español; traducir 2.700 entradas queda fuera hasta que Luis lo pida.
3. **Las facturas vencidas** salen del estatus OVERDUE (lo marca el trabajo programado de facturación).
4. **Sin recorrido Playwright** de los avisos nuevos (cubiertos con SQL real en xunit y con vitest los textos).
