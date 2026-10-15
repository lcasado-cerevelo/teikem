# Lote 59 — Órdenes de venta: backorders (P4, S1) (2026-10-15)

Sigue al Lote 58 ([lote58-decisiones.md](lote58-decisiones.md)). Diseño: maestro 13C («Backorder»).

## Qué se construyó
- **Vista de backorders** (`GET /sales-orders/backorders`): líneas con pendiente de órdenes solicitadas/parciales, **más antigua primero**, con pedido / reservado / pendiente, disponible hoy, lo surtible ahora y días de espera; buscador.
- **Surtir una línea** y **Surtir todo** (de la más antigua a la más reciente, cada línea en su transacción con el candado del ledger): reserva lo que haya, nunca más; guarda qué saldo reservó (`SalesOrderReservation`).
- **Cerrar con faltante**: borra el backorder y cierra la línea; lo reservado sigue y la cantidad pedida se conserva.
- Manual: capítulo 18 (sección 9) y FAQ «Lote 59».

## Pruebas
- xunit (InMemory): la vista (orden por antigüedad, disponible y surtible, buscador), surtir parte / todo / nada y después de completo, **surtir todo en orden de antigüedad** (la primera toma lo que necesita y la segunda recibe solo el resto, sin pasarse de lo que hay en mano), cerrar con faltante (conserva reserva y cantidad pedida; no se surte ni modifica), cancelar una orden con backorder libera para la siguiente, reglas (orden solicitada, línea con backorder, inexistentes) y aislamiento entre compañías.

## Decisiones por omisión (a revisar con Luis)
1. **Surtir una línea toma lo que haya aunque sea parcial** (no exige poder completar todo el pendiente).
2. **Surtir todo** respeta la antigüedad de la **orden** (momento en que se solicitó), no la de la línea.
3. **Cerrar con faltante no se deshace** (para lo que falta se crea otra orden); no cambia la cantidad pedida, para poder medir el cumplimiento (fill rate).
4. **El aviso automático «ya hay para este backorder»** (`SalesBackorderAvailable`) y el modo `AUTO` (reservar solo al entrar inventario) quedan para el lote de avisos de ventas (S3); la vista ya muestra lo surtible.
5. Una orden con **todas sus líneas cerradas y nada reservado ni despachado** no cambia sola de estatus aquí; el despacho (S2) decide si pasa a despachada o cancelada.
