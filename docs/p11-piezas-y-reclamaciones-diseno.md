# Piezas, escaneo por pieza y reclamaciones — diseño (2026-10-18)

Dictado por Luis (2026-10-17/18). **Todavía no hay código**: este documento junta lo decidido y lo que falta antes de construir. Reemplaza el supuesto del Lote 44 (D-FG17: «el mismo número de paquete repetido en cada pieza de la línea»).

## Decidido por Luis
1. **Cada pieza (paquete) tiene su propio número, único**, distinto al número de la orden. La **orden** tiene su propio número único en toda la base (el conduce), que es el que usan el cliente y el consignatario. El número de la pieza lo **genera Teikem** al someter la orden; hoy se usa una nomenclatura que mezcla el número del cliente, el del consignatario y fecha y hora con milisegundos (para inferir cosas al verlo); el de la orden es parecido, sustituyendo la hora por un secuencial del día por consignatario.
2. **Lo que se escanea son las piezas.** Si no se escanearon todas las piezas en un estatus, la orden está **incompleta**.
3. **Los faltantes nacen en cualquiera de los estatus que asume la orden después de Sometida** (Recogida, Sin asignar, Asignada, En tránsito): una pieza que no se escaneó en el estatus por el que pasó la orden es un faltante «Por verificar». Regla concreta (confirmada): la pieza queda faltante **cuando la orden pasa a la siguiente acción sin esa pieza, o al «Cerrar conteo»**; la orden avanza igual (con la primera pieza) y muestra «4 de 5».
4. **El número de la orden no se escanea en la estación:** dice «no lo encuentro». **El audio** de la estación: **«Sí»** si se encontró lo escaneado, **«No»** si no se encontró, **«Ok»** si está duplicado.
5. **Flujo de los faltantes y la reclamación:** Por verificar → Perdido → Reclamada → Resuelta. Una reclamación lleva: orden, línea, piezas faltantes, descripción, valor declarado y notas. Una vez en reclamación ya no pasa nada con esas piezas; se despeja dentro de la reclamación.
6. **Si el paquete aparece** (estando Por verificar o Perdido): se **enruta igual que cualquier otro**, con la diferencia de que hay que **recrear una orden** (la original ya está cerrada o en reclamación). **La orden nueva se crea cuando se cierra el conteo (cuando se termina el conteo)**: las piezas que estaban perdidas y que pertenecían a una misma orden se juntan en **una sola orden nueva**. En la **estación de escaneo de la web (staff)** el sistema muestra una pantalla «esto es lo que va a pasar» (se recrea la orden, a quién, con qué pieza) y hay que **confirmarlo**; en la **app móvil** siempre lo **anuncia pero no se detiene: lo permite** (el audio avisa y sigue). La pieza también puede ponerse en otra orden del mismo cliente o devolverse (estatus Devuelta y orden de vuelta: lotes siguientes).

## Cómo se parte
| Lote | Qué |
|---|---|
| **109 — Piezas (hecho)** | Entidad pieza (`OrderPiece`): una fila por pieza con su número único, generada al someter la orden; labels por pieza con su número; búsqueda por número de pieza (la de la línea deja de ser el código del label) |
| **110 — Escaneo por pieza** | Cada escaneo de una pieza con la acción escogida se registra; conteo «4 de 5»; incompleta; «Cerrar conteo»; el número de orden ya no se escanea; audio Sí/No/Ok |
| **111 — Reclamaciones** | Faltantes «Por verificar» → «Perdido» → reclamación (Reclamada → Resuelta), con su pantalla y permisos |
| 112+ | Devolución (Devuelta + orden de vuelta), reubicar piezas, tipo «pick up», cargo por devolución, acceso del consignatario |

## Lo que falta decidir (antes del 109)
1. ~~El formato del número de la pieza.~~ **Resuelto (Luis, 2026-10-18):** `{Code del cliente}{Code del consignatario o su ID}{aammdd}{hhmmssfff}{n}`, sin rayas nuestras, Codes de máximo 10 caracteres tal cual vengan; el configurable por cliente se queda y este es el de omisión. (Propuesta original: `{código del cliente}{código del consignatario}{aaaammddhhmmssfff}` más el número de la pieza dentro de la orden para que nunca se repita (p. ej. ACME-PR0123-20261018153042123-01). Hoy cada cliente tiene un formato configurable de número de paquete (`PackageNumberFormat`, por omisión PQT-#####); ¿el formato nuevo reemplaza el configurable o lo convierte en uno más (con esos datos como opciones)?
2. **Etiquetas ya impresas / órdenes viejas:** no hay órdenes en producción; las de desarrollo se pueden regenerar. ¿Confirmado?
3. **La orden recreada al cerrar el conteo** (punto 6): una sola orden por cada orden original, con las piezas que aparecieron. ¿Mismo cliente y mismo consignatario que la original, y la reclamación queda enlazada a la nueva orden? ¿Y la reclamación se cierra sola cuando aparecen todas las piezas, o la resuelve alguien?
4. **Una línea de 3 piezas del importador y de la entrada rápida:** crean 3 piezas con su número cada una (ya no un número por línea).

## Estado (2026-10-18)
- **109 hecho:** piezas con número único (formato del sistema con los Codes), labels por pieza, búsqueda por número de pieza. Ver `docs/lote109-decisiones.md`.
- **Pendiente decidido por Luis para el 110:** el número de la **orden** (conduce) pasa a `{Code cliente}{Code consignatario}{aammdd}{secuencial del día por consignatario}`, sin rayas (reemplaza `CND-0000001`; afecta la búsqueda del portal y la página pública), el escaneo por pieza y el audio Sí/No/Ok.
