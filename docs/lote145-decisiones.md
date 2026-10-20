# Lote 145 — Despachar contra documento y «el despacho exige documento» (4c) (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Ajuste de la compañía «El despacho exige documento»** (`Tenant.DispatchRequirement`, sección nueva y idempotente al final de `Diseño/logistica-db-update.sql`, `CHECK`): `FREE` (por omisión, como siempre), `ANY_DOCUMENT`, `EXIT_NOTICE`, `SALES_ORDER`. Está en Ajustes de la compañía → Operación (`dispatchRequirement` en `TenantSettingsDto`/`UpdateRequest`, al final y opcional).
- **Servidor:** con cualquier valor distinto de `FREE`, el **despacho manual** sin documento responde **409** con el mensaje de cada valor (`DispatchRequirementRules.ManualBlocked`). **Empacar y la recolección que se empaca quedan fuera** (ya van contra una orden).
- **Web:** en Recolección, el modo «Despacho manual» muestra el aviso de que la compañía exige documento (con enlace a Avisos de salida) y bloquea el botón. La ficha de un aviso de salida tiene **Despachar** (permiso `warehouse.pick`): una ventana con la cantidad por línea (por omisión todo lo reservado; 0 = esa línea no sale; no más de lo reservado) y una nota; llama a `POST /exit-notices/{id}/dispatch`.
- `openapi.json`/`schema.d.ts` de la web con el campo nuevo.

## Cómo se probó
- xunit: la exigencia solo se activa con un valor válido distinto de FREE; el despacho manual se bloquea con 409 y sin movimientos; contrato posicional de los ajustes actualizado. Suite .NET completa verde salvo lo ya existente sin SQL Server (omitidas).
- vitest: reglas del despacho, ventana Despachar (permiso, cantidad por defecto, error si excede, cuerpo enviado), selector de ajustes (guarda `dispatchRequirement`). `npm run check` verde.

## Decisiones a revisar
1. **«Contra» como acción de la ficha**, no como selector dentro del formulario de Recolección: el despacho contra documento ya existe (aviso, venta) con su reserva; el formulario libre es el que se bloquea. Si prefiere un selector «Contra» dentro del formulario de despacho manual, es otro lote.
2. **`SALES_ORDER` / `ANY_DOCUMENT`** bloquean el despacho manual igual; el despacho de ventas ya existía y no cambió.
3. La **recolección sin empacar** (`/pick-batches` sin `pack`) no se bloquea (es el paso previo a empacar). La app de almacén (lote 146) recibirá el ajuste para ocultar el despacho libre.
4. No se agregó prueba de pantalla del bloqueo en Recolección (el servidor lo cubre con 409).
