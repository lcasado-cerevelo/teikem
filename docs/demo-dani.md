# Libreto de la demostración a Dani (actualizado 2026-10-07)

**Todo se hace en la app del aparato (el Zebra o el emulador). Nada en la web.** Está escrito para que lo pueda seguir alguien que nunca ha tocado el sistema:
cada paso dice **dónde tocar** y **qué debe ver** en la pantalla. Los textos entre « » son los que aparecen escritos en la app.
Los datos son de la base local `Teikem`: **Depot** (compañía 2) y **Solutions** (compañía 3). Se armó leyendo el código y la base; **no se probó en un aparato**:
conviene ensayarlo una vez antes. Los códigos de barras para imprimir están en `docs/demo/codigos-demo-dani.pdf`.

---

## 0. Palabras que se usan

| Palabra | Qué es |
|---|---|
| **Posición** | Un lugar físico del almacén (un estante) con un código, por ejemplo `01-E-03` o `A-01`. |
| **SKU** | El código interno de un producto, por ejemplo `171-AC-201-M`. |
| **Escanear** | Apuntar el lector del aparato (gatillo) al código de barras impreso. **Si no hay lector** (emulador): toque el campo, escriba el código y toque **«Aceptar»**. Con el lector, escanear equivale a escribir y aceptar. |
| **Inicio** | La pantalla con los 5 botones grandes: **Recibir**, **Acomodar**, **Despacho**, **Conteo**, **Consultar**. Desde cualquier pantalla se vuelve con el botón **«Volver»** (abajo). |
| **Recibo ciego** | Recibir mercancía sin orden de compra: se escanea lo que llegó y se dice cuánto. |
| **Acomodo** | Llevar lo recibido a su posición definitiva en el estante. |
| **Conteo** | Contar lo que realmente hay en el estante y compararlo con lo que dice el sistema. |
| **«Contar a ciegas»** | La app **no dice** cuánto debería haber; solo se anota lo que se ve. |

## 1. Antes de empezar

| Qué | Cómo está hoy | Qué hacer |
|---|---|---|
| Aparato de Solutions | Registrado («Emulador», ALM-SOL), con PIN | Nada |
| Aparato de Depot | Registrado («Emulador», ALM-DEPOT), con PIN | Nada |
| Posición `A-01` de Solutions | Ya creada | Nada (`scripts/demo/solutions-nueva-posicion.sql`, por si recrea la base) |
| Conteos viejos de pruebas | Pueden quedar conteos abiertos de ensayos anteriores | Entre a **Conteo**: si arriba aparece **«Conteos abiertos»**, toque cada uno en **Continuar** y termínelo con **«Dejar en 0 y terminar»** (o dé de baja el conteo en la web: Conteo cíclico → papelera). **Con un conteo abierto la app no deja hacer otra cosa** |
| Reconteo (escenarios C y D) | El administrador de Solutions tiene el permiso «Contar» y por eso la app **no le hace reconteo** | Antes de C y D: `sqlcmd -S localhost -E -C -d Teikem -v Modo=ON -i scripts\demo\solutions-modo-contador.sql`. Esperar 5 minutos o reiniciar el API (guarda los permisos en memoria). **Al terminar:** el mismo comando con `Modo=OFF` |
| Ajustes de conteo | Las dos compañías: «Solo los marcados», margen 0 % (cualquier diferencia pide recontar), muestra el número | Nada |
| Recibos hechos | Depot ya tiene `REC-00001` (hecho antes de este libreto); el producto del escenario A es otro, sin existencia | Nada |

**Para entrar a la app** (siempre igual): abra la app → si el teléfono tiene varias compañías, toque la compañía (**Advance Depot** o **Advance Solutions**) → toque su usuario →
escriba su **PIN** → llega a **Inicio**. Arriba de los botones dice **«Almacén: …»** (el almacén con el que se trabaja; si hay más de uno aparece el botón **«Cambiar»**).

En Solutions ningún producto trae código de barras: se escribe (o se escanea de la hoja) el **SKU**.

---

## A — Depot: recibo ciego con acomodo y reparto por posición

**Qué se muestra:** llega mercancía sin orden de compra; se recibe, queda en la posición de recepción `R1` y luego se **reparte** en cinco estantes distintos con una sola tarea de acomodo.

| Dato | Valor |
|---|---|
| Producto | `171-AC-201-M` GLOVE NITRILE PF N/ST MEDIUM — código de barras `3022123181425` (sin existencia hoy) |
| Cantidad recibida | 100 |
| Modo del recibo | «Con acomodo» (el de Depot) |
| Cantidad por posición | 20 |
| Posiciones donde se reparte | `01-E-03`, `01-E-04`, `01-E-05`, `01-E-06`, `01-E-09` (cupo 40 y vacías) |
| Posición de cupo chico (para el aviso) | `09-A-08` (cupo 10, vacía) |

| Paso | Qué hace usted | Qué debe ver |
|---|---|---|
| 1 | Entre a **Depot** (sección «Para entrar»). En **Inicio** toque **Recibir**. Elija el modo **«Con acomodo»** y toque **«Recibo ciego»** | «Sin orden de compra ni aviso: se recibe libre» |
| 2 | Escanee `3022123181425`. Escriba la cantidad **100**. Confirme el recibo | Recibo hecho; se crea una **tarea de acomodo** |
| 3 | Toque **Volver** para ir a **Inicio** y entre a **Acomodar**. Toque la tarea de ese producto. En «Cantidad por posición» escriba **20** | «Pendiente de acomodar: 100» |
| 4 | Escanee, una por una, las 5 posiciones (`01-E-03` … `01-E-09`) | Cada una agrega una línea `<posición> · 20`; al final «Repartido: 100 · quedan 0 sin acomodar» |
| 5 | Escanee una sexta posición cualquiera | «Ya no hay unidades por acomodar: los 100 están repartidos. Confirma el reparto.» |
| 6 | Toque **Confirmar reparto** | «Listo: 171-AC-201-M, 100 en 5 posición(es); quedan 0 pendientes» |

**Variantes para enseñar** (con otro producto de guantes: `171-AC-201-L`, `-S` o `-XL`, y otras posiciones vacías):

| Qué hace usted | Qué debe ver |
|---|---|
| «Cantidad por posición» = **40** | Reparte 40 + 40 + 20; la tercera avisa «`<pos>` recibe solo 20 (lo que quedaba), no 40» |
| Con 20 por posición, escanee `09-A-08` | «Cupo para 10: recibirá 20. Se puede confirmar igual.» (el cupo **avisa, no bloquea**) |
| Escanee la misma posición dos veces | «Esa posición ya está en el reparto.» |

## B — Depot: despacho que sale de varias posiciones

**Qué se muestra:** se pide más de lo que hay en un solo estante; la app muestra los estantes que tienen el producto y cada uno aporta, como máximo, lo que tiene.

| Dato | Valor |
|---|---|
| Producto | `171-DU-1724` UNDERPAD 17X24 3PK/100EA — código de barras `1201804326993` |
| Posiciones | `13-C-20` con 60 y `14-C-20` con 60 (120 en total, nada reservado) |
| Cantidad a despachar | 100 |
| Con más posiciones | `171-AC-100-A` TOURNIQUET — código `3726918104001`: `09-C-15` 60, `10-C-15` 60, y `09-A-01`, `09-A-03`, `09-A-07`, `09-A-09`, `09-A-15` con 20 cada una (220 en total). Pida 150 → 60 + 60 + 20 + 10 |

| Paso | Qué hace usted | Qué debe ver |
|---|---|---|
| 1 | En **Inicio** toque **Despacho**. Escanee `1201804326993` | «Posiciones con existencia»: las dos con «disponible 60» |
| 2 | Escriba la cantidad **100** y toque **«Marcar las sugeridas»** | `13-C-20` toma **60**, `14-C-20` toma **40** → «Tomado 100 de 100» |
| 3 | (Para enseñar el límite) marque **solo** `13-C-20` | «No alcanza sola: faltan 40. Marca más posiciones abajo.» |
| 4 | Toque **«Usar estas posiciones»** | «Agregado: 1201804326993 desde 2 posiciones» |
| 5 | Toque **«Completar despacho»** (saca el inventario sin empacar; se manda cuando hay señal) | La línea queda enviada |
| 6 | (Aparte) pida **130** cuando solo hay 120 | «No alcanza: faltan 10. Baja la cantidad.» |

## C y D — Solutions: contar mal, segunda oportunidad y terminar el conteo

**Qué se muestra:** se cuenta una posición **a ciegas**. Si la cantidad no coincide con lo que hay en el sistema, la app pide **contar otra vez** sin decir cuánto era; si la segunda cifra también está mal, la línea queda para revisión del supervisor.
La posición `GENERAL` tiene **33 productos**: en la demo solo se cuentan dos, y al final se enseña qué pasa cuando faltan productos por contar.
**Necesita el modo contador activado (sección 1).**

| Dato | Valor |
|---|---|
| Posición | `GENERAL` |
| Producto C | `53350` PRODIGY CONTROL SOLUTION HIGH 4ML — existencia 20 — se cuenta primero **15** y luego **20** |
| Producto D | `53310` PRODIGY CONTROL SOLUTION LOW 4ML — existencia 19 — se cuenta primero **10** y luego **12** |

| Paso | Qué hace usted | Qué debe ver |
|---|---|---|
| 1 | Entre a **Solutions**. En **Inicio** toque **Conteo**. Arriba elija **«Por posición»**. Escanee `GENERAL` | Se abre el conteo de `GENERAL` con el aviso «Conteo a ciegas: no se muestran las cantidades del sistema.» |
| 2 | **C:** escriba `53350` y toque **Aceptar**. En «Cantidad encontrada» escriba **15** y toque **Agregar** | «No coincide con lo esperado. Vuelve a contar y acepta de nuevo.» (no dice cuánto era) |
| 3 | Escriba ahora **20** y toque **Agregar** | «Coincide: contaste 20 y se esperaba 20.» La línea se cierra |
| 4 | **D:** escriba `53310` y toque **Aceptar**; cantidad **10** → **Agregar** | «No coincide con lo esperado. Vuelve a contar y acepta de nuevo.» |
| 5 | Escriba **12** y toque **Agregar** | «Contaste 12 y se esperaba 19. Queda para revisión.» No hay tercer intento |
| 6 | Intente cambiar o quitar esa línea | «Esa línea ya se verificó; no se puede cambiar ni quitar.» |
| 7 | Toque **«Terminar esta posición»** | Pregunta «**Faltan 31 producto(s) por contar**» con tres botones: **Seguir contando**, **Guardar y seguir después**, **Dejar en 0 y terminar** |
| 8 | Toque **Guardar y seguir después** | «Guardado. El conteo CC-… sigue abierto: retómalo desde Conteo.» y vuelve a **Inicio** |
| 9 | En **Inicio** toque, por ejemplo, **Acomodar** | «Termina o cancela el conteo en curso antes de usar esto.» (mientras hay un conteo abierto no se puede hacer otra cosa) |
| 10 | Toque **Conteo** | Arriba, **«Conteos abiertos»** con `CC-… · GENERAL` y «2 de 33 contados». Toque ese conteo (**Continuar**) |
| 11 | Ya dentro | «Retomaste el conteo CC-… de GENERAL: ya llevas 2 de 33 contados.» Se ven las dos líneas contadas |
| 12 | Toque **Terminar esta posición** y esta vez **Dejar en 0 y terminar** | Se manda el conteo completo (los 31 que faltaban quedan en 0) y vuelve a **Inicio**, ya sin bloqueo |

Después de la demo: ese conteo queda para que alguien lo revise en la web (los 31 en 0 son una diferencia de prueba): **no lo reconcilie**; dé de baja el conteo (Conteo cíclico → papelera) y ponga
`Modo=OFF` (sección 1).

## E — Solutions: conteo por producto, con el producto en varias posiciones

**Qué se muestra:** un mismo producto está en dos posiciones y se cuenta en las dos. La app **no vuelve a ofrecer** la posición que ya se contó, y tocar el producto en la lista lo vuelve a poner en el campo para seguir.
Primero se mete existencia en la posición nueva `A-01` con un recibo ciego directo (así el producto queda en dos posiciones).

| Dato | Valor |
|---|---|
| Producto | `00050-7` GLOBAL COTTON SWABS 300CT. (sin código de barras: se escribe el SKU) |
| Existencia hoy | 50 en `GENERAL` |
| Recibo ciego directo | 20 unidades a `A-01` (cupo 100) → queda `GENERAL` 50 y `A-01` 20 |
| Cantidad contada | 50 en `GENERAL`, 20 en `A-01` |

| Paso | Qué hace usted | Qué debe ver |
|---|---|---|
| 1 | En **Inicio** toque **Recibir**. Elija el modo **«Directo a posición»** y toque **«Recibo ciego»**. Escanee `00050-7`, cantidad **20**, escanee la posición destino `A-01` y confirme | Recibo hecho; el producto queda también en `A-01` |
| 2 | **Volver** a **Inicio** → **Conteo** → elija **«Por producto»**. Escriba `00050-7` y toque **Aceptar** | «El producto está en varias posiciones: elige en cuál lo contaste.» con una lista (`GENERAL`, `A-01`) |
| 3 | Toque `GENERAL`. En «Cantidad encontrada» escriba **50** y toque **Agregar** | «00050-7 agregado en GENERAL.» y la línea aparece en la lista |
| 4 | **Toque el producto en la lista** (o escríbalo otra vez) | El SKU `00050-7` aparece en el campo de arriba y la pantalla sube hasta él. Toque **Aceptar** |
| 5 | Mire la lista de posiciones | Solo sale `A-01`: **`GENERAL` ya no aparece** porque ya se contó. Toque `A-01`, cantidad **20**, **Agregar** |
| 6 | Toque otra vez el producto en la lista y **Aceptar** | «Ya contaste este producto en todas las posiciones donde el sistema dice que está.» (para corregir una cantidad se toca ✎ en su línea). Toque **Cancelar** |
| 7 | Toque **«Terminar conteo»** | Vuelve a **Inicio**; el conteo se envía |

Para mostrar el reconteo con este mismo producto: cuente `GENERAL` y luego `A-01` con **«Por posición»**, como en C.

## F — Depot: conteo por producto con el producto en seis posiciones

**Qué se muestra:** igual que E pero en Depot, con un producto que ya está en seis posiciones (no hace falta recibir nada). Sirve para enseñar cómo se va **tachando** cada posición contada.

| Dato | Valor |
|---|---|
| Producto | `56-CM-100F-42` COMFORD ZONE FOAM MATRESS 6X42 — código `+B676CM100F420+` |
| Posiciones | `07-B-14`, `07-C-14`, `07-D-14`, `08-B-14`, `08-C-14`, `08-D-14` (8 en cada una, 48 en total) |
| Cantidad contada | `07-B-14` → 8 · `07-C-14` → 8 · `07-D-14` → 6 (queda una diferencia de −2) |

| Paso | Qué hace usted | Qué debe ver |
|---|---|---|
| 1 | Entre a **Depot**. **Inicio → Conteo → «Por producto»**. Escanee `+B676CM100F420+` | «El producto está en varias posiciones: elige en cuál lo contaste.» con las 6 posiciones |
| 2 | Toque `07-B-14`, cantidad **8**, **Agregar** | «56-CM-100F-42 agregado en 07-B-14.» |
| 3 | Toque el producto en la lista → **Aceptar** | La lista ahora trae **5** posiciones (ya no sale `07-B-14`) |
| 4 | Toque `07-C-14`, cantidad **8**, **Agregar**; repita el paso 3; toque `07-D-14`, cantidad **6**, **Agregar** | Cada vez la lista de posiciones pendientes se acorta |
| 5 | Toque **«Terminar conteo»** | Vuelve a **Inicio**; la diferencia de `07-D-14` queda registrada para quien reconcilie. Las posiciones que no se contaron **no** se mandan |

---

## Orden y cuidados

| Compañía | Orden sugerido |
|---|---|
| Solutions | `Modo=ON` (y esperar 5 min) → C y D → E → `Modo=OFF` |
| Depot | A → B → F |

- Cada línea contada o verificada se cierra: para repetir C o D use otro producto (por ejemplo `00052-1`, existencia 237) o abra otro conteo.
- Repetir A, B o E **cambia las existencias**. A usa un producto sin existencia y posiciones vacías; para repetirlo habría que usar otro producto del mismo tipo y otras posiciones vacías.
- Conteo, acomodo, recibo y la consulta de posiciones de Despacho necesitan señal al servidor.
- **Un conteo abierto bloquea las demás acciones** de la app (Recibir, Acomodar, Despacho, Consultar y el cambio de almacén) hasta terminarlo: es a propósito. Si algo se queda abierto, **Conteo → Conteos abiertos → Continuar**.
- Si recrea la base (`scripts\recrear-base.ps1`) se pierden los aparatos registrados, los PIN y la posición `A-01`.
