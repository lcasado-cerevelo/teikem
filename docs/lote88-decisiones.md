# Lote 88 — Los dos ríos del Pulso (P12, 1 de N) (2026-10-17)

Primer lote de P12 (dashboards y Pulso completos): las **dos corrientes** de la maqueta —«Paquetes en la calle» y «Dinero COD»— como paneles del Pulso.

## Plan de P12 (lo que sigue)
- **Lote 89:** Radar de envíos abiertos (búsqueda y agrupar por pueblo o cliente) y avisos de «Necesita tu atención» para órdenes sin chofer, COD sin cuadrar y SLA en riesgo (este último espera al POD).
- **Lote 90:** `KpiDailySnapshot` y el trabajo nocturno (fotos diarias) para dar histórico a los ríos y a los indicadores.
- **Lote 91:** indicadores y gráficos de COD, facturación y ventas; Actividad para Operación y Contabilidad.
- **Lote 92:** programación de informes por correo. Kemi AI sigue «no verificado»: decidir si queda en alcance.

## Qué se construyó
- **Servidor:** permisos `pulse.orders` y `pulse.cod` (categoría PULSE) y los paneles `ORDERS_RIVER` (orden 10; datos `orders.view`; módulo `LTL_GROUND`) y `COD_RIVER` (orden 11; datos `cod.view`; módulo `COD`) en el registro de paneles; `PulseRiverService` y `GET /api/v1/pulse/rivers/orders` y `/cod`.
- **Plantillas de rol y SQL:** `pulse.orders` para Despachador, Facturación y Solo lectura; `pulse.cod` para Facturación y Solo lectura (el administrador los recibe por «todos»); sección **2026-10-17 (z)** de `update.sql` para los roles ya creados.
- **Web:** `RiversPanel.tsx` (los dos ríos con el estilo de la maqueta, la tubería azul / naranja, barritas de 7 días en Entrada y Entregado y un clic por tarjeta a donde se atiende), `riverApi.ts`, `riverRules.ts`, registro en `pulsePanels.tsx`, textos es/en; COD abre en la pestaña pedida con `?tab=`.
- Manual 07 §6, manual de pantallas L88 con capturas, FAQ y este documento.

## Cómo se probó
- xunit (`PulseRiverTests`, 4): el río de órdenes (11 órdenes de ejemplo: entrada de hoy, en almacén, por despachar con y sin ruta con chofer, en ruta con sus choferes, entregadas hoy y hace 2 días por el historial, canceladas y dadas de baja fuera, barritas de 7 días), el río del COD (por cobrar con lo que falta, cobrado por método, cuadrado sin remesa con clientes, remesa lista con el neto), paneles y permisos, y otra compañía en ceros. Pines de permisos actualizados (92) y plantillas de rol. Suite completa verde.
- Recorrido por la API contra SQL Server real: los dos ríos con datos reales y el Pulso del administrador trae los dos paneles (órdenes 10, COD 11).
- vitest: reglas (4) y los dos paneles con fetch simulado (3). `npm run check` completo (3029 pruebas). Playwright contra el API real, escritorio y móvil: los dos ríos con sus tarjetas, «Entrada» con la orden recién creada y sus barritas, y los clics a COD → Reconciliar y a la Sala de despacho.

## Decisiones a revisar con Luis
1. **Fotos de ahora, no instantáneas:** cada tarjeta cuenta lo que hay en ese momento; las barritas de 7 días solo existen donde el dato tiene fecha (creación y entrega). El histórico del resto llega con `KpiDailySnapshot` (Lote 90).
2. **Qué es cada punto del flujo de órdenes** (a confirmar): Entrada = creadas hoy; En almacén = Inbound; Por despachar = en almacén o planificadas sin ruta con chofer; En ruta = en tránsito o en destino; Entregado = entregadas hoy. La maqueta decía «escaneados 0/214» en En almacén: aquí no hay ese conteo de recibo por orden, así que se muestra «recibidas en el almacén».
3. **«Por cobrar» solo con la entrega ya ocurrida** (en destino, entregada o parcial): una orden en camino todavía no se puede cobrar.
4. **El dinero supone una sola moneda** (la de la compañía), como las remesas.
5. Los **permisos de plantilla**: Despachador no ve el río del COD (no tiene `cod.view`); el administrador de la compañía puede dárselo con un rol propio.
