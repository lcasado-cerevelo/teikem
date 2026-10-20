# Lote 139 — Rotación: sugerir u obligar en las salidas (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Ajuste de compañía `Tenant.RotationEnforcement`** (`SUGGEST` por omisión | `ENFORCE`), sección nueva al final de `Diseño/logistica-db-update.sql` (idempotente, con CHECK). `PUT/GET /tenant/settings` lo llevan como último campo opcional.
- **Servidor:** con `ENFORCE`, `PickBatchService.PlanAsync` (recolección y despacho manual) compara lo pedido (posición/lote explícitos) con la asignación de la rotación sin restricciones (`PickBatchRules.SameAllocation`); si difiere, **409** con el mensaje de `RotationRules.EnforcedMismatch`. Sin posición ni lote se asigna y reparte solo (ya lo hacía). Productos por serie sin cambio. Transferencias y ajustes no se tocan (decidido con Luis).
- **`GET /inventory/exit-options`** devuelve `rotationEnforcement` (es la única fuente que usan la web y la app).
- **Web (Recolección y Despacho manual):** propuesta del servidor (rank 1, FEFO **o FIFO**; antes la pantalla solo conocía FEFO), advertencia roja si se escoge otra posición/lote en *sugerir*; en *obligar* columnas «Automática (rotación)», sin «Sugerir posiciones» y el cuerpo no manda posición ni lote. Ajustes → Operación: selector «Cómo se aplica en las salidas».
- **App de almacén (Despachar):** en *sugerir*, escanear otra posición abre «Se salta la rotación» (Cancelar/Continuar) — antes un producto por lote se rechazaba; en *obligar*, sin campo de posición: se ve el plan y un botón «Sacar así». La app guarda la última respuesta de `rotationEnforcement` para decidirlo sin señal.

## Cómo se probó
- xunit: `RotationTests` (reglas puras: parseo, `IsEnforced`, `SameAllocation`) y `RotationServiceTests` (sugerir deja escoger otro lote; obligar rechaza con 409 y el mensaje exacto, acepta el lote correcto o ninguno, reparte 7 en 5+2; `exit-options` informa el modo) + prueba de contrato de `TenantSettings*`. Pasan.
- vitest: `collectForm.test.ts` (propuesta, advertencia, cuerpo automático), `TenantSettingsPage.test.tsx` (selector). `tsc -b` y oxlint sin errores.
- jest (app-almacen): 128 suites / 750 pruebas, incluyendo despacho en *obligar* (reparte en el orden del servidor y no deja sacar sin existencia), advertencia en *sugerir* y la memoria de la configuración sin señal. `tsc --noEmit` limpio.
- No se levantó SQL Server ni la app en un teléfono en esta sesión: el árbitro es el CI.

## Decisiones a revisar
1. **El campo de la app de almacén en *sugerir* para productos por lote** pasó de «bloqueado» (decisión del 2026-10-05) a «advertencia y se deja continuar», para cumplir lo pedido (sugerir = se puede cambiar con advertencia). Si quieres que esos productos sigan bloqueados, es un `if`.
2. **Solo compañía:** no hay excepción por producto del modo (sí del método FEFO/FIFO).
3. **Productos por serie** quedan fuera de la regla (la serie manda sobre la posición).
4. **`schema.d.ts` de `app-almacen`:** solo se agregó `rotationEnforcement` a mano en `StockExitPageDto`; regenerarlo completo mete miles de líneas de lotes anteriores (el CI lo regenera).

## Qué quedó fuera
- Los demás pedidos del 2026-10-20 (lote/vencimiento en la línea del recibo, avisos de inventario, aviso de salida, costo, portal de almacén) van en lotes aparte.
