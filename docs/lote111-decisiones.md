# Lote 111 — Reclamaciones (111a) (2026-10-18)

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
4. **Falta (111b)**: la **orden nueva** al cerrar el conteo con las piezas que aparecieron (mismo cliente y consignatario, una por orden original), la pantalla de confirmación «esto es lo que va a pasar» en la web de staff y el anuncio sin detenerse en la app móvil. Hoy la pieza queda marcada como «apareció» (`ReappearedAtUtc`) lista para eso.
5. Sin capturas Playwright nuevas en este lote (la pantalla se cubrió con vitest y contra el API real).
