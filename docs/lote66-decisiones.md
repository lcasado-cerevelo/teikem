# Lote 66 — Avisos de ventas (P4, S3) (2026-10-16)

## Qué se construyó
- **Cinco eventos** sobre el motor de avisos del Lote 51 (`SalesEventProvider`, módulo Órdenes de venta): solicitada, despachada, cancelada, crédito por autorizar y backorder que ya se puede surtir. Con variables, textos por omisión en español/inglés y enlace a la pantalla.
- **Publicación en la misma transacción** de la operación (`SalesOrderService.Notifications.cs`): al solicitar, al cancelar (a mano o sola por faltante) y al quedar despachada por completo. El de crédito se guarda aparte antes de rechazar la solicitud (422), con clave de no repetición por situación.
- **Trabajo `SALES_BACKORDER_SCAN`** (cada 10 min, encendido): detecta backorder surtible con la vista de backorders y publica un aviso por orden; la clave cambia solo si cambia lo surtible. Cubre todas las vías por las que entra inventario sin tocar esos módulos.
- Manual (14 §3.1, 13, 18 §15) y FAQ.

## Pruebas
- xunit (10): variables y textos de cada evento, solicitada con y sin backorder, solicitud fallida sin aviso, cancelar con motivo, despachada solo al completar, cancelada sola, crédito (una vez por situación, nueva situación avisa, la solicitud sigue rechazada), escaneo de backorder (sin inventario nada, con inventario avisa, no repite, cambia y avisa), sin publicador no pasa nada, catálogo consistente (códigos ≤ 60, variables conocidas) y registro del trabajo. Suite completa verde; los de avisos y trabajos contra SQL Server real también.

## Decisiones por omisión (a revisar con Luis)
1. **No se siembra ninguna regla**: el administrador decide a quién avisar (los avisos nacen apagados por diseño del motor).
2. **Backorder surtible por trabajo cada 10 minutos**, no con ganchos en recibo/ajuste/transferencia/cuarentena (evita tocar esos módulos y cubre todo); el costo es hasta 10 min de demora.
3. **El modo automático de backorder (surtir solo) sigue pendiente**; el aviso solo informa.
4. **El aviso de crédito sale al fallar la solicitud**, no al autorizarla; el de «solicitada» sale también con autorización de crédito.
5. **«Despachada» avisa solo al completarse**, no en cada despacho parcial.

## Fuera de este lote
- Pantalla/contador de avisos en la app del almacén (etapa (b) de P2.1) y SMS/push.
