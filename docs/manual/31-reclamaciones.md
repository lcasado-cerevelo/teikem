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
- **4.1 Cerrar el conteo y la orden nueva (Lote 111b).** Al pulsar **Cerrar conteo** la estación muestra **«Cerrar el conteo de {orden}»** con (a) la **lista de las piezas que no se escanearon** (quedarán «Por verificar») y (b) si aparecieron piezas perdidas o reclamadas, la **orden nueva** que se creará con ellas. No pasa nada hasta que se pulsa **«Confirmar y cerrar el conteo»**; «Cancelar» lo deja todo como estaba. (Si no falta ni apareció nada, se cierra sin preguntar.) La app del almacén hará exactamente lo mismo cuando se construya.
  - **La orden nueva** es una orden de entrega con el **mismo cliente y consignatario** (mismas paradas de recogida y entrega, tipo de servicio y prioridad), **conduce nuevo** y las **mismas piezas con sus mismos números** (los labels siguen valiendo). Se crea una por cada orden original.
  - **Sus estatus:** primero repite lo que tenía la original —**Sometida** y, si la original llegó a **Recogida**, **Recogida**— con **las mismas fechas**; después se le aplica el estatus de la acción con que se escanearon las piezas (con la fecha de ese escaneo), igual que a cualquier otra orden. **Enrutar** también la asigna a una ruta abierta de su zona (si no hay quién la reciba, el cierre se deshace con el mensaje de la ruta).
  - **Cobro:** si el contrato **cobra por recogido**, la original ya se cobra completa y la orden nueva **no se cobra de nuevo**. Si **cobra por entrega** (o mixto), la orden nueva se **cotiza con sus piezas y se cobra cuando se entregue**, y a la original se le **quitan esas piezas** (y se vuelve a cotizar) para no cobrarlas dos veces; si la original ya se facturó completa, la nueva no se cobra.
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
| Va a cerrar el conteo de la orden {n}… (`needsConfirmation`) | 200 | Hay piezas sin escanear o que aparecieron: no se hizo nada todavía; reenviar `POST /api/v1/scan/close-count` con `confirm: true` (y `planDate` si la orden nueva se enruta). |

## 6. Endpoints
`GET /api/v1/claims/to-verify` (`search`, `skip`, `take`), `POST /api/v1/claims/lost`, `POST /api/v1/claims`, `GET /api/v1/claims`, `GET /api/v1/claims/{publicId}`, `POST /api/v1/claims/{publicId}/resolve`. La compañía sale siempre de la sesión.

## 7. Casos frecuentes
- *Marqué perdida una pieza por error:* no hay paso atrás manual; si aparece y se escanea, deja de ser faltante (y se quita de su reclamación si la tenía y no está cerrada).
- *La reclamación desapareció de la lista:* todas sus piezas aparecieron; se eliminó (queda en el historial de auditoría).
- *Quiero reclamar solo algunas piezas:* por el API se mandan los números en `pieceNumbers`; en pantalla se reclaman todas las «Perdido» de la orden.
