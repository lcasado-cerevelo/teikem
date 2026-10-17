# Lote 93 — Intentos de entrega (P9, 1 de 4) (2026-10-17)

Primer lote de P9 (Liquidación a choferes). El plan original ponía `DeliveryAttempt` en P7 (app del chofer), pero P7 se dejó para lo último y hoy **nada** marca órdenes como entregadas fuera de los cambios de estatus; por eso la bitácora de intentos se construye aquí.

## Qué se construyó
- **Tabla** `DeliveryAttempt` (`update.sql` (ad)), entidad y reglas (`DeliveryAttemptRules`): número, chofer, ruta, resultado, razón de fallo, nota, origen, anulación con motivo y `SettledRunId` (lo llenará la liquidación). Índice único del intento vigente por orden y número. EntityType `DELIVERY_ATTEMPT` para la bitácora.
- **Servicio** `DeliveryAttemptService`: anotar desde el estatus, registrar a mano, anular y listar.
- **Efecto** `DeliveryAttemptOrderEffect` (dominio OrderStatus): al pasar a Entregada o Fallida anota el intento con el chofer de la ruta vigente; ignora entregas especiales y no duplica un entregado.
- **API** `/api/v1/delivery-attempts` (listar, crear, anular) con `driverpay.view` / `driverpay.manage`.
- Manual 22 §1 (capítulo nuevo), FAQ y este documento.

## Cómo se probó
- xunit `DeliveryAttemptTests` (7): reglas, registro manual y su secuencia, validaciones con mensaje exacto, anulación (motivo, ya anulado, ya liquidado, número reutilizable), filtros de la lista y aislamiento entre compañías, y el efecto (con chofer de la ruta, sin ruta, entrega especial, sin duplicar).
- Contra SQL real (`db-init` aplicó (ad)): alta de un intento fallido y uno entregado de una orden existente (la ruta se detecta sola), listado filtrado y anulación.

## Decisiones a revisar con Luis
1. **Intentos antes de la app:** hasta que exista la app (P7) los intentos nacen del estatus de la orden o se escriben a mano. Una orden entregada antes de este lote no tiene intentos: hay que registrarlos si se quieren pagar.
2. **Sin chofer** se permite en el registro automático (ruta sin chofer) pero la liquidación no lo pagará hasta que se complete.
3. **Entrega especial = por viaje:** no genera intentos ni se registran a mano.
4. Se reutilizan `driverpay.view` / `driverpay.manage`; no hay permisos nuevos.
