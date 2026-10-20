# Lote 149 — El recibo exige documento (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Ajuste de la compañía «El recibo exige documento»** (`Tenant.ReceiptRequirement`, SQL idempotente con `CHECK`): `FREE` (por omisión, como siempre), `ANY_DOCUMENT` (aviso de llegada u orden de compra), `ASN` (solo aviso de llegada de un cliente), `PURCHASE_ORDER` (solo orden de compra, directa o por el aviso nacido de ella). Es la contraparte de las salidas: en Ajustes → Operación quedó una sección **Entradas** (antes) y otra **Salidas** (rotación, cómo se aplica y «el despacho exige documento»), y luego **Valuación**.
- **Servidor (`ReceiptService.CreateCoreAsync`):** con cualquier valor distinto de `FREE`, un recibo **a ciegas o una devolución manual** (sin documento) responde **409** con el mensaje del valor; un recibo contra un documento de otro tipo del exigido también (409). Los recibos de una **devolución de venta** (`SalesReturnService`) no pasan por aquí y quedan fuera.
- `openapi.json`/`schema.d.ts` con el campo nuevo al final de los ajustes.

## Cómo se probó
- xunit `ReceiptRequirementTests` (reglas puras y servicio: ciego bloqueado/libre, solo aviso de cliente, solo orden de compra) y contrato posicional de los ajustes; suite completa verde.
- vitest: el selector guarda `receiptRequirement` y «Entradas» va antes que «Salidas».

## Decisiones a revisar
1. **Cubre el alta del recibo**, no cambia recibos ya creados. La pantalla web y el aparato muestran el mensaje del servidor al intentar un recibo a ciegas; todavía no ocultan esas opciones cuando la compañía exige documento (se puede pulir).
2. Un recibo contra aviso **de una orden de compra** cuenta como «orden de compra»; contra el aviso de un cliente, como «aviso de llegada».
