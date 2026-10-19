# Lote 129 — Nivel de zona (Isla/Metro) con tarifas opcionales por nivel (dictado de Luis, 2026-10-19)

## Qué se construyó
- **`ZoneGroup`** (sección (bh) de `update.sql`): niveles por compañía (código, nombre, orden, chofer por omisión, baja lógica). `DispatchZone.ZoneGroupId`; `TransportOrder.DeliveryZoneId` (resuelta con `OrderZoneResolver` al crear la orden y al cambiar su consignatario, y a pedido con `POST /dispatch-zones/assign-orders`); `DriverDeliveryRate.ZoneGroupId` y `RateComponent.ZoneGroupId` (opcionales) con los índices únicos de «una fila abierta» rehechos incluyendo el nivel (`UQ_DriverDeliveryRate_Open2`, `UQ_RateComponent_Open2`).
- **Reglas:** la tarifa con nivel aplica solo a ese nivel y gana sobre la general (`ContractRateResolver`: +4 de especificidad; `DriverPayoutRules.ForZoneGroup`); sin tarifa del nivel, la general. La cotización y la facturación del flete pasan el nivel de la orden (`RateQuoteRequest.ZoneGroupId`); la liquidación a choferes lo toma por orden. Al crear una ruta de una zona sin chofer propio, el chofer por omisión es el del nivel (`TripService.DefaultDriverAsync`).
- **API:** `/zone-groups` (CRUD y baja), `dispatch-zones` con nivel y `assign-orders`, `orders?zoneIds&zoneGroupIds&noZone`, `zoneGroup` en las tarifas; la fila y la ficha de la orden traen zona y nivel; la fuente de órdenes gana «Zona de entrega» y «Nivel de zona».
- **Web:** panel «Niveles de zona» y campo «Nivel» en Zonas de despacho; «Nivel de zona» en tarifas del chofer y del contrato (formulario y columna); filtros Zona/Nivel y columna Zona en Órdenes; «Zona de entrega» en la ficha.

## Cómo se probó
- xunit: `ZoneGroupTests` (5: catálogo con chofer por omisión y nivel en la zona, resolución de zona de la orden por ZIP/pueblo con ambigüedad, tarifa del contrato por nivel, tarifa del chofer por nivel y filtros/columnas). Ajustes en las pruebas de forma de los DTO y del índice único. Suite completa verde.
- vitest: `npm run check` verde (ajustes en pruebas de zonas, tarifas y columnas).
- Playwright `lote129-nivel-de-zona.spec.ts` (escritorio y móvil) contra el API y SQL reales: niveles, zonas con nivel, orden con zona y nivel, filtros, tarifas con nivel (409 duplicada, 400 nivel inexistente), cotización del nivel mayor que la general, pantallas.

## Decisiones a revisar
1. **El nivel no es obligatorio en nada:** zonas sin nivel, tarifas sin nivel y compañías sin niveles siguen igual.
2. **Chofer por omisión del nivel** solo cuando la zona no tiene chofer propio (la zona manda).
3. **La zona de la orden se guarda al crear/editar** (no se recalcula sola al cambiar miembros de zona): para eso está «Asignar zona a las órdenes» (`all=true` recalcula todas).
4. **Tarifa por zona (no solo por nivel):** no se hizo; se agrega `DispatchZoneId` a la tarifa con la misma regla de preferencia si alguna compañía lo pide.
5. **Migración de Colavora (para esa sesión):** `Zip.Zone` (METRO/ISLA) → crear los dos niveles y poner a cada zona de ruta el nivel mayoritario de sus ZIP; los servicios ISLAND se migran tal cual (los contratos históricos quedan fieles); después Advance puede pasar a «un servicio + tarifa por nivel» desde la pantalla. El chofer por omisión de ISLA lo elige Luis.
