# Guía de la demostración a José (preparada 2026-10-25)

Es una **guía**, no un libreto: dice qué enseñar, en qué orden, con qué datos y qué cuidar. Lo que se lee de los documentos y del código está marcado como **verificado en el código**; lo que **nadie ha probado de punta a punta** va en la sección «Ensayo de esta noche». Los nombres de menú van como aparecen en la interfaz en inglés.

## 0. Lo que sí y lo que no (para no prometer de más)

| Tema | Estado real | Qué decir |
|---|---|---|
| Correo → aviso de llegada y de salida | Construido (módulo Integrations, lectores de cuerpo, PDF y adjunto CSV/Excel). El IMAP **real** nunca se probó contra un servidor; con un `.eml` subido a mano se procesa igual que un correo recibido | «Llega un correo y se crean los avisos». En la demo se sube el correo a mano; no se conecta un buzón real |
| App de almacén: recibir con acomodo / sin acomodo / parcial | Construido. **No existe un botón «acomodo parcial»**: son dos cosas, «Recibir una parte» y repartir una tarea de acomodo en varias posiciones | Enseñar las dos y llamarlas por su nombre |
| Despacho (aviso de salida) en el aparato | Construido (Despacho → aviso SAL-… → sacar por posición → Despachar) | — |
| Portal del cliente | Construido (envíos, almacén, informes, mensajes, facturas). La entrada exige confirmar la computadora por correo **y** MFA | Entrar con un usuario ya preparado (sección 3) |
| Mapa de rutas con el chofer | **Parcial**: la web tiene un plano esquemático (puntos numerados unidos por una línea, sin calles) y el punto vivo del chofer en *Operations → Monitor*. **No hay mapa con calles ni Google Maps**. El portal del cliente **no** muestra ruta ni chofer | Decirlo de frente: «el mapa real es lo que sigue» |
| Escaneo de paquetes (app de choferes) | Construido (Scan: Recoger / Llegó / Enrutar / Salir / Devolver). La cámara del emulador **no lee códigos reales**: escribir el número de pieza o usar un teléfono físico | Mostrarlo con teléfono físico si se puede |

## 1. Cómo mostrar los móviles desde tu computadora

La idea: **todo corre en tu PC** (API, web, portal) y las pantallas de los móviles se **espejan en ventanas** de la misma PC. Tú compartes esas ventanas (o la pantalla entera) en Teams/Zoom o las proyectas.

**El método más cómodo: teléfono físico + `scrcpy`** (gratis; espeja la pantalla del teléfono en una ventana y puedes tocar con el ratón y escribir con tu teclado; esto además esquiva el problema del teclado del emulador).
- Instalar una vez: `winget install Genymobile.scrcpy`. Activar «Depuración USB» en el teléfono y conectarlo por USB.
- `adb devices` debe mostrar el teléfono. Abrir: `scrcpy --window-title "Almacén"`. Con dos teléfonos: `scrcpy -s <serial>` en cada uno, con otro título.
- **Truco para el API:** con el teléfono por USB, `adb reverse tcp:5000 tcp:5000` hace que el teléfono vea tu API como `http://localhost:5000`. No hace falta IP ni Wi-Fi.
- Ventaja para la demo del escaneo: la cámara del teléfono sí lee un código de barras impreso o puesto en otra pantalla.

**Alternativa: emulador de Android** (el que ya tienes, `teikem_e2e_api34`).
- Ábrelo en **ventana propia**: en Android Studio, Settings → Tools → Emulator → desmarca «Launch in the Running Devices tool window». Esa ventana se comparte directo.
- Desde el emulador el API es `http://10.0.2.2:5000/`.
- La ubicación del chofer se simula en el emulador: «…» (Extended controls) → Location → carga una ruta (GPX/KML) y dale «Play route».
- Cámara: no sirve para escanear. En la app, escribir el código y tocar «Aceptar» hace lo mismo que el lector.

**Qué app necesita qué**
- **App de almacén (`app-almacen`)**: solo Android (no tiene versión web utilizable). Se instala con el APK de `scripts/construir-apk.ps1` (la variante `development` admite HTTP plano) o con `npm run android`.
- **App de choferes (`app-movil`)**: development build (`npm run android`), no sirve Expo Go. Necesita `.env` con `EXPO_PUBLIC_BASE_URL=http://10.0.2.2:5000/api/` (emulador). Con teléfono por USB y `adb reverse`: `http://localhost:5000/api/`.
- **Portal del cliente (`app-cliente`)**: en el navegador: `npm run web` desde `app-cliente` (puerto 8081; si está ocupado Expo propone otro). El API debe tener ese origen en `Cors:Origins` y `Auth:PasswordReset:WebBaseUrl` apuntando a esa URL.
- **Web del personal (`web-app`)**: Vite; el puerto 3000 ya es un origen permitido por el API. Apúntala con `VITE_API_URL=http://localhost:5000`.
- Cada app de Expo con Metro necesita su **propio puerto** (`--port 8082`, `8084`…).

**Distribución sugerida de pantalla:** web del personal grande a un lado; a la derecha, las ventanas de los móviles (una a la vez). Para cambiar de tema, se cambia de ventana, no de pantalla completa.

## 2. Preparación (la noche anterior, en este orden)

1. API arriba contra la base local `Teikem` (tiene Advance Depot, Advance Logistics y De La Paz). **No usar la base `SEPHAS` ni correr maestros de `Database/`.**
2. Correr una vez, en la base local:
   - `sqlcmd -S localhost -d Teikem -E -C -f 65001 -b -i scripts\integraciones\axiscare-depot.sql` (si la integración «Axiscare avisos por correo» no existe; **hoy existe, apagada**).
   - `sqlcmd -S localhost -d Teikem -E -C -f 65001 -b -i scripts\demo\jose-demo-datos.sql` (enciende el módulo **Integrations** de Advance Depot, agrega los lectores «Demo llegada (CSV)» y «Demo salida (CSV)» y prende la integración). Es idempotente; se validó en una transacción con rollback.
3. Generar los correos: `node scripts/demo/generar-correos-demo.mjs 1` deja `scripts/demo/correos/aviso-de-llegada-1.eml` y `aviso-de-salida-1.eml`. Para repetir la demo: otro número (el mismo contenido no se procesa dos veces).
4. Registrar el/los aparatos de almacén de **Advance Depot** (hoy hay tres aparatos Zebra registrados de pruebas anteriores): *System → Devices* → crear aparato → código de registro (8 caracteres, 24 h) → en el aparato escribir la dirección del servidor y el código; cada usuario con su **PIN** (4-6 dígitos, se define en «Mi cuenta» en la web). El usuario necesita los permisos `inventory.view`, `warehouse.receive` y `warehouse.pick`.
5. Portal del cliente: **la compañía con el portal encendido es Advance Logistics (tenant 2) y De La Paz (tenant 3)**; Advance Depot no tiene el módulo `CLIENT_PORTAL`. Preparar con tiempo un usuario de portal **ya activo, con MFA inscrito y la computadora confirmada** (invitar desde la ficha del cliente → usuarios del portal; el enlace vence en 48 h, la contraseña pide mínimo 12 caracteres). Iniciar sesión una vez antes de la demo.
6. Transporte (Advance Logistics): hay 69 choferes y 441 rutas históricas, pero **no hay vehículos** (`Vehicle` vacío). Preparar: una ruta de hoy con un chofer que tenga usuario (p. ej. Emmanuel Morales, usuario 8), un vehículo si el despacho lo pide, y 3-4 órdenes con dirección. Las paradas necesitan coordenadas o salen en «Sin ubicación».
7. Imprimir (o tener en otra pantalla) los códigos de barras de los productos y de las posiciones: `docs/demo/codigos-demo-dani.pdf` sirve de modelo (generador: `scripts/demo/generar-codigos-demo.mjs`).

### Datos que ya existen en la base local (verificado 2026-10-25)

| Qué | Dato |
|---|---|
| Compañía / cliente / almacén | Advance Depot · cliente **AxisCare** (`AXISCARE`) · almacén **Almacén Depot** (`ALM-DEPOT`), modo «con acomodo», posición de recepción **R1** (zona de recibo) |
| Productos para **llegadas** (del cliente AxisCare, sin existencia hoy) | `171-AC-201-M` (código 3022123181425), `171-AC-201-L` (3022123181432), `171-DU-1724` (1201804326993), `171-AC-100-A` (3726918104001) |
| Productos con **existencia** para **salidas** | `402-BIO-1067S` (en 47 posiciones, ~11 mil), `402-WOG4014412P` (12 posiciones, ~15 mil) |
| Aparatos de almacén | 3 Zebra ya registrados en Advance Depot |
| Avisos existentes | Ninguno (la base no tiene avisos de llegada ni de salida) |
| Integración | «Axiscare avisos por correo» (existe, apagada, con 3 lectores de PDF) |

## 3. Bloque 1 — Correo → aviso de llegada y de salida (web)

**Qué se quiere que José entienda:** el cliente manda un correo y el sistema crea solo el aviso; nadie teclea.

1. *System → Integrations / API*: mostrar la integración «Axiscare avisos por correo» (buzón, lectores, a quién avisa). Explicar los lectores en una frase: cada uno dice «si el documento es así, crea esto».
2. Botón **Upload file** (Subir archivo): subir `aviso-de-llegada-1.eml`. Esperar el resultado de la corrida («archivos leídos, avisos creados»).
3. *Warehouse → Arrivals & departures* (`/warehouse/notices`): aparece el aviso **LLE-…** con 3 líneas (100 guantes M, 60 guantes L, 120 underpads).
4. Subir `aviso-de-salida-1.eml` → aparece el aviso **SAL-…** con sus líneas y la **reserva** hecha contra existencias reales.
5. Cerrar con la campanita/aviso del sistema: «le avisa al equipo cuando llegó algo».

**Cuidados:** un SKU que no existe como producto activo del cliente manda **todo el documento** a la bandeja (pestaña *Inbox*) con el motivo; es buena demostración de control, pero no la hagas por accidente. Un `.eml` ya subido se rechaza por repetido.

## 4. Bloque 2 — App de almacén (aparato)

Registrar y entrar antes de la demo (sección 2). Después de crear los avisos, tocar **Sincronizar ahora** en Inicio para que bajen al aparato.

### 4a. Recibir **con acomodo** (almacén en modo «con acomodo», el actual)
1. **Recibir** → tocar el aviso LLE-… (o escribirlo y «Aceptar»). Se ve «Lo que dice el documento» (Esperado · Recibido · Falta).
2. Escanear un producto → cantidad → **Agregar**. Dice «Se recibe en la posición de recepción R1».
3. **Confirmar recibo**: se crean las **tareas de acomodo**.
4. **Acomodar** → tocar la tarea → escanear la posición real (la app sugiere hasta 3) → «Listo: … quedó en …».

### 4b. **Acomodo parcial** (son dos cosas; enseñar ambas)
- **Recibir una parte**: con un aviso, capturar solo lo que ya llegó (p. ej. 60 de 100) y tocar **Recibir una parte**. Se acomoda lo recibido mientras el aviso sigue abierto («Recibido parcial» en la web). Cuando llega el resto: otra parte o **Confirmar recibo**.
- **Repartir una tarea en varias posiciones**: en Acomodar poner «Cantidad por posición» (p. ej. 20) y escanear varias posiciones; **Confirmar reparto**. Lo que no se repartió queda como tarea nueva («quedan N pendientes»).

### 4c. Recibir **sin acomodo** (directo a posición)
- El modo es **por almacén**, no por aparato. Para enseñarlo: en la web, *Warehouses* → editar «Almacén Depot» → «Modo de recepción» = *Directo a posición* → en el aparato **Sincronizar ahora**. (Afecta recibos **nuevos**; vuelve a ponerlo en «Con acomodo» al terminar.)
- **Recibir** → aviso → producto → cantidad → **Siguiente** → escanear la **posición destino** (no puede ser de recepción ni de cruce) → **Confirmar recibo**. No se crean tareas de acomodo.
- Limitación a saber: con aviso, cada producto entra a **una sola posición** por recibo; «Recibir una parte» no existe en modo directo.

### 4d. Despachar el aviso de salida
- **Despacho** → tocar SAL-… → escanear el producto → ir a la posición que indica («Ve a …») → cantidad → **Terminar** → **Despachar**. Funciona sin señal (se envía después).
- En la web volver a *Arrivals & departures* para ver el aviso despachado.

> Empacar/cerrar el envío final de un aviso de salida se hace en la web, no en el aparato.

## 5. Bloque 3 — Transporte

**Orden de la demo (el flujo completo está cubierto por el código):**

1. **Portal del cliente** (navegador, usuario preparado): *Nuevo envío* → crear una orden → ver *Mis envíos* con la barra de progreso (Sometido / Recibida / En ruta / Completada). Si hay tiempo: *Warehouse* del portal (existencias, llegadas, salidas) y *Messages*.
2. **App de choferes — escaneo**: Scan → proceso **Recoger** (la orden pasa a *Recogido*) → **Llegó** (*Sin asignar*). Escribir el número de pieza o escanear con el teléfono.
3. **Web — despacho**: *Operations → Dispatch* (`/ops/dispatch`): crear la ruta con chofer, agregar las órdenes, **Dispatch**. El chofer recibe «Nuevo manifiesto».
4. **App de choferes**: aceptar el manifiesto (la ruta queda *En curso*, y la app empieza a mandar la ubicación).
5. **Web — mapa**: *Operations → Monitor* (`/ops/monitor`): paradas en orden, la línea de la ruta y el **punto vivo del chofer** con «hace N s». Con el emulador, correr la ruta GPX; el punto se mueve al refrescar y puede tardar hasta ~2 minutos en el primer envío.
6. **Entrega** en la app (firma, foto, GPS) → *Entregado* → en el portal el cliente ve el comprobante.

**Lo que falta (decirlo tú antes de que lo pregunte):** mapa con calles (hoy es un plano esquemático), ver al chofer o la ruta desde el portal del cliente, y optimización de rutas con un motor real (hoy es una heurística propia).

## 6. Ensayo de esta noche (lo que **nadie ha probado**)

Haz un ensayo completo, cronometrado, y marca cada punto:
- [ ] `.eml` de llegada y de salida: que la corrida cree LLE- y SAL- (el código del lector de CSV existe, pero no lo ejecuté contra el API; si falla, mira la pestaña *Inbox* de la integración).
- [ ] Que los avisos se bajen al aparato tras «Sincronizar ahora».
- [ ] 4a, 4b, 4c y 4d en el aparato de verdad (el libreto anterior, `docs/demo-dani.md`, avisa que sus pasos no se probaron en aparato y es anterior a los lotes 161-199: el modo ya no se pregunta, sale del almacén).
- [ ] Login del portal desde cero (correo de confirmación + MFA) y que el CORS permita el origen del portal.
- [ ] Login del chofer en `app-movil` (con `.env` creado) y que mande ubicación con la ruta en curso.
- [ ] Que el punto del chofer aparezca en *Monitor*.
- [ ] `npm run check` en `app-cliente` (tiene cambios sin commitear).

## 7. Plan B si algo falla en vivo

| Si falla… | Haz esto |
|---|---|
| El correo no crea el aviso | Crear el aviso a mano en *Arrivals & departures* (misma pantalla que ya mostraste) y decir que el correo es la forma automática |
| IMAP / buzón | No lo menciones como probado: la demo usa «Subir archivo» |
| El aparato no baja el aviso | **Sincronizar ahora** con señal; verificar que el usuario tenga el aviso asignado o el permiso «ver todo» |
| La cámara no escanea | Escribir el código y «Aceptar» |
| El punto del chofer no se mueve | Enseñar el recorrido histórico de una ruta ya hecha en *Monitor* |
| El portal no deja entrar | Capturas de pantalla del portal ya preparadas (`docs/manual/frontend/img/`) |

## 8. Después de la demo

- Volver el almacén a «Con acomodo» si lo cambiaste.
- Apagar la integración si no se va a usar: `UPDATE Integration SET IsActive = 0 …` o desde la pantalla.
- No dejes conteos ni recibos abiertos en los aparatos (bloquean todo lo demás).
