# Lote 94 — Liquidación a choferes: el flujo (P9, 2 de 4) (2026-10-17)

Segundo lote de P9. Construye la corrida de liquidación con su cálculo congelado y su flujo de aprobación; la exportación con plantillas y la pantalla vienen en los lotes siguientes.

## Qué se construyó
- **Tablas** `DriverSettlementRun` (LIQ-#####) y `DriverSettlementLine` (`update.sql` (ae)); el DDL original trae `CarrierSettlement` / `SettlementLine`, otro diseño que no se toca. `DeliveryAttempt.SettledRunId` y `DriverTrip.SettlementRunId` reservan lo pagado. Dominio de estatus `DriverSettlementStatus`, EntityType `DRIVER_SETTLEMENT`, contador `DRIVERSETTLE`.
- **Servicio** `DriverSettlementService`: vista previa, generar (bloquea órdenes y viajes, recalcula con lo bloqueado, congela fórmula y desglose, reserva), revisar, aprobar (los viajes pasan a LIQUIDADO), cancelar (libera) y ajustar una línea (conserva el calculado, exige motivo).
- **API** `/api/v1/driver-settlements` (listar, ver, preview, generar, revisar, aprobar con AAL2, cancelar, ajustar línea).
- **Permisos nuevos** `driverpay.approve` y `driverpay.export` (a TenantAdmin, plantilla y clonados; `update.sql` (ae)).
- `InventoryQueries.LockDriverTripsAsync` (único archivo con SQL crudo permitido).
- Manual 22 §2, FAQ y este documento.

## Cómo se probó
- xunit `DriverSettlementTests` (11): las tres fórmulas con la corrida congelada (cambiar política y tarifas no la mueve), tarifa faltante en $0 con nota, viajes (monto congelado, sin tarifa, fuera de período, pasan a LIQUIDADO al aprobar), validaciones con mensaje exacto, no pagar dos veces y cancelar libera, entregas especiales e intentos sin chofer fuera, vista previa sin guardar y filtro por chofer, el flujo y sus mensajes, ajuste manual y la lista con aislamiento entre compañías. Suite completa verde.
- Contra SQL real (`db-init` aplicó (ae)): preview, generar de dos órdenes con intentos, revisar, ajustar (total recalculado), aprobar (403 `aal2_required`, como corresponde), cancelar y listar.

## Decisiones a revisar con Luis
1. **Tarifa de entrega = servicio de la orden + tipo de paquete de su primera línea de carga.** Una orden con varios paquetes distintos paga la tarifa de uno solo; si se quiere pagar por paquete, se cambia el cálculo.
2. **Liquidación incremental:** se pagan los intentos del período; un reintento en otro período se paga en su propia liquidación (el total coincide con el cálculo completo en las tres fórmulas).
3. **Una liquidación por chofer o de todos** (`driverPublicId` opcional); las líneas llevan al chofer.
4. **Aprobar y exportar con permisos propios** (solo el administrador por omisión), para separar quien genera de quien aprueba.
5. **Cancelar pone el total en 0** (como una corrida de facturación) y conserva las líneas para la bitácora.
6. **El ajuste solo puede subir o bajar el monto a un valor ≥ 0**; no hay deducciones ni bonos como líneas aparte todavía.
