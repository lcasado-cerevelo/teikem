# 27 — App del cliente (Teikem Clientes)
<!-- kemi:portal -->

Lote 102 (portal de clientes, 2026-10-17). La app **aparte** del cliente: **web** (instalable en el escritorio o la pantalla de inicio) y **móvil** (Android e iOS), hecha con Expo. El cliente entra con su correo; ve **sus** envíos y **sus** avisos. Las reglas de acceso y de aislamiento son las del [capítulo 26](26-portal-acceso.md); las pantallas con sus capturas, en [el manual de pantallas L102](frontend/l102-app-cliente.md).

## 1. Qué puede hacer el cliente
| Pantalla | Qué hace |
|---|---|
| **Entrar** | Correo y contraseña; la casilla «No confiar en esta computadora» (se recuerda); «Poner en mi escritorio» (web) |
| **Confirmar la computadora** | La primera vez desde una computadora nueva: le llega un correo con un enlace (24 h, un solo uso); lo abre (también desde el teléfono) y vuelve a entrar |
| **Segundo paso (MFA)** | La primera vez inscribe su app de autenticación (clave o enlace, código y **códigos de recuperación**); después escribe el código (se recuerda 30 días en la computadora de confianza) |
| **Documento legal** | Política de privacidad y términos de uso: el botón «Acepto» se habilita solo al llegar al final del texto |
| **Inicio** | Saludo, el cliente actual, cuántas envíos tiene en total y por estatus, y las 5 más recientes |
| **Envíos** | Sus envíos con búsqueda por conduce, envío, factura o consignatario, **filtro de fecha** con rangos (Hoy, Ayer, Últimos 7 días, Esta semana, Semana pasada, Últimos 30 días, Este mes, Mes pasado, Este año, Personalizado con Desde/Hasta; Lote 213) y «Cargar más» (20 por página); la tabla dice **«N de M»**; cada uno con su **barra de progreso** (capítulo 28). **Nuevo envío**: capítulo 30 (en la app instalada, la tableta y el celular solo la entrada detallada) |
| **Ficha del envío** | La **barra de progreso** con la lista de etapas, conduce, estatus, servicio, entrega, paquetes, COD y el **seguimiento** (cada cambio de estatus con su fecha) |
| **Mensajes** | Su conversación con el equipo de servicio al cliente (capítulo 29) y las de sus consignatarios (capítulo 35). En la web ancha (Lote 213) es una bandeja de dos columnas: lista de conversaciones a la izquierda y la abierta a la derecha |
| **Avisos** | Los resúmenes que su compañía configuró mandarle por la bandeja del portal (recogido, salió a entrega, entregado, COD procesado, factura emitida) |
| **Ajustes** | La cuenta; **cambiar de cliente** (si tiene acceso a varios); idioma (es/en); **Abrir con huella** (solo teléfono); «No confiar en esta computadora»; **mis computadoras de confianza** (con «Quitar»); cerrar sesión |

## 1b. Idioma y tema (claro y oscuro)
Decidido con Luis (2026-10-17): **toda pantalla tiene los dos temas y los dos idiomas, con su opción para cambiarlos**.
- **Idioma:** español por omisión; **Español / English** en el login, en la página pública «¿Cómo va mi envío?», en el documento legal y en Ajustes. Cambia al instante, sin recargar, y se recuerda.
- **Tema:** **Automático** (por omisión: sigue al teléfono o al navegador), **Claro** u **Oscuro**, en los mismos lugares. Cambia al instante y se recuerda; Claro y Oscuro mandan sobre el sistema. Todos los colores (incluidas las etiquetas, avisos y la barra de progreso) salen de una paleta por tema, con **contraste de accesibilidad AA** vigilado por pruebas.
- Los textos que viene del servidor (estatus de la compañía, el documento legal, avisos) salen en el idioma elegido.

## 2. Abrir con huella (teléfono)
- En **Ajustes → Seguridad → Abrir con huella** (solo aparece si el teléfono tiene lector **y** una huella registrada; si no, la pantalla explica por qué). Al activarla se pide identificarse una vez.
- Con la huella activa, la app **se bloquea** al abrirla (con una sesión guardada) y al volver de segundo plano pasados **2 minutos**: pide la huella (o el PIN o patrón del teléfono como respaldo) en lugar de la contraseña.
- **Tras 5 fallos seguidos**, o con «Usar mi contraseña», se cierra la sesión del aparato y vuelve el login normal.
- La huella **no reemplaza** el primer ingreso, la confirmación de una computadora nueva ni el código MFA: solo abre la sesión que ya estaba guardada en ese teléfono. Si la sesión venció o se revocó, hay que entrar con la contraseña.
- En la web no existe la huella (un próximo paso sería usar passkeys).

## 3. «No confiar en esta computadora»
La casilla del login se **recuerda** (se guarda en el aparato o el navegador): al volver sale marcada. Marcada: entra **una sola vez**, esa computadora **no se recuerda** (la próxima vez pide otra vez el correo de confirmación) y el código MFA se pide **siempre**. Se puede cambiar también en Ajustes. Úsela en equipos compartidos o públicos.

## 4. Instalar la web como app
El navegador (Chrome, Edge, Android) ofrece instalar el portal: en el login aparece **«Poner en mi escritorio»** y, al pulsarlo, el ícono queda en el escritorio o la pantalla de inicio y se abre como una app. En **iPhone y iPad** el navegador no deja hacerlo con un botón: el login muestra la instrucción «Compartir → Añadir a pantalla de inicio». Si ya está instalada, no se muestra nada.

## 5. Mensajes y errores
Los mensajes de los servicios son los del servidor (capítulos 24 a 26): `Credenciales inválidas.`, `El usuario no tiene acceso activo al portal.`, `El enlace no es válido o venció. Vuelva a intentar entrar para recibir uno nuevo.`, etc. Propios de la app:
| Situación | Texto |
|---|---|
| Sin conexión con el servidor | «No hay conexión con el servidor. Revise su internet e intente de nuevo.» |
| El servidor pide el reto anti-robot | «Confirme que no es un robot para continuar.» (en la web aparece el widget de Cloudflare; **en el teléfono todavía no**: pide entrar desde el navegador) |
| Falla la huella | «No se pudo verificar. Le quedan N intentos antes de pedirle la contraseña.» |
| Envío de otro cliente o inexistente | El mismo mensaje de «no encontrada» del servidor |

## Casos frecuentes
- **«Me pide confirmar la computadora otra vez»:** es una computadora o navegador nuevo, pasaron 30 días sin usarla, cambió su contraseña, marcó «No confiar» o la quitó en Ajustes.
- **«No me aparece Abrir con huella»:** el teléfono no tiene lector o no tiene huellas registradas; regístrelas en los ajustes del teléfono.
- **«No veo una orden que sé que existe»:** la app solo muestra los envíos del cliente con el que entró; si tiene acceso a varios, cámbielo en Ajustes.
- **«Los avisos están vacíos»:** la compañía decide, cliente por cliente, qué avisos manda y por qué canal (capítulo 23); si el canal «bandeja del portal» está apagado no llega nada aquí.

**Información legal (2026-10-21):** al pie de Ajustes están los enlaces **Privacidad** y **Términos de uso** (lectura del documento vigente, ruta `/legal-view`), los derechos reservados y «Powered by Teikem». Ver capítulo 24.

> Capturas del portal del cliente: ver [capturas-apps.md](frontend/capturas-apps.md).

## 6. Pie de página y totales (Lote 213, 2026-10-23)
- En la **web de escritorio** el pie (Privacidad · Términos de uso, © derechos reservados, Powered by Teikem) es un **pie de página de verdad**: pegado al borde de abajo cuando el contenido no llena la ventana y al final del contenido cuando es más largo. En el celular va al final del contenido.
- **Toda tabla dice cuántas filas tiene en total** («N de M»: las que se ven de las que cumplen el filtro), aunque se traiga por partes con «Cargar más».
- **Intentos fallidos (2026-10-24):** un envío que salió a la calle, no se entregó y volvió al almacén lleva bajo su estatus una píldora ámbar «1 intento fallido» / «2 intentos fallidos» (Mis envíos, Inicio, ficha y barra de progreso), para distinguirlo de uno solo retrasado. Ver el capítulo de pantallas `frontend/l213-portal-cliente.md`.
- **Vista por aparato (2026-10-24):** app instalada, tabletas y celulares ven la forma móvil; la web en computadora, la ancha (capítulo 30). **Exportar es igual en todas partes: Excel, CSV y PDF**, también en la app instalada (se guarda el archivo y se abre «Compartir»).
- **Toda tabla se exporta** («Exportar · Excel · CSV · PDF» al pie de la tabla): baja el archivo con **todas las filas que cumplen el filtro** (búsqueda, fechas, estatus), no solo las que se ven, hasta 5000. Lo tienen Mis envíos, Mis facturas, Mi almacén (existencias, productos, movimientos, valor, llegadas y salidas) y Mis consignatarios; los Informes tienen su propio «Exportar a Excel (CSV)».
  - **Excel (.xlsx)** y **PDF** llevan arriba, como los de la web de la compañía: el **logo**, el nombre de la compañía, el título de la tabla, «Generado el …» y la oración de filtros («Filtros: Búsqueda «farm» · Fechas del … al …» o «Sin filtros»). En Excel las fechas son fechas, los números son números (se suman) y los encabezados traen filtro automático; el PDF es A4 (horizontal con más de 5 columnas) con los números a la derecha y la página abajo.
  - **El logo del PDF y del Excel es el de la compañía** que sirve al cliente (el normal, que se lee sobre blanco; si solo subió el invertido, va sobre una placa del color de su barra); **si la compañía no subió ninguno, sale el de Teikem**.
  - **Los colores del PDF son los de la compañía** (Ajustes → Marca): el encabezado de la tabla, el título y la línea bajo el encabezado salen en el color de la barra y del acento que la compañía definió; con la marca de siempre, el azul marino y el naranja de siempre. No se usa ningún otro color.
  - **CSV** lleva solo la tabla (UTF-8, Excel lo abre con acentos). **En el celular solo hay CSV** y se comparte el archivo en lugar de bajarse.

## 7. Contraseña: ojito, mayúsculas, mensajes claros y «¿Olvidó su contraseña?» (Lote 219)
La pantalla de entrada del portal lleva arriba el **logo de su compañía** y, en cada campo de contraseña (entrar, elegir la contraseña de la invitación, cambiarla en Mi perfil), un **ojito** dentro del campo para ver lo que escribe. En la web avisa «Las mayúsculas están activadas.» al teclear con ellas puestas. Si se equivoca, el mensaje le dice qué revisar. **«¿Olvidó su contraseña?»** (debajo de la contraseña) pide su correo y manda un enlace de un solo uso (60 minutos) que abre `/reset-password` en el portal; si usted tiene la verificación en dos pasos, la pantalla le pide también su código. Detalle y mensajes exactos en el capítulo [25 §7](25-acceso-seguro.md).

