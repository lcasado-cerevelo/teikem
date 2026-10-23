# Script de pruebas — App de choferes (Advance / De La Paz)

Para: Jarvis
Preparado: 2026-07-02

## Cómo usar este documento

- Marca cada caso como **✅ Pasó**, **❌ Falló** o **⚠️ Raro** (funcionó pero algo se vio mal).
- Si algo falla, anota: qué hiciste exactamente, qué esperabas que pasara, qué pasó en realidad, y si puedes, una captura de pantalla.
- No te saltes los casos "sin conexión" — son los más importantes. Para simular sin conexión, activa el modo avión (no solo apagues el WiFi, el modo avión es más confiable).
- Prueba en el orden que aparece, de arriba hacia abajo — varios casos dependen de que el anterior haya dejado datos (ej. una orden completada, un comentario agregado).

---

## 0. Preparación

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 0.1 | Instalación | Instala el APK que te enviaron (Advance o De La Paz, según cuál te toque) | Instala sin error. El ícono y el nombre en el celular deben coincidir con la marca correcta (ícono naranja "ADVANCE APP" o ícono rosado "De La Paz App") |
| 0.2 | Ambos instalados a la vez (si te dan los dos APKs) | Instala el de Advance y el de De La Paz en el mismo celular | Los dos quedan instalados como apps SEPARADAS, cada una con su ícono. No se reemplazan entre sí |
| 0.3 | Primer abrir | Abre la app | Muestra la pantalla de login con el logo y colores de la marca correcta |

---

## 1. Inicio de sesión

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 1.1 | Login correcto | Escribe tu usuario y contraseña correctos, toca "Iniciar sesión" | Entra a la pantalla principal (Home) |
| 1.2 | Campo vacío | Deja usuario o contraseña en blanco, toca "Iniciar sesión" | Muestra mensaje de "Requerido" bajo el campo vacío, no te deja continuar |
| 1.3 | Usuario con formato inválido | Escribe algo sin arroba (ej. "juan") en usuario | Muestra "Email inválido" |
| 1.4 | **Contraseña incorrecta** ⚠️ arreglado hoy | Escribe tu usuario correcto y una contraseña equivocada | Debe decir simplemente **"Usuario o contraseña incorrectos"**. NO debe mostrar texto técnico raro (status, error, desc, msg) |
| 1.5 | Ojito de contraseña | Toca el ícono de ojo en el campo de contraseña | Alterna entre mostrar/ocultar la contraseña |
| 1.6 | **Login sin conexión** | Activa modo avión, intenta iniciar sesión | Debe decir **"Sin conexión — No hay señal. Verifica tu conexión e intenta de nuevo"**, NO un mensaje técnico |

---

## 2. Pantalla principal (Home)

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 2.1 | Datos del encabezado | Mira arriba a la izquierda | Muestra tu foto/iniciales, nombre de la empresa, tu nombre, y una etiqueta verde "Online" |
| 2.2 | Botón de salir (arriba a la derecha) | Toca el ícono de la esquina superior derecha | Cierra la sesión y regresa al login |
| 2.3 | Tarjeta "Entregas para hoy" | Toca la tarjeta | Abre el listado con TODAS tus entregas abiertas del día |
| 2.4 | Tarjeta "Manifesto de entrega" | Toca la tarjeta | Abre la lista de manifiestos de entrega |
| 2.5 | Tarjeta "Recogidas para hoy" | Toca la tarjeta | Abre el listado, pero solo con las recogidas asignadas a ti |
| 2.6 | Tarjeta "Manifesto de recogida" | Toca la tarjeta | Abre la lista de manifiestos, pero solo de recogida |
| 2.7 | Números correctos | Compara los números en las tarjetas contra lo que sabes que tienes asignado | Los números deben cuadrar (o avísame si no cuadran, puede ser un bug aparte) |

---

## 3. Pestaña de Manifiestos (ícono de camión, abajo)

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 3.1 | Acceso directo | Toca el ícono de camión en la barra de abajo (no desde una tarjeta) | Muestra los manifiestos de ENTREGA (no recogida) por default |
| 3.2 | Tocar un manifiesto | Toca cualquier manifiesto de la lista | Abre el listado de órdenes de ESE manifiesto solamente, con el conteo correcto entre paréntesis |

---

## 4. Listado de órdenes

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 4.1 | Info visible por orden | Revisa una tarjeta de orden en la lista | Debe mostrar: nombre del cliente, ciudad/zip, remitente, tipo de servicio, dirección, referencia, cantidad de piezas |
| 4.2 | Abrir una orden | Toca cualquier orden | Abre "Detalles del envío" en la pestaña "Detalles" |
| 4.3 | Botón atrás | Desde el listado, usa la flecha de regreso | Vuelve a la pantalla anterior (Home o Manifiestos, según de donde vinieras) |

---

## 5. Detalles del envío — pestaña Detalles

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 5.1 | Mapa | Entra a una orden, mira la parte de arriba | Muestra un mapa con un marcador en la dirección de entrega (si la dirección se pudo ubicar) |
| 5.2 | **Tocar el marcador del mapa** ⚠️ nuevo hoy | Toca el pin/marcador en el mapa | Debe abrir la ubicación en Google Maps o el navegador del celular (fuera de la app) |
| 5.3 | Info debajo del mapa | Baja con el dedo | Debe mostrar: cliente, remitente, dirección completa, teléfono, fecha, estatus, waybill, factura, tipo de servicio, piezas, y el monto COD si aplica |
| 5.4 | Orden sin dirección ubicable | Busca una orden con dirección incompleta o rara, si la hay | Debe decir algo como "mapa no ubicado" en vez de trabarse o crashear |

---

## 6. Detalles del envío — pestaña Piezas

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 6.1 | Ver piezas | Toca la pestaña "Piezas" | Lista cada pieza con su código de barras y tipo de paquete |
| 6.2 | Orden sin piezas | Si encuentras una orden sin piezas | Debe decir "sin piezas" en vez de quedar en blanco/vacío raro |

---

## 7. Detalles del envío — pestaña Comentarios

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 7.1 | Ver comentarios existentes | Toca la pestaña "Comentarios" | Muestra el historial de comentarios de esa orden |
| 7.2 | Agregar comentario | Escribe algo en el campo de arriba, toca enviar | El comentario aparece arriba de la lista inmediatamente |
| 7.3 | Campo vacío | Intenta enviar sin escribir nada | El botón de enviar debe estar deshabilitado/gris |
| 7.4 | **Comentario con acentos** ⚠️ revisar | Escribe un comentario con tildes, ej. "día de mañana, acción pendiente, José" | Debe guardarse y mostrarse CON los acentos correctos, no con signos de interrogación (?) en su lugar |

---

## 8. Acciones — Completar pedido (entrega exitosa)

Entra a una orden → pestaña "Acciones" → "Completar pedido".

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 8.1 | Nombre vacío | Deja "Recibido por" en blanco, toca "Marcar como entregado" | Avisa que debes indicar quién recibió, no te deja continuar |
| 8.2 | **COD solo si aplica** ⚠️ arreglado hoy | Abre una orden SIN monto COD | La sección de COD (tipo, monto, documento) NO debe aparecer |
| 8.3 | COD cuando sí aplica | Abre una orden CON monto COD | Debe aparecer la sección de COD, mostrando el tipo y monto esperado en el título |
| 8.4 | Agregar COD sin tipo | En una orden con COD, deja el tipo sin seleccionar, toca "Agregar COD" | Avisa que selecciones el tipo |
| 8.5 | Agregar COD con monto inválido | Selecciona tipo, deja el monto en 0 o vacío, toca "Agregar COD" | Avisa que el monto no es válido |
| 8.6 | **Quitar tipo de COD seleccionado por error** ⚠️ nuevo hoy | Selecciona un tipo de COD por accidente, luego toca la ✕ que aparece al lado | El campo vuelve a quedar vacío/sin seleccionar, sin tener que cerrar y reabrir la pantalla |
| 8.7 | Agregar COD correctamente | Selecciona tipo, escribe un monto válido, toca "Agregar COD" | El COD aparece en una lista abajo, con opción de "Quitar" |
| 8.8 | Quitar un COD agregado | Toca "Quitar" en un COD de la lista | Desaparece de la lista |
| 8.9 | Foto obligatoria | Si la orden requiere foto, intenta enviar sin tomar foto | Avisa que tomes la foto, no te deja continuar |
| 8.10 | Tomar foto | Toca "Tomar foto" | Pide permiso de cámara (primera vez), abre la cámara, y al tomar la foto la muestra como vista previa con opción "Volver a tomar" |
| 8.11 | **Firmar** ⚠️ arreglado hoy (varias veces) | Dibuja tu firma en el recuadro con el dedo | La firma debe registrarse y verse en el recuadro — MUY IMPORTANTE: prueba firmar rápido y firmar lento, varias veces seguidas |
| 8.12 | Borrar firma | Después de firmar, toca "Borrar" | El recuadro de firma queda en blanco otra vez |
| 8.13 | Firma obligatoria | Si la orden requiere firma, intenta enviar sin firmar | Avisa que captures la firma |
| 8.14 | GPS visible | Revisa la parte de abajo del formulario | Debe mostrar coordenadas GPS o "sin ubicación" |
| 8.15 | **Botones de abajo** ⚠️ arreglado hoy | Mira los botones "Atrás" y "Marcar como entregado" | Deben verse del mismo tamaño razonable, texto centrado, y NO deben quedar tapados por los botones de navegación del celular |
| 8.16 | **Espacio raro arriba de Firma** ⚠️ arreglado hoy | En una orden SIN COD (formulario corto) | NO debe haber un hueco grande y vacío entre "Tomar foto" y "Firma" |
| 8.17 | Completar entrega con conexión | Llena todo correctamente, envía | Dice "Entrega enviada", y la orden desaparece del listado |
| 8.18 | Barra de navegación del celular | Mientras usas la app en cualquier pantalla | La barra de navegación de Android (los 3 botones/gestos de abajo) debe estar oculta la mayoría del tiempo; deslizando desde el borde inferior debe aparecer temporalmente |

---

## 9. Acciones — Excepción de pedido (no se pudo entregar)

Entra a una orden → pestaña "Acciones" → "Excepción de pedido".

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 9.1 | Motivo obligatorio | Intenta guardar sin seleccionar motivo | Avisa que selecciones un motivo |
| 9.2 | Comentario obligatorio | Selecciona motivo, deja el comentario vacío | Avisa que escribas un comentario |
| 9.3 | Foto (si aplica) | Si la orden la requiere, intenta guardar sin foto | Avisa que tomes la foto |
| 9.4 | Guardar excepción con conexión | Llena todo, guarda | Dice "Excepción enviada", te lleva a la pestaña de Comentarios, y la orden desaparece del listado (una excepción también cierra la orden) |
| 9.5 | Botones de abajo | Igual que en 8.15 | Mismo chequeo: tamaño, texto centrado, no tapados |

---

## 10. Modo SIN CONEXIÓN — la parte más importante

**Para todos estos casos: activa modo avión ANTES de cada paso indicado.**

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 10.1 | **Completar entrega sin señal** ⚠️ arreglado hoy | Con modo avión activado, completa una entrega (foto, firma, todo) | El mensaje debe decir **"Se enviará al reconectar"** — SIN texto técnico feo como "Network error" |
| 10.2 | **La entrega desaparece de la lista al toque** | Después del caso anterior, sin salir de la app, ve al listado de "Entregas para hoy" | La orden que acabas de completar ya NO debe aparecer en la lista, aunque sigas sin señal |
| 10.3 | **No te desloguea por falta de señal** ⚠️ arreglado hoy | Con modo avión activado, deja la app abierta un rato, o ciérrala y ábrela de nuevo (sin quitar el modo avión) | La app debe seguir mostrando tu sesión iniciada (tu nombre, tus entregas guardadas) — NO debe mandarte a la pantalla de login |
| 10.4 | **Nombre visible sin señal** | Con modo avión, abre la app desde cero (ciérrala del todo primero) | Tu nombre e iniciales deben seguir apareciendo en el encabezado, aunque no haya señal |
| 10.5 | **Reconectar y verificar que no "revive" la entrega** ⚠️ arreglado hoy — el más importante | Después de 10.1 y 10.2: quita el modo avión (recupera señal), espera unos 10-15 segundos | La entrega que completaste NO debe volver a aparecer en el listado. Si reaparece, es un bug — anótalo con la hora exacta |
| 10.6 | Excepción sin señal | Repite 10.1 pero con una excepción en vez de una entrega completa | Mismo comportamiento: mensaje de "se enviará al reconectar", sin texto feo, la orden desaparece de la lista |
| 10.7 | Comentario sin señal | Con modo avión, agrega un comentario a una orden | El comentario debe aparecer en la lista igual (aunque esté pendiente de enviar) |
| 10.8 | Varias acciones sin señal seguidas | Con modo avión, completa 2 o 3 entregas distintas seguidas | Todas deben quedar guardadas localmente sin errores raros ni crashes |
| 10.9 | Reconectar con varias pendientes | Quita el modo avión después del caso anterior | Todas las entregas pendientes deben enviarse solas en los siguientes segundos/minutos, sin que tengas que hacer nada |
| 10.10 | Sincronizar manual (ver sección 11) | Ve a Perfil → toca "Sincronizar" mientras sigues offline | Debe fallar de forma silenciosa o avisar que no hay conexión, sin crashear la app |

---

## 11. Pestaña de Perfil (ícono de persona, abajo)

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 11.1 | Datos visibles | Entra a Perfil | Muestra tu usuario, y tu nombre/foto en el encabezado |
| 11.2 | Cambiar idioma a inglés | Toca "English" | Toda la app cambia a inglés inmediatamente (prueba entrar a Home, a una orden) |
| 11.3 | Volver a español | Toca "Español" | Todo vuelve a español |
| 11.4 | Sincronizar (con señal) | Toca el botón "Sincronizar" | Muestra un mensaje con cuántas entregas se sincronizaron |
| 11.5 | Cerrar sesión desde Perfil | Toca "Cerrar sesión" | Vuelve a la pantalla de login |

---

## 12. Cosas generales / navegación

| # | Caso | Pasos | Esperado |
|---|------|-------|----------|
| 12.1 | Cerrar y reabrir la app (con señal) | Cierra la app completamente, ábrela de nuevo | Debe entrar directo a Home sin pedirte login de nuevo (si la sesión sigue vigente) |
| 12.2 | Dejar el celular inactivo un rato | Deja el celular bloqueado 10-15 minutos, desbloquéalo y abre la app | Debe seguir funcionando normal, sin deslogueo inesperado |
| 12.3 | Rotar el celular | Gira el celular a horizontal en cualquier pantalla | La app está diseñada solo para vertical — no debería rotar ni verse rara |
| 12.4 | Permisos denegados — cámara | La primera vez que pida cámara, dile que NO | Debe avisar "activa el permiso de cámara" en vez de crashear |
| 12.5 | Permisos denegados — ubicación | La primera vez que pida ubicación, dile que NO | La app debe seguir funcionando (la entrega se puede completar sin GPS, solo no se guarda ubicación) |

---

## Resumen de resultados (llenar al terminar)

| Sección | Total casos | Pasaron | Fallaron | Raros |
|---|---|---|---|---|
| 0. Preparación | 3 | | | |
| 1. Login | 6 | | | |
| 2. Home | 7 | | | |
| 3. Manifiestos | 2 | | | |
| 4. Listado de órdenes | 3 | | | |
| 5. Detalles | 4 | | | |
| 6. Piezas | 2 | | | |
| 7. Comentarios | 4 | | | |
| 8. Completar pedido | 18 | | | |
| 9. Excepción | 5 | | | |
| 10. Sin conexión | 10 | | | |
| 11. Perfil | 5 | | | |
| 12. General | 5 | | | |

## Bugs encontrados (detalle)

| # de caso | Qué pasó | Cómo reproducirlo | Captura (si tienes) |
|---|---|---|---|
| | | | |
