# Lote 96 — Liquidación a choferes: la pantalla (P9, 4 de 4) (2026-10-17)

Último lote de P9: la pantalla de la liquidación a choferes y de los intentos de entrega.

## Qué se construyó
- **Pantalla** `Contabilidad → Liquidación a choferes` (`features/driverpay/`): pestaña **Liquidaciones** (lista con búsqueda y filtros, «Nueva liquidación» con vista previa de lo que entraría, ficha con las líneas por día, ajuste por línea, revisar, aprobar con reautenticación, cancelar y exportar con plantilla) y pestaña **Intentos de entrega** (lista con filtros, registrar a mano buscando la orden, anular con motivo).
- La ruta `/money/driver-pay` (antes pendiente) ya apunta a la pantalla; permisos y módulo como en el menú.
- API: `DeliveryAttemptDto.ClientName` y la descripción de la línea de entrega con el cliente (el número de envío se repite entre clientes); openapi y cliente generado (web y `app-almacen`).
- Textos es/en (`pay.*`), reglas puras (`settlementRules.ts`), manual de pantallas L96 con capturas, capítulo 22 §4, FAQ y este documento.

## Cómo se probó
- vitest: reglas (6) y la pantalla con fetch simulado (10: lista, ficha agrupada por día, flujo revisar/aprobar, permisos, ajuste con motivo, cancelar, nueva liquidación con vista previa y aviso de intentos sin chofer, período al revés, exportar, intentos registrar y anular). `npm run check`.
- Playwright contra el API real, escritorio y móvil (360 px, sin scroll horizontal): chofer con tarifas de entrega (10) y de intento (5 y 3) creado por API, un intento fallido por API y el entregado a mano desde la pantalla, nueva liquidación (1 línea de $18.00), ajuste a $20, revisar, aprobar con reautenticación, exportar (se descarga) y la liquidación queda Exportada.
- Hallazgos corregidos: sin el módulo de Facturación, pedir las plantillas daba 403 y sacaba de la pantalla («Módulo apagado»); ahora solo se piden con el módulo encendido y fallan en silencio. Exportar no refrescaba el estatus (era una llamada suelta, no una mutación); ahora invalida la caché.

## Decisiones a revisar con Luis
1. **Selector de chofer con `fleet.view`:** sin ese permiso la pantalla solo puede liquidar «todos» (y registrar intentos no se puede sin elegir chofer).
2. **Plantillas con módulo Facturación:** igual que las remesas COD; separarlas del módulo de Facturación es un cambio chico si hace falta.
3. **Sin captura de «aprobar» (reautenticación) en el manual:** es el mismo diálogo de siempre.

## Cierre de P9
Lotes 93 (intentos de entrega), 94 (liquidación), 95 (exportación) y 96 (pantalla). Pendientes de P9: pagos por viaje con bono/deducción como líneas aparte, formato definitivo de nómina (con un archivo de muestra real) y el cobro por intento al cliente (hoy solo el pago al chofer).
