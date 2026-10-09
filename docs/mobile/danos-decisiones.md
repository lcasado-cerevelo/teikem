# Daños (2026-10-08) — decisiones

## Qué se construyó
- **Servidor:** tabla `DamageReport`, catálogos `DamageOrigin`/`DamageCause`, estatus `DamageStatus` (REPORTED → QUARANTINED → DISCARDED | RECOVERED), permiso `warehouse.damage`, `DamageService` sobre `InventoryAdjustmentService` (el ledger mueve el inventario), `DamageReportsController`, fuente de datos `DAMAGE_REPORT`.
- **Web:** Almacén → Daños (lista, reportar, dar salida, recuperar) y «Reportar daño» en la ficha del recibo.
- **App:** tile Daño (escanear posición o número de recibo → producto → cantidad → causa → cuarentena o dar salida).

## Cómo se probó
Servidor: 17 pruebas nuevas (3275 en total) más `db-init`/`db-update` en SQL Server local. Web: `npm run check` (1369 pruebas). App: `npm run check` (540 pruebas). Sin probar en el Zebra ni con CI de este envío.

## Decisiones tomadas (a revisar)
1. **Ambas salidas** (pedido del dueño): al reportar se elige cuarentena o dar salida de una vez; lo de cuarentena sale (tirado, devuelto…) o se recupera después.
2. **Sin fotos y sin reclamos** (pedido del dueño). La causa (vino así / accidente en el camino / accidente en el almacén / otra) es un catálogo informativo.
3. **El recibo no se toca:** lo dañado de un recibo se reporta aparte. Se recibe solo lo bueno; lo dañado «no entra como bueno» (cuarentena = entra directo a esa posición; desechar de una vez = no entra al inventario). Consecuencia: contra lo esperado del recibo hay una diferencia que el sistema **no netea** sola.
4. **Cuarentena automática:** sin indicar posición, la primera activa del almacén por código en una zona QUARANTINE; el API admite indicarla (`quarantineBinId`); la web y la app todavía no la preguntan.
5. **Productos con serie:** fuera por ahora (422); se ajustan con un ajuste de inventario.
6. **La app solo reporta** (necesita señal); dar salida o recuperar lo de cuarentena es de la web.
7. **Numeración `DAN-#####`** derivada del id (sin secuencia propia).
8. **Permiso único** `warehouse.damage` para reportar y resolver (Operador de almacén lo tiene).
