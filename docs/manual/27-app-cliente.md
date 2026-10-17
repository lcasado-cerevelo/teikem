# 27 — App del cliente (Teikem Clientes)

Lote 102 (portal de clientes, 2026-10-17). La app **aparte** del cliente: **web** (instalable en el escritorio o la pantalla de inicio) y **móvil** (Android e iOS), hecha con Expo. El cliente entra con su correo; ve **sus** órdenes y **sus** avisos. Las reglas de acceso y de aislamiento son las del [capítulo 26](26-portal-acceso.md); las pantallas con sus capturas, en [el manual de pantallas L102](frontend/l102-app-cliente.md).

## 1. Qué puede hacer el cliente
| Pantalla | Qué hace |
|---|---|
| **Entrar** | Correo y contraseña; la casilla «No confiar en esta computadora» (se recuerda); «Poner en mi escritorio» (web) |
| **Confirmar la computadora** | La primera vez desde una computadora nueva: le llega un correo con un enlace (24 h, un solo uso); lo abre (también desde el teléfono) y vuelve a entrar |
| **Segundo paso (MFA)** | La primera vez inscribe su app de autenticación (clave o enlace, código y **códigos de recuperación**); después escribe el código (se recuerda 30 días en la computadora de confianza) |
| **Documento legal** | Política de privacidad y términos de uso: el botón «Acepto» se habilita solo al llegar al final del texto |
| **Inicio** | Saludo, el cliente actual, cuántas órdenes tiene en total y por estatus, y las 5 más recientes |
| **Órdenes** | Sus órdenes con búsqueda por conduce, orden, factura o consignatario y «Cargar más» (20 por página); cada una con su **barra de progreso** (capítulo 28) |
| **Ficha de la orden** | La **barra de progreso** con la lista de etapas, conduce, estatus, servicio, entrega, paquetes, COD y el **seguimiento** (cada cambio de estatus con su fecha) |
| **Avisos** | Los resúmenes que su compañía configuró mandarle por la bandeja del portal (recogido, salió a entrega, entregado, COD procesado, factura emitida) |
| **Ajustes** | La cuenta; **cambiar de cliente** (si tiene acceso a varios); idioma (es/en); **Abrir con huella** (solo teléfono); «No confiar en esta computadora»; **mis computadoras de confianza** (con «Quitar»); cerrar sesión |

## 1b. Idioma y tema (claro y oscuro)
Decidido con Luis (2026-10-17): **toda pantalla tiene los dos temas y los dos idiomas, con su opción para cambiarlos**.
- **Idioma:** español por omisión; **Español / English** en el login, en la página pública «¿Cómo va mi orden?», en el documento legal y en Ajustes. Cambia al instante, sin recargar, y se recuerda.
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
| Orden de otro cliente o inexistente | El mismo mensaje de «no encontrada» del servidor |

## Casos frecuentes
- **«Me pide confirmar la computadora otra vez»:** es una computadora o navegador nuevo, pasaron 30 días sin usarla, cambió su contraseña, marcó «No confiar» o la quitó en Ajustes.
- **«No me aparece Abrir con huella»:** el teléfono no tiene lector o no tiene huellas registradas; regístrelas en los ajustes del teléfono.
- **«No veo una orden que sé que existe»:** la app solo muestra las órdenes del cliente con el que entró; si tiene acceso a varios, cámbielo en Ajustes.
- **«Los avisos están vacíos»:** la compañía decide, cliente por cliente, qué avisos manda y por qué canal (capítulo 23); si el canal «bandeja del portal» está apagado no llega nada aquí.
