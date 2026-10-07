# Libreto de la demostración a Dani (2026-10-07)

Datos tomados de la base local `Teikem` (compañías: **Advance Depot** = id 2, **Advance Solutions** = id 3). «Default/Tipo» = Depot. Las pantallas y mensajes
citados son los textos reales de la app (`app-almacen/src/kernel/i18n/es.json`). Todo se armó leyendo el código y la base; **no se probó en un aparato**:
ensáyelo una vez antes de la demostración.

## 0. Preparación

| Qué | Estado hoy | Qué hacer |
|---|---|---|
| Aparato de **Solutions** | Registrado («Emulador», almacén ALM-SOL); solo el admin tiene PIN | Nada para A–F salvo el contador (siguiente fila) |
| **Contador** (para el reconteo, escenarios C y D) | **No existe.** Cada compañía tiene un solo usuario (el admin) y hasta el rol *WarehouseOperator* trae el permiso «Contar» | Ver «El contador» abajo |
| Aparato de **Depot** | **No hay ninguno registrado** y el admin de Depot **no tiene PIN** | Web, compañía Depot: Sistema → Aparatos → crear código; Roles y usuarios → asignar PIN. En la app: registrar el aparato con ese código (la app admite varias compañías) |
| Orden de compra en Depot | **No hay órdenes ni recibos** | Crearla en la web (escenario A) |
| Posición extra en Solutions | Solo existía `GENERAL` | **Ya creada**: `A-01` (zona GEN, cupo 100) con `scripts/demo/solutions-nueva-posicion.sql` (idempotente, ya corrida en su base) |
| Existencia en `A-01` | Vacía | Pasar 20 unidades con **Transferencias** en la web (escenario E, paso 1) |
| Ajustes de conteo | Las dos compañías: «¿Quién ve lo esperado al contar?» = *Solo los marcados*, margen de reconteo **0 %** (cualquier diferencia pide recontar), «Mostrar el número esperado» = sí | Nada (se ven en Sistema → Ajustes de la compañía → «Conteo cíclico: lo esperado al contar») |
| Conteos viejos en Solutions | `CC-00001` y `CC-00002` abiertos y vacíos (de pruebas) | Opcional: cancelarlos en la web |

Notas: en Solutions ningún producto tiene código de barras; se **escribe el SKU** (la ayuda de la pantalla dice «Código de barras o SKU»).

### El contador (necesario para C y D)

El reconteo («No coincide… vuelve a contar») **solo lo ve un contador a ciegas**: alguien con el permiso *Capturar conteo* **sin** el permiso *Contar* (supervisor).
Con el admin la app muestra lo esperado desde el inicio y no hay reconteo. En la web (compañía Solutions → **Roles y usuarios**):

1. **Roles → Nuevo rol** «Contador»: marcar solo *Capturar conteo* (`warehouse.count.capture`) y *Ver inventario* (`inventory.view`; la app lo necesita para bajar productos y posiciones). **No** marcar «Contar».
2. **Usuarios:** crear/invitar a un usuario con ese rol, asignarle **PIN**, y marcar **«Ve lo esperado al contar: Sí»** (con el ajuste *Solo los marcados* hace falta; si prefiere no marcarlo, ponga el ajuste de la compañía en *Todos*).
3. En el aparato, entrar con el PIN de ese usuario.

---

## Escenario A — Depot: recibo con acomodo y reparto por posición

**Datos:** producto `171-AC-100-A` TOURNIQUET (código `3726918104001`; hoy 120 u. en `09-C-15` y `10-C-15`, 60 c/u). Proveedor `171-RC Imports`.
Almacén `ALM-DEPOT` (modo de recepción: **con acomodo**; posición de recepción `R1`, cupo 410).
Posiciones vacías de la zona PCK para acomodar (cupo): `09-A-01` (50), `09-A-03` (70), `09-A-07` (70), `09-A-09` (40), `09-A-15` (70).
Posiciones vacías con cupo chico, para el aviso: `09-A-08`, `09-A-12`, `09-A-14` (cupo **10**).

1. **Web → Compras:** orden nueva, proveedor `171-RC Imports`, almacén `ALM-DEPOT`, línea `171-AC-100-A` × **100** → **Enviar** (solo una orden en borrador se envía; se recibe desde «Enviada»).
2. **App → Recibir:** escanear el número de la orden → escanear el producto → cantidad 100 → el modo del recibo sale **«Con acomodo»** («Entra a la zona de recepción y se acomoda después con tareas») → confirmar el recibo.
3. **App → Acomodar:** abrir la tarea (aparece «Pendiente de acomodar: 100»).
4. En **«Cantidad por posición (opcional)»** escribir **20**. Ayuda: «Escanea cada posición: en cada una se deja esta cantidad».
5. Escanear `09-A-01`, `09-A-03`, `09-A-07`, `09-A-09`, `09-A-15`. Cada escaneo suma una línea `09-A-01 · 20` y el total: «Repartido: 100 · quedan 0 sin acomodar».
   Un sexto escaneo responde: «Ya no hay unidades por acomodar: los 100 están repartidos. Confirma el reparto.»
6. **Confirmar reparto** → «Listo: 171-AC-100-A, 100 en 5 posición(es); quedan 0 pendientes».

Variantes que lucen:
- **El resto:** cantidad por posición **40** con 100 → 40 + 40 + **20**; la tercera posición muestra «`<pos>` recibe solo 20 (lo que quedaba), no 40».
- **El cupo:** con 20 por posición, escanear `09-A-08` (cupo 10) → «Cupo para 10: recibirá 20. **Se puede confirmar igual.**»
  → *Ojo:* en Acomodar el cupo **avisa pero no bloquea** (decisión de diseño). Lo que sí topa solo es el listado de posiciones marcables (Despacho y Recibo directo).
- Una posición repetida: «Esa posición ya está en el reparto.»

## Escenario B — Depot: despacho que sale de varias posiciones

**Datos:** producto `171-DU-1724` UNDERPAD 17X24 3PK/100EA (código `1201804326993`): **`13-C-20` con 60** y **`14-C-20` con 60** (120 en total, nada reservado).
Para más posiciones: `56-CM-100F-42` COMFORD ZONE FOAM MATRESS 6X42 (se escribe el SKU): 6 posiciones con **8** c/u (`07-B-14`, `07-C-14`, `07-D-14`, `08-B-14`, `08-C-14`, `08-D-14`) = 48.

1. **App → Despacho:** escanear `1201804326993`. La app consulta de dónde puede salir y muestra **«Posiciones con existencia»** (las dos, con «disponible 60»).
2. Escribir cantidad **100** (más de lo que hay en una sola posición).
3. **«Marcar las sugeridas»** (o tocar cada posición): `13-C-20` toma **60** (todo lo disponible) y `14-C-20` toma lo que falta, **40** → «Tomado 100 de 100». Una posición **nunca toma más de lo que tiene**.
   Si solo se marca la primera: «No alcanza sola: faltan 40. Marca más posiciones abajo.»
4. **«Usar estas posiciones»** → «Agregado: 1201804326993 desde 2 posiciones». También se puede escanear cada posición en vez de tocarla.
5. **«Completar despacho»** (saca el inventario sin empacar; se manda con señal). Empacar es opcional y pide cliente → consignatario (Depot tiene **1 cliente** activo).
6. Pedir **130** (hay 120) → «No alcanza: faltan 10. Baja la cantidad.»

Con el colchón: 30 unidades → 8 + 8 + 8 + 6 en cuatro posiciones.

## Escenario C — Solutions: contar mal, segunda oportunidad y coincide  *(entrar como el Contador)*

**Datos:** `53350` PRODIGY CONTROL SOLUTION HIGH 4ML — **20** en la posición `GENERAL`.
Este reconteo ocurre en **Conteo → «Por posición»** (el camino «Por producto» no verifica contra lo esperado, ver E y F).

1. **App → Conteo → «Por posición»** → escanear/escribir `GENERAL`. (Se abre el conteo de esa posición; el contador no ve cantidades esperadas.)
2. Escanear/escribir `53350` → cantidad **15** → Aceptar.
3. Respuesta: **«No coincide con lo esperado. Vuelve a contar y acepta de nuevo.»** (nunca dice cuánto era) y el campo de cantidad se limpia.
4. Cantidad **20** → Aceptar → **«Coincide: contaste 20 y se esperaba 20.»** (el total del inventario). La línea queda cerrada.
5. **Terminar conteo** (las demás productos de la posición quedan sin contar; no hace falta contarlos para la demostración).

## Escenario D — Solutions: dos intentos y se acabó  *(entrar como el Contador)*

**Datos:** `53310` PRODIGY CONTROL SOLUTION LOW 4ML — **19** en `GENERAL`.

1. Conteo → «Por posición» → `GENERAL` → `53310` → contar **10** → «No coincide con lo esperado. Vuelve a contar y acepta de nuevo.»
2. Contar **12** (otra vez mal) → **«Contaste 12 y se esperaba 19. Queda para revisión.»**
3. La línea **ya no se puede cambiar**: no hay tercer intento (el servidor responde 409 «La línea ya se verificó; no se puede cambiar su cantidad.»). El supervisor reconcilia después en la web con la primera cifra (10) y la última (12).
   *(Si la compañía no mostrara el número saldría «No coincide con lo esperado. Queda para revisión.»)*

## Escenario E — Solutions: conteo «Por producto» con el producto en varias posiciones

**Datos:** `00050-7` GLOBAL COTTON SWABS 300CT. — hoy **50** en `GENERAL`. La posición **`A-01`** ya está creada (script).

1. **Preparar (web → Almacén → Transferencias y ajustes → pestaña Transferencias):** transferir **20** de `00050-7` de `GENERAL` a `A-01` (queda en el kárdex). Resultado: `GENERAL` **30** · `A-01` **20**.
2. **App → Conteo → «Por producto»** → escribir `00050-7`. Se abre un conteo con varios productos («Lo contado»).
3. La app avisa: **«El producto está en varias posiciones: elige en cuál lo contaste.»** → elegir `GENERAL`, cantidad **30** → agregar → «00050-7 agregado en GENERAL.»
4. Escanear otra vez `00050-7` → elegir `A-01` → cantidad **20** → «00050-7 agregado en A-01.» (dos líneas del mismo producto, una por posición).
   Si se escanea de nuevo y se elige una ya contada: «00050-7 ya está contado en GENERAL: toca su línea de la lista para corregir la cantidad.»
5. **Terminar conteo.** La diferencia (si contó distinto) se ve al reconciliar en la web → Conteo.

> Este camino **no verifica contra lo esperado** (no hay «Coincide/No coincide» en el aparato). Si quiere mostrar el reconteo con este mismo producto, cuente cada posición por separado en **«Por posición»** (`GENERAL` y luego `A-01`), como en el escenario C.

## Escenario F — Depot: conteo «Por producto» con el producto en varias posiciones

**Datos:** `56-CM-100F-42` COMFORD ZONE FOAM MATRESS 6X42 — **8** en cada una de 6 posiciones: `07-B-14`, `07-C-14`, `07-D-14`, `08-B-14`, `08-C-14`, `08-D-14` (48 en total).
(No usar `171-DU-1724` ni `171-AC-100-A`: esas las mueven los escenarios B y A.)

1. **App (compañía Depot) → Conteo → «Por producto»** → escribir `56-CM-100F-42` → **«El producto está en varias posiciones: elige en cuál lo contaste.»** (lista las 6).
2. Elegir `07-B-14` → cantidad **8** → agregar. Repetir con `07-C-14` (**8**) y `07-D-14` (**6**, para dejar una diferencia).
3. **Terminar conteo.** En la web (Conteo → reconciliar) se ve la diferencia de `07-D-14` (−2). Las otras tres posiciones se pueden dejar sin contar.
4. Para mostrar el reconteo con este producto en Depot: hace falta el mismo **Contador** en Depot y contar una posición con «Por posición» (p. ej. `07-C-14`: contar 5 → «Vuelve a contar» → 8 → «Coincide»).

---

## Orden sugerido y cuidados

- **Solutions** (aparato ya registrado): preparar el Contador → C → D → E. **Depot** (hay que registrar el aparato): A → B → F.
- Cada línea contada o verificada **se cierra**: para repetir un escenario hay que usar otro producto (o cancelar el conteo y abrir otro).
- Repetir A o B **cambia las existencias** (A suma 100 de TOURNIQUET; B saca 100 de UNDERPAD). Para volver al punto de partida, ajustar en la web (Transferencias y ajustes) o recrear la base con `scripts\recrear-base.ps1` (vuelve a migrar Depot y Solutions; hay que volver a correr el script de la posición `A-01`, crear el Contador y registrar los aparatos).
- Todo lo del aparato necesita **señal** al servidor (conteo, acomodo, consulta de posiciones de Despacho).
