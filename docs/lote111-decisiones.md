# Lote 111 — Reclamaciones (111a) y orden nueva de las piezas que aparecen (111b) (2026-10-18)

Luis dictó: la pieza faltante sigue **Por verificar → Perdido → Reclamada → Resuelta**; la reclamación `REC-#####` lleva orden, línea, piezas faltantes, descripción, valor declarado y notas; una vez en reclamación ya no pasa nada con esas piezas, se despeja dentro de la reclamación; si una pieza aparece y la reclamación NO está cerrada, se quita de ella (y si no quedan piezas, la reclamación se elimina); si está cerrada, el escaneo dice «No».

## Qué se construyó
- **SQL (update.sql, sección (as))**: tabla `Claim`; `OrderPiece.ClaimId/ReappearedAtUtc/ReappearedAction`; dominio de estatus `ClaimStatus` (CLAIMED inicial → RESOLVED terminal) y EntityType `CLAIM`; permisos `claims.view` y `claims.manage` propagados a los `TenantAdmin` ya clonados.
- **API** `api/v1/claims` (`ClaimsController`, módulo LTL_GROUND): `to-verify`, `lost`, crear, lista, ficha y `resolve`. `ClaimService`: marcar perdidas (todas las «Por verificar» de la orden o las indicadas), crear (junta las «Perdido»; número `REC-` + id con ceros; descripción «2 × Cajas; 1 × Sobre»; estatus por `StatusService`), resolver (PAID con monto / REJECTED / CLOSED; las piezas pasan a «Resuelta»; 409 si ya estaba resuelta).
- **Escaneo**: una pieza Por verificar / Perdido / Reclamada que se escanea responde `REAPPEARED` (audio «Sí»): se quita de su reclamación (la reclamación sin piezas queda inactiva), se marca `ReappearedAtUtc/ReappearedAction` y se registra el escaneo; la de una reclamación Resuelta responde «No» («La pieza pertenece a una reclamación cerrada.»). Aplica también a órdenes ya en estatus final.
- **Web** (Operación → Reclamaciones, `/ops/claims`, `claims.view`): pestaña «Por verificar» (cola con buscador y ficha: marcar perdidas, crear reclamación con valor y notas) y pestaña «Reclamaciones» (lista, filtro por estatus, ficha y resolver); es/en, claro/oscuro, ordenar y exportar el filtro completo.

## Cómo se probó
- xunit: `ClaimServiceTests` (12: cola, perdidas, crear, validaciones, resolver, reaparición, reclamación sin piezas, reclamación cerrada, orden final), `ClaimsControllerSecurityTests`; pines de permisos 97 → 99. vitest: `ClaimsScreen.test.tsx` (5) y `claimsRules.test.ts` (4); el menú pasó a 49 ítems.
- Contra SQL real (`db-update` dos veces sobre Teikem_fg2, la segunda sin cambios): orden de 4 piezas, 2 llegan, «Cerrar conteo» deja 2 Por verificar, se marcan perdidas, se crea `REC-00001` con 2 piezas; al aparecer ambas la reclamación se elimina (404 en la ficha, 0 en la lista).

## Decisiones a revisar con Luis
1. **Reclamar** junta **todas** las piezas «Perdido» de la orden en la pantalla (el API acepta piezas sueltas). Una orden puede tener varias reclamaciones si se pierden piezas en momentos distintos.
2. **Marcar perdida no tiene vuelta atrás manual**: lo único que la deshace es que la pieza aparezca.
3. **Resolución**: Pagada (con monto), Rechazada o Cerrada; el monto solo se guarda si es Pagada. Si quieres otras (por ejemplo «Reemplazada»), se agregan.
5. Sin capturas Playwright nuevas en este lote (la pantalla se cubrió con vitest y contra el API real).

## 111b — La orden nueva de las piezas que aparecen
Luis confirmó que «la orden» es la **orden de entrega** (con recogida y entrega), no la de compra ni la de venta.
- **`ReappearedOrderService`**: al cerrar el conteo de una orden con piezas que aparecieron, crea UNA orden nueva: mismo cliente, contrato, tipo de servicio, prioridad y las mismas paradas (foto de recogida y entrega); números de orden/factura/empaque del cliente y conduce nuevo del día del consignatario; las piezas **se mudan** (mismos números, `Sequence` 1..n, sin faltante ni reclamación ni marca de «apareció») a líneas nuevas con su parte del peso y volumen; la orden entra al estatus de la acción con que se escanearon (se registra su historial).
- **Confirmación**: `POST /api/v1/scan/close-count` acepta `confirm`. Sin él y con piezas que aparecieron responde `needsConfirmation` + `preview` y **no hace nada**; la web muestra «Esto es lo que va a pasar». Con `confirm: true` (la app móvil lo manda siempre: avisa y no se detiene) lo hace y devuelve `newOrderPublicId/newOrderNumber/newWaybillNumber`. Antes de marcar faltantes se mudan las piezas que aparecieron, para que no cuenten como «Por verificar» de la original.
- Pruebas: xunit +3 (pide confirmación sin cambiar nada; crea la orden con paradas, piezas y estatus; sin piezas que aparecieron no pide nada), vitest +1 (diálogo y confirmación); contra SQL real: la 3.ª pieza aparece, cerrar sin confirmar pide confirmar, confirmar crea la orden nueva `UNASSIGNED` con la pieza, cerrar otra vez no crea otra.

### Decisiones a revisar con Luis (111b)
1. **No se cotiza ni se cobra** la orden nueva (es el mismo envío ya cotizado); si prefieres cotizarla, es un cambio chico.
2. **Enrutar** deja la orden nueva en «Sin asignar» (no hay una ruta que la reciba); las demás acciones la dejan en el estatus de la acción.
3. **El anuncio por audio en la app móvil** no está construido: la app de almacén todavía no tiene «Cerrar conteo»; cuando lo tenga manda `confirm: true` y lee el mensaje.
