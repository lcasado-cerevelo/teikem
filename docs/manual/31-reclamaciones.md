# 31 — Piezas faltantes y reclamaciones

Lote 111 (2026-10-18, dictado por Luis). Qué pasa con una pieza que no llegó: sigue un camino de cuatro pasos y, cuando se pierde de verdad, se reclama en un documento propio, la **reclamación `REC-#####`**.

**Quién puede:** ver, `claims.view`; marcar perdidas, crear y resolver reclamaciones, `claims.manage` (los administradores de la compañía ya los traen). Módulo **LTL_GROUND**. **Dónde:** menú Operación → **Reclamaciones** (`/ops/claims`).

## 1. El camino de una pieza faltante
| Paso | Cómo llega | Qué significa |
|---|---|---|
| **Por verificar** | Lo marca la estación de escaneo (la orden pasó a la acción siguiente sin la pieza, o se pulsó «Cerrar conteo») — ver capítulo 05 | Falta, pero puede que aparezca |
| **Perdido** | Alguien con `claims.manage` la marca perdida | Se da por perdida; todavía no hay reclamación |
| **Reclamada** | Se crea la reclamación con ella | Está dentro de `REC-#####`; ya no pasa nada con la pieza fuera de la reclamación |
| **Resuelta** | Se resuelve la reclamación | Cerrada: una pieza que se escanee responde «No» |

## 2. Pestaña «Por verificar»
- Lista las **órdenes con piezas faltantes** (Por verificar o Perdido, sin reclamación): orden, conduce, cliente, estatus de la orden y «N de M» (faltantes de total). Buscador libre por **número de orden, conduce o número de pieza**; ordenar columnas y exportar todo el filtro.
- Al abrir una orden: la tabla de sus piezas faltantes y, con `claims.manage`:
  - **Marcar N como perdidas:** pasa **todas** las «Por verificar» de la orden a «Perdido» (por el API se pueden indicar piezas sueltas).
  - **Nueva reclamación:** *valor declarado* (opcional, no negativo) y *notas* (hasta 1000 caracteres); **Crear reclamación (N)** junta las piezas «Perdido» de esa orden en una reclamación `REC-#####` y la abre.
- La reclamación lleva: orden, línea(s), piezas, descripción (por ejemplo «2 × Cajas; 1 × Sobre»), valor declarado y notas.

## 3. Pestaña «Reclamaciones»
- Lista con número, orden, cliente, descripción, piezas, valor declarado, estatus (**Reclamada** / **Resuelta**) y fecha; buscador por reclamación, orden, conduce o pieza; filtro por estatus.
- **Resolver** (solo `claims.manage`, solo si está Reclamada): *Pagada* (pide el **monto pagado**), *Rechazada* o *Cerrada*; notas opcionales. La reclamación queda **Resuelta** y sus piezas «Resuelta». El monto solo se guarda si fue pagada.

## 4. Cuando una pieza aparece (estación de escaneo)
Si se escanea una pieza que estaba **Por verificar, Perdido o Reclamada**:
- **Reclamación sin cerrar:** la pieza se **quita de la reclamación**; si la reclamación se queda sin piezas, **se elimina**. La pieza deja de ser faltante, se escanea como cualquier otra (resultado «apareció», audio «Sí») y queda anotada para que, al cerrar el conteo, se cree su orden nueva (ver §4.1).
- **Reclamación cerrada (Resuelta):** el escaneo responde **«No»**: *La pieza pertenece a una reclamación cerrada.*
- **4.1 La orden nueva (Lote 111b).** Al pulsar **Cerrar conteo** en la estación de escaneo de la orden original, si tiene piezas que aparecieron, la web muestra **«Esto es lo que va a pasar»**: se crea **una orden de entrega nueva** con esas piezas (las que aparecieron de esa orden original), con el **mismo cliente y el mismo consignatario** (las mismas paradas de recogida y entrega, tipo de servicio y prioridad), un **conduce nuevo** y las **mismas piezas con sus mismos números** (los labels ya impresos siguen valiendo). Se confirma con **«Confirmar y cerrar el conteo»**; con «Cancelar» no pasa nada. La orden nueva entra al estatus de la acción con que se escanearon las piezas (si fue «Enrutar» queda en «Sin asignar», porque todavía no hay ruta). **No se cotiza ni se cobra de nuevo** (es el mismo envío ya cotizado en la original). La orden original conserva sus demás piezas. La app móvil manda la confirmación sola: avisa y no se detiene.
- Aunque la orden ya esté en un estatus final (entregada incompleta), la pieza faltante que aparece se acepta; no se rechaza como «final».

## 5. Mensajes de error
| Mensaje | HTTP | Cuándo |
|---|---|---|
| Indique la orden. | 400 | Falta la orden al marcar perdidas o reclamar. |
| Indique al menos una pieza. | 400 / 422 | 400: se mandó una lista de piezas vacía; 422: la orden no tiene piezas «Por verificar» que marcar. |
| La pieza {n} no es de esa orden. | 400 | Un número de pieza ajeno a la orden. |
| La pieza {n} no está «Por verificar». | 422 | Marcar perdida una pieza que no está en ese paso. |
| La pieza {n} no está «Perdido»: márquela como perdida antes de reclamarla. | 422 | Reclamar una pieza que no está perdida. |
| La orden no tiene piezas perdidas por reclamar. | 422 | No hay piezas «Perdido» sin reclamación en esa orden. |
| El valor declarado no puede ser negativo. | 400 | Valor declarado (o monto pagado) negativo. |
| Las notas no pueden pasar de 1000 caracteres. | 400 | Notas muy largas. |
| Indique la resolución: pagada, rechazada o cerrada. | 400 | Resolver sin elegir resolución válida. |
| Indique el monto pagado. | 400 | Resolución «Pagada» sin monto. |
| La reclamación ya está resuelta. | 409 | Resolver dos veces. |
| La pieza pertenece a una reclamación cerrada. | 200 (resultado «No») | Escanear la pieza de una reclamación Resuelta. |
| Reclamación no encontrada. / Orden no encontrada. | 404 | Identificador inexistente (o de otra compañía). |
| Al cerrar el conteo se creará una orden nueva con {n} pieza(s)… (`needsConfirmation`) | 200 | Hay piezas que aparecieron: no se hizo nada todavía; reenviar `POST /api/v1/scan/close-count` con `confirm: true`. |

## 6. Endpoints
`GET /api/v1/claims/to-verify` (`search`, `skip`, `take`), `POST /api/v1/claims/lost`, `POST /api/v1/claims`, `GET /api/v1/claims`, `GET /api/v1/claims/{publicId}`, `POST /api/v1/claims/{publicId}/resolve`. La compañía sale siempre de la sesión.

## 7. Casos frecuentes
- *Marqué perdida una pieza por error:* no hay paso atrás manual; si aparece y se escanea, deja de ser faltante (y se quita de su reclamación si la tenía y no está cerrada).
- *La reclamación desapareció de la lista:* todas sus piezas aparecieron; se eliminó (queda en el historial de auditoría).
- *Quiero reclamar solo algunas piezas:* por el API se mandan los números en `pieceNumbers`; en pantalla se reclaman todas las «Perdido» de la orden.
