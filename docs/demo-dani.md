# Libreto de la demostración a Dani (2026-10-07)

**Todo se hace en la app del aparato.** Nada en la web. Lo que hay que preparar antes se hace con scripts (una línea cada uno) o ya está hecho.
Datos de la base local `Teikem`: **Depot** (compañía 2) y **Solutions** (compañía 3). Los mensajes son los textos reales de la app. Se armó leyendo el código y la
base; **no se probó en un aparato**: ensáyelo una vez antes. Los códigos de barras para imprimir: `docs/demo/codigos-demo-dani.pdf`.

## Antes de empezar

| Qué | Estado hoy | Qué hacer |
|---|---|---|
| Aparato de Solutions | Registrado («Emulador», ALM-SOL), con PIN | Nada |
| Aparato de Depot | Registrado («Emulador», ALM-DEPOT), con PIN | Nada |
| Posición `A-01` de Solutions | Ya creada | Nada (`scripts/demo/solutions-nueva-posicion.sql`, por si recrea la base) |
| Reconteo (escenarios C y D) | El admin de Solutions tiene el permiso «Contar» y por eso la app **no le hace reconteo** | Antes de C y D: `sqlcmd -S localhost -E -C -d Teikem -v Modo=ON -i scripts\demo\solutions-modo-contador.sql`. Esperar 5 minutos o reiniciar el API (guarda los permisos en memoria). **Al terminar:** el mismo comando con `Modo=OFF` |
| Ajustes de conteo | Las dos compañías: «Solo los marcados», margen 0 % (cualquier diferencia pide recontar), muestra el número | Nada |
| Recibos hechos | Depot ya tiene `REC-00001` (ciego, con acomodo, hecho antes de este libreto): el producto del escenario A de abajo es otro, sin existencia | Nada |

En Solutions ningún producto trae código de barras: se escribe (o se escanea de la hoja) el SKU.

---

## A — Depot: recibo ciego con acomodo y reparto por posición

Se hace un **recibo ciego** (sin orden de compra) con acomodo: la mercancía entra a `R1`. Luego, en **Acomodar**, se escribe cuánto va en cada posición y se escanean las
posiciones: cada escaneo suma una posición con esa cantidad hasta repartir todo.

| Dato | Valor |
|---|---|
| Producto | `171-AC-201-M` GLOVE NITRILE PF N/ST MEDIUM — código `3022123181425` (sin existencia hoy) |
| Cantidad recibida | 100 |
| Modo del recibo | «Con acomodo» (el de Depot) |
| Cantidad por posición | 20 |
| Posiciones a escanear | `01-E-03`, `01-E-04`, `01-E-05`, `01-E-06`, `01-E-09` — cupo 40 y vacías |
| Posición con cupo chico (aviso) | `09-A-08` — cupo 10, vacía |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Recibir → modo «Con acomodo» → **«Recibo ciego»** | «Sin orden de compra ni aviso: se recibe libre» |
| 2 | Escanear `3022123181425`, cantidad 100, confirmar el recibo | Recibo hecho; aparece la tarea de acomodo |
| 3 | Acomodar → abrir la tarea; «Cantidad por posición» = 20 | «Pendiente de acomodar: 100» |
| 4 | Escanear las 5 posiciones | Cada una suma `<pos> · 20`; al final «Repartido: 100 · quedan 0 sin acomodar» |
| 5 | Un sexto escaneo | «Ya no hay unidades por acomodar: los 100 están repartidos. Confirma el reparto.» |
| 6 | Confirmar reparto | «Listo: 171-AC-201-M, 100 en 5 posición(es); quedan 0 pendientes» |

| Variante | Qué haces | Resultado |
|---|---|---|
| El resto | Cantidad por posición 40 | 40 + 40 + 20; la tercera avisa «`<pos>` recibe solo 20 (lo que quedaba), no 40» |
| El cupo | Con 20 por posición, escanear `09-A-08` | «Cupo para 10: recibirá 20. Se puede confirmar igual.» (en Acomodar el cupo **avisa, no bloquea**) |
| Repetida | Escanear la misma posición dos veces | «Esa posición ya está en el reparto.» |

## B — Depot: despacho que sale de varias posiciones

Se escanea un producto que está en varias posiciones y se pide más de lo que hay en una sola: la app muestra las posiciones con existencia y cada una toma, como máximo,
lo que tiene; la siguiente completa lo que falta.

| Dato | Valor |
|---|---|
| Producto | `171-DU-1724` UNDERPAD 17X24 3PK/100EA — código `1201804326993` |
| Posiciones | `13-C-20` con 60 · `14-C-20` con 60 (120 en total, nada reservado) |
| Cantidad a despachar | 100 |
| Con más posiciones | `171-AC-100-A` TOURNIQUET — código `3726918104001`: `09-C-15` 60 · `10-C-15` 60 · `09-A-01`, `09-A-03`, `09-A-07`, `09-A-09`, `09-A-15` con 20 cada una (220 en total). Pedir 150 → 60 + 60 + 20 + 10 |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Despacho: escanear `1201804326993` | «Posiciones con existencia»: las dos con «disponible 60» |
| 2 | Cantidad 100; tocar «Marcar las sugeridas» | `13-C-20` toma **60**, `14-C-20` toma **40** → «Tomado 100 de 100» |
| 3 | (Alternativa) marcar solo `13-C-20` | «No alcanza sola: faltan 40. Marca más posiciones abajo.» |
| 4 | «Usar estas posiciones» | «Agregado: 1201804326993 desde 2 posiciones» |
| 5 | «Completar despacho» (saca el inventario sin empacar; se manda cuando hay señal) | — |
| 6 | Pedir 130 (hay 120) | «No alcanza: faltan 10. Baja la cantidad.» |

## C — Solutions: contar mal, segunda oportunidad y coincide

Con el modo contador activado (ver arriba), se cuenta **Por posición**; si la cantidad se aparta de lo esperado la app pide contar otra vez sin decir cuánto era, y al
contarla bien responde con el número del inventario.

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

## D — Solutions: dos intentos mal y se acabó

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

Primero se pone existencia en la posición nueva con un **recibo ciego directo a posición** (20 unidades a `A-01`), así el producto queda en dos posiciones. Luego se
cuenta **Por producto**: al escanearlo, la app pide elegir la posición y se agrega una línea por posición. Este camino no verifica contra lo esperado (la diferencia
se ve al reconciliar).

| Dato | Valor |
|---|---|
| Producto | `00050-7` GLOBAL COTTON SWABS 300CT. (sin código de barras: se escanea el SKU) |
| Existencia hoy | 50 en `GENERAL` |
| Recibo ciego directo | 20 unidades a `A-01` (cupo 100) → queda `GENERAL` 50 · `A-01` 20 |
| Cantidad contada | 50 en `GENERAL`, 20 en `A-01` |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Recibir → modo **«Directo a posición»** → «Recibo ciego» → escanear `00050-7`, cantidad 20, escanear la posición destino `A-01`, confirmar | Recibo hecho; el producto queda en `A-01` |
| 2 | Conteo → «Por producto» → escribir `00050-7` | «El producto está en varias posiciones: elige en cuál lo contaste.» |
| 3 | Elegir `GENERAL`, cantidad 50 | «00050-7 agregado en GENERAL.» |
| 4 | Escanear otra vez, elegir `A-01`, cantidad 20 | «00050-7 agregado en A-01.» |
| 5 | Escanear otra vez y elegir una ya contada | «00050-7 ya está contado en GENERAL: toca su línea de la lista para corregir la cantidad.» |
| 6 | Terminar conteo | — |

Para mostrar el reconteo con este mismo producto: contar `GENERAL` y luego `A-01` con «Por posición», como en C.

## F — Depot: conteo por producto con el producto en varias posiciones

Igual que E, pero en Depot, con un producto que ya está en seis posiciones (no hace falta recibir nada).

| Dato | Valor |
|---|---|
| Producto | `56-CM-100F-42` COMFORD ZONE FOAM MATRESS 6X42 — código `+B676CM100F420+` |
| Posiciones | `07-B-14`, `07-C-14`, `07-D-14`, `08-B-14`, `08-C-14`, `08-D-14` — 8 en cada una (48 en total) |
| Cantidad contada | `07-B-14` 8 · `07-C-14` 8 · `07-D-14` 6 (queda una diferencia de −2) |

| Paso | Qué haces | Qué responde la app |
|---|---|---|
| 1 | Conteo → «Por producto» → `56-CM-100F-42` | «El producto está en varias posiciones: elige en cuál lo contaste.» (lista las 6) |
| 2 | Elegir cada posición y escribir su cantidad | «`<sku>` agregado en `<pos>`.» |
| 3 | Terminar conteo | La diferencia de `07-D-14` queda registrada para quien reconcilie |

---

## Orden y cuidados

| Compañía | Orden sugerido |
|---|---|
| Solutions | `Modo=ON` (y esperar 5 min) → C → D → E → `Modo=OFF` |
| Depot | A → B → F |

- Cada línea contada o verificada se cierra: para repetir C o D use otro producto (por ejemplo `00052-1`, 237) o cancele el conteo y abra otro.
- Repetir A, B o E **cambia las existencias**. A usa un producto sin existencia y posiciones vacías; para repetirlo habría que usar otro producto del mismo tipo (los guantes `171-AC-201-L`, `171-AC-201-S` y `171-AC-201-XL` también están sin existencia) y otras posiciones vacías.
- Conteo, acomodo, recibo y la consulta de posiciones de Despacho necesitan señal al servidor.
- Si recrea la base (`scripts\recrear-base.ps1`) se pierden los aparatos registrados, los PIN y la posición `A-01`.
