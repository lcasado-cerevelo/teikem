# Libreto de la demostración a Dani (2026-10-07)

Datos de la base local `Teikem`: **Depot** (compañía 2) y **Solutions** (compañía 3). Los mensajes son los textos reales de la app. Se armó leyendo el código y la
base; **no se probó en un aparato**: ensáyelo una vez antes.

## Antes de empezar

| Qué | Estado hoy | Qué hacer |
|---|---|---|
| Aparato de Solutions | Registrado («Emulador», almacén ALM-SOL) | Nada |
| Aparato de Depot | No hay; el admin de Depot no tiene PIN | Web (Depot): Sistema → Aparatos → crear código; Roles y usuarios → PIN. En la app: registrar el aparato con ese código |
| Orden de compra de Depot | No hay órdenes ni recibos | Crearla en la web (escenario A) |
| Posición `A-01` de Solutions | **Ya creada** con `scripts/demo/solutions-nueva-posicion.sql` | Nada |
| Contador en Solutions (escenarios C y D) | No existe: el admin y hasta el rol WarehouseOperator tienen el permiso «Contar» | Web (Solutions) → Roles y usuarios: rol «Contador» con solo *Capturar conteo* (`warehouse.count.capture`) e *inventario: ver* (`inventory.view`), sin «Contar»; un usuario con ese rol, con PIN y «Ve lo esperado al contar: Sí» |
| Ajustes de conteo | Las dos compañías: «Solo los marcados», margen de reconteo 0 %, muestra el número | Nada |

En Solutions ningún producto trae código de barras: se escribe el SKU.

---

## A — Depot: recibo con acomodo y reparto por posición

Se recibe una orden de compra con acomodo (la mercancía entra a `R1`) y luego, en **Acomodar**, se escribe cuánto va en cada posición y se escanean las posiciones:
cada escaneo suma una posición con esa cantidad hasta repartir todo.

| Dato | Valor |
|---|---|
| Proveedor / almacén | `171-RC Imports` / `ALM-DEPOT` (modo «Con acomodo», recepción `R1`) |
| Producto | `171-AC-100-A` TOURNIQUET — código `3726918104001` (hoy 60 en `09-C-15` y 60 en `10-C-15`) |
| Orden de compra | 100 unidades, estatus Enviada |
| Cantidad por posición | 20 |
| Posiciones a escanear | `09-A-01` (cupo 50), `09-A-03` (70), `09-A-07` (70), `09-A-09` (40), `09-A-15` (70) — todas vacías |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Web → Compras: orden nueva y **Enviar** | — |
| 2 | App → Recibir: escanear la orden y el producto, cantidad 100, modo «Con acomodo», confirmar | Recibo hecho; aparece la tarea de acomodo |
| 3 | App → Acomodar: abrir la tarea; «Cantidad por posición» = 20 | «Pendiente de acomodar: 100» |
| 4 | Escanear las 5 posiciones | Cada una suma `<pos> · 20`; al final «Repartido: 100 · quedan 0 sin acomodar» |
| 5 | Un sexto escaneo | «Ya no hay unidades por acomodar: los 100 están repartidos. Confirma el reparto.» |
| 6 | Confirmar reparto | «Listo: 171-AC-100-A, 100 en 5 posición(es); quedan 0 pendientes» |

| Variante | Qué haces | Resultado |
|---|---|---|
| El resto | Cantidad por posición 40 | 40 + 40 + 20; la tercera avisa «`<pos>` recibe solo 20 (lo que quedaba), no 40» |
| El cupo | Con 20 por posición, escanear `09-A-08` (cupo 10, vacía) | «Cupo para 10: recibirá 20. Se puede confirmar igual.» (en Acomodar el cupo **avisa, no bloquea**) |
| Repetida | Escanear la misma posición dos veces | «Esa posición ya está en el reparto.» |

## B — Depot: despacho que sale de varias posiciones

Se escanea un producto que está en dos posiciones y se pide más de lo que hay en una sola: la app muestra las posiciones con existencia y cada una toma, como máximo,
lo que tiene; la segunda completa lo que falta.

| Dato | Valor |
|---|---|
| Producto | `171-DU-1724` UNDERPAD 17X24 3PK/100EA — código `1201804326993` |
| Posiciones | `13-C-20` con 60 · `14-C-20` con 60 (120 en total, nada reservado) |
| Cantidad a despachar | 100 |
| Producto con más posiciones | `56-CM-100F-42` COMFORD ZONE FOAM MATRESS 6X42 (escribir el SKU): 6 posiciones con 8 (`07-B-14`, `07-C-14`, `07-D-14`, `08-B-14`, `08-C-14`, `08-D-14`); pedir 30 → 8 + 8 + 8 + 6 |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | App → Despacho: escanear `1201804326993` | «Posiciones con existencia»: las dos con «disponible 60» |
| 2 | Cantidad 100; tocar «Marcar las sugeridas» | `13-C-20` toma **60**, `14-C-20` toma **40** → «Tomado 100 de 100» |
| 3 | (Alternativa) marcar solo `13-C-20` | «No alcanza sola: faltan 40. Marca más posiciones abajo.» |
| 4 | «Usar estas posiciones» | «Agregado: 1201804326993 desde 2 posiciones» |
| 5 | «Completar despacho» (sin empacar; Depot tiene 1 cliente si quiere empacar) | Se manda cuando hay señal |
| 6 | Pedir 130 (hay 120) | «No alcanza: faltan 10. Baja la cantidad.» |

## C — Solutions: contar mal, segunda oportunidad y coincide *(entrar como el Contador)*

El contador cuenta **Por posición**; si se aparta del esperado la app le pide contar otra vez sin decirle cuánto era, y al contarlo bien responde con el número del
inventario.

| Dato | Valor |
|---|---|
| Producto | `53350` PRODIGY CONTROL SOLUTION HIGH 4ML |
| Posición | `GENERAL` — existencia 20 |
| Cantidad contada | primero 15, luego 20 |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Conteo → «Por posición» → `GENERAL` → `53350` | — |
| 2 | Cantidad 15 → Aceptar | «No coincide con lo esperado. Vuelve a contar y acepta de nuevo.» |
| 3 | Cantidad 20 → Aceptar | «Coincide: contaste 20 y se esperaba 20.» (la línea se cierra) |
| 4 | Terminar conteo | — |

## D — Solutions: dos intentos mal y se acabó *(entrar como el Contador)*

Mismo camino, pero la segunda cifra también está mal: no hay tercer intento. La línea se cierra y queda para revisión del supervisor.

| Dato | Valor |
|---|---|
| Producto | `53310` PRODIGY CONTROL SOLUTION LOW 4ML |
| Posición | `GENERAL` — existencia 19 |
| Cantidad contada | primero 10, luego 12 |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Conteo → «Por posición» → `GENERAL` → `53310`; cantidad 10 | «No coincide con lo esperado. Vuelve a contar y acepta de nuevo.» |
| 2 | Cantidad 12 | «Contaste 12 y se esperaba 19. Queda para revisión.» |
| 3 | Intentar cambiarla otra vez | No se puede (el servidor responde 409: «La línea ya se verificó; no se puede cambiar su cantidad.») |

## E — Solutions: conteo por producto con el producto en varias posiciones

Se cuenta **Por producto**; al escanear el producto, que está en dos posiciones, la app pide elegir en cuál lo contó y se agrega una línea por posición.
Este camino no verifica contra lo esperado (la diferencia se ve al reconciliar en la web).

| Dato | Valor |
|---|---|
| Producto | `00050-7` GLOBAL COTTON SWABS 300CT. |
| Posiciones | `GENERAL` y `A-01` (ya creada) |
| Existencia hoy | 50 en `GENERAL` → **preparar:** web → Transferencias y ajustes → Transferencias: pasar 20 a `A-01` → `GENERAL` 30 · `A-01` 20 |
| Cantidad contada | 30 en `GENERAL`, 20 en `A-01` |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Conteo → «Por producto» → escribir `00050-7` | «El producto está en varias posiciones: elige en cuál lo contaste.» |
| 2 | Elegir `GENERAL`, cantidad 30 | «00050-7 agregado en GENERAL.» |
| 3 | Escanear otra vez, elegir `A-01`, cantidad 20 | «00050-7 agregado en A-01.» |
| 4 | Escanear otra vez y elegir una ya contada | «00050-7 ya está contado en GENERAL: toca su línea de la lista para corregir la cantidad.» |
| 5 | Terminar conteo | — |

Para mostrar el reconteo con este mismo producto: contar `GENERAL` y luego `A-01` con «Por posición», como en C.

## F — Depot: conteo por producto con el producto en varias posiciones

Igual que E, pero en Depot, con un producto que está en seis posiciones.

| Dato | Valor |
|---|---|
| Producto | `56-CM-100F-42` COMFORD ZONE FOAM MATRESS 6X42 (escribir el SKU) |
| Posiciones | `07-B-14`, `07-C-14`, `07-D-14`, `08-B-14`, `08-C-14`, `08-D-14` — 8 en cada una (48 en total) |
| Cantidad contada | `07-B-14` 8 · `07-C-14` 8 · `07-D-14` 6 (queda una diferencia de −2) |
| No usar | `171-DU-1724` y `171-AC-100-A` (las mueven B y A) |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Conteo → «Por producto» → `56-CM-100F-42` | «El producto está en varias posiciones: elige en cuál lo contaste.» (lista las 6) |
| 2 | Elegir cada posición y escribir su cantidad | «`<sku>` agregado en `<pos>`.» |
| 3 | Terminar conteo | La diferencia de `07-D-14` se ve al reconciliar en la web → Conteo |

---

## Orden y cuidados

| Compañía | Orden sugerido |
|---|---|
| Solutions | preparar el Contador → C → D → E |
| Depot | registrar el aparato → A → B → F |

- Cada línea contada o verificada se cierra: para repetir un escenario use otro producto o cancele el conteo y abra otro.
- Repetir A o B cambia las existencias (A suma 100 de TOURNIQUET; B saca 100 de UNDERPAD). Para volver al inicio: ajustar en la web, o recrear la base con `scripts\recrear-base.ps1` (hay que volver a correr el script de `A-01`, crear el Contador y registrar los aparatos).
- Conteo, acomodo y la consulta de posiciones de Despacho necesitan señal al servidor.
