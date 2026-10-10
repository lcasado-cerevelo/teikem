# Preguntas frecuentes (FAQ)

Cada entrada cita el mensaje exacto que puede ver el usuario (`title` del error) y explica la causa probable y
qué hacer. Los mensajes están verificados contra el código del Lote 1 (`src/Teikem.Infrastructure/Services`,
`src/Teikem.Infrastructure/Abstractions/Exceptions.cs`, `src/Teikem.Api/Auth/Policies.cs`).

## Lote 1 — Plataforma y seguridad

### Acceso y sesión

**¿Qué significa "Credenciales inválidas."?**
El correo no existe (o el usuario está inactivo), la cuenta está bloqueada temporalmente por intentos
fallidos, o la contraseña es incorrecta. El mensaje es el mismo en los tres casos a propósito, para no revelar
cuál de los dos datos falló. Espera unos minutos si sospechas que tu cuenta se bloqueó (5 intentos fallidos
bloquean 15 minutos) e inténtalo de nuevo; si sigue fallando, pide a un administrador que revise tu cuenta.

**¿Qué significa "Los usuarios de portal se autentican en el portal de clientes."?**
Tu cuenta es de tipo portal (cliente final), no de uso interno. Este acceso (`/api/v1/auth/login`) es solo
para personal de la compañía; el portal de clientes llega en un módulo posterior.

**¿Qué significa "No pertenece a esa compañía."?**
Pediste iniciar sesión (o cambiar) a un `tenantId` en el que no tienes membresía activa. Revisa con qué
compañía tienes cuenta, o pide que te agreguen como usuario ahí.

**Pertenezco a varias compañías: ¿por qué no me pregunta a cuál entrar?**
Desde 2026-09-30 el sistema entra directo a tu compañía predeterminada (o a la primera por nombre). Cambia de compañía con el
selector de la cabecera, entre el reloj y el tema; solo muestra las compañías a las que tienes acceso.

**¿Por qué me pide verificar el correo, cambiar la contraseña y configurar la verificación en dos pasos al entrar?**
Es el primer ingreso obligatorio (desde 2026-09-30, para todos). La contraseña la dio un administrador: hay que confirmar que el
correo es suyo, poner una contraseña que solo usted conozca y activar el segundo factor. Se hace una sola vez.

**¿Qué significa "El código no es válido o venció." (400) en el primer ingreso?**
El código de 6 dígitos del correo está mal escrito o pasaron más de unos minutos. Toque "Enviar otro código" y use el más
reciente. Varios intentos fallidos bloquean la cuenta 15 minutos, como la contraseña.

**¿Qué significa "No se pudo enviar el correo. Intente de nuevo en unos minutos." (409)?**
El proveedor de correo (Brevo) no aceptó el envío o no está configurado (`Brevo__ApiKey`, `Brevo__FromEmail`). Si persiste,
avise a soporte.

**¿Qué significa "La contraseña nueva debe ser distinta de la que le dieron." (400)?**
Escribió la misma contraseña temporal que le dio el administrador. Elija una nueva.

**¿Qué significa "Verifique primero su correo." (409) o "Complete primero la verificación del correo y el cambio de contraseña." (403)?**
Los pasos del primer ingreso van en orden: correo → contraseña → verificación en dos pasos. Vuelva a entrar y la pantalla lo
lleva al paso que falta.

**¿Qué significa "Complete primero su primer ingreso en la web (correo, contraseña y verificación en dos pasos)." (403) en la app del almacén?**
Ese usuario todavía no terminó su primer ingreso. Que entre una vez a la web, complete los tres pasos y luego use su PIN.

**Perdí el teléfono de la app de autenticación, ¿cómo entro?**
Entre con uno de sus códigos de recuperación en lugar del código de 6 dígitos y configure el MFA en el teléfono nuevo desde Mi
cuenta. Si también perdió los códigos, pida a un administrador de su compañía que le reinicie el MFA (Sistema → Usuarios);
le llegará un correo de aviso y al entrar lo configura de nuevo.

**¿Qué significa "Active primero la verificación en dos pasos." (409)?**
Pidió códigos de recuperación nuevos sin tener el MFA activo. Actívelo primero en Mi cuenta.

**Recibí un correo de que mi verificación en dos pasos fue reiniciada y no lo pedí.**
Avise de inmediato a su administrador y cambie su contraseña: alguien con permiso de administrador reinició su segundo factor.

**¿Qué significa "El usuario no tiene ninguna compañía activa."?**
Tu usuario existe pero no tiene ninguna membresía activa en ninguna compañía. Pide a un administrador que te
dé de alta en su tenant o reactive tu membresía.

**¿Qué significa "La compañía está inactiva."?**
La compañía a la que perteneces fue desactivada en la plataforma. Contacta a soporte de Teikem.

**¿Qué significa "Código MFA inválido."?**
El código de la app autenticadora venció (dura 30 segundos, revisa la hora del teléfono) o el código de
recuperación ya se usó o está mal escrito. Prueba con el código actual de la app o con otro código de
recuperación no usado.

**Perdí mi teléfono con la app de MFA, ¿qué hago?**
Usa un código de recuperación de un solo uso (se te mostraron al activar el MFA) en el paso de verificación.
Si también los perdiste, contacta a un administrador de tu compañía (permiso `admin.users`); el Lote 1 no
tiene un flujo de autoservicio para restablecer el MFA sin esos códigos.

**¿Qué significa "Refresh token inválido."?**
El token para renovar tu sesión no existe, ya se usó (los refresh tokens son de un solo uso y se rotan en
cada llamada) o fue revocado (por ejemplo, alguien cerró esa sesión). Vuelve a iniciar sesión.

**¿Qué significa "Sesión expirada."?**
Tu sesión superó los días configurados por tu compañía (`SessionDays`). Vuelve a iniciar sesión.

**¿Qué significa "La membresía ya no está activa."?**
Al renovar tu sesión, el sistema detectó que ya no tienes membresía activa en ese tenant (te suspendieron o
te quitaron). Contacta a tu administrador.

**¿Qué significa "Esta acción requiere reautenticación reciente (AAL2)."?**
Estás intentando una acción sensible (cambiar permisos de un rol, encender/apagar un módulo, dar de alta una
compañía, etc.) y tu sesión no tiene una reautenticación reciente. Llama a `POST /api/v1/auth/reauth` con tu
contraseña (y tu código MFA si tienes) y reintenta la acción con el token nuevo que te devuelve.

**¿Qué significa "La sesión actual no se revoca desde la lista; use logout."?**
Intentaste cerrar, desde la lista de sesiones, la misma sesión con la que estás conectado ahora. Usa
`POST /api/v1/auth/logout` en vez de la lista de sesiones.

**¿Qué significa "Contraseña incorrecta."?**
Al cambiar tu contraseña o reautenticarte, la contraseña actual que escribiste no coincide. Verifícala.

**¿Qué significa "Esta contraseña aparece en brechas conocidas; elija otra."?**
La contraseña nueva coincide con una filtrada públicamente. Elige otra distinta. (En este lote esta
verificación no está activa realmente porque el entorno no tiene salida a Internet, pero el mensaje existe en
el código para cuando se conecte.)

**¿Qué significa "TOTP ya está enrolado y confirmado; desactívelo primero (requiere AAL2)."?**
Ya tienes el segundo factor activo; no puedes volver a enrolar sin desactivar el actual primero (y para
desactivarlo necesitas AAL2 reciente).

**¿Qué significa "Primero inicie el enrolamiento TOTP."?**
Intentaste confirmar un código sin haber llamado antes a enrolar (`POST /auth/mfa/totp/enroll`).

**¿Qué significa "Código inválido."?**
El código que escribiste al confirmar el TOTP no coincide con el que genera tu app en ese momento. Revisa la
hora del dispositivo y vuelve a intentar.

### Permisos y módulos

**¿Qué significa "Falta el permiso '<código>'."?**
Tu usuario (ni por rol ni por permiso extra) no tiene el permiso indicado. Pide a un administrador de tu
compañía (`admin.users`/`admin.roles`) que te lo asigne, si corresponde a tu función. También aparece en las rutas
transversales (contactos, historial de estatus y valores de campos personalizados) cuando el registro pertenece a una
entidad con módulo propio: leer exige el permiso de lectura de esa entidad (`clients.read`, `locations.read`, `contracts.read`,
`portalusers.manage`) y escribir campos personalizados exige el de edición (`clients.update`, `locations.update`,
`contracts.update`). Cada rechazo queda como PERMISSION_DENIED en la bitácora de seguridad.

**No tengo permiso para algo que debería poder hacer.**
Revisa `GET /api/v1/me`: el arreglo `permissions` lista tus permisos efectivos (los de tu rol más los que te
dieron extra). Si falta el permiso, pide que te lo agreguen por rol (afecta a todos con ese rol) o como
permiso extra puntual a tu usuario.

**No veo un módulo/pantalla.**
Dos causas posibles: (1) el módulo del que depende esa pantalla está apagado para tu compañía —revísalo en
`GET /api/v1/modules` o en `enabledModules` de `/me`—; (2) no tienes el permiso asociado a esa pantalla.

**¿Qué significa "El módulo '<clave>' no está habilitado para esta compañía."?**
Intentaste usar un endpoint de un módulo que tu compañía tiene apagado. Pide a un administrador
(`admin.tenant`) que lo encienda, si corresponde a lo que tu compañía necesita.

**¿Qué significa "'<módulo>' depende de '<dependencia>', que debe encenderse primero."?**
Ese módulo necesita otro encendido antes (por ejemplo, Facturación de alquiler necesita Equipos en alquiler).
Enciende primero la dependencia.

**¿Qué significa "'<módulo>' es un módulo núcleo y no se puede apagar."?**
Los módulos núcleo (última milla y sistema) son obligatorios en toda compañía; no se pueden apagar.

### Usuarios y roles

**¿Qué significa "Ya existe el rol '<nombre>'."? / "Ya existe la compañía '<nombre>'."? / "Ya existe el valor '<code>' en <entity>."? / "Ya existe una vista '<nombre>' para <fuente>."? / "Ya existe el indicador/gráfico '<nombre>'."? / "Ya existe el campo '<clave>' en <entidad>."?**
En todos estos casos intentaste crear algo con un nombre/código/clave que ya existe en tu compañía (o, para
compañías, en la plataforma) o en ese contexto. Usa otro nombre o edita el existente en vez de crear uno
nuevo.

**¿Qué significa "El rol tiene usuarios asignados; reasígnelos antes de eliminarlo."?**
No se puede borrar un rol mientras alguien lo tenga asignado. Cambia primero el rol de esos usuarios.

**¿Qué significa "Roles desconocidos: <lista>." / "Permisos desconocidos: <lista>."?**
Uno o más de los nombres/códigos que enviaste no existen en el catálogo. Revisa `GET /api/v1/roles` o
`GET /api/v1/permissions` para ver los válidos.

**¿Qué significa "El usuario ya pertenece a esta compañía."?**
Intentaste dar de alta a alguien que ya tiene membresía en tu tenant. Búscalo en `GET /api/v1/users` en vez de
crearlo de nuevo.

**¿Qué significa "Ese correo ya pertenece a un usuario de portal; un usuario de portal no puede ser a la vez usuario interno."?**
Intentaste dar de alta como usuario interno (`POST /api/v1/users`) un correo que ya es usuario de portal de un cliente (de tu
compañía o de otra). Un correo es interno o de portal, nunca ambos: usa otro correo para la persona interna. Esa cuenta de
portal tampoco se puede editar desde `/users` (responde 404): se administra desde el expediente de su cliente.

**¿Qué significa "No puede desactivarse a sí mismo."? / "No puede cambiar su propia membresía."?**
Por seguridad, no puedes quitarte a ti mismo el acceso desde estas pantallas. Pide a otro administrador que lo
haga si de verdad es necesario.

**¿Qué significa "Los usuarios de portal se administran desde el expediente del cliente (Lote 2)."?**
Estás intentando crear un usuario de tipo portal desde la pantalla de usuarios internos. Esa gestión llega con
el módulo de clientes (lote 2); todavía no existe esa pantalla.

### Catálogos y listas

**¿Qué significa "Dominio de catálogo '<clave>' no encontrado." / "Valor <entidad> '<código>' no encontrado."?**
El dominio o el código que pediste no existe. Revisa `GET /api/v1/catalogs/domains` o
`GET /api/v1/catalogs/{entity}` para ver los válidos.

**¿Qué significa "Un valor de sistema no se desactiva globalmente; use el override del tenant (IsEnabled=false)."?**
Los valores que vienen de fábrica (de sistema) no se apagan para toda la plataforma desde tu compañía; usa el
override (`PUT .../override` con `isEnabled:false`) para apagarlo solo en tu tenant.

**¿Qué significa "Las listas de sistema no se eliminan."? / "La lista está en uso por un campo personalizado; desactívela en vez de eliminarla."?**
No puedes borrar catálogos que vienen de fábrica, ni una lista propia que un campo personalizado está usando.
En este último caso, desactiva la lista en vez de borrarla.

**¿Qué significa "Los catálogos globales solo los edita el administrador de plataforma; use overrides o cree una lista propia."?**
No puedes editar directamente un catálogo compartido por toda la plataforma. Usa un override (cambia solo tu
tenant) o crea tu propia lista.

**¿Qué significa "El código de país debe ser ISO 3166-1 alfa-2 (2 letras)."? (HTTP 400)**
Los países se identifican con su código ISO de dos letras (`PR`, `US`, `DO`…). Lo verás al agregar un valor al catálogo
global `Country` (`POST /api/v1/catalogs/Country`, campo `code`), al crear o editar una localización (`country`), al capturar
una orden con consignatario nuevo (`newConsignee.country`) o en una fila del importador de órdenes (`country`). Usa el código
de dos letras; las paradas de la orden guardan el país con ese formato.

### Estatus

**¿Qué significa "Pipeline inválido: ..."?**
El cambio que intentaste (por ejemplo, deshabilitar una etapa) dejaría el pipeline sin una etapa inicial
válida. El sistema rechazó el cambio antes de guardarlo; revisa qué etapas tienes habilitadas.

**¿Qué significa "El estatus actual no permite la acción '<acción>'."?**
Tu compañía configuró que, en el estatus en que está ese registro, esa acción no se puede hacer. Revisa la
matriz de capacidades (`GET /api/v1/status/capabilities/{entityType}`).

**¿Qué significa "'<código>' es una etapa del pipeline, no un lateral/terminal."?**
Intentaste definir una entrada lateral hacia una etapa que en realidad es parte del flujo normal (pipeline),
no una salida lateral o terminal (como cancelado).

**¿Qué significa "El pipeline '<dominio>' no tiene etapa inicial habilitada."?**
Tu compañía deshabilitó todas las etapas iniciales posibles; no se puede dar de alta ningún registro nuevo
hasta habilitar al menos una.

**¿Qué significa "Un registro nuevo debe empezar en una etapa inicial; '<código>' no lo es."?**
Se intentó crear un registro directamente en una etapa que no es de arranque.

**¿Qué significa "El registro ya está en ese estatus."?**
Se intentó "cambiar" un registro al mismo estatus en el que ya está.

**¿Qué significa "'<código>' es terminal: no admite más transiciones."?**
Ese registro llegó a un estatus final (por ejemplo, cerrado o cancelado) y no puede volver a cambiar de
estatus.

**¿Qué significa "Salto ilegal: de '<origen>' solo se puede avanzar a '<siguiente>'."? / "No se permite pasar a '<destino>' desde '<origen>' según el modelo de esta compañía."? / "Desde el lateral '<origen>' solo se puede regresar a '<...>' o avanzar a '<...>'."?**
El cambio de estatus que se intentó no respeta el orden del pipeline ni las reglas laterales configuradas
para tu compañía. Revisa el pipeline y las entradas laterales configuradas.

### Contactos

**¿Qué significa "No hay resolver de pertenencia para '<entidad>' (el módulo dueño aún no está construido)."?**
Intentaste agregar un contacto a un tipo de entidad que todavía no existe en la plataforma (llega en un lote
posterior). No es un error tuyo; espera a que ese módulo esté disponible.

**¿Qué significa "Correo inválido."? / "Teléfono inválido."?**
El valor no tiene el formato esperado para ese tipo de contacto. Revisa que el correo tenga `@` y dominio, y
que el teléfono use solo dígitos, espacios, guiones o paréntesis.

### Campos personalizados

**¿Qué significa "Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra."?**
La clave técnica del campo (`fieldKey`) no cumple el formato. Usa solo minúsculas, números y guion bajo,
empezando con una letra, entre 2 y 60 caracteres (ej. `cost_center`).

**¿Qué significa "No se puede cambiar el tipo de un campo que ya tiene valores guardados."?**
Ya hay registros con datos en ese campo; cambiar su tipo (por ejemplo de texto a número) rompería esos datos.
Crea un campo nuevo si necesitas otro tipo.

**¿Qué significa "LOOKUP_REF requiere el dominio de catálogo referenciado."? / "Un campo de selección necesita opciones o una lista del catálogo (refEntity)."?**
Al crear un campo de tipo lista/selección, falta indicar de dónde vienen las opciones: o defines `options`
directamente, o apuntas a una lista existente con `refEntity`.

**¿Qué significa "Se esperaba un número." / "Se esperaba sí/no." / "Se esperaba una fecha (yyyy-MM-dd)." / "Se esperaba una fecha y hora ISO." / "Se esperaba una lista de valores."?**
El valor que enviaste para ese campo no tiene el tipo de dato que el campo espera. Revisa el `dataType` de la
definición (`GET /api/v1/custom-fields/definitions/{id}`) y el formato exacto.

### Análisis (vistas, indicadores, gráficos)

**¿Qué significa "Modo de rango inválido."? / "El rango personalizado necesita Desde y Hasta."? / "Desde no puede ser mayor que Hasta."?**
El rango de fechas que pediste no es válido: el modo no existe, o pediste un rango personalizado sin las dos
fechas, o la fecha de inicio es posterior a la de fin.

**¿Qué significa "Una vista necesita al menos una columna o una agrupación."?**
No se puede guardar una vista completamente vacía; agrega al menos una columna o una agrupación.

**¿Qué significa "La fuente <fuente> no se combina con '<secundaria>'."? / "La fuente <fuente> no tiene el campo '<campo>'."?**
Pediste combinar con una fuente relacionada, o usar un campo, que esa fuente no tiene definidos. Revisa
`GET /api/v1/analytics/data-sources` para ver las relaciones y campos válidos de cada fuente.

**¿Qué significa "Los elementos por default de la plataforma no se editan ni se eliminan."?**
Esa vista/indicador/gráfico viene de fábrica (`IsSystem`); no se puede modificar ni borrar. Crea uno propio
si necesitas una variante.

**¿Qué significa "Solo el dueño puede editar o eliminar este elemento."?**
No eres el dueño de esa vista/indicador/gráfico ni tienes un permiso de edición compartido sobre él.

**¿Qué significa "Cambiar el rango por defecto de un indicador/gráfico ajeno o de sistema requiere 'analytics.dates'."?**
Puedes cambiar libremente el rango de fecha de lo que es tuyo; para cambiar el rango por defecto de algo ajeno
o de sistema (afecta a todos los que lo ven) necesitas el permiso `analytics.dates`. Si solo quieres cambiar
tu propia vista de ese indicador/gráfico, usa `my-date-range` en vez de `default-date-range`.

**¿Qué significa "El campo de agrupación es obligatorio."? / "Tipo de gráfico: BAR, DONUT o LINE."?**
Al crear un gráfico faltó el campo por el que se agrupa, o el tipo indicado no es uno de los tres soportados.

### Auditoría

**¿Un intento sin permiso queda registrado?**
Sí. Cada vez que a alguien le falta un permiso para una acción protegida, queda un evento `PERMISSION_DENIED`
en seguridad, visible (con `admin.audit`) en `GET /api/v1/audit/security-events` o
`GET /api/v1/audit/activity`.

**¿Cómo rastreo qué produjo un cambio?**
Cada fila de `GET /api/v1/audit/changes` (o de "actividad") trae un `correlationId`, el mismo valor que la
cabecera `X-Correlation-Id` de la petición que originó el cambio.

### Configuración de la compañía

**¿Qué significa "Código de idioma de 2 letras."? / "Máscara inválida (1-127)."? / "Debe ser ≥ 1."? / "Entre 5 y 240 minutos."? / "Entre 1 y 365 días."?**
El valor que enviaste para ese ajuste está fuera del rango permitido. Revisa el límite indicado en el mensaje.

**¿Qué significa "BrandingJson no es JSON válido."? / "BrandingJson demasiado grande (los logos van a blob storage)."?**
El bloque de marca (branding) que enviaste no es JSON válido, o pesa más de 200.000 caracteres. No subas
imágenes en base64 dentro de este campo; los logos van a almacenamiento de archivos aparte.

#### Región y formatos (2026-10)

Mensajes verificados contra `src/Teikem.Domain/Tenancy/TenantFormatRules.cs` (todos son 400 con el campo en `errors`; si hay
error no se guarda nada del pedido). Detalle en el capítulo 01, sección 11.1.

**¿Qué significa "El separador de miles y el decimal no pueden ser el mismo" (400)?**
El separador de miles y el decimal quedarían iguales (por ejemplo, punto y punto) y los números serían ambiguos. Se compara el
resultado final: si solo cambias uno, choca con el otro valor guardado. Para 1.234,50 manda los dos en el mismo pedido:
`{ "thousandsSeparator": ".", "decimalSeparator": "," }`. En la pantalla el otro se intercambia solo.

**¿Qué significa "La zona horaria '<valor>' no la reconoce la plataforma. Use un nombre IANA, por ejemplo America/Puerto_Rico o America/New_York." (400)?**
La zona no existe en el servidor o está mal escrita (las mayúsculas cuentan: `America/Puerto_Rico`, no `america/puerto_rico`).
Usa el nombre IANA de la zona (Continente/Ciudad). También se acepta el nombre de Windows (`SA Western Standard Time`) y se
guarda con su nombre IANA.

**¿Qué significa "Indique la zona horaria de la compañía, por ejemplo America/Puerto_Rico." (400)?**
Mandaste la zona vacía. La zona es obligatoria: si no quieres cambiarla, no mandes el campo.

**¿Qué significa "Región desconocida: '<valor>'. Use PR (Puerto Rico) o US (Estados Unidos)." (400)?**
Solo hay dos regiones, Puerto Rico y Estados Unidos. Para otro país escoge la más parecida y cambia los campos que hagan falta
(zona, moneda, orden de fecha, separadores, teléfono).

**¿Qué significa "El código de moneda debe ser de 3 letras mayúsculas (ISO 4217), por ejemplo USD." (400)?**
La moneda se indica con su código ISO de 3 letras (USD, EUR, DOP). Las minúsculas se aceptan y se guardan en mayúsculas; números
o símbolos no.

**¿Qué significa "El símbolo de moneda es obligatorio y de 1 a 3 caracteres, por ejemplo $." (400)?**
El símbolo vino vacío o con más de 3 caracteres. Ejemplos válidos: `$`, `US$`, `€`.

**¿Qué significan "La posición del símbolo de moneda debe ser B (antes del monto) o A (después).", "Los decimales de la moneda deben ser 0, 2 o 3.", "El orden de la fecha debe ser MDY (mes/día/año), DMY (día/mes/año) o YMD (año/mes/día).", "El separador de fecha debe ser '/', '-' o '.'.", "El formato de hora debe ser 12 o 24." y "El primer día de la semana debe ser 0 (domingo) o 1 (lunes)." (400)?**
El valor no es uno de los permitidos para ese campo. La lista completa la da `GET /api/v1/tenant/format-options` y la tabla del
capítulo 01, sección 11.1.

**¿Qué significan "El separador de miles debe ser coma (','), punto ('.') o espacio (' ')." y "El separador decimal debe ser punto ('.') o coma (',')." (400)?**
El separador no es uno de los permitidos (por ejemplo, un apóstrofo o un texto vacío). El espacio sí vale como separador de
miles (1 234,50), no como decimal.

**¿Qué significa "El código de país del teléfono debe ser '+' seguido de 1 a 4 dígitos, por ejemplo +1." (400)?**
El código de país lleva el signo `+` y de 1 a 4 dígitos: `+1` (Puerto Rico y EE. UU.) o `+34` (España) son válidos; `1` (sin
el `+`) o `+12345` no.

**¿Qué significa "La máscara de teléfono debe tener de 1 a 30 caracteres sin acentos y al menos un '#' (cada # es un dígito), por ejemplo (###) ###-####." (400)?**
La máscara dice cómo mostrar un teléfono guardado solo con dígitos: cada `#` es un dígito y lo demás se copia tal cual. Necesita
al menos un `#`, no más de 30 caracteres y sin letras acentuadas ni otros caracteres especiales.

**Cambié la región a Estados Unidos: ¿por qué cambió también la zona horaria y los demás formatos?**
Cambiar de región sin mandar otros campos carga el juego completo de esa región (la zona pasa a `America/New_York`). Si quieres
la región pero con algún valor distinto, manda ese campo en el mismo pedido (manda sobre el valor de la región) o cámbialo
después por separado; la compañía quedará como *Personalizada*.

**Mandé `{ "regionCode": "PR" }` y no se restauraron los valores de Puerto Rico. ¿Por qué?**
Si la compañía ya es de Puerto Rico no es un cambio de región, así que no se toca nada. Para restaurar, manda el juego de la
región que trae `GET /api/v1/tenant/format-options` (es lo que hace el botón *Restaurar valores de la región*).

**Cambié la zona horaria: ¿desde cuándo cuenta "hoy" con la zona nueva? ¿Cambian las fechas guardadas?**
Desde la petición siguiente (con varias instancias del servidor, a más tardar en 10 minutos). Las fechas guardadas no cambian
(están en UTC); solo cambia en qué día local cae cada una. Desde el Lote 20 todas las pantallas (órdenes, rutas, flota,
mantenimiento, tarifas, compras) cuentan "hoy" en la zona de la compañía.

**A las 8 p. m. (o más tarde) la pantalla me dice que la fecha es "mañana" / no me deja usar la fecha de hoy. ¿Qué pasa?**
Era el comportamiento anterior al Lote 20: el servidor contaba "hoy" en UTC y en Puerto Rico, entre las 8:00 p. m. y la
medianoche, ya era "mañana" para él (ejemplos: la ruta del día, el monitor, la tarifa "no anterior a hoy", el documento que
"vence hoy"). Ahora "hoy" es el día de la compañía y cambia a la medianoche local. Si aún lo ve, revise la zona en Ajustes de
la compañía → Región y formatos: si es una zona al este de UTC (p. ej. Europa), su "hoy" se adelanta a la medianoche local.

**Una regla de fecha de un campo personalizado (`min`/`max`) acepta o rechaza distinto cerca de la medianoche. ¿Por qué?**
`min` y `max` de un campo de fecha son días relativos a hoy (`-1` = ayer, `0` = hoy) y "hoy" es el día de la zona de la
compañía, no el día UTC. Un campo de tipo *fecha* compara el día de calendario; uno de *fecha y hora* compara el instante con
la medianoche local de hoy. Los mensajes no cambian: `Fecha anterior a la mínima permitida.` y `Fecha posterior a la máxima
permitida.` (400 con el campo).

**¿El idioma del usuario cambia los formatos?**
No. El idioma (de cada usuario) solo decide en qué idioma salen los nombres de días y meses; el orden de la fecha, los
separadores, la hora y la moneda los decide la región de la compañía.

### Genérico

**¿Qué significa "<Recurso> '<id>' no encontrado."?**
El registro que pediste no existe, ya fue borrado/desactivado, o no pertenece a tu compañía (el filtro por
tenant hace que otras compañías "no existan" para ti).

**¿Qué significa "Datos inválidos." con una lista de `errors`?**
Enviaste varios campos con problemas a la vez; revisa la lista `errors` del cuerpo de la respuesta, cada
clave es el campo y el valor es el mensaje puntual de ese campo.

**¿Qué significa "No autenticado."?**
Tu petición no llevaba un token válido, o el token ya no es válido (sesión cerrada, contraseña cambiada,
`logout-all`). Vuelve a iniciar sesión.

**¿Qué significa "No tiene permiso para esta acción."?**
Es el mensaje genérico de acceso prohibido cuando no aplica un mensaje más específico de permiso.

**¿Qué significa un error con `"code": "internal"` y `status: 500`?**
Un error no controlado en el servidor. Guarda el `correlationId` de la respuesta y compártelo con soporte
para que lo busquen en los registros del servidor.

## Lote 2 — Clientes y contratos

Mensajes verificados contra `src/Teikem.Infrastructure/Services/{Client,Location,Contract,Rate,SpecialService,PortalUser}Service.cs`,
`src/Teikem.Infrastructure/Clients/*` y `src/Teikem.Domain/Clients/*`. El capítulo completo está en
[02-clientes-y-contratos.md](02-clientes-y-contratos.md).

### Clientes

**¿Qué significa "Ya existe un cliente con ese código."?**
Enviaste un `code` explícito que ya usa otro cliente de tu compañía (o dos altas simultáneas chocaron). Deja el código
vacío para que se genere desde el nombre (con sufijo `-2`, `-3`… si hace falta) o elige otro.

**¿Por qué el código generado no es igual al nombre, o termina en `-2`?**
El código se forma con el nombre en mayúsculas, sin acentos, con `-` en vez de espacios y símbolos, y se recorta a 20
caracteres. Si ya existía uno igual se agrega `-2`, `-3`… recortando la base si no cabe (`FARMACIA-LAS-MARIA-2`).

**¿Qué significa "El nombre del cliente no contiene letras ni dígitos para generar el código."?**
El nombre solo tiene símbolos o espacios. Escribe un nombre con letras o números, o envía un `code` explícito.

**¿Puedo cambiar el nombre de un cliente?**
No. El nombre es la identidad del cliente y no hay forma de editarlo (`PATCH .../profile` no lo acepta). Si hay un error de
captura, da de baja el cliente (`POST .../deactivate`) y crea uno nuevo; la razón social (`legalName`) sí se edita.

**¿Qué significa "El registro fue modificado por otro usuario; recargue e intente de nuevo."?**
Enviaste `rowVersion` en el PATCH y otro usuario guardó antes que tú. Vuelve a abrir la ficha y repite el cambio.

**¿Qué significa "El punto de recogido debe ser un almacén activo del cliente (tipo PICKUP o BOTH)."?**
La localización que elegiste como recogido habitual no es del cliente, está inactiva o no es un almacén (por ejemplo es un
consignatario DELIVERY). Crea o elige una localización del cliente de tipo PICKUP o BOTH.

**¿Qué significa "El patrón debe incluir al menos un '#' para el consecutivo." / "Carácter no permitido en el patrón: 'x'…"?**
El patrón de numeración necesita al menos un `#` (cada `#` es un dígito) y solo admite letras, dígitos y `- _ / . # @`.
Ejemplo válido: `AX-#####` → `AX-00001`. Deja el campo vacío para volver al patrón por defecto (`ORD-#####`, `FAC-#####`, `PQT-#####`).

**¿Por qué la vista previa muestra `FAC-00001` si el mock decía `FAC-0001`?**
Los patrones por defecto del sistema tienen cinco dígitos, uniformes con el de orden (decisión del Lote 2). Si tu cliente
quiere cuatro, fija `FAC-####` en su numeración.

**¿Qué significa "Ya existe un contacto principal activo para este cliente."?**
Dos altas o ediciones simultáneas intentaron dejar dos contactos principales. Normalmente no ocurre: al marcar uno como
principal el anterior deja de serlo solo. Recarga y repite.

**¿Por qué mi contacto dejó de ser el principal al inactivarlo?**
Un contacto inactivo nunca es el principal. Marca otro contacto activo como principal si lo necesitas.

**¿Qué significa "Contacto '<id>' no encontrado."?**
Ese contacto no existe o no pertenece al cliente indicado en la URL (los contactos se alcanzan siempre a través de su cliente).

**¿Qué significa "Tipo de servicio desconocido: 'X'."?**
En los niveles de servicio (SLA) del alta o del contrato usaste un código que no está en el catálogo `ServiceType`. Consulta
`GET /api/v1/catalogs/ServiceType`.

**Dí de baja un cliente y no aparece en la lista, ¿se borró?**
No. Es una baja lógica: aparece con `GET /api/v1/clients?includeInactive=true`, su ficha sigue disponible y `POST .../reactivate`
lo devuelve a la lista.

### Consignatarios y localizaciones

**¿Qué significa "Una dirección corporativa o postal (facturación) debe pertenecer a un cliente; no puede ser compartida."?**
Intentaste crear (o volver compartida) una localización de tipo CORPORATE o BILLING sin cliente. Esas dos son siempre
direcciones de un cliente; indica `clientPublicId`.

**¿Qué significa "El cliente ya tiene una dirección corporativa activa; desactívela o edítela." (o "…dirección postal (facturación) activa…")?**
Solo puede haber una dirección física y una postal activas por cliente. Edita la existente, o desactívala antes de crear
otra. Reactivar una desactivada cuando ya hay otra activa da el mismo mensaje.

**¿Qué significa "Indique un cliente o marque la localización como compartida, no ambos."?**
En el PATCH enviaste `makeShared: true` y `clientPublicId` a la vez. Envía solo uno.

**¿Qué significa "La ventana horaria requiere hora de inicio y hora de fin." / "La hora de fin de la ventana debe ser posterior a la de inicio."?**
La ventana de entrega se guarda completa o vacía (`clearWindow: true` la borra), y el fin debe ser posterior al inicio
(formato `HH:mm:ss`).

**¿Qué significa "Los minutos de servicio no pueden ser negativos."?**
`defaultServiceMinutes` debe ser 0 o mayor.

**¿Por qué el punto de recogido del cliente volvió a "misma que la corporativa"?**
El almacén que era su recogido por defecto se desactivó, cambió de dueño o cambió a un tipo que no es PICKUP/BOTH. Vuelve a
elegir un almacén en el perfil del cliente.

**¿Qué significa "La localización fue modificada por otro usuario; recargue e intente de nuevo."?**
Igual que en clientes: `rowVersion` desactualizado. Recarga la ficha.

**¿Qué significa "Localización '<guid>' no encontrado."?**
No existe o pertenece a otra compañía.

### Contratos

**¿Qué significa "La fecha fin no puede ser anterior a la fecha de inicio."?**
`endDate` es anterior a `startDate` (o cambiaste "cliente desde" a una fecha posterior a la fecha fin ya guardada). Corrige
una de las dos; `clearEndDate: true` borra la fecha fin.

**¿Qué significa "Indique una fecha fin o márquela para borrar, no ambas."?**
Enviaste `endDate` y `clearEndDate: true` juntos. Envía solo uno.

**¿Qué significa "Ya existe un contrato con el número 'X'."?**
Enviaste un `contractNumber` que ya existe en tu compañía. Déjalo vacío para que se numere solo (`{CÓDIGO}-C2`, `-C3`…).

**¿Qué significa "El estatus actual no permite la acción 'EDIT_CONTRACT'."?**
El contrato está en un estatus donde no se edita (por defecto EXPIRED y CANCELLED): ni datos generales, ni modelo de
facturación, ni tarifas, ni servicios especiales. Si tu compañía necesita editar contratos cancelados, un administrador puede
cambiarlo en `PUT /api/v1/status/capabilities/CONTRACT`. La ficha lo anticipa con `canEdit: false`.

**¿Qué significa "No se puede activar el contrato: el cliente está suspendido. Reactive al cliente primero."?**
El cliente está en SUSPENDED. Devuélvelo a ACTIVE (`POST /api/v1/clients/{publicId}/status {"toCode":"ACTIVE"}`) y vuelve a activar el contrato.

**¿Qué significa "El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este."?**
Solo puede haber un contrato ACTIVE por cliente. Pasa el anterior a EXPIRED o CANCELLED y luego activa el nuevo.

**Mi contrato tiene fecha fin pasada y sigue apareciendo como vigente, ¿es un error?**
No. La fecha fin es informativa: no hay vencimiento automático. Si el contrato terminó de verdad, pásalo a EXPIRED (o
CANCELLED) manualmente; si no se renovó, se sigue trabajando con lo que hay.

**Desmarqué "Despacho" (o COD, Especiales, Pieza extra) y el monto sigue en la ficha, ¿no se guardó?**
Sí se guardó: desmarcar un componente no borra lo configurado, solo deja de cobrarse. Al volver a marcarlo, reaparece.

**¿Qué significa "El por ciento del cargo por COD debe estar entre 0 y 100." / "El cargo fijo por COD no puede ser negativo."?**
Con `type: PERCENT` el valor es un por ciento del monto COD cobrado (0..100); con `type: FIXED` es un monto por orden (≥ 0).
Si no quieres cobrar COD, apaga el checkbox `billCodFee` en el modelo de facturación.

**¿Qué significa "El tipo de servicio 'STANDARD' está repetido: solo puede haber un nivel de servicio por tipo."?**
La lista de SLA trae dos veces el mismo tipo (sin distinguir mayúsculas). Deja uno por tipo; `PUT` reemplaza la lista completa.

**¿Qué significa "Solo puede haber un nivel de servicio activo por tipo de servicio."?**
Dos escrituras simultáneas chocaron en el índice único. Recarga y repite el `PUT`.

### Tarifas y cotización

**¿Qué significa "El componente 'Por servicio' está apagado en el modelo de facturación del contrato; enciéndalo antes de trabajar sus tarifas." (o "…'Pieza extra'…tramos")?**
El checkbox correspondiente del contrato está desmarcado. Enciéndelo en `PATCH /api/v1/contracts/{publicId}/billing-model`
y vuelve a intentar. Las filas que ya existían no se perdieron.

**¿Qué significa "Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete en el contrato; edítela o ciérrela antes de crear otra."?**
Para ese par servicio+paquete ya hay una fila que estará vigente en la fecha de inicio que enviaste (abierta, o cerrada
con una fecha posterior). No puede haber dos tarifas vigentes el mismo día: para cambiar el monto usa `PATCH` (cierra y abre
una nueva); para dejar de cobrarla usa `.../close`.

**¿Por qué al editar la tarifa me devuelve un `id` distinto?**
Editar nunca sobrescribe el monto: cierra la fila vigente en la fecha nueva y abre otra. El historial completo se ve con
`?includeHistory=true`, y la tarifa vigente en una fecha pasada con `?asOf=yyyy-MM-dd`.

**¿Qué significa "La tarifa ya está cerrada; cree una nueva en lugar de editarla." / "El componente ya está cerrado." / "El tramo ya está cerrado…"?**
La fila que intentas editar o cerrar ya tiene fecha de cierre. Crea una fila nueva (misma combinación permitida una vez cerrada la anterior).

**¿Qué significa "La nueva vigencia (…) no puede ser anterior al inicio de la fila actual (…)." / "La fecha (…) no puede ser anterior al inicio de vigencia de la fila (…)."?**
La fecha que enviaste en `effectiveFrom`/`effectiveTo` es anterior al inicio de la fila. Usa una fecha igual o posterior (el mismo día está permitido y deja una fila de longitud cero como historial).

**¿Qué significa "Rango inválido: 'desde' debe ser al menos 2 (la pieza 1 va en la tarifa por servicio) y 'hasta' debe ser mayor o igual que 'desde' o quedar vacío (abierto)."?**
Los tramos de pieza extra empiezan en la pieza 2 (la 1 la paga la tarifa por servicio) y `toUnit` no puede ser menor que
`fromUnit`; déjalo vacío para "6+".

**¿Qué significa "El tramo 4–7 se traslapa con el tramo vigente 2–5."?**
Los tramos de un mismo componente no pueden cruzarse (ni al crear ni al editar Desde/Hasta). Ajusta el rango o cierra el tramo que estorba.

**¿Qué significa "Solo las tarifas por servicio se editan aquí; los tramos de pieza extra se editan en /tiers." / "Los tramos solo aplican a componentes de pieza extra."?**
Usaste la ruta equivocada: `PATCH .../rate-components/{id}` es para tarifas por servicio; los tramos van en `.../rate-components/{id}/tiers`.

**¿Qué significa "El componente de pieza extra está cerrado; cree uno nuevo para agregar tramos."?**
El componente ya tiene fecha de cierre. Crea otro componente EXTRA_PIECE para ese servicio y paquete.

**¿Qué significa "El par servicio/tipo de paquete está repetido; envíe una sola línea por (servicio, tipo de paquete) con el total de piezas."?**
En la cotización enviaste dos líneas con el mismo servicio y tipo de paquete. Agrúpalas en una sola sumando las piezas: así
la primera pieza paga la tarifa base y las demás la pieza extra.

**¿Qué significa "La cantidad de piezas debe ser al menos 1." / "Indique al menos una línea (servicio, tipo de paquete, piezas)."?**
Cada línea necesita al menos una pieza y la cotización al menos una línea.

**La cotización dice `baseSource: "NONE"` o `extraSource: "NONE"`, ¿qué significa?**
No hay tarifa para ese servicio y paquete ni en el contrato ni entre las tarifas genéricas de la compañía (o el componente
está apagado y no hay genérica). La base sale `null` y la pieza extra 0. Configura la tarifa en el contrato.

**¿Qué significa "La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe."?**
Intentó abrir una nueva versión o cerrar una tarifa, un tramo o un servicio especial con una fecha pasada. El historial de
tarifas es el que sustenta lo que ya se cotizó y facturó, así que no se puede cambiar hacia atrás. Use hoy (o deje la fecha
vacía) o una fecha futura. Si necesita cargar tarifas históricas al configurar un contrato, hágalo con el alta de la tarifa,
que sí admite `effectiveFrom` pasado. La misma regla y el mismo mensaje aplican, en el Lote 4, a las tres tarifas del
chofer (sección "Choferes y tarifas" más abajo).

### Servicios especiales

**¿Qué significa "El cliente no tiene un contrato vigente; cree o active un contrato antes de configurar servicios especiales."?**
El cliente no tiene ningún contrato ACTIVE ni DRAFT (solo EXPIRED/CANCELLED, o ninguno). Crea un contrato nuevo.

**¿Qué significa "El componente 'Servicios especiales' está apagado en el contrato vigente; enciéndalo en el modelo de facturación para agregar servicios especiales."?**
El checkbox "Especiales" del contrato vigente está desmarcado. Las filas existentes se siguen mostrando (`componentEnabled: false`), pero no se pueden crear ni editar hasta encenderlo.

**¿Qué significa "Indique el tipo de servicio especial: un tipo existente (typeId) o el nombre de uno nuevo (newTypeName), no ambos."?**
Envía solo `typeId` (tipo de la lista `GET /api/v1/special-service-types`) o solo `newTypeName`.

**Escribí "VAGON DEL MUELLE" y me dice "El cliente ya tiene una tarifa vigente en esa fecha para el tipo 'Vagón del muelle'…"**
Los nombres se comparan sin mayúsculas, acentos ni espacios dobles: es el mismo tipo, y el cliente ya tiene una tarifa
vigente de ese tipo. Edítala (`PATCH`) o ciérrala.

**¿Qué significa "El tipo de servicio especial está inactivo; reactívelo o elija otro."?**
El `typeId`/`specialServiceTypeId` apunta a un tipo dado de baja (`POST /api/v1/special-service-types/{id}/deactivate`).
Reactívalo con `.../reactivate`, elige otro tipo, o crea el servicio con `newTypeName` (si coincide con el tipo inactivo,
se reactiva solo). El mismo mensaje aparece en el Lote 4 al agregar una tarifa por viaje de un chofer con un tipo inactivo.

**¿Qué significa "El tipo tiene tarifas vigentes en N cliente(s); ciérrelas antes de inactivarlo."?**
No se puede dar de baja un tipo de servicio especial mientras algún cliente tenga una tarifa abierta de ese tipo. Cierra esas
tarifas (`.../special-services/{id}/close`) y vuelve a intentarlo; el historial se conserva.

**¿Qué significa "El tipo tiene tarifas por viaje vigentes en N chofer(es); ciérrelas antes de inactivarlo."?**
Es el mismo candado que el anterior, agregado en el Lote 4: además de clientes, ese tipo de servicio especial también se usa
como "tipo de viaje" en las tarifas por viaje de uno o más choferes (capítulo 04, sección 8.3). Cierra esas tarifas
(`POST /api/v1/drivers/{publicId}/trip-rates/{id}/close`) antes de inactivar el tipo.

**¿Qué significa "El tipo ya está inactivo." / "El tipo ya está activo."?**
Pediste dar de baja un tipo que ya estaba inactivo, o reactivar uno que ya estaba activo. No hay nada que hacer.

**¿Qué significa "La tarifa ya está cerrada; agregue un servicio especial nuevo si necesita volver a cobrarlo."?**
La fila ya tiene fecha de cierre. Crea otra del mismo tipo.

### Usuarios de portal

**¿Qué significa "Invitación inválida o vencida."?**
Es la única respuesta de `accept-invite` cuando algo falla, a propósito (no revela si el correo existe: un correo que no
existe o que pertenece a un usuario interno recibe exactamente la misma respuesta). Causas posibles:
el correo no coincide con la invitación, el enlace venció (`expiresAtUtc` de la invitación, 48 horas por defecto) o ya se usó, la compañía **reenvió** la
invitación y este es el enlace viejo (solo vale el último), la contraseña tiene menos de 12 caracteres (el chequeo contra
brechas conocidas existe en el código pero hoy es no-op, como en el cambio de contraseña del Lote 1), el usuario ya no está en
INVITED (aceptó, fue suspendido o dado de baja), o el módulo de portal de la compañía está apagado. Pide a la compañía que
reenvíe la invitación (`.../resend-invite`) y usa el enlace más reciente.

**¿Qué significa "Ese correo no está disponible para el portal de esta compañía."?**
Ese correo ya está registrado en la plataforma: como usuario de portal de tu compañía (en cualquier cliente y en cualquier
estatus salvo dado de baja), como usuario de portal de otra compañía, o como usuario interno. El mensaje es el mismo en todos
los casos a propósito (no revela dónde está el correo). Si es un usuario de portal tuyo, búscalo en el cliente correspondiente;
si fue dado de baja (DISABLED) en **ese mismo cliente**, la invitación no da 409: lo reinvita. Si está dado de baja en otro
cliente, reinvítalo desde ese cliente (mover una persona de cliente es una decisión de negocio). En cualquier otro caso usa
otro correo. Cada 409 de estos deja un `SecurityEvent` ROLE_CHANGE/FAILURE `portal_invite_conflict` en la bitácora de seguridad.

**¿Qué significa "Solo se puede reenviar la invitación a un usuario que todavía no la ha aceptado (estatus INVITED)."?**
El usuario ya aceptó (o fue suspendido/dado de baja). No hay nada que reenviar; si olvidó su contraseña, ese flujo llega con el módulo del portal.

**¿Qué significa "Solo se puede suspender un usuario de portal en estatus ACTIVE." / "Solo se puede reactivar un usuario de portal en estatus SUSPENDED."?**
Suspender es solo desde ACTIVE y reactivar solo desde SUSPENDED. Un INVITED no se suspende (dalo de baja con `.../remove` si no
debe entrar); un DISABLED no se reactiva: se vuelve a invitar con el mismo correo (`.../invite`).

**¿Qué significa "El usuario de portal ya está dado de baja."?**
Ya está en DISABLED (terminal). No hay más transiciones desde ahí; si la persona debe volver, invítala de nuevo con el mismo
correo desde el mismo cliente (`.../invite`): renace en INVITED con su historial y fija contraseña nueva al aceptar.

**¿Qué significa "Los usuarios de portal se autentican en el portal de clientes." al iniciar sesión?**
La cuenta es de portal y la aplicación interna no la acepta. El login del portal llega en su propio módulo.

**¿Qué significa "El módulo 'CLIENT_PORTAL' no está habilitado para esta compañía."?**
La compañía tiene apagado el portal de clientes. Un administrador puede encenderlo en `PUT /api/v1/modules/CLIENT_PORTAL` (requiere reautenticación reciente).

**¿Por qué no veo al usuario de portal en `/api/v1/users`?**
Los usuarios de portal no son usuarios de la compañía: se administran desde el expediente del cliente (`.../portal-users`) con el permiso `portalusers.manage`.

**¿Qué significa "Catálogo <Dominio> 'X' no encontrado."?**
El código de catálogo que enviaste (`paymentTerm`, `currency`, `locationType`, `country`, `role`, `type` del COD…) no existe.
Consulta `GET /api/v1/catalogs/<Dominio>`.

**Invité a un usuario de portal y ahora no puede aceptar la invitación, aunque el enlace es reciente.**
Además de un token vencido o ya usado, la aceptación falla si la compañía está desactivada por el administrador de plataforma
o si el módulo Portal de clientes está apagado. La respuesta es siempre la misma (`Invitación inválida o vencida.`) para no
revelar la causa; revise el estado de la compañía y del módulo y reenvíe la invitación.

## Lote 3 — Órdenes de transporte

**¿Qué significa "El número de orden generado con el patrón del cliente excede 40 caracteres; acorte el patrón en la ficha del cliente."? (HTTP 409)**
También aparece como "número de factura" o "número de paquete". Teikem genera esos números con el patrón de la ficha del
cliente (`PATCH /api/v1/clients/{id}/number-settings`) y **nunca recorta** el consecutivo: si el consecutivo tiene más dígitos
que los `#` del patrón, los antepone. Con un patrón muy largo, el número resultante ya no cabe en los 40 caracteres del campo.
La orden no se crea (ni en la captura, ni en el PATCH que agrega paquetes, ni en la fila del importador) y no se gasta ningún
consecutivo. Acorta el patrón (o déjalo en blanco para usar el patrón por defecto) y vuelve a intentarlo.

**Eliminé una orden en captura y ahora confirmar, cancelar, repreciar o cambiar su estatus responde "Orden no encontrado." (HTTP 404). ¿Por qué?**
Eliminar una orden (solo en su estatus inicial) es una baja lógica: la orden sigue visible en su ficha y en el listado con
`includeInactive=true`, pero ya no admite ninguna acción (editar, eliminar, confirmar, repreciar, cancelar ni estatus). Si la
necesitas, captúrala de nuevo: su número de orden y de empaque quedaron libres.

**Cambié el consignatario de una orden y no me avisó de factura repetida. ¿Es correcto?**
Sí, si el número de factura de esa orden lo generó Teikem. El chequeo de factura repetida por consignatario solo aplica a
facturas **tecleadas** al crear la orden; la orden recuerda si su factura se tecleó, así que el resultado no cambia aunque
después se modifique en la ficha del cliente quién asigna las facturas.

**¿Qué significa "El comentario admite como máximo 500 caracteres."? (HTTP 400, campo `comment`)**
Todo cambio de estatus guarda su comentario en la bitácora de estatus, que admite hasta 500 caracteres. Aplica a
`POST /api/v1/orders/{id}/cancel`, `POST /api/v1/orders/{id}/status` y a los cambios de estatus de clientes, contratos y
usuarios de portal. El estatus no cambia; acorta el comentario y vuelve a intentarlo.

**Tengo el permiso `contacts.manage` pero agregar, editar o desactivar un contacto de una orden responde "Falta el permiso 'orders.edit'." (HTTP 403). ¿Por qué?**
Los contactos pertenecen a un registro dueño y escribirlos exige, además de `contacts.manage`, el permiso de edición del
módulo dueño: `orders.edit` para una orden, `clients.update` para un cliente o un contacto de cliente, `locations.update`
para un consignatario, `contracts.update` para un contrato. El permiso se revisa antes de buscar el registro, así que la
respuesta es la misma exista o no la orden. Leer los contactos exige el permiso de lectura del dueño (`orders.view`).
Los teléfonos que el importador guarda en la orden al confirmar un lote no piden `orders.edit` (los crea la importación,
que ya exige `orders.create`). En el Lote 4, la misma regla aplica a `fleet.manage` (contactos de VEHICLE/DRIVER/DISPATCH_ZONE)
y `fleet.maintenance` (contactos de MAINTENANCE_SCHEDULE/WORK_ORDER/FUEL_LOG).

**Guardo campos personalizados de una parada, del COD de una orden, de un lote o de una plantilla de importación y responde "ORDER_STOP '…' no encontrado." (HTTP 404). ¿Por qué?**
El registro no existe en tu compañía. Para `ORDER_STOP` el id es el de la parada de una orden; para `ORDER_COD`, el id de
una orden **con COD**; para `IMPORT_BATCH` e `IMPORT_TEMPLATE`, el id del lote o de la plantilla. Escribirlos exige `orders.edit`.

**¿Qué significa "Máximo 150 caracteres." (HTTP 400, campo `email`) al invitar a un usuario de portal?**
El correo del usuario de portal admite hasta 150 caracteres. No se creó ninguna cuenta; usa un correo más corto. En
`accept-invite` un correo así recibe la respuesta neutra "Invitación inválida o vencida.".

**Soy de Facturación: ¿cómo autorizo una orden que excede el límite de crédito? ¿Por qué "Falta el permiso 'orders.edit'." (HTTP 403)?**
Desde la ficha de la orden (en DRAFT), `GET /api/v1/orders/{id}/quote` muestra el aviso (`credit.exceeds=true`) y
`POST /api/v1/orders/{id}/confirm` con `{"overrideCredit": true}` la confirma. Con la plantilla Facturación (`orders.view`
+ `orders.credit_override`, sin `orders.edit`) eso solo funciona cuando el crédito **realmente** se excede: confirmar sin
`overrideCredit`, o una orden que cabe en el crédito, sigue exigiendo `orders.edit` (403 "Falta el permiso 'orders.edit'.").
Sin `orders.credit_override`, `overrideCredit: true` responde 403 "Falta el permiso 'orders.credit_override'.". La
autorización queda en el historial de estatus ("Crédito excedido autorizado por …") y en la bitácora de seguridad
(ROLE_CHANGE `credit_override`). La creación con `confirmNow` y la confirmación del importador siguen exigiendo `orders.create`.

**El indicador "COD por cobrar" bajó al cancelar una orden. ¿Es correcto?**
Sí. Suma el COD PENDING de las órdenes activas que **no** están canceladas: una orden cancelada nunca se entrega, así que
su COD no está por cobrar. Si tu compañía personalizó el filtro del indicador, no se toca; el filtro original se corrige solo.

**¿Cómo armo el gráfico "Órdenes por tipo de paquete"?**
La fuente Órdenes (`TRANSPORT_ORDER`) tiene los campos `PackageType` (etiqueta) y `PackageTypeCode` (código) con el
**tipo de paquete principal** de cada orden: el tipo con más piezas entre sus líneas (si empatan, el de la primera línea).
Una orden mixta cuenta una sola vez, bajo su tipo principal; una entrega especial queda sin tipo. Crea un gráfico de dona
agrupado por `PackageType`.

**Al validar una importación: "El archivo supera el límite de 5.000 filas o 2 MB." (HTTP 400, campo `content` o `file`)**
El archivo tiene más de 5.000 filas de datos (sin contar la cabecera ni las filas vacías) o pesa más de 2 MB. Divídelo en
varios archivos. El campo es `content` cuando el CSV viaja en JSON (`/import/validate`) y `file` cuando se sube como archivo multipart (`/import/validate-file`).

**Una fila de la importación hacia un consignatario que no admite facturas repetidas sale con error en `clientInvoiceNumber` y no la puedo elegir (HTTP 400 en `rows[n]`).**
El consignatario no permite repetir el número de factura y ya existe otra orden con ese número: la fila no se puede
crear. Si el consignatario sí admite repetidas, la fila sale con un aviso y solo se crea si confirmas el lote con
`confirmDuplicateInvoice: true` ("Crear de todos modos"); sin él, la fila queda fallida con el mensaje de factura repetida
y el resto del lote sigue.

**¿Qué significa "El cliente está dado de baja; solo se consulta su historial."? (HTTP 409)**
El cliente fue dado de baja (`POST /clients/{id}/deactivate`). Su ficha, historial, órdenes, contratos y tarifas se
siguen consultando, pero no se puede crear nada nuevo para él: órdenes (captura o importación), contratos, componentes o
tramos de tarifa, servicios especiales, ni invitar/reenviar un usuario de portal. Cancelar o cerrar algo que ya existía
sigue permitido. Reactiva el cliente (`POST /clients/{id}/reactivate`) para volver a crear.

**Al crear o cambiar una orden a un consignatario, "El consignatario 'X' no permite facturas repetidas: la orden N ya usa el número F." o la versión "…confirme si desea crear la orden de todos modos." (HTTP 409)**
Tecleaste un número de factura del cliente (`clientInvoiceNumber`) que ese mismo consignatario ya usa en otra orden
activa. Si el consignatario tiene `allowDupInvoice: false`, no se puede crear con ese número (usa otro o corrige la
factura). Si tiene `allowDupInvoice: true`, repite la petición agregando `confirmDuplicateInvoice: true` ("Crear de
todos modos"). El error trae `errors.existingOrderNumber` y `errors.existingOrderPublicId` con la orden que ya la usa.
Solo aplica a facturas **tecleadas**; si Teikem genera el número de factura, nunca se dispara.

**¿Qué significa "El cliente está suspendido; no se pueden crear ni confirmar órdenes."? (HTTP 422)**
El cliente está en estatus `SUSPENDED` (capítulo 02, sección 1.6). No se pueden crear órdenes nuevas para él, ni
confirmar una que ya estaba en captura. Reactívalo (`ACTIVE`) para seguir.

**¿Qué significa "El consignatario es obligatorio: elija uno del directorio o capture uno nuevo." o "Indique un consignatario del directorio o uno nuevo, no ambos."? (HTTP 400, campo `consignee`)**
Toda orden necesita exactamente un consignatario: o el `publicId` de una localización del directorio
(`consigneeLocationPublicId`), o los datos de uno nuevo (`newConsignee`), nunca los dos a la vez ni ninguno.

**¿Qué significa "El cliente excede su límite de crédito: límite X.XX, en curso Y.YY, esta orden Z.ZZ." (HTTP 422, `code: "credit_exceeded"`)?**
Al confirmar, la suma de lo que el cliente tiene en curso (órdenes ya cotizadas que no son borrador ni terminales) más
el monto de esta orden pasa su límite de crédito. No es un bloqueo definitivo: la orden queda como estaba (sin
confirmar) y quien tenga el permiso `orders.credit_override` puede repetir la confirmación con `overrideCredit: true`
para autorizarla (ver capítulo 03, sección 3.3; en el Lote 4, `overrideCredit` también se acepta junto con
`driverPublicId` al crear una entrega especial con chofer, capítulo 04, sección 11).

**¿Qué significa "No hay tarifa vigente para X/Y; configure la tarifa en el contrato del cliente antes de confirmar."? (HTTP 422)**
Al cotizar, alguna línea (combinación de tipo de servicio y tipo de paquete) no tiene tarifa vigente ni en el contrato
del cliente ni en las tarifas genéricas del tenant. La orden no se confirma ni se congela con un monto en $0: agrega el
componente de tarifa que falta (capítulo 02, sección 5) y vuelve a intentar.

**¿Qué significa "El servicio especial ya no está vigente; elija otro antes de confirmar."? (HTTP 409)**
El servicio especial de una entrega especial se cerró (o venció su vigencia) después de crear la orden pero antes de
confirmarla. Edita la orden con otro servicio especial vigente del cliente y confirma de nuevo.

**Edito una orden confirmada y responde "El estatus actual no permite la acción 'EDIT_CARGO'." (HTTP 422). ¿Cómo la corrijo?**
Por defecto, la carga de una orden (consignatario, paquetes, COD) solo se edita mientras está en **DRAFT** (borrador).
Una orden confirmada ya no se edita salvo que un administrador habilite `EDIT_CARGO` para el estatus `CONFIRMED` desde
`PUT /api/v1/status/capabilities/TRANSPORT_ORDER`; en ese caso, editarla también exige la capacidad `REPRICE` (re-cotiza
para no dejar un monto viejo). Si no quieres relajar esa regla, cancela la orden y captura una nueva.

**¿Qué significa "El avance a 'X' lo realiza el módulo correspondiente (trips/entregas); desde aquí solo se registran estatus laterales."? (HTTP 422)**
`POST /api/v1/orders/{id}/status` solo mueve la orden a un estatus lateral (`ON_HOLD`, `PARTIAL`, `FAILED`) o la regresa
al pipeline del que salió. Avanzar el pipeline en sí (recogido, en tránsito, entregada…) lo hace, desde el Lote 4, la
asignación de chofer a una entrega especial (capítulo 04, sección 11), o lo hará el módulo de rutas y entregas de un
lote posterior para las demás órdenes.

**Invito el mismo correo desde otro cliente de mi compañía y ya era usuario de portal: ¿por qué a veces queda ACTIVE de inmediato y otras veces me pide invitación?**
Si la cuenta ya tenía contraseña (ya había aceptado alguna invitación anterior en cualquier cliente) y sigue habilitada,
el cliente nuevo se agrega directo en estatus `ACTIVE`, sin enlace (queda el evento `portal_client_added`). Si la
cuenta nunca fijó contraseña, o estaba desactivada porque no le quedaba ningún cliente vivo, nace `INVITED` con un
enlace de invitación como siempre. Ver capítulo 03, sección 6.

**Quité a un usuario de portal de un cliente y sigue pudiendo entrar con la misma cuenta en otro cliente. ¿Es un error?**
No: desde el Lote 3, una cuenta de portal puede pertenecer a varios clientes de la compañía (una fila por cliente). Quitar
o suspender actúa solo sobre la fila de ese cliente; la cuenta (contraseña, sesiones) se desactiva únicamente cuando no
le queda **ninguna otra** fila activa en la compañía, y se reactiva en cuanto vuelve a haber una.

## Lote 4 — Flota, choferes y mantenimiento

Mensajes verificados contra `src/Teikem.Domain/Fleet/*`, `src/Teikem.Infrastructure/Fleet/*` y
`src/Teikem.Infrastructure/Services/{Vehicle,Driver,DispatchZone,Fleet,Maintenance,FuelLog,DriverRate,
DriverPayPolicy,DriverTrip,SpecialDelivery}*.cs`. El capítulo completo está en
[04-flota-choferes-mantenimiento.md](04-flota-choferes-mantenimiento.md).

### Vehículos y sus documentos

**¿Qué significa "Ya existe un vehículo con ese código."?**
El `code` que enviaste ya lo usa otro vehículo de tu compañía, aunque esté dado de baja: el código no se libera al
eliminar un vehículo. Elige otro código.

**¿Qué significa "Ya existe un vehículo activo con ese VIN."?**
Otro vehículo **activo** ya tiene ese VIN. Si el otro vehículo está dado de baja (no activo), el VIN sí se puede
reutilizar.

**¿Qué significa "El código del vehículo se fija al crearlo; no se puede cambiar."?**
Enviaste `code` (o `homeWarehouseId`) en un `PATCH /vehicles/{publicId}`. Ninguno de los dos se edita después del alta.

**¿Qué significa "El VIN admite como máximo 40 caracteres."?**
El VIN se guarda en mayúsculas sin espacios; no puede exceder 40 caracteres.

**¿Qué significa "El año del modelo debe estar entre 1900 y {año+1}."?**
`modelYear` está fuera de rango: se acepta hasta el año siguiente al actual (el modelo del año próximo ya se vende).

**¿Qué significa "El odómetro no puede ser menor que la última lectura registrada ({km} km el yyyy-MM-dd)."?**
Intentaste corregir manualmente `currentOdometerKm` del vehículo por debajo de un hecho ya registrado: una carga de
combustible activa o una orden de trabajo cerrada. Puedes bajar un error de captura, pero nunca por debajo de esa
lectura. Revisa las cargas de combustible (sección "Bitácora de combustible") y las órdenes de trabajo del vehículo.

**¿Qué significa "El vehículo está dado de baja definitiva; no se puede reactivar."?**
El vehículo llegó al estatus terminal `INACTIVE` (baja definitiva); ya no se reactiva ni con el checkbox Activo. Si
fue un error, crea un vehículo nuevo.

**¿Qué significa "El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible."?**
El vehículo tiene el checkbox Activo apagado o está en el estatus terminal `INACTIVE`. Reactívalo
(`POST /vehicles/{id}/reactivate`, si no está dado de baja definitiva) antes de registrar una nueva orden de trabajo
o una carga de combustible.

**¿Qué significa "Tipo de vehículo desconocido: 'X'." / "Propiedad desconocida: 'X'." / "Tipo de combustible desconocido: 'X'."?**
El código que enviaste (`vehicleType`, `ownership` o `fuelType`) no existe en el catálogo del tenant. Consulta
`GET /api/v1/catalogs/{VehicleType|Ownership|FuelType}` para ver los códigos válidos.

**¿Qué significa "El tipo de documento de vehículo desconocido: 'X'."?**
El `docType` no es uno de `REGISTRATION`, `INSURANCE`, `INSPECTION`, `PERMIT` (u otro que tu compañía haya agregado a
ese catálogo).

**¿Qué significa "Documento no encontrado."? (documento de vehículo)**
El `id` del documento no pertenece al vehículo de la URL (es de otro vehículo, o de otro tenant). Los documentos se
consultan siempre bajo su vehículo (`/vehicles/{publicId}/documents/{id}`), nunca por id suelto.

**Renové un documento vencido y no lo desactivé: ¿por qué dejó de aparecer en "Documentos por vencer"?**
Un documento con vencimiento queda "superado" cuando el mismo dueño tiene otro **activo** del mismo tipo con
vencimiento posterior (`isSuperseded: true` en su ficha). Un documento superado no bloquea la disponibilidad, no
cuenta para el próximo vencimiento y no aparece en el panel "Documentos por vencer": no hace falta desactivar el
viejo a mano al renovar.

### Choferes, licencias, certificaciones, dispositivos y zonas

**¿Qué significa "Ya existe un chofer con ese código."?**
El `code` (empleado) ya lo usa otro chofer de tu compañía, aunque esté eliminado: el código no se libera. Elige otro.

**¿Qué significa "El código del chofer se fija al crearlo; no se puede cambiar."?**
Enviaste `code`/`employeeCode` en un `PATCH /drivers/{publicId}`. El código de un chofer es inmutable.

**¿Qué significa "El chofer fue eliminado; solo se consulta su historial."?**
El chofer llegó al estatus terminal `INACTIVE` (acción "Eliminar chofer"). Su ficha, licencias, certificaciones y
viajes se siguen consultando, pero no admite ediciones ni tarifas nuevas.

**¿Qué significa "El chofer fue eliminado; no se puede reactivar."?**
"Eliminar chofer" es una baja **definitiva** (estatus terminal): a diferencia del checkbox Activo, no tiene vuelta
atrás. Si el chofer regresa, da de alta uno nuevo.

**¿Qué significa "El chofer ya fue eliminado."?**
Intentaste `DELETE /drivers/{publicId}` sobre un chofer que ya estaba en el estatus terminal. No hay nada que hacer.

**¿Qué significa "Falta el permiso 'admin.users'."? (al vincular o quitar el usuario de un chofer)**
Vincular o desvincular el usuario interno de un chofer (`userId`/`clearUser`) exige, además de `fleet.manage`, el
permiso `admin.users`. Pide a un administrador que lo agregue a tu rol, o que haga el vínculo él mismo.

**¿Qué significa "Solo un usuario interno se puede vincular a un chofer."?**
El `userId` que enviaste corresponde a un usuario de **portal** (cliente final), no a un usuario interno de la
compañía. Solo los usuarios internos se vinculan a un chofer.

**¿Qué significa "El usuario no tiene una membresía activa en esta compañía."?**
El usuario existe, pero su membresía en tu tenant está suspendida o inactiva. Reactívala primero
(`PATCH /api/v1/users/{id}/membership`, capítulo 01) o elige otro usuario.

**¿Qué significa "El usuario ya está vinculado a otro chofer."?**
Cada usuario interno se vincula a **un solo** chofer por compañía. Desvincúlalo del otro chofer primero
(`clearUser: true`) si en verdad debe pasar a este.

**¿Qué significa "La zona de despacho está inactiva."?**
El `dispatchZoneId` que enviaste como zona primaria del chofer está dado de baja. Reactívala o elige otra zona.

**¿Qué significa "La zona tiene choferes asignados; reasígnelos antes de inactivarla."?**
No se puede inactivar una zona mientras algún chofer activo la tenga como zona primaria. Cámbiales la zona
(`PATCH /drivers/{id} {"dispatchZoneId": ...}` o `clearZone: true`) y vuelve a intentar.

**¿Qué significa "Ya existe una zona de despacho con ese código."?**
El `code` de la zona ya lo usa otra zona de tu compañía. Elige otro.

**¿Qué significa "Clase de licencia desconocida: 'X'." / "Tipo de certificación desconocido: 'X'."?**
El código que enviaste no existe en el catálogo `LicenseClass`/`CertificationType` de tu compañía. Consulta
`GET /api/v1/catalogs/{LicenseClass|CertificationType}`.

**¿Qué significa "Licencia no encontrada." / "Certificación no encontrada." / "Dispositivo no encontrado."?**
El `id` no pertenece al chofer de la URL (es de otro chofer, o de otro tenant). Estos recursos se consultan siempre
bajo su chofer (`/drivers/{publicId}/licenses/{id}`, etc.), nunca por id suelto.

**Un dispositivo dejó de recibir notificaciones después de eliminar al chofer, ¿es correcto?**
Sí. Al "Eliminar chofer" (estatus terminal), todos sus dispositivos quedan desactivados automáticamente, además de
desvincularse el usuario, quitarse la zona y cerrarse sus tarifas abiertas.

### Documentos por vencer y disponibilidad para despacho

**¿Qué significa "withinDays debe estar entre 0 y 365."?**
El parámetro `withinDays` del panel "Documentos por vencer" está fuera de rango. Usa un valor entre 0 y 365 (30 por
defecto).

**¿Qué significa "Tipo de documento desconocido: 'X'."? (panel "Documentos por vencer")**
El `docType` del filtro no es uno de `REGISTRATION`, `INSURANCE`, `INSPECTION`, `PERMIT`, `LICENSE`, `CERTIFICATION`.

**¿Qué significa "Entidad desconocida: 'X'; use VEHICLE o DRIVER."?**
El filtro `entity` solo acepta `VEHICLE` o `DRIVER`.

**El chofer/vehículo sale "no disponible" en el panel de disponibilidad y no entiendo por qué.**
Revisa el arreglo `issues` de la respuesta: cada motivo trae `code`, `message` y `blocking`. Los que bloquean de
verdad (`blocking: true`) son: chofer o vehículo con el checkbox Activo apagado, chofer o vehículo en un estatus
distinto del inicial, chofer sin licencia vigente, documento del vehículo vencido (no superado), o una orden de
trabajo del vehículo en `IN_PROGRESS`. Los demás (certificación vencida, documento que vence pronto, vehículo sin
documentos) solo son avisos: el recurso puede seguir `available: true`.

### Mantenimiento preventivo y órdenes de trabajo

**¿Qué significa "Indique el vehículo o el tipo de vehículo del programa, no ambos."?**
Un programa de mantenimiento aplica a **un** vehículo o a **un** tipo de vehículo, nunca a los dos ni a ninguno.
Envía exactamente uno de `vehiclePublicId`/`vehicleType`.

**¿Qué significa "Un programa por kilometraje exige un intervalo en km mayor que 0."? / "Un programa por tiempo exige un intervalo en días mayor que 0."?**
El disparador (`trigger`) que elegiste (`MILEAGE`, `TIME` o `BOTH`) exige el intervalo correspondiente
(`intervalKm`/`intervalDays`), mayor que cero.

**¿Qué significa "El último servicio solo se captura en programas de un vehículo; en los de tipo se toma de sus órdenes de trabajo cerradas."?**
Un programa **por tipo de vehículo** no tiene un "último servicio" propio: se calcula, vehículo por vehículo, con su
última orden de trabajo cerrada ligada a ese programa. `lastServiceKm`/`lastServiceDate` solo se capturan en un
programa **por vehículo**.

**¿Qué significa "Programa de mantenimiento no encontrado."?**
El `scheduleId` no existe en tu compañía.

**¿Qué significa "El programa de mantenimiento no aplica a este vehículo."?**
El programa que indicaste en la orden de trabajo es de otro vehículo, o de un tipo de vehículo distinto al del
vehículo de la orden.

**¿Qué significa "Una orden correctiva no se asocia a un programa preventivo."?**
Enviaste `scheduleId` (que siempre es de un programa preventivo) junto con `maintenanceType: CORRECTIVE`. Quita el
programa o cambia el tipo a `PREVENTIVE`.

**¿Qué significa "El estatus actual no permite la acción 'EDIT_WORK_ORDER'."?**
La orden de trabajo está en un estatus donde no se edita (por defecto `CLOSED` y `CANCELLED`): ni el encabezado ni
sus tareas. Un administrador puede relajarlo en `PUT /api/v1/status/capabilities/WORK_ORDER` si tu compañía lo
necesita; la ficha lo anticipa con `canEdit: false`.

**¿Qué significa "La orden tiene tareas: los costos de labor y partes se calculan con la suma de sus tareas."?**
Con tareas activas, `laborCost`/`partsCost` del encabezado son la **suma** de las tareas: no se capturan a mano.
Edita los costos de cada tarea, o desactívalas todas si prefieres capturar el costo directo en el encabezado.

**¿Qué significa "La orden tiene N tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla."? (HTTP 422)**
No se puede cerrar la orden de trabajo con tareas activas pendientes. Marca `isCompleted: true` en cada tarea, o
desactívala si ya no aplica.

**¿Qué significa "Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje."?**
La orden está ligada a un programa por kilometraje (o ambos) y le falta el odómetro de cierre. Envía `odometerKm` en
el cuerpo del cambio de estatus, o captúralo antes en el encabezado.

**¿Qué significa "La fecha de cierre no puede ser futura."?**
`completedDate` (al cerrar la orden) no puede ser una fecha por venir.

**Cerré la orden de trabajo y el vehículo volvió solo a "Activo". ¿Es correcto?**
Sí. Al iniciar una OT (`IN_PROGRESS`) el vehículo pasa a `MAINTENANCE` si estaba `ACTIVE`; al cerrarla o cancelarla,
si no queda ninguna otra OT `IN_PROGRESS` de ese vehículo, regresa a `ACTIVE` automáticamente (aunque `MAINTENANCE`
se hubiera puesto a mano).

**¿Qué significa "Ya existe una orden de trabajo con ese número; intente de nuevo."?**
Dos altas simultáneas chocaron al sacar el número `OT-#####`. Reintenta la petición: el número es automático y
consecutivo, así que no hay que elegir uno.

### Bitácora de combustible

**¿Qué significa "Los litros deben ser mayores que 0."?**
`liters` debe ser un número positivo.

**¿Qué significa "El vehículo de una carga no se cambia; desactívela y registre otra."?**
El vehículo de una carga de combustible es inmutable. Si te equivocaste de vehículo, desactiva la carga
(`POST /fuel-logs/{id}/deactivate`) y registra una nueva en el vehículo correcto.

**¿Qué significa "La carga de combustible está inactiva; no se puede corregir."?**
Intentaste editar (`PATCH`) una carga que ya está dada de baja lógica. Reactivarla no está disponible en este
lote; registra una carga nueva si hace falta.

**¿Qué significa "La lectura de odómetro (…) es menor que la de una carga anterior del mismo vehículo (…)."? / "…es mayor que la de una carga posterior del mismo vehículo (…)."?**
El odómetro de las cargas de un vehículo debe ser monótono por fecha: no puede quedar por debajo de una carga
anterior ni por encima de una posterior. Revisa la fecha y la lectura que capturaste.

**¿Qué significa "La carga de combustible cambió mientras se guardaba; recargue e intente de nuevo."?**
Dos correcciones a la misma carga chocaron (bloqueo de fila del vehículo). Recarga la carga y repite la corrección.

**¿Por qué el km/L de una carga sale vacío (`null`)?**
Falta el odómetro de esa carga, o de la carga anterior con odómetro del mismo vehículo (la primera carga con
odómetro de la serie nunca tiene km/L propio), o la distancia calculada no es positiva. El km/L y el costo/km se
calculan al leer, sobre la serie activa completa del vehículo, no se guardan.

### Tarifas del chofer y política de pago

**¿Qué significa "El chofer fue eliminado; sus tarifas y viajes solo se consultan."?**
El chofer está en el estatus terminal `INACTIVE` ("Eliminar chofer"). Ya no se le agregan, editan ni cierran
tarifas ni viajes nuevos; su historial y sus viajes existentes se siguen viendo.

**¿Qué significa "Tarifa no encontrada."?**
El `id` de la tarifa no pertenece al chofer de la URL (es de otro chofer, o de otro tenant). Las tres tarifas
(entrega, intento, viaje) se consultan siempre bajo `/drivers/{publicId}/...`, nunca por id suelto.

**¿Qué significa "Indique el servicio y el tipo de paquete de la tarifa."?**
Al agregar una tarifa por entrega faltó `serviceType` o `packageType`; ambos son obligatorios y exactos (no hay
comodín "cualquier paquete").

**¿Qué significa "El chofer ya tiene una tarifa vigente para {Servicio} + {Paquete}; edite esa tarifa o ciérrela antes de agregar otra."?**
Ya existe una tarifa por entrega abierta (o vigente en la fecha indicada) para ese mismo par servicio+paquete.
Edítala (`PATCH`, cierra y abre una versión nueva) o ciérrala (`.../close`) antes de agregar otra.

**¿Qué significa "El servicio y el paquete se fijan al crear la tarifa; quite la fila y cree una nueva."?**
Enviaste `serviceType`/`packageType` en un `PATCH` de tarifa por entrega. Esos dos campos son inmutables: cierra la
fila y crea una nueva con la combinación correcta.

**¿Qué significa "La tarifa ya está cerrada; agregue una nueva si necesita volver a pagarla."?**
Intentaste editar o cerrar una fila de tarifa (entrega, intento o viaje) que ya tiene fecha de cierre. Agrega una
fila nueva.

**¿Qué significa "El intento {n} no existe; los niveles configurados van de 1 a {N}."?**
El número de intento que enviaste supera los niveles configurados en la política de la compañía
(`DriverPayPolicy.AttemptLevels`, tope 20). Sube el número de niveles con `POST /driver-pay-policy/attempt-levels`
antes de fijar la tarifa de un intento más alto.

**¿Qué significa "El chofer ya tiene una tarifa vigente para el intento {n}; edite esa tarifa o ciérrela antes de agregar otra."?**
Igual que la de entrega, pero para el nivel de intento indicado.

**¿Qué significa "El intento {n} tiene una tarifa vigente hasta el {fecha}; la nueva debe empezar en esa fecha o después."?**
Ese nivel de intento tiene una fila **ya cerrada a futuro**; la nueva vigencia que intentas abrir no puede empezar
antes de que termine esa fila. Usa esa fecha o una posterior.

**¿Qué significa "Se alcanzó el máximo de 20 niveles de intento."?**
`AttemptLevels` de la compañía ya está en el tope (20). No se pueden agregar más niveles de intento (ni tampoco se
quitan en este lote).

**¿Qué significa "Fórmula de pago desconocida: 'X'."?**
El código de fórmula (`payoutFormula`/`formula`) no es uno de `DELIVERY_PLUS_ATTEMPTS`, `DELIVERY_INCLUDES_FIRST` o
`FAILED_REPLACES_DELIVERY`.

**¿Qué significa "Sin servicios especiales: agréguelos en Clientes y contratos antes de configurar tarifas por viaje."?**
Tu compañía no tiene ningún tipo de servicio especial activo (capítulo 02, sección 6): el "tipo de viaje" de las
tarifas por viaje es ese mismo catálogo. Crea al menos uno antes de configurar tarifas por viaje.

**¿Qué significa "El tipo de viaje es obligatorio."?**
Al agregar una tarifa por viaje (o un viaje manual) faltó `specialServiceTypeId`.

**¿Qué significa "El tipo de viaje se fija al crear la tarifa; quite la fila y agregue una con el tipo correcto."?**
Enviaste `specialServiceTypeId` en un `PATCH` de tarifa por viaje. Ese campo es inmutable: cierra la fila y agrega
una nueva con el tipo correcto.

**¿Qué significa "El chofer ya tiene una tarifa vigente para el tipo de viaje '{tipo}'; edite esa tarifa o ciérrela antes de agregar otra."?**
Igual que la de entrega/intento, pero para ese tipo de viaje.

**Hice una vista previa de pago y una línea sale en $0 con la nota "sin tarifa configurada". ¿Es un error?**
No: es intencional (R15). Cuando falta la tarifa de la entrega o de un intento, esa línea se muestra en $0 con la
nota, en vez de omitirse, para que el vacío quede visible y se pueda corregir configurando la tarifa que falta.

### Viajes pagados al chofer y entrega especial con chofer

**¿Qué significa "Viaje no encontrado."?**
El `tripPublicId` no pertenece al chofer de la URL (es de otro chofer o de otro tenant).

**¿Qué significa "El viaje nace de una entrega especial; cancele la orden o reasigne el chofer."?**
Un viaje ligado a una orden (entrega especial) vigente no se cancela directamente desde
`/drivers/{id}/trips/{tripId}/cancel`. Cancela la orden de transporte, o asigna la entrega a otro chofer
(`POST /orders/{id}/driver`): el viaje anterior se cancela automáticamente.

**¿Qué significa "El chofer no está disponible para despacho: {motivos}."?**
Al asignar un chofer a una entrega especial, la disponibilidad (la misma del panel de la sección
"Disponibilidad para despacho") encontró al menos un motivo bloqueante: chofer/vehículo inactivo o en un estatus
distinto del inicial, chofer sin licencia vigente, etc. Revisa `GET /api/v1/fleet/availability` para ver el detalle
de ese chofer y resuélvelo (renueva la licencia, reactívalo…) antes de reintentar.

**¿Qué significa "El chofer solo se asigna en una entrega especial."?**
Enviaste `driverPublicId` al crear una orden que **no** es una entrega especial (`isSpecialDelivery: false`). El
chofer solo se indica en entregas especiales; las demás órdenes se despachan desde Sala de despacho (módulo
posterior).

**¿Qué significa "Solo las entregas especiales se asignan a un chofer desde aquí; las demás órdenes pasan por Sala de despacho."?**
Llamaste a `POST /orders/{publicId}/driver` sobre una orden normal (no entrega especial). Ese endpoint es
exclusivo de entregas especiales.

**¿Qué significa "La entrega especial ya llegó a destino o terminó; no se puede asignar ni reasignar el chofer."?**
La orden ya pasó de `IN_TRANSIT`, o está en un estatus lateral o terminal (cancelada, en espera, etc.). El chofer ya
no se puede asignar ni reasignar en ese punto.

**¿Qué significa "La orden ya está asignada a ese chofer."?**
Intentaste asignar el mismo chofer que ya tiene el viaje vigente de esa orden. No hay nada que cambiar.

**¿Qué significa "La orden ya tiene un viaje vigente; recargue e intente de nuevo."?**
Dos asignaciones (o reasignaciones) simultáneas a la misma orden chocaron. Recarga la ficha de la orden y repite.

**Reasigné la entrega a otro chofer, ¿qué pasa con el viaje del chofer anterior?**
Se cancela automáticamente (queda `CANCELLED` e `isActive: false`) antes de crear el viaje del chofer nuevo: la
base de datos garantiza que solo hay un viaje vigente por orden en todo momento.

**¿Por qué el historial de la orden muestra varios cambios de estatus seguidos al asignarle un chofer?**
La asignación avanza la orden **etapa por etapa** (sin saltos) desde donde estaba hasta `IN_TRANSIT`, respetando el
pipeline configurado por tu compañía (salta las etapas que tengas deshabilitadas). Cada etapa queda como un
registro de historial propio, con el mismo comentario ("Entrega especial asignada a …" o "Reasignada a …").

## Lote 5 — Trips y rutas

### Números y alta de la ruta

**¿Qué significa "Indique la fecha de la ruta."? (400)**
`POST /api/v1/trips`, `PATCH /api/v1/trips/{publicId}` (cuando cambia la fecha), `POST /api/v1/trips/reassign-zone`
y `POST /api/v1/trips/plan-day` exigen `planDate`. Envíala en formato fecha (`"2026-09-28"`).

**¿Qué significa "La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días."? (400)**
La fecha del plan solo se acepta entre ayer y hoy + 60 días. No se pueden crear ni mover rutas a fechas muy
lejanas en el pasado o en el futuro. Corrige `planDate` (o `dispatchZoneIds`/`planDate` de "Planificar el día").

**¿Qué significa "La hora de salida debe caer en la fecha de la ruta (±12 h por zona horaria)."? (400)**
`plannedStartUtc` debe caer dentro de la fecha del plan, con un margen de 12 horas hacia atrás y hacia adelante
(para cubrir zonas horarias). Si no envías nada, la ruta usa 12:00 UTC (08:00 hora de Puerto Rico) de esa fecha.

**¿Qué significa "Ya existe una ruta con ese número."? (409)**
Dos altas simultáneas chocaron en el número consecutivo (muy raro: el contador se bloquea internamente hasta el
commit). Reintenta la solicitud; el sistema le dará el siguiente número disponible.

**¿Por qué mi `POST /api/v1/trips` con zona pero sin chofer no trae ningún chofer asignado?**
El chofer "estándar" de la zona solo se asigna si hay **exactamente un** chofer activo con esa zona como zona
primaria y está disponible ese día, y el módulo `CATALOG` está encendido. Si hay cero, dos o más choferes con esa
zona primaria, o el único candidato no está disponible, la ruta nace sin chofer (no es un error) y lo asignas a
mano con `PATCH`.

### Cabecera de la ruta, reasignación y planificación del día

**¿Qué significa "El número de la ruta se fija al crearlo; no se puede cambiar."? (400)**
El `PATCH /api/v1/trips/{publicId}` no acepta `code` ni `tripCode` en el cuerpo. Quita esa llave del JSON; el
número no se edita nunca.

**¿Qué significa "El estatus de la ruta cambia con sus acciones (optimizar, despachar, eliminar)."? (400)**
No existe un `PATCH` que cambie el estatus directamente (`status`/`statusCode`/`toCode`): el estatus se mueve
optimizando, despachando, registrando la salida o eliminando la ruta. Quita esas llaves del cuerpo.

**¿Qué significa "Ese campo no se puede modificar."? (400)**
El cuerpo del `PATCH` trae `tenantId`, `version`, `routeVersion` u `originWarehouseId`: ninguno se edita por esta
vía. El tenant sale de tu sesión; la versión de la ruta cambia solo al optimizar.

**¿Qué significan "Indique el chofer o quítelo, no ambos.", "Indique el vehículo o quítelo, no ambos.", "Indique
la zona o quítela, no ambas." e "Indique la hora de salida o quítela, no ambas."? (400)**
Enviaste a la vez el valor nuevo (`driverPublicId`, `vehiclePublicId`, `dispatchZoneId`, `plannedStartUtc`) y su
bandera de "quitar" (`clearDriver`, `clearVehicle`, `clearZone`, `clearPlannedStart`). Envía solo uno de los dos.

**¿Qué significa "Una ruta despachada debe conservar chofer, vehículo y hora de salida; cámbielos en lugar de
quitarlos."? (422)**
Tu compañía habilitó la capacidad `EDIT_TRIP` en `DISPATCHED` o `IN_PROGRESS`, así que la cabecera de una ruta
despachada se puede corregir (por ejemplo, cambiar el chofer si el camión se averió). Pero una ruta despachada
**no se puede quedar sin chofer, sin vehículo ni sin hora de salida**: el chofer ya la ve en su app y las ETAs
dependen de la salida. En el `PATCH /api/v1/trips/{publicId}` envía el chofer, el vehículo o la hora **nuevos**
(`driverPublicId`, `vehiclePublicId`, `plannedStartUtc`) en lugar de `clearDriver`, `clearVehicle` o
`clearPlannedStart`. Por la misma razón, registrar la salida (`POST /trips/{publicId}/start`) de una ruta sin
chofer o sin vehículo responde 422 "La ruta … no se puede despachar: …".

**¿Qué significa "La fecha y la zona de una ruta despachada no se cambian."? (422)**
Aunque tu compañía habilitó `EDIT_TRIP` para el estatus actual de la ruta, la fecha del plan y la zona de despacho
de una ruta **ya despachada** nunca se editan (cambiarían por completo el sentido de la ruta). Si necesitas mover
las entregas a otro día o zona, crea o usa otra ruta.

**¿Por qué la reasignación en bloque (`POST /api/v1/trips/reassign-zone`) no cambió el chofer de algunas rutas
abiertas?**
La reasignación solo toca las rutas **abiertas** (`DRAFT` o `PLANNED`, activas) de esas zonas y esa fecha, y además
respeta la capacidad `EDIT_TRIP`: si tu compañía la negó para el estatus de una ruta
(`/status/capabilities/TRIP`), esa ruta se omite, igual que las ya despachadas, porque tampoco se le podría cambiar
el chofer con un `PATCH`. La respuesta lista en `trips` solo las rutas actualizadas y `tripsUpdated` las cuenta
(puede ser 0, con 200). Si necesitas reasignarlas, vuelve a permitir `EDIT_TRIP` en ese estatus.

**¿Qué significan "Indique al menos una zona.", "Máximo 50 zonas por reasignación." e "Indique el chofer."? (400)**
`POST /api/v1/trips/reassign-zone` exige `dispatchZoneIds` con al menos una zona (máximo 50) y `driverPublicId`.
Completa el que falte.

**¿Qué significa "La ruta {código} está cerrada; solo se consulta."? (422)**
La ruta fue eliminada (`CANCELLED`, `isActive: false`) o ya terminó (`COMPLETED`). No admite `PATCH`, agregar
órdenes ni un segundo `DELETE`; su ficha sigue disponible para consulta. En el listado `GET /api/v1/trips` las
rutas eliminadas no aparecen salvo que pidas `includeCancelled=true` o filtres `status=CANCELLED`.

**¿Qué significa "La ruta {código} ya fue despachada; no se puede editar ni eliminar."? (422)**
La ruta está `DISPATCHED` o `IN_PROGRESS` y su estatus no tiene la capacidad `EDIT_TRIP` habilitada (el valor de
fábrica). No admite `PATCH` de cabecera, agregar/quitar órdenes, optimizar, reordenar, pin manual ni `DELETE`.
Si necesitas corregir el chofer, el vehículo o la hora de salida de una ruta ya despachada, pídele a un
administrador que habilite `EDIT_TRIP` para ese estatus en `PUT /api/v1/status/capabilities/TRIP`.

**¿Qué significa "Estatus de ruta desconocido: 'X'."? (400)**
El filtro `status` de `GET /api/v1/trips` lleva un código que no existe en `TripStatus` (`DRAFT`, `PLANNED`,
`DISPATCHED`, `IN_PROGRESS`, `COMPLETED`, `CANCELLED`). Corrige el código; puedes repetir `status` para filtrar
por varios a la vez.

**¿Qué significa "La zona de despacho está inactiva." al planificar el día o al crear/editar una ruta? (400)**
Una de las zonas indicadas está dada de baja. Quítala de la lista o reactívala primero en `dispatch-zones`. En
"Planificar el día", si no envías `dispatchZoneIds`, se planifican **todas las zonas activas** de tu compañía y
las inactivas simplemente se ignoran, sin error.

**¿Qué significa "Ruta no encontrada."? (404)**
El `publicId` no corresponde a ninguna ruta **de tu compañía**, ya sea porque no existe o porque es de otro
tenant. Por seguridad, ambos casos dan el mismo `404` (nunca se revela si la ruta existe en otra compañía).

### Órdenes en la ruta y lista "Sin asignar"

**¿Qué significa "Indique al menos una orden." y "Agregue como máximo 200 órdenes por solicitud."? (400)**
`POST /api/v1/trips/{publicId}/orders` exige al menos un `orderPublicIds` y admite como máximo 200 distintos por
solicitud (los duplicados se colapsan solos). Si necesitas agregar más, hazlo en varias llamadas.

**¿Qué significa "Orden no encontrado."? (404)**
El `orderPublicId` no es de tu compañía, no existe, está inactivo, o (al quitar) esa orden no está registrada en
absoluto. Verifica el id; si la orden existe pero no está en ESA ruta, el mensaje es otro (ver abajo).

**¿Qué significa "Hay órdenes que no se pueden asignar a la ruta."? (422)**
Al agregar varias órdenes de una vez, **una sola** que no sea elegible rechaza la solicitud **completa** (nada se
agrega): revisa `errors`, que trae el número de cada orden problemática y su motivo exacto (ver la tabla de
elegibilidad más abajo). Quita esas órdenes de la solicitud, o resuelve su causa (confírmalas, sácalas de un
lateral, etc.) y vuelve a intentar.

**¿Cuáles son los motivos exactos por los que una orden no es elegible para una ruta?**
- `La orden está en Entrada; confírmela antes de asignarla a una ruta.` — todavía está en `DRAFT` (etapa inicial).
- `Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho.` — usa
  `POST /api/v1/orders/{publicId}/driver` (capítulo 04).
- `La orden está en '{estatus}'; regrésela al pipeline antes de asignarla a una ruta.` — está en un lateral
  (`ON_HOLD`, `PARTIAL`, `FAILED`, …); primero regrésala al pipeline normal.
- `La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.` — está inactiva, en una etapa terminal, o
  ya llegó a `IN_TRANSIT` o después.
- `El estatus actual no permite la acción 'ASSIGN_TRIP'.` — tu compañía apagó esa capacidad para ese estatus
  (`PUT /api/v1/status/capabilities/TRANSPORT_ORDER`).
- `La orden no tiene una parada de entrega pendiente.` — no le queda ninguna parada `DELIVERY` sin terminar.

**¿Qué significa "La orden ya está en esta ruta."? (409)**
Intentaste agregar una orden que ya tiene una parada vigente en la misma ruta. No hay nada que hacer; ya está.

**¿Qué significa "La orden ya está asignada a la ruta {código}."? (409)**
La orden ya tiene una ruta vigente **distinta** de la que estás editando. Quítala de esa ruta primero
(`DELETE .../orders/{orderPublicId}`) si quieres moverla a otra.

**¿Qué significa "La orden ya está asignada a otra ruta." al agregar una orden? (409)**
Dos altas simultáneas a la misma orden (en rutas distintas, o desde el escaneo Outbound y Sala de despacho a la
vez) chocaron; la que llegó primero ganó. Recarga la lista "Sin asignar" y repite si aún la necesitas en tu ruta.

**¿Qué significa "Una ruta admite como máximo 300 paradas."? (400)**
Es el tope técnico duro de este lote. Divide la carga en dos rutas si necesitas más de 300 paradas.

**¿Qué significa "La orden no está en esta ruta."? (404)**
Intentaste quitar (`DELETE .../orders/{orderPublicId}`) una orden que no tiene una parada vigente en ESA ruta
(puede estar en otra ruta, o en ninguna). Verifica en qué ruta está realmente antes de quitarla.

**¿Qué significa "Use dispatchZoneId o noZone, no ambos."? (400)**
El filtro de "Sin asignar" (`GET /api/v1/trips/unassigned-orders`) no acepta `dispatchZoneId` y `noZone=true` a la
vez: usa uno u otro.

**¿Qué significa "El rango de fechas es inválido." en "Sin asignar"? (400)**
`requestedFrom` es posterior a `requestedTo`. Corrige el orden del rango.

### Optimización, secuencia manual y pin por parada

**¿Qué significa "La ruta no tiene paradas que optimizar."? (422)**
La versión vigente de la ruta no tiene ninguna orden asignada. Agrega al menos una orden antes de optimizar; no se
crea ninguna corrida cuando pasa esto.

**¿Qué significa "La ruta cambió mientras se optimizaba; vuelva a optimizar."? (409)**
La optimización corre en tres fases y el motor calcula **sin bloquear la ruta**: entre que empezó y que terminó,
alguien más la modificó (agregó/quitó una orden, la reordenó a mano, la editó, u otra optimización llegó primero).
La corrida queda registrada con `ERROR`. Simplemente vuelve a pedir `POST .../optimize`: como la ruta ya está en
su estado más reciente, la nueva corrida sí debería aplicar.

**¿Qué significa "El optimizador no pudo calcular la ruta; la corrida quedó registrada con error."? (409)**
El motor (`HEURISTIC`) lanzó un error interno, tardó más de 60 segundos, o devolvió un resultado que no pasó las
validaciones (por ejemplo, una parada repetida o ajena a la solicitud). Consulta
`GET /api/v1/trips/{publicId}/optimization-runs` para ver el detalle técnico (`errorMessage`) y vuelve a intentar;
si persiste, es un caso para soporte.

**¿Qué significa "La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir."? (400)**
`PUT /api/v1/trips/{publicId}/route/sequence` exige que `routeStopIds` sea una **permutación exacta** de las
paradas de la versión vigente de esa ruta: ni de más, ni de menos, ni repetidas, ni de otra ruta (u otra versión ya
archivada). Pide primero la ficha (`GET .../{publicId}`) para tomar los `id` correctos de `stops`.

**¿Qué significan "Indique la latitud y la longitud.", "La latitud debe estar entre -90 y 90." y "La longitud
debe estar entre -180 y 180."? (400)**
`PUT /api/v1/trips/{publicId}/stops/{routeStopId}/location` exige `lat` y `lng` numéricos dentro de rango. Revisa
que no estén invertidos (latitud y longitud al revés es el error más común).

**¿Qué significa "Parada no encontrada en esta ruta."? (404)**
El `routeStopId` no pertenece a la versión **vigente** de esa ruta (es de otra ruta, de otro tenant, o de una
versión ya archivada por una optimización posterior). Vuelve a pedir la ficha para tomar los ids vigentes.

**¿Por qué después de fijar el pin manual de una parada cambiaron las ETAs de las paradas siguientes?**
El pin manual recalcula toda la ruta con la nueva coordenada (misma fórmula que la optimización: haversine × 1.3 a
35 km/h), así que los tramos y horarios posteriores a esa parada se ajustan. Es el comportamiento esperado.

### Despacho y salida

**¿Qué significa "La ruta {código} no se puede despachar: {motivos}."? (422)**
El despacho tiene uno o más **bloqueantes** activos; `errors` trae el código de cada uno (`NO_DRIVER`,
`NO_VEHICLE`, `NO_STOPS`, `DRIVER_UNAVAILABLE`, `VEHICLE_UNAVAILABLE`, `ORDER_NOT_ELIGIBLE`) con su mensaje. Antes
de despachar, revisa `GET /api/v1/trips/dispatchable` o la ficha de la ruta (`issues`) para ver exactamente qué
falta: asignar chofer/vehículo, resolver la disponibilidad, agregar paradas o resolver la orden no elegible
(sección de elegibilidad de arriba).

**¿Qué significa "El pipeline de rutas de esta compañía no tiene habilitada la etapa DISPATCHED." (o "ACTIVE")? (422)**
Tu compañía deshabilitó esa etapa en `PUT /api/v1/status/capabilities` o en el pipeline de `TripStatus`/
`RouteStatus`. Sin `DISPATCHED` habilitado no se puede despachar ninguna ruta; sin `ACTIVE` la versión de la ruta
no puede congelarse al despachar. Vuelve a habilitar la etapa si necesitas usar el despacho.

**¿Qué significa "Orden {número}: {motivo}" al despachar? (422)**
Una orden que estaba en la ruta dejó de ser elegible entre que se armó el selector y el momento del despacho (por
ejemplo, alguien la canceló, la mandó a un lateral, o tu compañía le apagó `ASSIGN_TRIP`). El motivo es el mismo
catálogo de la sección de elegibilidad. Quita esa orden de la ruta o resuelve la causa, y vuelve a despachar.

**¿Qué significa "Seleccione al menos una ruta." y "Máximo 50 rutas por despacho."? (400)**
El despacho en lote (`POST /api/v1/trips/dispatch`) exige `tripPublicIds` con al menos una ruta, hasta un máximo
de 50 por solicitud. Divide la selección si necesitas despachar más.

**¿Un id ajeno o inexistente en el despacho en lote hace fallar toda la solicitud?**
No. El despacho en lote responde siempre `200`; cada ruta se procesa en su propia transacción. Un id que no
corresponde a ninguna ruta tuya queda en la lista de resultados con `dispatched: false` y
`error: "Ruta no encontrada."`; las demás rutas del lote se despachan normalmente.

**¿Qué significa "La ruta {código} no está despachada; despáchela antes de registrar su salida."? (422)**
`POST /api/v1/trips/{publicId}/start` exige que la ruta esté `DISPATCHED`. Si sigue en `DRAFT`/`PLANNED`,
despáchala primero con `POST .../dispatch`.

**¿Qué significa "La ruta {código} ya salió."? (422)**
La ruta ya está `IN_PROGRESS`: la salida ya se registró una vez y no se repite. Consulta la ficha para ver
`actualStartUtc`.

**¿Qué significa "La orden va en la ruta {código} ya despachada; no se puede cancelar mientras la ruta esté en
curso."? (422)**
Intentaste cancelar una orden que va en una ruta `DISPATCHED`/`IN_PROGRESS`. Una vez despachada, la ruta y sus
órdenes quedan comprometidas con el chofer; para resolverlo, coordina con Despacho (quitarla de la ruta requiere
que la ruta esté abierta, o esperar al cierre en el Lote 7). Si la orden va en una ruta `DRAFT`/`PLANNED`
(todavía no despachada), cancelarla la libera automáticamente sin este error.

### Zonas de despacho: miembros y resolución "código postal/pueblo → zona"

**¿Qué significa "Indique el criterio de la zona (POSTAL_CODE, POSTAL_RANGE o MUNICIPALITY)." y "Criterio de zona
desconocido: 'X'."? (400)**
`POST /api/v1/dispatch-zones/{id}/members` exige `matchType` con uno de esos tres valores exactos (en mayúsculas).
`POLYGON` existe como catálogo pero todavía no se soporta (ver el siguiente mensaje).

**¿Qué significa "Las zonas por polígono todavía no se soportan; use código postal, rango postal o municipio."? (400)**
Este lote solo resuelve zonas por código postal exacto, rango postal o municipio. Define el territorio con uno de
esos tres criterios en su lugar.

**¿Qué significan "Indique el valor del criterio." e "Indique el municipio."? (400)**
Falta `matchValue` en el alta del miembro de la zona. Para `MUNICIPALITY` el mensaje nombra el municipio
específicamente; para `POSTAL_CODE`/`POSTAL_RANGE` es el mensaje genérico.

**¿Qué significa "El código postal debe tener 5 dígitos (ej. 00949)."? (400)**
`matchValue` de un criterio `POSTAL_CODE` no tiene la forma de 5 dígitos. Se acepta el formato ZIP+4
(`"00949-1234"`), que se recorta automáticamente a los primeros 5 dígitos.

**¿Qué significa "El rango postal debe tener la forma 00900-00999 (inicio menor o igual que el fin)."? (400)**
`matchValue` de un criterio `POSTAL_RANGE` debe ser dos códigos postales de 5 dígitos separados por un guion, con
el primero menor o igual que el segundo.

**¿Qué significa "El municipio admite como máximo 120 caracteres."? (400)**
El nombre del municipio en `matchValue` es demasiado largo. Los municipios se comparan sin acentos ni mayúsculas
("Bayamón" = "bayamon" = "BAYAMON"), así que no hace falta escribirlos de una forma particular.

**¿Qué significa "La zona ya tiene ese criterio."? (409)**
Ya existe exactamente ese criterio (mismo tipo y mismo valor normalizado) en la misma zona. No hace falta
agregarlo de nuevo.

**¿Qué significa "El valor '{v}' ya pertenece a la zona {código}."? (409)**
Dos zonas **activas** no pueden compartir territorio: el código postal, el rango o el municipio que intentas
agregar (o que tiene un miembro de una zona que estás reactivando) ya lo cubre otra zona activa. Quita el criterio
de la otra zona primero, o inactiva la zona dueña, si de verdad el territorio cambió de zona.

**¿Qué significa "Criterio de zona no encontrado."? (404)**
El `memberId` que intentas borrar no pertenece a ESA zona (`{id}` de la URL): es de otra zona, o de otro tenant.
Verifica que estás usando el `id` de la zona correcta.

**¿Qué significa "Indique el código postal o el pueblo."? (400)**
`GET /api/v1/dispatch-zones/resolve` exige al menos uno de `postalCode` o `city`.

**¿Por qué `resolve` devuelve `dispatchZoneId: null` sin dar error?**
No es un error: significa que ninguna zona activa tiene un miembro que cubra ese código postal ni ese pueblo (o
que el resultado quedó `ambiguous: true` porque dos zonas empatan en el mismo nivel de precedencia — CP exacto,
luego rango postal, luego municipio). En ese caso, agrega el criterio que falta a la zona correspondiente, o
resuelve el empate quitando el criterio de una de las dos zonas.

**¿Qué significa "La zona tiene rutas abiertas; ciérrelas o cámbielas de zona antes de inactivarla."? (409)**
No se puede inactivar una zona de despacho mientras tenga rutas en `DRAFT`/`PLANNED` (además de la regla ya
existente del capítulo 04: no se puede inactivar con choferes activos asignados como primaria). Elimina o
reasigna esas rutas a otra zona primero.

### Estación de escaneo Outbound

**¿Qué significan "Escanee o escriba un código." y "El código no puede exceder 40 caracteres."? (400)**
`POST /api/v1/scan/outbound` exige `code` no vacío y de hasta 40 caracteres. Vuelve a escanear o escribe el código
a mano.

**¿Qué significa que el escaneo responda `NOT_FOUND` con "No se encontró la orden."?**
El código escaneado (número de orden, empaque o factura) no coincide con ninguna orden de tu compañía. Revisa que
sea el código correcto; el escaneo nunca da `404`, siempre `200` con este resultado tipado.

**¿Qué significa `NOT_FOUND` "Hay varias órdenes con ese código; escanee el empaque."?**
El código escaneado (por ejemplo, un número de factura del cliente) coincide con **más de una** orden. Escanea en
su lugar el número de empaque, que sí identifica una sola orden.

**¿Qué significa `ALREADY_ASSIGNED` "Ya estaba en la ruta {código}."?**
La orden ya tiene una ruta vigente (la misma que le acabas de escanear, u otra). No se vuelve a asignar; el
resultado solo confirma cuál es su ruta actual. La estación pronuncia "dup" para que el operador sepa que no hace
falta escanearla de nuevo.

**¿Qué significa `NOT_ELIGIBLE` al escanear una orden?**
La orden no cumple la elegibilidad para rutas (ver la tabla de motivos de la sección "Órdenes en la ruta"): por
ejemplo, sigue en Entrada sin confirmar, está en un estatus lateral, o su estatus no tiene `ASSIGN_TRIP`
habilitado. El mensaje trae el motivo exacto y la estación pronuncia "notfound".

**¿Qué significa `FOUND_UNASSIGNED` "No se pudo resolver la zona de despacho por código postal ni pueblo; queda
sin asignar."?**
La dirección de entrega de la orden no coincide con ningún criterio de ninguna zona activa. Agrega un miembro
(código postal, rango o municipio) a la zona correspondiente; mientras tanto, la orden queda "sin asignar" y se
puede agregar a mano desde Sala de despacho.

**¿Qué significa `FOUND_UNASSIGNED` "El código postal o pueblo pertenece a varias zonas ({códigos}); queda sin
asignar."?**
Dos zonas activas empatan en el mismo nivel de precedencia para esa dirección (por ejemplo, el mismo municipio
está registrado en dos zonas). Resuelve el empate quitando el criterio de una de las dos zonas.

**¿Qué significa `FOUND_UNASSIGNED` "No hay ruta abierta para la zona {código} en la fecha {fecha}; queda sin
asignar."?**
La zona de la orden se resolvió correctamente, pero no hay ninguna ruta `DRAFT`/`PLANNED` de esa zona para esa
fecha. Crea una ruta para esa zona y fecha (a mano, o con "Planificar el día") y vuelve a escanear: el escaneo
nunca crea rutas por sí mismo.

### Planificar el día

**¿Qué significa "Máximo 50 zonas por planificación."? (400)**
`POST /api/v1/trips/plan-day` acepta como máximo 50 zonas en `dispatchZoneIds` por solicitud. Divide la
planificación en varias llamadas, o no envíes la lista para planificar todas las zonas activas de una vez.

**¿"Planificar el día" crea una ruta nueva por zona cada vez que lo ejecuto?**
No: es **idempotente**. Si ya hay una ruta abierta (`DRAFT`/`PLANNED`, activa) de esa zona y esa fecha, la
reutiliza (la de menor número si hubiera más de una) y solo agrega las órdenes que sigan sin ruta vigente.
Repetirlo varias veces seguidas no crea rutas de más ni duplica órdenes.

**¿Por qué algunas zonas sin órdenes no generan ninguna ruta al planificar el día?**
Por defecto, "Planificar el día" solo crea una ruta nueva si hay al menos una orden asignable en esa zona. Si
necesitas que el escaneo Outbound tenga una ruta destino desde temprano aunque todavía no haya órdenes, envía
`createEmptyTrips: true`.

**¿Qué significa el aviso `CAPACITY_HARD_CAP` "La ruta llegó al máximo de 300 paradas; la orden queda sin
asignar." en el resultado de "Planificar el día"?**
No es un error HTTP: es un aviso informativo por zona. Esa orden en particular habría hecho que la ruta pasara de
300 paradas (el tope técnico), así que se dejó sin asignar; agrégala a mano a otra ruta de la misma zona.

### Monitoreo

**¿Por qué al buscar en el monitor (`GET /api/v1/trips/monitor?search=…`) los totales no cambian?**
Es a propósito: `totals` siempre refleja **todas** las rutas de la fecha y la zona filtradas, sin importar el
texto de búsqueda; `search` solo acota la lista `trips` que se muestra. Así el resumen del día no "parpadea" al
escribir en el buscador.

### Permisos y RBAC

**¿Qué diferencia hay entre `trips.view`, `trips.plan`, `trips.optimize`, `trips.dispatch` y `trips.scan`?**
`trips.view` es de solo lectura (listado, ficha, "Sin asignar", corridas de optimización, monitor).
`trips.plan` cubre crear/editar/eliminar la ruta, agregar/quitar órdenes, reordenar, el pin manual, la
reasignación en bloque y "Planificar el día". `trips.optimize` es exclusivo para lanzar la optimización
automática. `trips.dispatch` cubre el selector de despacho, despachar y registrar la salida (y también asignar el
chofer de una entrega especial, capítulo 04). `trips.scan` es solo para la estación de escaneo Outbound. Un rol
puede tener cualquier combinación; por ejemplo, el Operador de almacén trae `trips.view` y `trips.scan` pero no
`trips.plan` ni `trips.dispatch`.

**¿Por qué `POST /api/v1/contacts/OPTIMIZATION_RUN/{id}` o `PUT /api/v1/custom-fields/values/OPTIMIZATION_RUN/{id}`
responden 404 aunque la corrida existe?**
Las corridas de optimización son una bitácora de **solo lectura**: se consultan con
`GET /api/v1/trips/{publicId}/optimization-runs` (permiso `trips.view`), pero no admiten contactos ni valores de
campos personalizados por id suelto. Para esos endpoints toda corrida responde **404**
(`OPTIMIZATION_RUN` usa el resolver cerrado, igual que `DRIVER_RATE` y `FLEET_DOCUMENT`), exista o no: así nadie
puede escribir sobre una corrida ni averiguar qué ids existen. En campos personalizados, quien no tiene
`trips.view` recibe antes **403** ("Falta el permiso 'trips.view'."), también sin revelar si la corrida existe.
Si necesitas anotar algo sobre una optimización, hazlo en la ruta (`TRIP`), que sí admite contactos y campos
personalizados con el permiso `trips.plan`.

## Lote 6 — Inventario y almacén

### Almacenes y ubicaciones

**¿Qué significa "El código solo admite letras, números, guion y guion bajo (máximo 30)."? (400)**
El código de un almacén, zona o muelle solo acepta `A-Z`, `0-9`, `-` y `_`, hasta 30 caracteres (se guarda en
mayúsculas). Corrige el código en la solicitud.

**¿Qué significa "Indique el código de la posición o su pasillo/rack/nivel/posición."? (400)**
Al crear una posición hay que dar un código explícito o al menos una de sus partes (pasillo, rack, nivel,
posición): con las partes, el sistema arma el código compuesto (`A01-R02-N3-P04`).

**¿Qué significa "Ya existe un almacén con ese código."? (409)**
El código de almacén es único por compañía. Elige otro código; el existente no se puede reutilizar aunque esté
dado de baja.

**¿Qué significa "El código del almacén no se puede cambiar."? (400)**
El código de un almacén (o de un muelle: `El código del muelle no se puede cambiar.`) es inmutable una vez creado. Si lo
escribiste mal, da de baja el almacén y crea uno nuevo con el código correcto (solo si sigue vacío). El código de una
**zona** sí se puede editar desde el Lote 11 (ver la sección "Lote 11" al final).

**¿Qué significa "El almacén {code} tiene inventario o documentos abiertos; no se puede dar de baja."? (409)**
La baja de un almacén es **definitiva**: solo procede si no tiene saldo (en mano ni reservado) y no tiene recibos,
conteos, tareas, recolecciones, planes de cruce de muelle o citas abiertas. La respuesta trae `errors` con el
detalle por tipo (`inventory`, `receipts`, `cycleCounts`, `tasks`, `pickBatches`, `crossDockPlans`,
`appointments`): cierra o vacía lo que aparezca ahí antes de reintentar.

**¿Qué significa "El almacén está dado de baja; solo se consulta."? (422)**
Intentaste editar o volver a dar de baja un almacén ya inactivo. La baja de almacén no tiene reversa: si necesitas
operar en él, da de alta un almacén nuevo.

**¿Qué significa "La posición {code} tiene inventario; no se puede desactivar."? (409)**
Mueve o retira el inventario de esa posición (transferencia o ajuste de salida) antes de desactivarla.

**¿Qué significa "Indique el almacén: la compañía tiene más de uno."? (400)**
Varias acciones (recibir, recolectar, contar, crear una orden de compra o un plan de cruce de muelle) usan el
único almacén activo del tenant si no se indica ninguno. Con más de un almacén activo hay que enviar
`warehousePublicId` explícitamente.

### Productos y categorías

**¿Qué significa "Ya existe un producto con ese SKU para ese dueño."? (409)**
El SKU es único **por dueño**: el mismo SKU puede repetirse entre un producto propio del tenant y uno de un
cliente 3PL distinto, pero no dos veces para el mismo dueño. Cambia el SKU o revisa si ya existe el producto.

**¿Qué significa "No se puede cambiar {el tipo de seguimiento / la unidad de medida base / el dueño} de un
producto que ya tiene movimientos."? (409)**
El tipo de seguimiento (NONE/LOT/SERIAL), la unidad de medida base y el dueño (`ClientId`) del producto se fijan
en la práctica desde el primer movimiento del ledger (una recepción, un ajuste, etc.). Si el producto todavía no
tiene ningún movimiento, esos campos sí se pueden editar.

**¿Qué significa "El producto {sku} tiene inventario en mano ({qty}); no se puede desactivar."? (409)**
Saca o transfiere el inventario en mano del producto (a cero) antes de darlo de baja.

**¿Qué significa "El mínimo de picking requiere una posición preferida en una zona PICKING."? (400)**
`MinPickQty` dispara el reabasto automático hacia la posición preferida del producto; esa posición tiene que
existir y estar en una zona de tipo `PICKING`. Asigna primero `preferredBinPublicId`/`preferredWarehousePublicId`
en una posición de picking, o quita `minPickQty`.

### Inventario, Kárdex y ajustes

**¿Qué significa "Inventario insuficiente de {sku} en {bin}: disponible {x}, solicitado {y}."? (409,
`insufficient_stock`)**
Un despacho, transferencia, ajuste de salida o recolección pidió más de lo disponible (en mano menos reservado)
en esa posición. La operación se revierte completa (no queda ningún movimiento parcial). Reduce la cantidad,
recolecta de otra posición o corrige el saldo con un conteo cíclico.

**¿Qué significa "El motivo {código} lo asigna el sistema."? (400)**
`RECEIPT_VARIANCE`, `COUNT_VARIANCE` y `PICK_BATCH_REVERSAL` son motivos que solo el sistema usa (recepción,
conteo y reversa de una recolección, respectivamente); también `OPENING_BALANCE` (saldo inicial de la migración, Lote 10) y
`TRACKING_CONVERSION` (conversión a serie, Lote 26). Un ajuste manual capturado por un usuario debe usar otro
motivo del catálogo `AdjustmentReason` (`DAMAGE`, `LOSS`, `FOUND`, `EXPIRED`, `PO_SHORTAGE`, `OTHER`).

**¿Qué significa "El origen y el destino no pueden ser la misma posición."? (400)**
Una transferencia mueve inventario entre dos posiciones distintas (pueden ser de almacenes distintos). Si
necesitas "mover" dentro de la misma posición, no hay nada que hacer: ya está ahí.

**¿Qué significa "El lote {n} ya existe con otras fechas; corrija las fechas o use otro número de lote."? (409)**
El número de lote ya existe para ese producto con fechas de fabricación/vencimiento distintas a las que
capturaste. El sistema nunca sobrescribe las fechas de un lote existente: usa el mismo número sin capturar fechas
(se reutiliza tal cual) o da de alta un número de lote distinto.

**¿Qué significa "La reserva a liberar excede lo reservado."? (409)**
Se intentó liberar más cantidad reservada de la que realmente hay reservada en esa posición/lote (por ejemplo,
al cancelar una asignación de cruce de muelle dos veces). Es una guarda interna; si la ves como usuario, revisa
si la operación ya se hizo antes.

### Recepción (ASN y recibos)

**¿Qué significa "El almacén no tiene una posición de recepción (zona STAGING); indíquela."? (422)**
El almacén necesita al menos una posición activa en una zona de tipo `STAGING` para poder recibir mercancía (ahí
"aterriza" antes del putaway). Crea una zona `STAGING` con al menos una posición, o indica `stagingBinId`
explícitamente si ya existe.

**¿Qué significa "El aviso de llegada ya tiene un recibo abierto o confirmado."? (409)**
Un mismo aviso de llegada (ASN) admite un solo recibo activo a la vez. Si el recibo anterior se equivocó,
elimínalo (solo si sigue abierto: Esperado, Recibiendo o Discrepancia) antes de crear uno nuevo contra el mismo aviso.

**¿Qué significa "La orden de compra debe estar enviada o recibida parcial para recibir contra ella."? (422)**
Solo se recibe contra una PO en `SENT` o `PARTIAL`. Una `DRAFT` primero se debe enviar (`POST .../send`); una
`RECEIVED` o `CANCELLED` ya no admite más recepciones.

**¿Qué significa "El recibo {n} ya fue confirmado; no se puede modificar."? (422)**
Los recibos se confirman **completos**, no línea por línea, y una vez confirmados quedan congelados (no se
capturan más líneas ni se cambian cantidades). Si algo salió mal después de confirmar, usa un ajuste manual de
inventario o, si aplica, una devolución (`RETURN`).

**¿Qué significa "Las líneas del aviso de llegada no se eliminan; capture 0 como recibido."? (400)**
Una línea que viene del aviso de llegada (o de la orden de compra) no se borra del recibo, porque documenta lo
esperado: si al final no llegó nada, captura `receivedQty: 0` en esa línea en vez de intentar eliminarla.

**¿Qué significa "El producto {sku} no pertenece al cliente del aviso de llegada."? (400)**
Un aviso de llegada de un cliente 3PL solo admite productos cuyo dueño (`ClientId`) sea ese mismo cliente.
Verifica el dueño del producto o el cliente del aviso.

### Tareas de almacén (cola, putaway, reabasto)

**¿Qué significa "Las tareas de tipo {tipo} no se completan desde la cola."? (422)**
Este lote solo trae handlers para `PUTAWAY`, `REPLENISH`, `COUNT` y `CROSSDOCK`. Los tipos `PICK`, `PACK` y `LOAD`
todavía no tienen una pantalla ni un handler que los complete desde la cola (quedan para lotes posteriores).

**¿Qué significa "Las tareas de conteo se completan desde Conteo cíclico."? (422)**
Una tarea `COUNT` se puede **iniciar** desde la cola de tareas, pero solo se **completa** reconciliando su conteo
(`POST /api/v1/cycle-counts/{id}/reconcile`), porque ahí es donde se calculan y asientan los ajustes.

**¿Qué significa "La cantidad excede la de la tarea."? (400)**
Al completar una tarea desde la cola, la cantidad indicada no puede ser mayor que la de la tarea. Si sobró
producto, complétala por la cantidad de la tarea; el remanente (si lo hay, por menos de lo pedido) queda como una
tarea nueva automáticamente.

### Conteo cíclico

> Lote 14: "reconciliar" es ahora **Confirmar conteo y ajustar** (`POST …/reconcile`, en un paso desde Pendiente o Contado) y el estatus
> final es Concordancia o Diferencia; el mensaje 409 de abajo no cambió.

**¿Qué significa "El conteo de {sku} en {bin} ({contado}) es menor que lo reservado ({reservado}); libere la
reserva antes de reconciliar."? (409)**
Al reconciliar, lo contado no puede quedar por debajo de lo que ya está reservado en esa posición (por ejemplo,
para un cruce de muelle): el ajuste dejaría el saldo reservado por encima del saldo en mano, lo que el sistema
nunca permite. Libera esa reserva (o espera a que se mueva) antes de reconciliar esa línea.

**¿Qué significa "Faltan {n} línea(s) por contar."? (422)**
"Terminar de contar" (`OPEN → COUNTED`) exige que todas las líneas del conteo tengan una captura (cantidad o
series). Completa las que falten o elimínalas si no aplican.

**¿Por qué una línea del conteo aparece marcada `systemQtyChanged`?**
Significa que el saldo en mano se movió (por otro movimiento del ledger) entre el momento en que se tomó la foto
del sistema y el momento en que se reconcilió. El ajuste que se asentó es correcto (se calculó contra el saldo
**actual**, no contra la foto vieja), pero la marca sirve para que alguien revise si ese movimiento intermedio
tiene sentido.

### Recolección y empaque

**¿Qué significa "Una recolección solo puede tener productos de un mismo dueño."? (400)**
No se mezclan en una misma recolección productos propios del tenant con productos de un cliente 3PL, ni de dos
clientes distintos. Haz recolecciones separadas por dueño.

**¿Qué significa "Inventario insuficiente de {sku} en {where}: disponible {x}, solicitado {y}."? (409,
`insufficient_stock`) al recolectar**
No hay suficiente disponible del producto (FEFO agotado o posición/lote/serie explícito sin existencia) para
cubrir la cantidad pedida. La recolección completa se cancela, incluido el número `EMP-#####` (no queda ningún
hueco en la numeración). Reduce la cantidad o revisa el saldo disponible antes de reintentar.

**¿Qué significa "La orden de la recolección {n} ya avanzó a '{estatus}'; la recolección ya no se puede
eliminar."? (422)**
Una recolección ya empacada (`PACKED`) solo se elimina si su orden real todavía está en la **etapa inicial** (por
ejemplo, sin confirmar todavía). Si la orden ya avanzó, hay que trabajar sobre la orden directamente (ver el
siguiente mensaje).

**¿Qué significa "Esta orden nació de la recolección {n}; elimínela desde Recolección y empaque para restaurar el
inventario."? (409)**
Una orden creada al "empacar" una recolección solo se elimina desde `DELETE /api/v1/pick-batches/{publicId}`, no
desde `DELETE /api/v1/orders/{publicId}`, porque eliminarla desde ahí no revierte el inventario que salió al
recolectar. Ve a Recolección y empaque para eliminarla correctamente.

### Compras y faltantes

**¿Qué significa "El estatus actual no permite la acción 'EDIT_PURCHASE_ORDER'."? (422)**
Por defecto, una orden de compra solo se edita en `DRAFT`. Fuera de ese estatus (enviada, recibida parcial,
recibida o cancelada) la edición está negada, salvo que el administrador del tenant la habilite para `SENT` o
`PARTIAL` desde `/api/v1/status/capabilities/PURCHASE_ORDER`.

**¿Qué significa "La línea de {sku} ya tiene recepciones: no se elimina, no baja de lo recibido ({x}) y su costo
no cambia."? (400/409)**
Aunque tu compañía habilite editar una orden de compra fuera de `DRAFT`, una línea que ya recibió mercancía queda
protegida: no se puede quitar de la orden, no se puede pedir menos de lo ya recibido, y su costo unitario no
cambia (ya se usó para calcular el valor de lo recibido).

**¿Qué significa "Una orden de compra recibida completa no se cancela."? (422)**
Una orden `RECEIVED` es terminal: ya se recibió todo lo pedido (o se resolvió el faltante). No hay nada que
cancelar; si algo llegó mal, corrígelo con un ajuste de inventario.

**¿Qué significa "La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar."? (409)**
No se puede cancelar una orden de compra mientras tiene un recibo abierto (Esperado, Recibiendo o Discrepancia: una recepción a medio capturar) contra
ella. Confirma ese recibo o elimínalo primero.

**¿Qué significa "Una orden de compra con recepciones no se elimina; cancélela."? (409)**
Una vez que una orden de compra tiene alguna recepción confirmada, ya no se borra (se conserva por su historial
de inventario): en su lugar se cancela (posible desde `DRAFT`, `SENT` o `PARTIAL`).

**¿Qué significa "La cantidad del ajuste excede el faltante pendiente ({pendiente})."? (400)**
Un ajuste manual sobre el faltante de una línea no puede pedir más de lo que realmente falta por recibir en esa
línea (ordenado − recibido − ya resuelto). Revisa el pendiente actual (`GET .../shortage-lines`) antes de resolver.

**¿Qué significa "Cerrar y Reordenar resuelven el faltante completo ({pendiente}); para una parte use el ajuste
manual."? (400)**
Las acciones `CLOSE` y `REORDER` siempre resuelven **todo** el pendiente de la línea; no aceptan una cantidad
parcial. Si solo quieres resolver una parte del faltante, usa `MANUAL_ADJUSTMENT` con la cantidad exacta.

### Cruce de muelle

**¿Qué significa "El módulo 'CROSSDOCK' no está habilitado para esta compañía."? (403, `module_disabled`)**
El cruce de muelle es una demostración funcional, apagada por defecto. Un administrador la enciende desde
Configuración de la compañía (acción sensible: exige reautenticación reciente, `POST /api/v1/auth/reauth`).

**¿Qué significa "El muelle ya tiene una cita que se solapa con ese horario."? (409)**
Dos citas no pueden ocupar el mismo muelle al mismo tiempo (ventana semiabierta: una cita que termina a las 10:00
no choca con otra que empieza a las 10:00). Elige otro horario o otro muelle.

**¿Qué significa "La cantidad excede lo disponible para cruce de muelle ({disponible})."? (409)**
La cantidad que intentas asignar a una orden supera lo que todavía se puede asignar de esa línea de recibo (lo
recibido/esperado menos lo ya asignado a otras órdenes, o el putaway pendiente si el recibo ya está confirmado).
Revisa `GET /api/v1/cross-dock-plans/{id}/candidates` para ver lo asignable real.

**¿Qué significa "La recepción de la línea todavía no se confirma; la mercancía se mueve después de confirmar."?
(422)**
Cuando la asignación es sobre un recibo todavía **abierto**, la mercancía física no se mueve (el `CROSSDOCK`)
hasta que ese recibo se confirme: al confirmarlo, el sistema reparte lo realmente recibido entre las asignaciones
y ahí sí queda lista para moverse.

**¿Qué significa "La tarea de cruce de muelle se completa por la cantidad confirmada ({qty})."? (400)**
Al completar la tarea `CROSSDOCK` desde la cola, la cantidad tiene que ser exactamente la que quedó **confirmada**
en la asignación (no la que se había planeado originalmente, si hubo un faltante al confirmar el recibo).

**¿Qué significa "Mueva o cancele las asignaciones pendientes antes de completar el plan."? (422)**
Un plan de cruce de muelle solo se completa (estatus terminal) cuando ninguna de sus asignaciones sigue
`PLANNED`: muévelas (para que salgan del inventario) o cancélalas primero.

## Lote 7A — Pulso de almacén y Actividad reciente

**¿Qué significa "No tiene permiso para ver la actividad del módulo WAREHOUSE."? (403)**
Pediste la pestaña Almacén de "Actividad reciente" (`GET /api/v1/analytics/activity?module=WAREHOUSE`) sin el permiso
`inventory.view`, o con el módulo `WMS_LOTSERIAL` apagado. Pida el permiso a un administrador; sin `module`, el panel abre
la primera pestaña que sí puede ver. Si además no tiene `analytics.view` (p. ej. el Operador de almacén), el 403 llega antes,
desde la política del endpoint.

**¿Qué significa "El máximo por página es 50."? (400)**
El panel pide como máximo 50 eventos por página (`take` ≤ 50); use "Ver más" (`skip`) para las siguientes.

**¿Qué significa "La ventana debe ser 24h, 48h o today."? (400)**
El parámetro `window` solo acepta `24h` (por defecto), `48h` o `today` (desde la medianoche de hoy en UTC; el tenant aún no tiene zona
horaria configurada, así que en Puerto Rico "hoy" empieza a las 20:00 del día anterior).

**¿Por qué un ajuste FOUND de un faltante de compra aparece dos veces en Actividad reciente?**
Porque son dos hechos: se resolvió un faltante de la orden de compra (`PO_SHORTAGE_RESOLVED`, opcional, enlaza a la orden)
y cambió el saldo de inventario con un motivo manual (`INVENTORY_ADJUSTED`, obligatorio, enlaza al producto). Con "Solo
obligatorios" queda solo el ajuste.

**Renombré o deshabilité un evento del catálogo `ActivityEventType`; ¿por qué un obligatorio sigue apareciendo?**
Actividad reciente respeta el override del tenant del catálogo (`PUT /api/v1/catalogs/ActivityEventType/{código}/override`,
permiso `admin.catalogs`): la etiqueta renombrada se muestra en el feed (en el idioma que el override traiga; los demás
idiomas conservan la etiqueta base) y un evento **opcional** deshabilitado deja de aparecer. Un evento **obligatorio**
(por ejemplo `PO_CANCELLED` o `RECEIPT_CONFIRMED`) no se puede ocultar ni volver opcional desde el override: sigue
apareciendo, también con "Solo obligatorios". Con `"extraJson": "{\"defaultOn\":true}"` en el override se enciende para todo el tenant un
opcional apagado por defecto (`BIN_MOVED`).

**¿Por qué dar de baja una categoría no aparece como "Producto dado de baja"?**
La categoría se registra en la bitácora con su propio tipo (`PRODUCT_CATEGORY`); `PRODUCT_DEACTIVATED` es solo para
productos. Una baja de producto aparece aunque el producto se haya reactivado después dentro de la ventana.

**¿Qué suma el indicador "Unidades recibidas"?**
Lo que realmente entró en los últimos 7 días: movimientos de recepción más las diferencias de recepción (con su signo). Un
recibo con 10 esperados y 8 recibidos suma 8.

**¿Qué cuenta el indicador "Conteos con diferencia"?**
Conteos cíclicos reconciliados en los últimos 30 días con al menos una línea con diferencia o con ajuste, aunque sobrantes y
faltantes se compensen en el total.

**¿Por qué alguien sin `inventory.view` puede ver conteos cíclicos en Análisis?**
Las fuentes de datos de Análisis (entre ellas `CYCLE_COUNT` y las de almacén del Lote 6) se consultan con `analytics.view`
y el módulo ANALYTICS, sin el permiso del módulo de negocio. Es una decisión a revisar (`docs/lote7A-decisiones.md`); si
no debe verlas, quítele `analytics.view` o no le comparta reportes de esas fuentes. Desde el Lote F8a los **indicadores y
gráficos** sí exigen poder leer su fuente (`CYCLE_COUNT` → `inventory.view`, ver capítulo 07, sección 3); las **vistas e
informes** siguen con la regla anterior.

## Lote 8A — Backend de la app de almacén: aparatos, PIN, idempotencia, sincronización y operaciones atómicas

**¿Qué significa "Este teléfono ya está registrado en {compañía} como {código}. Pide al administrador un código de otra compañía." (409)?**
El teléfono ya tiene un registro en la compañía de ese código (un teléfono puede estar en varias compañías, pero una sola
vez en cada una). El código no se gastó. Si quería trabajar en esa compañía, use el registro que ya tiene: en "¿Quién
eres?" elija esa compañía. Si quería otra compañía, pida el código al administrador de esa otra compañía.

**¿Cómo agrego a una persona a varias compañías al crearla?**
En Sistema → Roles y usuarios → "Nuevo usuario", marque las compañías en "También agregar a estas compañías" (solo salen aquellas
donde usted administra usuarios). Recibe los mismos roles, por nombre, en cada una. Si falta un rol en alguna compañía sale
`En {compañía} no existen los roles: {roles}.` (400) y no se crea en ninguna: cree el rol en esa compañía o cambie los roles.

**¿Qué significa "No puede agregar usuarios a esa compañía." (403)?**
Usted no administra usuarios (`admin.users`) en esa compañía, o no es miembro activo de ella. Pida a un administrador de esa
compañía que agregue a la persona desde allá.

**¿Cómo uso el mismo teléfono en dos compañías?**
En "¿Quién eres?" toque "Registrar otra compañía" y teclee el código que le dio el administrador de la otra compañía
(Sistema → Aparatos). Desde entonces, al entrar la app pregunta "¿En qué compañía vas a trabajar?".

**¿Qué significa "El código de registro no es válido o venció." (401)?**
El código de 8 caracteres que teclea el aparato es de **un solo uso** y vence en 24 horas; también deja de servir si el
administrador generó uno nuevo (`POST /api/v1/devices/{id}/enroll-code`) o desactivó el aparato. Pida al administrador
un código nuevo desde la pantalla de aparatos.

**¿Qué significa "El aparato no está registrado o fue desactivado." (401)?**
El aparato fue desactivado (o su secreto ya no vale porque se volvió a registrar). Al desactivarlo se cierran en el acto
sus sesiones: el token que tenga el aparato deja de servir (401 también en la sincronización) y no se puede renovar.
Reactívelo y vuelva a entrar con el PIN, o registre el aparato de nuevo. Cada intento rechazado del login por aparato o de
la lista de usuarios queda en Auditoría → eventos de seguridad como `LOGIN` / `FAILURE` con `stage = device` y el motivo
(`device_invalid`, `device_inactive` o `tenant_unusable`); si el aparato existe, en su compañía. Sirve para ver si un
aparato perdido y desactivado sigue intentando entrar.

**¿Qué significa "PIN incorrecto." (401)?**
El PIN no coincide, el usuario no tiene PIN en esta compañía, no es miembro activo o es el administrador de plataforma
(que nunca entra por aparato). Cada fallo cuenta; al 5.º seguido el PIN se bloquea. Un acierto reinicia el contador.

**¿Qué significa "PIN bloqueado por 15 minutos." (423)?**
Hubo 5 PIN incorrectos seguidos. Mientras dura el bloqueo ni el PIN correcto entra. Espere 15 minutos, o vuelva a
definir el PIN (Mi cuenta → `PUT /api/v1/me/pin`, o un administrador con `PUT /api/v1/users/{id}/pin`), lo que quita el
bloqueo.

**¿Qué significa "La contraseña actual es incorrecta." (400, en `currentPassword`)?**
Para definir o cambiar su propio PIN (`PUT /api/v1/me/pin`) debe escribir su contraseña de acceso actual. Revise la
contraseña; si la olvidó, cámbiela primero. Cada contraseña equivocada cuenta en el bloqueo de la cuenta (el mismo del
login web): tras 5 seguidas la cuenta queda bloqueada 15 minutos y, mientras tanto, este mismo mensaje sale aunque
escriba la contraseña correcta (y tampoco puede entrar a la web). Espere los 15 minutos y vuelva a intentarlo con la
contraseña correcta.

**¿Por qué me aparece "Contraseña incorrecta." o "La contraseña actual es incorrecta." con la contraseña correcta?**
Desde el Lote 8A la reautenticación (`POST /api/v1/auth/reauth`), el cambio de contraseña (`PUT /api/v1/auth/password`)
y el PIN propio (`PUT /api/v1/me/pin`) cuentan las contraseñas equivocadas en el mismo bloqueo del login: 5 seguidas
bloquean la cuenta 15 minutos. Mientras dure, esas rutas responden como si la contraseña fuera incorrecta (401
`Contraseña incorrecta.` en la reautenticación; 400 de contraseña actual incorrecta en las otras dos) y el login web
responde `Credenciales inválidas.`. Espere 15 minutos. Si no fue usted quien falló, avise a su administrador: alguien
pudo intentar adivinar su contraseña, por ejemplo desde un aparato donde entró con su PIN.

**¿Qué significa "La sesión de un aparato no administra el segundo factor." (403)?**
Se intentó activar o confirmar la verificación en dos pasos (`POST /api/v1/auth/mfa/totp/enroll` o `/confirm`) con la
sesión de un aparato, que se abre solo con el PIN. Si se permitiera, quien viera el PIN en un aparato compartido (o
quien lo asignó) se quedaría con el secreto y los códigos de recuperación, y el dueño de la cuenta ya no podría entrar a
la web con su contraseña. Active la verificación en dos pasos desde la web (Mi cuenta), con su contraseña.

**¿Por qué no aparezco en la lista de usuarios del aparato?**
La lista muestra solo usuarios internos activos, con membresía activa en la compañía del aparato, con **PIN definido** y
con `inventory.view`, ordenados por nombre. Defina su PIN en Mi cuenta y pida el permiso si le falta. Si tiene PIN pero
no `inventory.view`, el login por aparato responde 403 `Falta el permiso 'inventory.view'.`.

**En la lista de usuarios del aparato alguien aparece como "Usuario" en vez de su nombre.**
Esa persona no tiene un nombre completo cargado en su cuenta. La lista nunca muestra el correo (el aparato suele ser
compartido), así que sin nombre se ve el texto genérico `Usuario`. Pida a un administrador que le complete el nombre
(`PUT /api/v1/users/{id}`).

**¿Qué significa "No puede asignar ni quitar el PIN de un usuario con más permisos que usted." (403)?**
Con `devices.manage` (o `admin.users`) puede asignar el PIN de otros, pero solo de usuarios cuyos permisos sean un
subconjunto de los suyos: con ese PIN se entra como esa persona en un aparato sin contraseña ni MFA. Pida a un
administrador con esos permisos que lo haga, o que la persona defina su PIN en Mi cuenta.

**¿Qué significa "Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta." (403 en el login del aparato, 401 al renovar la sesión)?**
Su PIN no lo definió usted: se lo asignó otra persona (con `devices.manage` o `admin.users`), y esa persona ya no tiene
todos los permisos que usted tiene hoy (a usted le dieron más permisos o un rol nuevo, o a ella se los quitaron, se
desactivó o dejó la compañía). Como el PIN abre su cuenta sin contraseña ni MFA, deja de servir hasta que usted lo
defina en Mi cuenta (`PUT /api/v1/me/pin`, con su contraseña) o se lo reasigne alguien con al menos sus permisos. Si
tenía una sesión abierta en el aparato, el siguiente refresh la cierra con este mismo mensaje (401). En la bitácora de
seguridad queda `LOGIN` / `BLOCKED` (o `TOKEN_REVOKED`) con `reason = pin_assigner_lower_privileges`.

**Al asignar el PIN de otro usuario recibo 403 `aal2_required`.**
Asignar el PIN de otro exige una reautenticación reciente (AAL2), igual que cambiar roles o permisos: confirme su
contraseña (`POST /api/v1/auth/reauth`) y repita. Este 403 solo lo recibe quien **sí** tiene `devices.manage` o
`admin.users`: sin ninguno de los dos, el permiso se rechaza antes (403 de permiso, evento `PERMISSION_DENIED`) y
reautenticarse no sirve de nada.

**¿Qué significa "La clave de idempotencia ya se usó con otro contenido." (409)?**
La cabecera `Idempotency-Key` identifica UNA operación: repetirla con el mismo cuerpo devuelve la respuesta guardada
(con `Idempotent-Replayed: true`); con otro cuerpo o en otra ruta es un error. Genere una clave nueva por operación.
La clave distingue mayúsculas: `abc-1` y `ABC-1` son claves distintas y no chocan entre sí.

**¿Qué significa "La operación con esta clave ya no puede repetirse con los permisos actuales." (409)?**
La operación original comprobó, por su cuenta, un módulo o un permiso que no es el que exige la ruta (por ejemplo
`warehouse.count`, que decide si el conteo se ve a ciegas), y desde entonces cambió: se ganó **o** se perdió, no
importa el sentido. El API no repite la respuesta vieja (ya no correspondería a los permisos de hoy). Consulte el
registro directamente (por ejemplo `GET /api/v1/cycle-counts/{id}`) en lugar de reenviar la operación; no la reenvíe
con otra clave, porque se ejecutaría otra vez. Esto no cambia lo que exige la propia ruta (`[RequirePermission]`,
`[RequireModule]`, `[RequireAal2]`): esos se revisan en cada petición, repetición o no, y si ya no se cumplen dan su
403 (o `module_disabled`/`aal2_required`) de siempre, no este 409.

**¿Qué significa "La operación con esta clave todavía se está procesando." (409)?**
Otra petición con la misma clave sigue en curso. Espere y reintente con la misma clave: recibirá su respuesta.

**¿Qué significa "La clave de idempotencia no es válida." (400)?**
La clave está vacía, repetida o tiene más de 80 caracteres. Use un identificador único corto (por ejemplo un GUID).

**¿Por qué `Idempotency-Key` no tiene efecto en `/api/v1/users`?**
Las rutas que devuelven credenciales en claro (`/api/v1/auth`, `/api/v1/me`, `/api/v1/devices`, `/api/v1/platform` y
`/api/v1/users`, cuyo alta devuelve la contraseña temporal) quedan fuera de la idempotencia para no guardar secretos en
la bitácora. Son operaciones de administración, no de la cola del aparato. Lo mismo vale para invitar y reenviar la
invitación de un usuario de portal (`/api/v1/clients/{id}/portal-users/invite` y `.../resend-invite`), que devuelven el
token de invitación.

**¿La misma `Idempotency-Key` sirve para dos usuarios distintos?**
Sí: la clave es por compañía y usuario. Si otro usuario usa la misma clave, su operación se ejecuta como nueva (no
recibe la respuesta del primero ni un 409).

**Reenvié una operación rechazada (400) con la misma clave y volvió el mismo 400.**
Es lo esperado: los rechazos de negocio (menores a 500, salvo 401, 403, 408, 423 y 429) se guardan y se repiten con
`Idempotent-Replayed: true`. Corrija la solicitud y mándela con una clave nueva.

**¿Qué significa "Demasiados intentos; espere un minuto e intente de nuevo." (429)?**
El aparato (o la red del almacén) mandó demasiadas peticiones anónimas en un minuto: registrar el aparato admite 10 por
minuto por dirección IP; la lista de usuarios, el login por aparato y el heartbeat, 60 por minuto cada uno. Espere el
tiempo de `Retry-After` (60 s) y reintente. Si muchos aparatos salen por la misma IP y se topan con el límite, soporte
puede subirlo (`RateLimiting:DeviceAuthPerMinute`).

**Mandé 5 PIN incorrectos al mismo tiempo y el PIN quedó bloqueado.**
El bloqueo cuenta todos los intentos, lleguen en serie o en paralelo: al 5.º fallo el PIN se bloquea 15 minutos (423
`PIN bloqueado por 15 minutos.`) y los intentos que llegan después también reciben 423, aunque traigan el PIN correcto.

**El heartbeat del aparato responde `isActive: false`.**
El aparato fue desactivado (o la compañía perdió el módulo WMS). El heartbeat no da error para que la app bloquee la
entrada; pida al administrador que lo reactive. Con un secreto que no corresponde, el heartbeat responde 401
`El aparato no está registrado o fue desactivado.`.

**¿Qué significan "El tema no es válido; use LIGHT o DARK." (400), "El almacén por defecto está dado de baja." (422) y
"El aparato está desactivado; reactívelo antes de generar un código de registro." (422)?**
Al editar el aparato (`PATCH /api/v1/devices/{id}`) el tema solo admite `LIGHT` o `DARK` y el almacén por defecto debe
estar activo. Para generar un código de registro nuevo, primero reactive el aparato (`POST /api/v1/devices/{id}/reactivate`).

**Guardo campos personalizados en `USER_DEVICE` y responde 404 (o 403).**
El aparato no admite campos personalizados: su ruta polimórfica usa el resolver cerrado (siempre 404, como
`PRODUCT_CATEGORY`) y exige `devices.manage` (sin él, 403).

**¿Qué significan "El máximo por página es 500." y "El cursor no es válido." (400)?**
La sincronización (`/api/v1/sync/*`) pagina de a lo más 500 filas con el cursor opaco que devuelve `nextCursor`; no lo
construya a mano. Para la siguiente pasada use `since` = `serverTimeUtc` de la pasada anterior menos 5 minutos.

**Sincronizo órdenes de compra en el aparato y recibo 403.**
`GET /api/v1/sync/purchase-orders` pide, además de `inventory.view`, el permiso `purchasing.view` y el módulo
PURCHASING encendido (403 `El módulo 'PURCHASING' no está habilitado para esta compañía.`), igual que la consulta de
órdenes de compra en la web. El rol Operador de almacén ya trae `purchasing.view`.

**Recibí contra una orden de compra desde el aparato y el recibo quedó con menos de lo pedido.**
Con `lines` en `POST /api/v1/receipts` (contra aviso u orden de compra) lo escaneado manda sobre lo esperado: lo no
escaneado queda en 0 y la orden pasa a PARTIAL con su faltante. Sin `lines` se recibe lo esperado completo.

**¿Qué significa "No hay un producto con ese código." (404)?**
`GET /api/v1/products/by-barcode/{código}` busca el código de barras exacto y, si no, el SKU exacto, solo entre
productos activos. Revise la etiqueta o dé de alta el código de barras del producto.

**¿Qué significa "Para recolectar y empacar en una llamada use POST /api/v1/pick-batches/collect-and-pack." (400)?**
`POST /api/v1/pick-batches` solo recolecta. La variante que recolecta y empaca en una transacción tiene su propia ruta
(`/api/v1/pick-batches/collect-and-pack`) y responde `{ batch, order }`.

**Contar desde el aparato me da 403, pero veo el conteo.**
Ver la ficha (a ciegas) pide `inventory.view`; contar (alta, captura, lo encontrado y terminar) pide
`warehouse.count.capture`, y reconciliar, refrescar y eliminar piden `warehouse.count`. Pida el permiso que falta.

**¿Por qué no veo las cantidades esperadas del conteo?**
Sin `warehouse.count` el conteo es **a ciegas** (`isBlind = true`): las cantidades esperadas llegan vacías para no
condicionar lo que se cuenta, y también `varianceLines` y `netVariance` del encabezado (en la ficha y en la lista). La
diferencia se revisa al reconciliar en la web.

**¿Qué significa "La sesión de un aparato no cambia de compañía." (403)?**
Se intentó `POST /api/v1/auth/switch-tenant` con el refresh token de una sesión abierta con PIN en un aparato. Esa sesión
no tiene contraseña ni MFA, así que queda atada a la compañía del aparato. Para trabajar en otra compañía, entre con
contraseña (y MFA) desde la web, o registre un aparato en esa compañía. La sesión del aparato sigue viva.

**¿Qué significan "El código del aparato es obligatorio." y "El código del aparato admite hasta 30 caracteres." (400)?**
Al dar de alta un aparato (`POST /api/v1/devices`) el código es obligatorio (vacío o solo espacios cuenta como vacío) y
admite de 1 a 30 caracteres. El error llega en `errors.code`. Indique un código corto, por ejemplo el de la etiqueta del
aparato (`ZB-01`).

**Con el rol Solo lectura, la lista de conteos cíclicos ya no muestra "Diferencia neta".**
Desde el Lote 8A, sin `warehouse.count` el conteo es a ciegas también en la lista: el API no envía la diferencia y la
pantalla oculta esa columna. Antes estos roles veían la diferencia real. Si la persona debe verla, dele `warehouse.count`.

**¿Qué significa "Entre 1 y 365 días." en `deviceSessionDays` (400)?**
La vida de la sesión de los aparatos (`PUT /api/v1/tenant/settings`, `admin.tenant`) va de 1 a 365 días (30 por
defecto); cada renovación del aparato la extiende desde ese momento.

**¿Qué significa "Ya existe un aparato con ese código." (409)?**
El código del aparato (`POST /api/v1/devices`, campo `code`) es único por compañía, incluso si el aparato que ya
lo usa está desactivado (ese código no se reutiliza). Elija otro código.

**¿Qué significan "El nombre admite hasta 100 caracteres." y "El modelo admite hasta 80 caracteres." (400)?**
Límites de longitud del nombre y el modelo del aparato al darlo de alta o editarlo (`POST`/`PATCH
/api/v1/devices`).

**¿Qué significa "Aparato no encontrado." (404)?**
El `publicId` del aparato no existe, o es de otra compañía. Revise el enlace o la lista de aparatos.

**Asignar o quitar el PIN de otro usuario me da 403 (sin `aal2_required`), o "Falta el permiso 'devices.manage' o 'admin.users'."**
Para asignar o quitar el PIN de otro usuario (`PUT`/`DELETE /api/v1/users/{id}/pin`) hace falta uno de esos dos
permisos. La política de permisos lo rechaza antes que cualquier otra revisión (queda `PERMISSION_DENIED` con
`devices.manage|admin.users` en Auditoría → eventos de seguridad); el texto "Falta el permiso …" es la misma regla
revisada otra vez por el servicio. Pida que se lo asignen o que otro administrador haga el cambio.

**Quise asignar el PIN de un usuario y me da 404 "Usuario no encontrado." aunque el usuario existe.**
El usuario es de otra compañía, es una cuenta de portal, o es el administrador de plataforma: ninguno de los tres
tiene PIN de aparato (el administrador de plataforma nunca entra por esta vía, así que ni siquiera es visible
aquí).

**¿Qué significa "El PIN debe tener de 4 a 6 dígitos." (400, en `pin`)?**
Al definir el PIN (`PUT /api/v1/me/pin` o `PUT /api/v1/users/{id}/pin`), use solo de 4 a 6 números. Los espacios al
inicio y al final no cuentan.

**¿Qué significa "El PIN no puede ser una secuencia trivial." (400, en `pin`)?**
No se admiten PIN con todos los dígitos iguales ni secuencias consecutivas como `1234` o `9876`. Elija otro.

**¿Qué significa "La línea se repite en la solicitud." (400, en `lines[i]`)?**
En `PUT /api/v1/cycle-counts/{id}/lines/batch` la misma línea viene dos veces y no se guarda nada. Deje un solo
renglón por línea y reenvíe.

**¿Qué significa "El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo." (409)?**
Otra sesión definió el PIN de ese usuario en el mismo momento. Repita la operación.

**Cambié (o restablecí) el PIN y el aparato me sacó de la sesión.**
Es lo esperado: cambiar un PIN que ya existía (Mi cuenta o `PUT /api/v1/users/{id}/pin`), igual que quitarlo, cierra
las sesiones de ese usuario en los aparatos de la compañía (evento `TOKEN_REVOKED` con motivo `pin_changed`). Así,
quien hubiera entrado con el PIN viejo no conserva la sesión. Vuelva a entrar en el aparato con el PIN nuevo.

**Apagaron el módulo WMS_LOTSERIAL y el aparato responde "El aparato no está registrado o fue desactivado." (401) al
renovar la sesión.**
Sin el módulo no se puede entrar por aparato, y tampoco renovar una sesión de aparato ya abierta: el refresh responde
401 y la sesión queda revocada. Al encender otra vez el módulo, cada usuario vuelve a entrar con su PIN.

**El refresh de una sesión de aparato responde "Refresh token inválido." (401) en vez de "El aparato no está
registrado o fue desactivado."**
Son dos comprobaciones distintas. El segundo mensaje es sobre el **aparato** (desactivado, o su compañía sin el
módulo); el primero es sobre el **usuario**: cada refresh de una sesión de aparato también exige que el usuario siga
activo, con membresía activa en esta compañía y con `inventory.view` (la misma condición de `device/login`). Si al
usuario le quitaron el permiso, se desactivó o dejó la compañía, el refresh responde 401 `Refresh token inválido.` y
revoca la sesión (evento `TOKEN_REVOKED`); el aparato en sí puede seguir activo. Pida que le devuelvan el permiso o
la membresía y vuelva a entrar con el PIN.

**¿Por qué un intento de login por aparato aparece en la bitácora de seguridad sin usuario?**
Si el `userId` pedido no es (ni fue) miembro de la compañía del aparato, el evento `LOGIN` / `FAILURE` se registra sin
usuario (el id pedido va en el detalle como `requestedUserId`) para no revelar el nombre ni el correo de usuarios de
otras compañías. Un intento así suele indicar que alguien prueba ids al azar con un aparato registrado: revise ese
aparato y desactívelo si no lo reconoce.

**Edité un aparato y recibí "El registro fue modificado por otro usuario; recargue e intente de nuevo." (409).**
Otro administrador editó, desactivó, reactivó o regeneró el código de ese aparato después de que usted lo abrió.
Recargue el aparato (`GET /api/v1/devices/{publicId}`) y repita el cambio con la `rowVersion` nueva. El heartbeat y los
logins con PIN del aparato **no** provocan este 409: "visto por última vez", "último usuario" y "versión de la app" se
guardan aparte y no cambian la `rowVersion`.

**Mandé `POST /api/v1/devices/enroll` sin `enrollCode` (o `device/login` sin `deviceSecret`) y respondió 401, no 400.**
Es lo esperado: un código de registro ausente es un código inválido (401 `El código de registro no es válido o
venció.`) y un aparato sin secreto no está autenticado (401 `El aparato no está registrado o fue desactivado.`). Del
mismo modo, `PUT /api/v1/me/pin` sin `currentPassword` responde 400 `La contraseña actual es incorrecta.` en
`currentPassword`. Nunca llega el mensaje genérico en inglés ("The … field is required.").

**Dos personas registraron el mismo aparato al mismo tiempo con el mismo código y una recibió 401 en vez de 409.**
Es lo esperado: el código es de un solo uso, así que cuando dos registros compiten por el mismo código, quien pierde
la carrera recibe el mismo 401 `El código de registro no es válido o venció.` que un código inválido (nunca un 409
de conflicto), con el mismo evento de seguridad (`API_CREDENTIAL` / `FAILURE`, `device_enroll`) para no distinguir
"código ya usado" de "código inválido". Quien ganó queda registrado con éxito. Pida un código nuevo y regístrelo una
sola vez.

**Reactivé (o volví a registrar) un aparato y un token que tenía de antes ya no sirve, aunque el aparato esté activo.**
Es lo esperado: al desactivar, reactivar o volver a registrar el aparato se fija un sello (`SessionsNotBeforeUtc`)
con ese momento. Cualquier access token de sesión de aparato emitido **antes** de ese sello deja de aceptarse
(comparado al segundo, sin margen), aunque el aparato vuelva a estar activo. Vuelva a entrar en el aparato con el
PIN para obtener un token nuevo.

**El aparato perdió la señal a mitad de una operación con `Idempotency-Key`. ¿Se duplica al reintentar?**
No. La operación no se cancela cuando el aparato se desconecta: termina, su respuesta se guarda y el reintento con la
misma clave y el mismo cuerpo recibe esa respuesta con `Idempotent-Replayed: true` (el mismo recibo, sin otra entrada de
inventario ni otro número). Si el reintento llega mientras la primera sigue en curso, responde 409 `La operación con esta
clave todavía se está procesando.`: espere unos segundos y reintente con la misma clave. Dos reintentos simultáneos con
la misma clave tampoco duplican: uno ejecuta y los demás reciben la repetición o ese 409.

**Sincronizo con `modifiedSinceUtc` y al aparato nunca le llegan las bajas.**
El parámetro de la sincronización se llama `since` (UTC). Un parámetro con otro nombre se ignora sin error y la
respuesta es una carga completa de lo vigente, que no trae las filas con `isActive: false`. Use `since` = el
`serverTimeUtc` de la pasada anterior menos 5 minutos, con `cursor` y `take` (hasta 500).

**Eliminé un conteo cíclico y su tarea sigue en el aparato.**
Al eliminar un conteo abierto su tarea COUNT pasa a `CANCELLED` (con fecha de cierre). En la siguiente sincronización
por diferencia (`GET /api/v1/sync/warehouse-tasks?since=...`) llega con `isActive: false` y el aparato la borra. Si
sigue apareciendo, el aparato no está mandando `since` (ver la pregunta anterior).

**Filtro el conteo por diferencia (`onlyVariance=true`) y me llegan todas las líneas.**
Sin `warehouse.count` el conteo es a ciegas: filtrar por diferencia revelaría qué líneas no cuadran con lo esperado,
así que ese filtro se ignora. Los demás filtros (posición, producto, categoría, pendientes) sí aplican.

## Lote 8A-app — App de almacén (pantallas): Recibir, Acomodar, Despacho, Conteo, Consultar, Sincronización

Ver el [capítulo 9 — App de almacén](09-app-almacen.md) para cada pantalla completa (qué hace, quién puede, campos
y validaciones). Esta sección junta las dudas de uso diario.

**Cerré la app a mitad de un recibo (o un despacho, o un conteo). ¿Se perdió?**
No. Mientras no se confirme (o se cancele explícitamente), el documento sigue guardado en el aparato. Al volver a
abrir la app y entrar con el mismo o con otro usuario, la pantalla correspondiente lo retoma tal cual quedó (mismas
líneas capturadas); nunca empieza uno nuevo por encima. En Conteo, la lista de líneas esperadas se vuelve a pedir al
servidor al reabrir (no se guarda localmente, solo lo ya capturado), así que hace falta señal en ese momento.

**Toco Acomodar (o Despacho, o Conteo, o Consultar) y me dice "Termina o cancela el recibo en curso antes de usar
esto."**
Es la regla de "un documento a la vez por aparato": mientras haya un recibo, un despacho o un conteo abiertos, las
demás acciones quedan bloqueadas con este aviso hasta que se confirme o se cancele el que está en curso. El botón de
la acción que sigue abierta (no las otras) siempre la retoma en vez de bloquear.

**¿Por qué Recibir/Despacho/Conteo a veces funcionan sin señal y Acomodar/Consultar no?**
Recibir, recolectar en Despacho y capturar en Conteo son documentos propios de este aparato: se capturan sin señal y
se mandan solos cuando la haya (cola de salida). Acomodar (la lista de tareas de todo el almacén), empacar un
despacho (elegir consignatario) y abrir un conteo (reclamar la posición) tocan algo que otro aparato o la web podría
estar viendo o tomando al mismo tiempo, así que esas llamadas siempre son directas y necesitan señal en el momento.

**Una operación quedó "con error" en Sincronización. ¿Qué hago?**
Quiere decir que el servidor la rechazó por algo que reintentarla igual no arregla (un dato inválido, un documento
que ya no existe, un permiso que cambió) — nunca por falta de señal, eso la deja "pendiente" en cambio, y se manda
sola. Lea el mensaje de la fila: si el problema ya no aplica, toque "Reintentar"; si ya no corresponde (por ejemplo,
se canceló esa operación desde la web), toque "Descartar" para que deje de intentarse.

**Escaneo un producto en Despacho y me dice que solo despacha inventario de clientes 3PL.**
Esta entrega de Despacho solo recolecta y empaca inventario de clientes 3PL (el dueño del producto ya viene
sincronizado con él). Inventario propio del tenant todavía no se despacha desde el aparato: se completa desde la
web. El mismo aviso sale si se escanea un producto de un cliente distinto al del despacho que ya está en curso (un
despacho es de un solo cliente).

**Toco "Cancelar conteo" y me sale un error de permiso.**
Cancelar un conteo (`DELETE /api/v1/cycle-counts/{id}`) exige el permiso completo `warehouse.count`, no solo
`warehouse.count.capture` (el que basta para contar a ciegas). Terminar el conteo con lo que ya se contó sigue
funcionando igual sin `warehouse.count`; para poder cancelarlo hace falta ese permiso completo.

**En Despacho, "Empacar" me falla al buscar los consignatarios aunque puedo recolectar sin problema.**
Recolectar y empacar (el envío final) exigen `warehouse.pick`; buscar los consignatarios del cliente para elegir uno
exige, por separado, `locations.read`. Con `warehouse.pick` pero sin `locations.read` se puede recolectar todo el
despacho, pero "Empacar" devuelve el error de permiso del servidor al pedir la lista. Pida que el rol del aparato
tenga ambos permisos.

**Consulto un producto sin señal y me sale "Datos de las {hora} (hace N min, sin señal ahora)" (antes "Datos de hace N min"), pero yo no había consultado eso antes.**
Sí lo había hecho: la app guarda la última respuesta de cada código exacto consultado (por almacén), aunque haya
sido hace días. Si la búsqueda fue por texto libre y por producto en momentos distintos, cuenta como dos consultas
distintas (dos claves de caché), aunque el resultado final se parezca.

## Lote A3 — App de almacén: mejoras del Zebra y formatos de la compañía

Detalle en el [capítulo 9](09-app-almacen.md) (§1.1 lector, §1.2 formatos, §9 Sincronización) y en
`docs/mobile/loteA3-decisiones.md`. Ninguno de estos mensajes viene del servidor (no tienen código HTTP): son de la app.

### Mensajes nuevos

**¿Qué significa "La fecha no es válida. Escríbela así: MM/DD/AAAA" en Recibir?**
El vencimiento del lote se escribe en el orden de fecha de la compañía (Región y formatos; en Puerto Rico mes/día/año) y lo
escrito no es una fecha que exista (por ejemplo `31/01/2027` con mes 31, o un año de dos cifras). "Agregar" queda apagado hasta
corregirla o borrarla (el vencimiento es opcional). También se acepta año-mes-día (`2027-01-31`). El formato del ejemplo cambia
si la compañía usa otro orden (`DD/MM/AAAA`, `AAAA-MM-DD`).

**¿Qué significa "Lector: sin perfil" en Sincronización?**
DataWedge (el programa del lector de Zebra) rechazó crear el perfil `TeikemAlmacen` o la app está usando otro perfil (el
"Detalle" dice cuál, por ejemplo `activo: Profile0`). Con otro perfil, el lector puede escribir cada lectura como teclas
(aparece el teclado y no avanza solo). Toque **Volver a configurar el lector** con la app abierta y revise otra vez. Si sigue:
en la app DataWedge del aparato → Perfiles, que exista `TeikemAlmacen` asociado a `com.teikem.almacen` (y que ningún otro perfil
esté asociado a la app), con "Keystroke output" apagado e "Intent output" encendido (acción `com.teikem.almacen.SCAN`,
broadcast). Si no, avise a soporte con el texto del "Detalle".

**¿Qué significa "Lector: sin confirmar"?**
La app pidió el perfil a DataWedge pero DataWedge no contestó (todavía). No quiere decir que el lector falle: pruebe a escanear.
Si no avanza solo, toque **Volver a configurar el lector**, salga a Inicio y vuelva; si sigue sin confirmar, avise a soporte.

**¿Qué significa "Lector: no es un Zebra (se usa el teclado)"?**
El aparato no tiene DataWedge (un teléfono normal o un emulador). La app funciona igual: se escribe el código (botón ⌨) y se
toca Aceptar.

**¿Qué significan los bloques verdes "Agregado: … desde …" (Despacho) y "Listo: … quedó en …" (Acomodar)?**
Son la confirmación de la lectura: en Despacho, la línea ya se agregó al escanear la posición; en Acomodar, la tarea ya se
completó. Se quedan hasta la siguiente lectura.

### Preguntas frecuentes

**Al escanear aparece el teclado en pantalla, o el código se escribe en el campo de la cantidad, y no avanza solo.**
El lector está entregando la lectura "como teclas" (el perfil de DataWedge no quedó aplicado). Vea "Lector: sin perfil" arriba.
Con la versión del Lote A3 el campo de escaneo ya no abre el teclado al enfocarse, y la app apaga la salida por teclas del
perfil.

**¿Cómo escribo un código a mano si el teclado ya no aparece?**
Toque el botón **⌨** junto al campo: aparece el teclado. Escriba el código y toque **Aceptar** (o Enter). Toque ⌨ otra vez para
esconderlo. El teclado físico del Zebra escribe en el campo sin abrir el de pantalla.

**En el conteo, un producto no tiene etiqueta. ¿Tengo que teclear el código?**
No: toque el producto en "Lo que se espera aquí" y su código queda en el campo; toque **Aceptar**.

**En Despacho escaneé la posición y la línea entró con cantidad 1.**
Pasaba con la versión del Lote A3 (la cantidad venía en 1). Desde el **Lote A5** la cantidad viene vacía y escanear la posición sin
cantidad no agrega nada: sale `Escribe la cantidad primero y luego escanea la posición.` (ver la sección Lote A5). Si le quedó una línea
con 1 de una versión anterior, quítela con ✕ y vuelva a escanear el producto.

**Cambié la región o los formatos en la web y la app sigue igual.**
La app los trae en cada sincronización con señal: toque **Sincronizar ahora** (o espere un minuto con Inicio abierto). Si el
aparato no tiene señal, sigue con los últimos que recibió.

**La hora de la app no coincide con la del reloj del aparato.**
La app muestra las horas en la zona horaria de la compañía (Región y formatos), no en la del aparato. Si el reloj del aparato
está en otra zona, la diferencia es esperada; si la zona de la compañía está mal, la corrige un administrador en la web.

**Los números salen con punto de miles (1.250) en vez de coma.**
Es el separador de la compañía (Región y formatos). Las cantidades que se escriben a mano aceptan punto o coma como decimal,
como antes.

## Lote F1 — Frontend: acceso, menú, Pulso y Mi cuenta

Mensajes verificados contra `web-app/src/kernel/i18n/es.json` (validaciones de pantalla, en español, cliente) y contra
`src/Teikem.Infrastructure/Services/AuthService.cs` (mensajes del servidor que la pantalla muestra bajo el campo). Capítulo:
`docs/manual/frontend/f1-nucleo-y-mi-cuenta.md`.

**¿Qué significa "Escriba un correo válido."?**
Al iniciar sesión, lo que escribió en "Correo electrónico" no tiene forma de correo. Es una validación de la
pantalla (no llega a llamar al servidor); corrija el correo.

**¿Qué significa "Escriba su contraseña."?**
Dejó el campo "Contraseña" vacío al iniciar sesión. Escriba su contraseña.

**¿Qué significa "Escriba su contraseña actual."?**
En "Mi cuenta › Contraseña" dejó "Contraseña actual" vacío. Escríbala antes de guardar la nueva.

**¿Qué significa "La nueva contraseña debe tener al menos 12 caracteres."?**
La "Nueva contraseña" de "Mi cuenta › Contraseña" no llega al mínimo que exige la plataforma (12 caracteres).
Elija una más larga.

**¿Qué significa "Las contraseñas no coinciden."?**
En "Mi cuenta › Contraseña", lo que escribió en "Repita la nueva contraseña" no es igual a "Nueva contraseña".
Vuelva a escribirlas iguales.

**¿Qué significa "Incorrect password." (en inglés) bajo "Nueva contraseña" al cambiar mi contraseña?**
Es el mensaje que el servidor devuelve cuando la "Contraseña actual" que escribió no es la correcta; queda bajo
el campo "Nueva contraseña" porque el servicio (`AuthService.ChangePasswordAsync`) agrupa ahí cualquier rechazo
de ASP.NET Core Identity, sea por la contraseña actual o por la política de la nueva. Aparece en inglés porque
Identity no tiene un traductor de errores configurado en este lote; es un mensaje real del servidor, no un error
de la pantalla. Verifique que escribió bien su contraseña actual e inténtelo de nuevo.

**¿Qué significa "Escriba los 6 dígitos que muestra su app."?**
Al confirmar la activación de la verificación en dos pasos (Mi cuenta › Verificación en dos pasos, o al enrolar
durante el login), el código no tiene 6 dígitos. Escriba el código completo tal como lo muestra su app.

**¿Qué significa "Elija un rango."? (Pulso, "Mi rango de fecha")**
En el diálogo "Rango" de una tarjeta de Pulso no seleccionó ningún modo. Elija uno de la lista.

**¿Qué significa "Indique la fecha Desde." / "Indique la fecha Hasta."?**
En "Mi rango de fecha" eligió el modo personalizado y dejó una de las dos fechas vacía. Complete ambas.

**¿Qué significa "Desde no puede ser mayor que Hasta."?**
En el rango personalizado de una tarjeta de Pulso, la fecha "Desde" es posterior a "Hasta". Corrija el orden de
las fechas.

**¿Qué significa "Su usuario no tiene permiso para ver esta pantalla. Pida al administrador de su compañía que se lo asigne."?**
Intentó entrar a una pantalla (por URL directa o un enlace) para la que le falta el permiso que exige. Pida a un
administrador de su compañía que se lo asigne, si corresponde a su función.

**¿Qué significa "Este módulo no está habilitado para su compañía."?**
La pantalla a la que entró pertenece a un módulo que su compañía tiene apagado. Pida a un administrador que lo
active en Administración › Módulos (fuera del Lote F1).

**¿Qué significa "Su usuario no tiene permiso para ver los indicadores y gráficos del Pulso del día..."?**
Le falta el permiso `analytics.view`; la pantalla de inicio (Pulso del día) se ve igual, pero sin indicadores ni
gráficos. Pida a un administrador que le asigne el permiso.

**¿Qué significa "Los indicadores y gráficos del Pulso del día son parte del módulo de Análisis, que no está activo..."?**
El módulo `ANALYTICS` está apagado en su compañía. Pida a un administrador que lo active.

**¿Qué significa "No se pudo copiar; selecciónelo y cópielo a mano."?**
Al copiar la clave de MFA o los códigos de recuperación, el navegador no permitió usar el portapapeles
(por ejemplo, por permisos del navegador). Seleccione el texto a mano y cópielo con su teclado.

**¿Qué significa "No se pudo conectar con el servidor. Revise su conexión e intente de nuevo."?**
La pantalla no logró comunicarse con el API (sin red, o el servidor no responde). Revise su conexión a
Internet e intente de nuevo; si persiste, contacte a soporte.

**¿Qué significa "Ocurrió un error. Intente de nuevo."?**
Es el mensaje genérico cuando el servidor respondió con un error que la pantalla no sabe describir mejor
(por ejemplo, un error interno inesperado, 500). Intente de nuevo; si persiste, avise a soporte con la hora
en que ocurrió.

## Lote F6 — Frontend: almacén e inventario (mínimo) + consulta de órdenes

Mensajes verificados contra `web-app/src/kernel/i18n/es.json` (sección `warehouse.*` y `orders.*`, validaciones de
pantalla en el cliente) y contra el código de `web-app/src/features/warehouse/` y `web-app/src/features/orders/`.
Capítulo: `docs/manual/frontend/f6-almacen-e-inventario.md`. Los mensajes que **manda el servidor** (409/422 de reglas
de negocio) ya están documentados con más detalle en "Lote 6 — Inventario y almacén" arriba; aquí solo se listan los
que agrega la pantalla (validación antes de llamar al API) o que la pantalla muestra literalmente bajo un campo.

### Almacenes, zonas, posiciones y muelles

**¿Qué significa "El código es obligatorio."?**
Dejaste vacío el campo "Código" al dar de alta un almacén, una zona o un muelle. Escribe un código.

**¿Qué significa "El código solo admite letras, números, guion y guion bajo (máximo 30)."?**
El código de un almacén no acepta espacios ni otros símbolos. Usa solo `A-Z`, `0-9`, `-` o `_`, hasta 30 caracteres.

**¿Qué significa "El nombre es obligatorio."?**
Dejaste vacío "Nombre" al dar de alta o editar un almacén o una zona. Escribe un nombre.

**¿Qué significa "Indique el código de la posición o su pasillo/rack/nivel/posición."?**
Al dar de alta una posición hay que escribir su código directamente, o al menos uno de "Pasillo", "Rack", "Nivel"
o "Posición" para que el sistema arme el código.

**¿Qué significa "El peso máximo debe ser mayor que cero." / "...admite hasta 3 decimales."?**
El "Peso máximo (kg)" de una posición, si lo indicas, tiene que ser un número positivo con hasta 3 decimales.

### Productos y categorías

**¿Qué significa "El SKU es obligatorio."?**
Dejaste vacío el campo "SKU" al dar de alta un producto. Es el identificador del artículo; escríbelo antes de
guardar.

**¿Qué significa "El SKU no puede exceder 60 caracteres." / "El SKU no admite espacios ni caracteres de control."?**
El SKU tiene un límite de 60 caracteres y no acepta espacios ni caracteres invisibles (tabulaciones, saltos de
línea). Quita los espacios o acórtalo.

**¿Qué significa "El nombre del producto es obligatorio." / "...no puede exceder 200 caracteres."?**
El campo "Nombre" del producto es obligatorio y tiene un máximo de 200 caracteres.

**¿Qué significa "Seleccione el tipo de seguimiento."?**
El producto necesita indicar si se rastrea "Sin seguimiento", "Por lote" o "Por serie"; elige una opción.

**¿Qué significa "El costo y el precio no pueden ser negativos." / "El costo admite como máximo 4 decimales." /
"El precio admite como máximo 4 decimales." / "El costo o el precio excede el máximo permitido."?**
"Costo" y "Precio" del producto (o el costo unitario de una línea de orden de compra) deben ser cero o positivos,
con hasta 4 decimales, y no exceder el tope que admite el sistema.

**¿Qué significa "Los mínimos no pueden ser negativos."?**
"Mínimo de inventario", "Mínimo de picking" o "Máximo de picking" no aceptan valores negativos.

**¿Qué significa "El máximo de la posición de picking debe ser mayor o igual al mínimo."?**
"Máximo de picking" tiene que ser igual o mayor que "Mínimo de picking".

**¿Qué significa "El mínimo de picking requiere una posición preferida en una zona PICKING."?**
Si capturas "Mínimo de picking" también hay que elegir una "Posición preferida" que esté en una zona de tipo
PICKING; sin eso, el sistema no sabría dónde reabastecer.

**¿Qué significa "El peso y el volumen no pueden ser negativos." / "El peso admite como máximo 3 decimales..." /
"El volumen admite como máximo 4 decimales..."?**
"Peso (kg)" y "Volumen (m³)" del producto no aceptan negativos y tienen un límite de decimales y de magnitud.

**¿Qué significa "El nombre de la categoría es obligatorio." / "...no puede exceder 150 caracteres."?**
El nombre de una categoría de producto es obligatorio y tiene un máximo de 150 caracteres.

### Inventario: saldos, Kárdex, ajustes, transferencias y conciliación

> Desde el Lote 14 la pantalla se llama **Kárdex de movimientos** (`/warehouse/kardex`), los ajustes y transferencias tienen su pantalla
> **Transferencias y ajustes**, el ajuste se captura con **Subir/Bajar** y una cantidad positiva, y la conciliación guarda descuadres. Los
> mensajes nuevos y las preguntas están en la sección «Lote 14» de más abajo. "La cantidad del ajuste no puede ser cero." y "Indique la
> cantidad del ajuste (número, positivo para sumar o negativo para restar)." ya no salen en el modal de ajuste: ahora salen "Elija si el
> ajuste sube o baja el inventario.", "Indique la cantidad del ajuste." y "La cantidad debe ser mayor que cero.".

**¿Qué significa "La fecha 'desde' no puede ser posterior a la fecha 'hasta'."?**
En el filtro de rango del Kárdex, la fecha "Desde" quedó después de "Hasta". Corrige el orden de las fechas.

**¿Qué significa "Seleccione un producto." / "Seleccione un almacén." / "Seleccione una posición." / "Seleccione
un motivo."?**
Son campos obligatorios del formulario de "Ajustar" (o de "Transferir", para almacén/posición de origen y destino):
faltó elegir uno.

**¿Qué significa "La cantidad del ajuste no puede ser cero."?**
Un ajuste de inventario tiene que sumar o restar algo; una cantidad de 0 no tendría efecto.

**¿Qué significa "La cantidad admite como máximo 3 decimales."?**
Aplica a la cantidad de un ajuste, una transferencia, una línea de orden de compra, un recibo, un conteo o una
recolección: como mucho 3 decimales.

**¿Qué significa "Indique el número de lote." / "Indique al menos un número de serie."?**
Al ajustar (o transferir) un producto que se rastrea por lote o por serie, hay que capturar el lote o al menos una
serie: el sistema no puede asentar el movimiento sin saber a cuál lote/serie afecta.

**¿Qué significa "El origen y el destino no pueden ser la misma posición."?**
En "Transferir", la posición de origen y la de destino tienen que ser distintas.

### Recepción (recibos y avisos de llegada)

> Desde el Lote 13 la pantalla es un maestro-detalle con pestañas (Recibos, Avisos de llegada, Acomodo pendiente). Los mensajes nuevos y
> las preguntas de uso están en la sección «Lote 13» de más abajo.

**¿Qué significa "Elija el almacén." / "Elija el aviso de llegada." / "Elija la orden de compra." / "Indique el
producto."?**
Campos obligatorios al crear un recibo o una línea: según lo que elegiste en "Recibir" (Ciego, Devolución, contra
Aviso de llegada o contra Orden de compra), la pantalla exige el dato correspondiente.

**¿Qué significa "El recibo admite como máximo 200 líneas."? / "El aviso de llegada admite como máximo 200
líneas."?**
Es el tope de líneas por documento en este lote; si necesitas más, divide la recepción en varios recibos o avisos.

**¿Qué significa "Indique el cliente dueño de la mercancía del aviso de llegada."?**
Un aviso de llegada (ASN) siempre pertenece a un cliente 3PL; hay que elegirlo antes de guardar.

**¿Qué significa "El producto {sku} se controla por lote: indique el lote." / "...por serie: capture {qty}
número(s) de serie (hay {n})."?**
Al capturar una línea de recibo (o de conteo/recolección) de un producto con seguimiento por lote o por serie, hay
que dar el lote, o exactamente tantas series como la cantidad recibida/contada/recolectada.

**¿Qué significa "El número de serie '{serial}' está repetido."?**
Escribiste el mismo número de serie más de una vez en la misma línea; cada serie debe aparecer una sola vez.

### Tareas de almacén

> Desde el Lote 13 no hay una cola única: el acomodo está en Recibo › Acomodo pendiente y las acciones de cada fila son íconos (ver «Lote 13»).

**¿Qué significa "Su usuario no puede consultar el listado de usuarios."?**
Al intentar **Asignar** una tarea, su cuenta no tiene permiso para ver la lista de usuarios de la compañía; pida a
un administrador que se la asigne o que asigne la tarea por usted.

**¿Qué significa "Vacío usa la posición sugerida." / "Vacío completa la cantidad total de la tarea; el remanente
queda como tarea nueva."?**
Son ayudas del formulario "Completar tarea": no son obligatorios: dejarlos en blanco usa el valor por defecto que
describe el texto.

**¿Qué significa "No hay una posición sugerida para esta tarea."?**
El sistema no calculó una posición recomendada para esa tarea (normal fuera de Putaway); elige la posición destino
a mano.

### Conteo cíclico

> Desde el Lote 14 el conteo se trabaja en dos paneles, se confirma en un solo paso (**Confirmar conteo y ajustar**) y sus estatus son
> Pendiente, Contado, Concordancia y Diferencia. Ya no existen los botones **Terminar conteo** y **Reconciliar** ni la pestaña **Tareas de
> conteo** en la web. Vea la sección «Lote 14» de más abajo.

**¿Qué significa "El conteo admite como máximo 1000 líneas; acote los filtros."?**
Un conteo cíclico sin zonas ni posiciones toma todo el saldo en mano del almacén; si supera 1000 líneas, hay que
acotarlo eligiendo zonas o posiciones específicas antes de crearlo.

**¿Qué significa "Indique la posición." / "Indique la cantidad contada."?**
Al agregar o capturar una línea de conteo a mano, faltó la posición o la cantidad contada.

**¿Qué significa "Indique el lote por su id o por su número, no ambos."?**
Al agregar una línea de conteo con lote, hay que elegir un lote existente **o** escribir un número nuevo, no las
dos cosas a la vez.

**¿Qué significa "Faltan {n} línea(s) por contar."?**
(Desde el Lote 14 el botón se llama **Confirmar conteo y ajustar**, y sale como motivo bajo el botón apagado.) "Terminar conteo" exigía que todas las líneas ya tengan una captura (cantidad o series); revisa cuáles faltan en la
tabla (las que dicen "Pendiente").

### Recolección y empaque

> Desde el Lote 13 la captura está en el panel «Recolección» de la misma pantalla (rejilla de varias líneas) y Empacar y Eliminar son íconos de la fila (ver «Lote 13»).

**¿Qué significa "Indique al menos una línea a recolectar." / "La recolección admite como máximo 100 líneas."?**
Al crear una recolección hace falta al menos una línea, y como máximo 100.

**¿Qué significa "Una recolección solo puede tener productos de un mismo dueño."?**
No se pueden mezclar en la misma recolección productos propios con productos de un cliente, ni de dos clientes
distintos. Crea recolecciones separadas por dueño.

**¿Qué significa "El producto {sku} tiene serie: escanee las series a recolectar." / "En productos con serie la
cantidad debe ser igual al número de series escaneadas."?**
Un producto que se rastrea por serie exige capturar tantas series como la cantidad que se recolecta.

**¿Qué significa "Indique el cliente de la orden." / "El consignatario es obligatorio: elija uno del directorio o
capture uno nuevo."?**
Al **Empacar** una recolección, hace falta elegir el cliente que será dueño de la orden y un consignatario (del
directorio o uno nuevo).

**¿Qué significa "El nombre del consignatario es obligatorio." / "La dirección (línea 1) del consignatario es
obligatoria." / "La ciudad del consignatario es obligatoria."?**
Si eliges "Nuevo" consignatario al empacar, esos tres campos son obligatorios.

**¿Qué significa "Use el código ISO de 2 letras del país."?**
El campo "País" del consignatario nuevo espera el código de 2 letras (por ejemplo, `PR`, `US`), no el nombre
completo.

**¿Qué significa "Indique al menos una línea de paquete." / "La cantidad de piezas debe ser al menos 1." / "El
peso no puede ser negativo."?**
Al empacar, hace falta al menos un paquete, con al menos 1 pieza y un peso que no sea negativo.

### Proveedores

**¿Qué significa "El nombre del proveedor es obligatorio." / "Ya existe un proveedor activo con ese nombre."?**
El nombre de un proveedor es obligatorio y único entre los proveedores activos de la compañía.

**¿Qué significa "El correo electrónico no es válido."?**
El campo "Correo electrónico" del proveedor no tiene forma de correo; corrígelo o déjalo vacío.

### Órdenes de compra

**¿Qué significa "Indique el proveedor." / "Indique el producto."?**
Campos obligatorios al crear una orden de compra o una de sus líneas.

**¿Qué significa "La cantidad ordenada debe ser mayor que cero." / "El costo unitario no puede ser negativo."?**
Cada línea de la orden de compra necesita una cantidad positiva; el costo unitario, si lo indicas, no puede ser
negativo.

**¿Qué significa "Ese producto ya está en la orden de compra."?**
No se puede repetir el mismo producto en dos líneas de la misma orden de compra; edita la línea existente en vez
de agregar otra.

**¿Qué significa "La orden de compra debe tener al menos una línea." / "...admite como máximo 200 líneas."?**
Límites de líneas por orden de compra: al menos una, como máximo 200.

**¿Qué significa "La cantidad no puede exceder el faltante pendiente."?**
Al resolver un faltante con "Ajuste manual", la cantidad no puede ser mayor que lo que de verdad falta por recibir
en esa línea (columna "Pendiente" de la tabla de faltantes).

**¿Qué significa "Indique la posición donde entra la mercancía."?**
Al resolver un faltante con "Ajuste manual", hay que indicar en qué posición entra el inventario.

### Citas de muelle y cruce de muelle

**¿Qué significa "Indique el muelle." / "Indique el inicio programado." / "Indique la dirección de la cita (entrada
o salida)."?**
Campos obligatorios al agendar una cita de muelle.

**¿Qué significa "Una cita de un aviso de llegada debe ser de entrada (INBOUND)."?**
Si enlazas la cita a un aviso de llegada, la dirección tiene que ser "Entrada"; un aviso de llegada no se asocia a
una cita de salida.

**¿Qué significa "Una cita se enlaza a un aviso de llegada o a un viaje, no a ambos."?**
El campo "Se enlaza a" de una cita admite ninguno, un aviso de llegada o un viaje, pero no los dos a la vez.

**¿Qué significa "Elija una orden." / "Sin coincidencias exactas."?**
Al **Asignar** una línea de recibo a una orden en un plan de cruce de muelle, hay que escribir el número completo
de la orden, factura o lote de empaque (la búsqueda es por coincidencia exacta) y elegirla de la lista.

**¿Qué significa "Para buscar órdenes necesita el permiso orders.view y el módulo LTL_GROUND."?**
Sin ese permiso y ese módulo activos, la pantalla no puede buscar órdenes para asignarlas en el plan de cruce de
muelle; pida a un administrador que se lo asigne o que encienda el módulo, si corresponde a su función.

### Consulta de órdenes

**¿Qué significa "La orden no existe o no pertenece a su compañía."?**
El enlace a la ficha de la orden es incorrecto, la orden se eliminó o pertenece a otra compañía (tenant). Vuelva a
la lista de "Órdenes" y búsquela de nuevo.

## Lote F8a — Pulso del día: paneles, permisos y orden

Detalle en el capítulo [07 — Pulso del día](07-pulso-y-actividad.md), sección 3.

**¿Por qué no veo el panel Almacén?**
El panel Almacén exige, todo a la vez: el permiso `pulse.warehouse`, el permiso de datos `inventory.view` y el módulo
`WMS_LOTSERIAL` encendido para la compañía. Si falta cualquiera de los tres, el servidor no lo manda y la pantalla no lo
pinta. También puede estar **oculto** en su Pulso (o en el de la compañía): entre a "Organizar mi Pulso" y pulse "Mostrar"
en el panel. Pida a un administrador el permiso que falte (Sistema → Roles y usuarios).

**¿Por qué mi Pulso no se ve como el de otro usuario?**
Por tres razones posibles: (1) cada panel y cada indicador o gráfico depende de los permisos de cada quien (por ejemplo,
un indicador sobre órdenes solo lo ve quien tiene `orders.view`); (2) uno de los dos tiene un **Pulso personal** (el chip
"Pulso personal · Volver al de la compañía" lo indica), con su propio orden y ocultos; (3) el indicador o gráfico es privado
o compartido solo con algunas personas.

**¿Cómo vuelvo al Pulso de la compañía?**
Con "Volver al de la compañía" (`DELETE /api/v1/analytics/pulse/layout/mine`): se borran su orden y sus ocultos (paneles,
indicadores y gráficos, incluido el interruptor "Mostrar en Pulso" propio) y vuelve a ver el orden de la compañía. Sus
rangos de fecha propios se conservan.

**Marqué un indicador para el Pulso y un usuario no lo ve.**
"Mostrar en Pulso" (del indicador o del Pulso de la compañía) no basta: el usuario necesita además `pulse.indicators`
(`pulse.charts` para gráficos), poder ver el indicador por su visibilidad (de toda la compañía, o compartido con él), poder
**leer su fuente de datos** (p. ej. `orders.view` para `TRANSPORT_ORDER`, `inventory.view` para las de almacén,
`admin.audit` para la bitácora) y que el módulo de negocio esté encendido (Almacén → `WMS_LOTSERIAL`, Operación →
`LTL_GROUND`, Contabilidad → `COD` o `LTL_GROUND`). Revise también que ese usuario no lo haya ocultado en su Pulso personal.

**¿Qué significa "Alcance inválido: use mine o company."? (400)**
Al guardar el orden del Pulso, el parámetro `scope` debe ser `mine` (mi Pulso) o `company` (el de la compañía). La
pantalla lo manda sola; si llega este error desde una integración, corrija la llamada.

**¿Qué significa "Panel de Pulso desconocido: RADAR."? (400)**
La clave del panel no está en el registro del Pulso (hoy: `INDICATORS`, `CHARTS`, `WAREHOUSE`, `ACTIVITY`). Suele pasar con
una versión de la pantalla más nueva que el servidor; actualice la página o espere la actualización del servidor.

**¿Qué significa "Tipo inválido: use indicator o chart."? (400)**
Cada elemento del orden debe ser `indicator` (indicador) o `chart` (gráfico).

**¿Qué significa "Falta el permiso 'pulse.organize_company'."? (403)**
Intentó organizar el Pulso **de la compañía** sin ese permiso. Organice el suyo ("Organizar mi Pulso", no exige permiso) o
pida el permiso a un administrador.

**¿Qué significa "Panel de Pulso 'WAREHOUSE' no encontrado." o "Indicador '12' no encontrado." al guardar el orden? (404)**
Quiso ordenar un panel o un elemento que no puede ver (le falta el permiso, el permiso de datos, el módulo o la lectura de la
fuente, o el elemento se eliminó). Recargue el Pulso: solo aparecen los que sí puede ver. No se guarda nada del pedido.

**¿Qué significa "Fuente de datos 'TRANSPORT_ORDER' no encontrado." al crear un indicador o gráfico? (404)**
No puede leer esa fuente de datos (en el ejemplo le falta `orders.view`), así que tampoco puede crear indicadores o
gráficos sobre ella. Elija otra fuente o pida el permiso.

## Lote F8a — frontend: menú, Sistema y Análisis

Detalle en el capítulo [F8a — Menú completo, marca, Pulso por paneles, Sistema y Análisis](frontend/f8a-menu-sistema-analisis-y-marca.md).

**¿Por qué no veo el grupo "Análisis" (o cualquier otro) en el menú?**
Un grupo del menú solo se pinta si tiene al menos un ítem visible para usted: revise que tenga el permiso y que el
módulo de ese ítem esté encendido para su compañía. Un ítem sin función todavía (por ejemplo, "Sala de despacho")
sí aparece, pero abre una pantalla que avisa "Esta pantalla llega en un lote posterior."

**¿Cómo genero una nueva contraseña temporal para un usuario que ya tiene cuenta?**
No se puede desde "Nuevo usuario" (esa pantalla es solo para el alta). Pídale que use "¿Olvidó su contraseña?" en el
inicio de sesión, o cambie su contraseña desde Mi cuenta si tiene acceso a la cuenta.

**Creé un usuario sin contraseña y cerré el modal antes de copiar la temporal, ¿cómo la recupero?**
No se puede: se muestra una sola vez y no queda guardada en ningún lugar visible. Edite al usuario y pídale que use
"¿Olvidó su contraseña?", o asígnele una contraseña nueva si tiene ese permiso.

**¿Por qué no puedo editar el nombre o el estado de un usuario, aunque tengo `admin.users`?**
Porque "Este usuario pertenece a más de una compañía; solo el administrador de plataforma puede editar su cuenta."
(403): el nombre y el estado de la cuenta son de la persona, no de una compañía en particular. Sus roles y
permisos **en su compañía** sí se pueden editar igual; para el resto, pida al administrador de plataforma.

**¿Por qué "Restablecer PIN"/"Asignar PIN" me piden confirmar mi contraseña (reautenticación) y "Quitar PIN" no?**
Asignar o cambiar el PIN de otra persona es una acción sensible (AAL2); quitarlo no lo es (equivale a bloquearle el
acceso, no a dárselo). Si su sesión ya se reautenticó hace poco para otra acción, puede que no se lo vuelva a pedir.

**¿Por qué el código de un aparato nuevo ya no se puede escribir a mano?**
Antes de este lote la pantalla no pedía un código, pero el servidor sí lo exigía — esa inconsistencia ya no existe:
el servidor genera uno legible (`AP-XXXXXX`) automáticamente. El código de **registro** (el que se usa una sola vez
para dar de alta el aparato en Teikem Almacén) sigue siendo aparte y se muestra tras crear el aparato.

**Ajusté la etiqueta de un valor de catálogo y no veo el cambio en otra pantalla que ya tenía abierta.**
El cambio se aplica de inmediato en el servidor; una pantalla que ya tenía los datos cargados (por ejemplo, Pulso
abierto en otra pestaña del navegador) los actualiza en su próxima consulta, no retroactivamente en lo que ya
pintó. Recárguela si hace falta verlo ahí también.

**"Restaurar" un valor propio desactivado no parecía hacer nada, ¿ya se arregló?**
Sí: antes, la lista de valores excluía siempre los inactivos aunque se pidiera incluirlos, así que "Restaurar" no
tenía efecto visible. Ahora un valor propio restaurado vuelve a la lista activa y a ofrecerse al capturar.

**¿Por qué "Organizar el de la compañía" no aparece en mi Pulso?**
Exige el permiso `pulse.organize_company`, aparte de cualquier otro permiso `pulse.*` de los paneles. "Organizar mi
Pulso" no exige ningún permiso extra: siempre está disponible si tiene al menos un panel.

**¿Por qué las acciones de las tablas de Sistema ahora son iconos y no dicen el nombre?**
Es el estándar de la maqueta original (un icono a tono con la acción, con su nombre como tooltip al pasar el
cursor), no un cambio de qué se puede hacer — cada icono hace exactamente lo que hacía antes el botón de texto.

**Le exigí MFA a un usuario, ¿por qué no le activé yo mismo la verificación en dos pasos?**
Exigir MFA no la activa por esa persona — cada quien enrola la suya (escanea el código QR con su propia
aplicación de autenticación). Lo que hace "Exigir MFA" es que su **próximo** inicio de sesión le pida enrolar
antes de dejarla entrar; hasta que lo haga, no puede iniciar sesión.

**Un usuario perdió el teléfono con su verificación en dos pasos y quedó bloqueado, ¿cómo lo recupero?**
Un administrador con `admin.users` usa "Restablecer MFA" en su fila (tabla de usuarios): le quita la verificación
confirmada y sus códigos de recuperación. En su siguiente inicio de sesión, si todavía se le exige, se le pide
enrolar una nueva desde cero.

**¿"Ya no exigir MFA" le quita la verificación en dos pasos a la persona?**
No: solo quita la **obligación** puntual que usted le había puesto. Si esa persona ya tenía su propia
verificación activa (por su cuenta, o porque la compañía entera lo exige), la conserva.

## Lote 10 — Migración de datos

**¿Por qué la migración es un comando de línea de comandos y no una pantalla del sitio?**
Porque es un trabajo de aprovisionamiento (crea una compañía entera) que corre una o dos veces por compañía, con
archivos que contienen datos personales de clientes; no tiene sentido exponerlo como una pantalla ni darle permisos
propios. Escribe con los mismos servicios que el sitio, así que el resultado es idéntico a cargarlo a mano.

**Corrí `import-legacy` dos veces por error, ¿duplicó todo?**
No. Es idempotente: la segunda corrida encuentra la compañía, las categorías, los productos, los proveedores y los
clientes por su clave natural (nombre, SKU, "Código QuickBooks") y los cuenta como "Ya existían" en vez de crearlos
de nuevo. El saldo inicial tampoco se repite si ya hay un asiento `OPENING_BALANCE` hacia ese almacén.

**Cambié un precio en QuickBooks después de la carga inicial, ¿tengo que cargar todo otra vez?**
No. Corra `import-legacy <config.json> --update`: agrega lo nuevo y actualiza los productos, proveedores, clientes y
consignatarios que ya existen, sin tocar el saldo inicial. Para eso solo hace falta volver a exportar Items y
Customers de QuickBooks.

**¿Por qué el saldo inicial no se actualiza con `--update`?**
Porque una vez que la compañía factura y despacha en Teikem, el inventario ya no lo lleva QuickBooks: lo mueven los
recibos, despachos y ajustes de Teikem. Repetir el saldo inicial desde QuickBooks pisaría movimientos reales. Si
necesita recargar existencias desde cero, la vía es `db-reset` y una carga completa.

**Quiero probar la migración desde cero varias veces, ¿tengo que reinstalar la base a mano?**
No: `dotnet run --project src/Teikem.Api -- db-reset --yes` borra la base configurada y la vuelve a inicializar
exactamente como `db-init` sobre un servidor limpio. Es irreversible, por eso exige `--yes`; y solo actúa sobre un
servidor local salvo que agregue `--allow-remote`.

**¿`db-reset` puede borrar por error la base del WMS heredado (MSWM)?**
No: rehúsa expresamente cualquier base cuyo nombre empiece por `MSWM`, con el mensaje *"db-reset no toca la base del
WMS heredado."*, sin importar a qué servidor apunte la cadena de conexión.

**El dry-run rechazó algo con "ejecute db-init antes de la carga", ¿qué hago?**
Corra `dotnet run --project src/Teikem.Api -- db-init` sobre esa base (es aditivo: no borra nada) para que se
apliquen los catálogos nuevos del Lote 10 (los términos de pago de QuickBooks y el motivo `OPENING_BALANCE`), y vuelva
a correr el dry-run.

## Lote 11 — Cambios de Almacén, tanda 1 (cupo de posición, ocupación, zonas, posiciones, ubicaciones, ciudades y exportación)

Capítulos: [06 — Inventario y almacén, sección 1](06-inventario-y-almacen.md#1-almacenes-y-ubicaciones) y
[F6 — Almacén e inventario](frontend/f6-almacen-e-inventario.md). Cierre y decisiones: `docs/lote11-decisiones.md`.
Los mensajes marcados "pantalla" los muestra la interfaz antes de llamar al servidor; los demás los manda el API (con el
código HTTP indicado).

### Mensajes de error nuevos o cambiados

**¿Qué significa "El cupo máximo de la posición debe ser mayor que cero."? (400, también en pantalla)**
Escribió `0` o un número negativo en "Cupo máximo" al crear o editar una posición. El cupo son las unidades que caben en
la posición y, si lo indica, debe ser un entero mayor que cero. Si la posición no tiene un límite, deje el campo vacío
(vacío = sin cupo). Por el API el error viene en `errors.maxCapacityQty`.

**¿Qué significa "El cupo máximo debe ser un número entero."? (pantalla)**
Escribió decimales o un texto en "Cupo máximo". Use un número entero (por ejemplo `25`).

**¿Qué significa "El cupo máximo es demasiado grande."? (pantalla)**
El cupo pasa de 2.147.483.647, el máximo que admite el sistema. Escriba una cantidad realista o deje el campo vacío.

**¿Qué significa "Estado de ocupación desconocido: 'X'. Use EMPTY, PARTIAL, FULL o NO_CAPACITY."? (400)**
Solo el API: la consulta de posiciones (`GET /api/v1/warehouses/{publicId}/bins`) recibió en `occupancy` un valor que no
existe. Los valores válidos son `EMPTY` (vacía), `PARTIAL` (parcial), `FULL` (llena) y `NO_CAPACITY` (ocupada sin cupo);
se pueden mandar varios repitiendo el parámetro. La pantalla nunca lo produce: sus filtros usan esos cuatro valores.

**¿Qué significa "La zona no se puede mover a otro almacén."? (400)**
Solo el API: el `PATCH` de una zona trajo `warehouseId`. El código, el nombre y el tipo de una zona se editan, pero la zona
pertenece al almacén donde nació. Si necesita la misma zona en otro almacén, créela allá.

**¿Qué significa "Ya existe una zona con ese código en el almacén."? (409)**
Otra zona del mismo almacén ya usa ese código. Desde el Lote 11 también aparece al **editar** el código de una zona: en la
pantalla el mensaje sale debajo del campo Código y el formulario sigue abierto; elija un código distinto. El mismo código sí
puede existir en almacenes diferentes.

**Ya no aparece "El código de la zona no se puede cambiar.", ¿qué pasó?**
Se retiró a propósito: el código de una zona ahora se puede editar (Almacenes → clic en la zona, o Ficha › Zonas). Cambiarlo
no mueve las posiciones ni las existencias de la zona. Si un manual o una captura vieja lo menciona, ya no aplica. Lo que
sigue fijo es el almacén de la zona, y el código y la zona de una **posición**.

**¿Qué significa "El código es obligatorio." al editar una zona? (400 / pantalla)**
Vació el campo Código de la zona. Al editar, el código es obligatorio como al crear: déjelo con su valor actual o escriba
otro (letras, números, guion y guion bajo, máximo 30; si no cumple sale `El código solo admite letras, números, guion y
guion bajo (máximo 30).`).

**¿Qué significa "Zona no encontrada." al listar posiciones? (404)**
El filtro `zoneId` del listado de posiciones apunta a una zona que no es del almacén consultado (o no existe). Elija una
zona del propio almacén. (`zoneIds`, en plural, no da este error: un id ajeno simplemente no devuelve posiciones.)

**¿Qué significa "No hay ciudades ni códigos postales que coincidan."? (pantalla)**
En el selector "Ciudad o código postal" no hay ninguna localidad del catálogo con lo que escribió. Pruebe con menos letras,
con el nombre del municipio o con los primeros dígitos del código postal. La búsqueda no distingue mayúsculas ni acentos.

**¿Qué significa "Su usuario no puede consultar el catálogo de ciudades."? (pantalla)**
El servidor negó la consulta del catálogo de localidades a esa sesión. El catálogo solo pide una sesión iniciada, así que lo
normal es una sesión vencida: vuelva a iniciar sesión. Si persiste, avise a soporte.

**¿Qué significa "Ninguna posición coincide con los filtros."? (pantalla, Ubicaciones)**
Los filtros combinados no dejan ninguna posición (por ejemplo, la zona A con un tipo de zona que la zona A no tiene). Quite
algún filtro o use **Limpiar**.

**¿Qué significa "No se pudo generar el archivo. Intente de nuevo."? (pantalla)**
Falló la generación del Excel, CSV o PDF, o alguna de las lecturas de la consulta completa de una lista del servidor.
Vuelva a intentar; si la lista es muy grande, afine los filtros. Si sigue, avise a soporte con el nombre de la pantalla y
el formato.

**¿Qué significa "Se exportaron las primeras {count} filas (límite de exportación). Afine los filtros para exportar el resto."? (aviso)**
La consulta tiene más de 10.000 filas y el archivo lleva solo las primeras 10.000. Filtre por almacén, fecha o estatus y
exporte por partes.

**¿Qué significa "Solo se listan las primeras {count} posiciones: elija zonas para acotar la lista."? (pantalla, Nuevo conteo cíclico)**
El almacén tiene más posiciones de las que el selector puede cargar (10.000). Elija primero una o varias zonas y vuelva a
abrir la lista de posiciones.

### Preguntas frecuentes

**¿Por qué la ocupación dice "sin cupo configurado"?**
En Ubicaciones, el recuadro de una zona dice "unidades · sin cupo configurado" cuando **ninguna** de sus posiciones activas
tiene "Cupo máximo": la ocupación se calcula contra la suma de los cupos y sin cupos no hay contra qué comparar (por eso
solo muestra las unidades que hay). Escriba el cupo de las posiciones (Ficha del almacén › Posiciones, o "Nueva
posición"). En una posición suelta, "Ocupada sin cupo" significa lo mismo: tiene existencia pero no cupo.

**¿Por qué el recuadro de una zona dice "N posiciones sin cupo"?**
Porque esa zona mezcla posiciones con cupo y sin cupo. El porcentaje solo cuenta las que tienen cupo (suma de cupos contra
lo que hay en esas mismas posiciones); las demás se avisan aparte para que no falseen el número.

**¿Cómo se calcula "Vacía", "Parcial" y "Llena" en una posición?**
Se compara la existencia **en mano** (incluye lo reservado) con el cupo: sin existencia es Vacía; con existencia y menos que
el cupo, Parcial; con existencia igual o mayor que el cupo, Llena; con existencia y sin cupo, Ocupada sin cupo. Si la
existencia pasa del cupo, el porcentaje puede pasar de 100 %.

**¿Cómo exporto una tabla y qué exporta?**
Use el botón **Exportar** del pie de la tabla y elija Excel (.xlsx), CSV (.csv) o PDF (.pdf). Exporta **todas las filas que
cumplen los filtros de la pantalla**, no solo la página que ve, con las columnas visibles (sin la de acciones) y el mismo
texto que muestra la pantalla; en las listas que vienen del servidor lee todo el resultado, hasta 10.000 filas. El archivo se
llama como la tabla más la fecha (por ejemplo `almacenes-2026-09-29.xlsx`) y se genera en su navegador.

**En el CSV algunos textos empiezan con un apóstrofo, ¿es un error?**
No. Es una protección: si un texto empieza con `=`, `+`, `-` o `@`, Excel lo podría tomar por una fórmula, así que el CSV lo
guarda con un apóstrofo delante. Los números no se tocan.

**En el PDF salen signos de interrogación (?) en lugar de un símbolo.**
El PDF usa letra estándar que solo cubre caracteres latinos: tildes y eñes salen bien; otros símbolos (por ejemplo, letras de
otros alfabetos) se sustituyen. El Excel y el CSV sí los conservan.

**¿Por qué mi ciudad sale en mayúsculas?**
Porque la ciudad viene del catálogo postal de USPS. Fuera de Puerto Rico se guarda el nombre postal oficial, que está en
mayúsculas y sin acentos (`NEW YORK`). En Puerto Rico se guarda el **municipio** con su escritura normal (`Toa Baja`), aunque
la lista de opciones muestre el nombre postal en mayúsculas y el municipio entre paréntesis (`00952 · SABANA SECA (Toa Baja), PR`).
Desde la pantalla la ciudad se elige del catálogo, no se escribe a mano.

**No encuentro mi ciudad o mi código postal en el selector.**
El catálogo tiene 42.522 códigos postales de Estados Unidos, sus territorios y Puerto Rico. Busque por el municipio o por los
primeros dígitos del código postal. Si de verdad no existe, avise a soporte: el catálogo se carga desde el seed y un
cambio directo en la tabla puede tardar hasta una hora en verse.

**¿Por qué no puedo escribir el Estado ni el País de un almacén?**
Porque se llenan solos con la ciudad o código postal que elija (aparecen deshabilitados: "Se llena según la ciudad
elegida."). Si no elige ninguna localidad, el país queda en Puerto Rico. Un almacén cuya dirección no está en el catálogo no
puede capturar su estado desde la pantalla.

**¿Por qué no puedo cambiar el código ni la zona de una posición?**
Es una regla del sistema: el código y la zona de una posición quedan fijos desde el alta y el mensaje es `El código y la zona
de la posición no se pueden cambiar.` (400). Sí puede editar pasillo, rack, nivel, posición, cupo y peso máximo (cambiar las
partes **no** recalcula el código). Si necesita otro código u otra zona, cree una posición nueva y dé de baja la anterior
(solo procede si no tiene inventario ni tareas abiertas).

**¿Puedo cambiar el código de una zona? ¿Se pierden sus posiciones?**
Sí, y no se pierde nada: las posiciones y las existencias siguen ligadas a la zona. Solo el código nuevo debe ser distinto del
de las demás zonas del almacén. Los códigos de las posiciones no cambian.

**¿Por qué la papelera de una zona está deshabilitada?**
En "Zonas de este almacén" (lista de Almacenes) la papelera se deshabilita cuando la zona tiene posiciones activas; el
mensaje al pasar el cursor es "No se puede dar de baja: esta zona tiene posiciones creadas". Dé de baja o mueva primero sus
posiciones. El servidor aplica la misma regla: `La zona tiene posiciones activas; desactívelas primero.` (409).

**¿Por qué el filtro "Código" de Posiciones (o "Posición" de Ubicaciones) también encuentra por pasillo, rack o zona?**
Porque ese cuadro busca el texto dentro del código, del código de la zona, del pasillo, del rack, del nivel y de la
posición (antes solo miraba código y zona, y no se podía buscar por lo que la tabla llama "ubicación"). Para afinar use los
filtros Pasillo, Rack, Nivel y Posición de la ficha.

**Ordené por una columna y no quedó todo el resultado ordenado.**
En las listas paginadas por el servidor (Posiciones, Ubicaciones, Productos, Inventario, Recibos, etc.) el orden por columna
se aplica solo a las filas de la página que está viendo. Para ver todo en orden, exporte la tabla y ordénela en Excel, o use
los filtros para reducir el resultado a una sola página.

**¿Por qué ya no veo "Mostrar" (solo activos / incluir inactivos) en Almacenes?**
La lista de almacenes ahora trae todos y se filtra con **Estatus** (junto a Código, Nombre, Dirección y Tipo). Los inactivos
se ven atenuados. Para ver solo los activos, elija "Activo" en el filtro Estatus.

**La app de almacén ya instalada no encuentra la posición que escaneo (Acomodar, Despacho, Conteo).**
Es esperable con la versión anterior de la app: el listado de posiciones del servidor cambió de formato (ahora viene
paginado) y la app vieja no lo entiende. Instale la versión nueva de la app. Esta conclusión sale de comparar el código de
las dos versiones; no se probó en un aparato.

## Lote 12 — Cambios de Almacén, tanda 2 (marca y modelo, filtros de Productos y Compras, reportes PDF, Proveedores, Posiciones, cupo en bloque y cupo estimado)

Capítulos: [06 — Inventario y almacén, secciones 1.2, 2 y 8](06-inventario-y-almacen.md),
[10 — Migración de datos, sección 4](10-migracion-de-datos.md) y
[F6 — Almacén e inventario](frontend/f6-almacen-e-inventario.md). Cierre y decisiones: `docs/lote12-decisiones.md`.
Los mensajes marcados "pantalla" los muestra la interfaz antes de llamar al servidor; los demás los manda el API (con el
código HTTP indicado). Los mensajes del cupo en bloque se documentaron primero como "Lote 11 (complemento)"; pertenecen a
este cierre.

### Mensajes de error nuevos o cambiados

**¿Qué significa "Indique el cupo máximo (maxCapacityQty) o clear: true para quitarlo."? (400)**
Pidió el cupo en bloque (`POST /api/v1/warehouses/{id}/bins/capacity`) sin decir qué hacer. Envíe `maxCapacityQty` con el
cupo (entero mayor que cero) o `clear: true` para dejar las posiciones sin cupo. El error viene en `errors.maxCapacityQty`.

**¿Qué significa "Indique el cupo máximo o clear: true, no ambos."? (400)**
El cuerpo trae un cupo y además `clear: true`. Son opuestos: deje solo uno. El error viene en `errors.clear`.

**¿Qué significa "Indique al menos un filtro de posiciones (zoneIds, aisle, rack, level, position, search o binIds) o allBins: true para aplicarlo a todo el almacén."? (400)**
El cupo en bloque no trae ningún filtro de posiciones, así que se aplicaría a **todo** el almacén. Para evitar hacerlo por
accidente se exige confirmarlo con `allBins: true`; si no era esa la idea, agregue zonas, pasillo, rack, nivel, posición,
búsqueda o ids de posición. El error viene en `errors.allBins`.

**¿Qué significa "Ninguna posición de ALM-DEPOT tiene historial de existencias en el WMS; las posiciones quedan sin cupo."? (advertencia de `import-legacy`)**
El importador no encontró ninguna existencia histórica de ese almacén en MSWM (inventario, conteos ni acomodos), así que no
hay de dónde estimar el cupo. La carga sigue normal y las posiciones quedan "sin cupo". Revise que `sources.mswm.warehouseId`
sea el almacén correcto; si lo es, asigne los cupos con el cupo en bloque.

**¿Qué significa "La marca no puede exceder 100 caracteres."? (400, también en pantalla)**
La marca del producto tiene más de 100 caracteres (sin contar los espacios de los extremos). Acórtela. En la pantalla el
campo ya no deja escribir más de 100; el mensaje sale sobre todo al llamar al API directamente (`errors.brand`).

**¿Qué significa "El modelo no puede exceder 100 caracteres."? (400, también en pantalla)**
Lo mismo para el modelo del producto (`errors.model`).

**¿Qué significa "Almacén no encontrado." al listar productos? (404)**
Uno de los almacenes del filtro (`warehousePublicId` o `warehousePublicIds`) no existe o es de otra compañía. Quite ese
almacén del filtro o vuelva a elegirlo de la lista.

**¿Qué significa "Escriba una nota que explique el ajuste."? (400, también en pantalla)**
La nota del ajuste manual es obligatoria. Escriba por qué se ajusta el inventario (hasta 300 caracteres; solo espacios no
cuenta). Desde el ajuste del 2026-09-30 también la exige el API (`POST /api/v1/inventory/adjustments` responde 400 con el
error en `errors.notes`), así que aplica a todas las pantallas, a la app móvil y a las integraciones. Los ajustes que hace el
sistema solo (diferencias de recibo o de conteo, reversas de recolección, saldo inicial de la migración) no la piden.

**¿Qué significa "Las notas admiten como máximo 300 caracteres."? (400, también en pantalla)**
La nota del ajuste tiene más de 300 caracteres. Acórtela.

**¿Qué significa "Indique la cantidad del ajuste (número, positivo para sumar o negativo para restar)."? (pantalla)**
En "Añadir ajuste" la cantidad está vacía o no es un número. Escriba un número: positivo para sumar al inventario, negativo
para restar (no puede ser cero). Este texto reemplaza a "Se esperaba un número." en los ajustes; también lo ve quien ajusta
desde la pantalla Ajustes de inventario, porque comparten la regla. (Lote 14: esa pantalla salió del menú y el modal de ajuste ya no muestra este
texto; ver la sección «Lote 14».)

**¿Qué significa "Inventario insuficiente de {sku} en {posición}: disponible {x}, solicitado {y}." dentro del ajuste? (409)**
Restar esa cantidad dejaría el inventario de la posición en negativo. El mensaje aparece dentro del bloque de ajuste, que
sigue abierto. Corrija la cantidad (no puede restar más de lo disponible en esa posición) o elija otra posición.

**¿Qué significa "El teléfono debe tener 10 dígitos: (###) ###-####."? (pantalla)**
El teléfono del proveedor no tiene los dígitos que pide la máscara de la compañía (en Puerto Rico, 10; cada `#` es un
dígito). Escriba los dígitos (el campo pone la máscara solo; puede empezar con el código de país `+1`) o déjelo vacío. Un
proveedor cargado antes con otro formato tiene que corregirse para poder guardar. Si la compañía usa otra máscara (Ajustes de
la compañía → Región y formatos → Teléfono), el mensaje dice su cantidad de dígitos y su máscara. Antes del lote F9 decía
"El teléfono debe tener 10 dígitos: (xxx)xxx-xxxx.".

**¿Qué significa "Indique el almacén." al crear una orden de compra? (pantalla)**
El modal de la orden exige el almacén aunque el API lo acepte vacío cuando la compañía tiene uno solo. Elíjalo de la lista.
Si el API responde "Indique el almacén: la compañía tiene más de uno." (400) es la misma regla para quien llama sin almacén.

**¿Qué significa "Agregue al menos una línea con cantidad ordenada mayor que cero."? (pantalla)**
Todas las líneas de la orden tienen la cantidad vacía o en cero. Capture al menos una con cantidad mayor que cero, o quite las
líneas sobrantes. Vale al crear la orden y al guardar cambios en sus líneas. El API responde "La orden de compra debe tener al
menos una línea." o "La cantidad ordenada debe ser mayor que cero." (400).

**¿Qué significa "Indique el cupo máximo." / "Elija al menos un filtro (zona, pasillo, rack, nivel o posición) o marque «Todo el almacén»." en Asignar cupo? (pantalla)**
En el modal **Asignar cupo**, el primer mensaje sale si eligió "Cupo máximo" y dejó el campo vacío: escriba el cupo, o elija
"Quitar cupo". El segundo aparece en la vista previa cuando no hay ningún filtro puesto: agregue una zona, pasillo, rack,
nivel o posición, o marque "Todo el almacén" si de verdad quiere cambiar todo el almacén. Mientras falte alguno de los dos, el
botón de aplicar está deshabilitado.

**¿Qué significa "Ninguna posición coincide con el alcance: no hay nada que aplicar."? (pantalla)**
Los filtros del modal Asignar cupo no encuentran ninguna posición **activa** (o, con "Solo posiciones sin cupo", todas ya
tienen cupo). Cambie los filtros. No se cambia nada mientras esté en cero.

**¿Qué significa "No se pudo calcular a cuántas posiciones se aplicará: …"? (pantalla)**
La vista previa del modal Asignar cupo no pudo consultar el servidor (el texto que sigue es el motivo, por ejemplo un permiso
o una desconexión). El botón de aplicar queda deshabilitado; cierre el modal y vuelva a abrirlo, o revise su conexión y su
permiso `warehouse.manage`.

**¿Qué significa "No se pudo generar el reporte. Intente de nuevo."? (aviso de pantalla)**
El navegador no pudo leer los datos o armar el PDF de Reporte de inventario / Reporte de ajustes. Intente de nuevo; si sigue,
afine los filtros (menos productos) y revise su conexión.

**¿Qué significan los avisos de los reportes PDF?**
Salen en el recuadro de avisos de la primera página: "Productos sin existencia (en mano 0) no incluidos: N." (el reporte de
inventario no lista productos en cero); "Productos con existencia sin costo de compra: N. Su valor aparece como «—» y no se
suma en los totales." (falta el costo de compra en esos productos); "Saldos iniciales de la migración excluidos: N (no son
ajustes de la operación)." (el reporte de ajustes no cuenta el saldo inicial cargado por la migración); "La vista «…» depende
del estado actual del producto y no aplica a los movimientos…" (hay un indicador elegido y no filtra ajustes); "El reporte
incluye solo los primeros N productos / los N ajustes más recientes (límite de lectura). Afine los filtros para ver el resto."
(hay más de 10.000 filas: acote con los filtros). No son errores: son datos del reporte.

### Preguntas frecuentes

**¿Cómo corrijo el cupo de muchas posiciones a la vez (o pongo el mismo cupo a muchas)?**
En Posiciones, o en la pestaña Posiciones de la ficha del almacén, use el botón **Asignar cupo** (necesita `warehouse.manage`): elija
el alcance, escriba el cupo o elija "Quitar cupo", revise "Se aplicará a N posiciones" y aplique. Por el API es
`POST /api/v1/warehouses/{id}/bins/capacity` y los mismos filtros del listado de posiciones, por ejemplo
`{ "zoneIds": [3], "aisle": "01", "maxCapacityQty": 40 }`. Aplica a todas las que cumplen (no solo a una página) y responde
cuántas cumplían (`matched`) y cuántas cambiaron (`changed`). Para ver antes cuántas serán, pida el listado con esos filtros
y `take=1` y mire `total`. Necesita el permiso `warehouse.manage`.

**¿Cómo lleno solo las posiciones que no tienen cupo, sin tocar las que ya capturé?**
Agregue `onlyWithoutCapacity: true`. Por ejemplo `{ "allBins": true, "onlyWithoutCapacity": true, "maxCapacityQty": 50 }`
llena todas las posiciones activas del almacén que están sin cupo y deja intactas las demás.

**¿De dónde salió el cupo de mis posiciones?**
Lo estimó la migración desde el historial del WMS anterior: el mayor total que tuvo cada posición (inventario actual y
anterior, conteos y acomodos por día) redondeado hacia arriba a la decena. Las posiciones sin historial toman la mediana de
su pasillo, o si no de su zona, o si no del almacén; esa mediana solo se usa si sale de al menos 5 posiciones con historial
(con menos se pasa al siguiente nivel). Advance Solutions no tiene cupo estimado: sus posiciones quedan "sin cupo". El detalle, con el origen de cada cupo (`HISTORIAL`, `PASILLO`, `ZONA`,
`ALMACEN`), está en el CSV `-cupos` del reporte de la migración. Es una estimación: corríjala con el cupo en bloque o
editando la posición.

**¿La migración con `--update` me borra los cupos que corregí?**
No. `--update` solo llena el cupo de las posiciones que **no** tienen; una posición con cupo (capturado a mano o de una
carga anterior) lo conserva siempre y el CSV lo dice: "Se conserva el cupo actual (N)".

**¿Por qué el reporte de inventario no lista productos con 0?** Porque es una foto de lo que hay: solo entran los productos
con existencia en mano distinta de cero, y un aviso dice cuántos quedaron fuera. Si necesita también los de cero, use
**Exportar** de la tabla.

**¿Por qué el reporte de ajustes no trae los saldos iniciales de la migración?** Porque la migración los registró como
ajustes (motivo `OPENING_BALANCE`) pero no son ajustes de la operación; el reporte los excluye y avisa cuántos son
("Saldos iniciales de la migración excluidos: N"). Siguen visibles en el Kárdex de inventario.

**¿Por qué el valor del inventario sale «—»?** Porque el producto no tiene **Costo de compra**. El valor es Total × costo de
compra; sin costo no se calcula y no entra en los totales (el reporte avisa cuántos productos están así). Capture el costo en
el producto y genere el reporte otra vez. El sistema no guarda costo promedio ni costo por lote.

**¿Por qué no puedo cambiar el proveedor o el almacén de una orden ya creada?** Desde el ajuste del 2026-09-30 **sí se
pueden cambiar mientras la orden está en Borrador**. Una vez enviada quedan fijos (el API responde 409 "El proveedor y el
almacén solo se cambian mientras la orden de compra está en borrador."). Si la orden ya se envió y se equivocó, cancélela y
cree otra.

**¿Por qué, con un filtro de Almacén en Productos, siguen saliendo productos sin existencia?** El filtro acota las cantidades
de cada fila a ese almacén, no quita productos. Para quedarse con los que tienen existencia use el indicador "Unidades
totales": desde el ajuste del 2026-09-30 muestra los productos **activos con existencia en mano** mayor que cero (en el API,
`activeOnly=true&onlyOnHand=true`), que es lo que suma su cifra.

**¿Por qué el filtro "Estado" y el buscador de la tabla de Productos ya no están?** Los indicadores de arriba (SKUs activos,
Unidades totales, Bajo mínimo, Con número de serie) reemplazan al filtro Estado, y para buscar por texto están los filtros
Nombre y SKU.

**¿Adónde se fue "Ubicaciones"?** Se llama **Posiciones** desde el Lote 12 (mismo lugar del menú y misma dirección
`/warehouse/locations`).

**¿Por qué "Con número de serie" o "Bajo mínimo" está en naranja?** Porque hay algo que atender: productos bajo su mínimo o
productos con serie cuyas series capturadas son menos que su existencia ("N sin series completas"). Sin pendientes, el
indicador se ve con el color normal. Un clic en el indicador filtra la tabla para verlos.

### Ajustes decididos tras el cierre (2026-09-30)

**¿Qué significa "El proveedor y el almacén solo se cambian mientras la orden de compra está en borrador."? (409)**
Intentó cambiar el proveedor o el almacén de una orden que ya no está en Borrador (enviada, recibida parcial o completa, o
cancelada). Desde Enviada quedan fijos. Si hay que corregirlos, cancele la orden y cree otra con los datos correctos. Mandar
el **mismo** proveedor o almacén que ya tiene la orden no da este error (no es un cambio).

**¿Qué significa "El campo number de la orden de compra no se puede cambiar."? (400)**
El `PATCH` de la orden trae un campo que nunca cambia: `number`, `orderDate`, `currency`, `status` o `statusCode` (el
mensaje dice cuál). Quítelo del cuerpo. `supplierId` y `warehousePublicId` ya no dan este error: se cambian en Borrador.

**¿Qué significa "El proveedor está dado de baja; no admite órdenes de compra nuevas." / "El almacén está dado de baja; no admite órdenes de compra nuevas." al editar una orden? (422)**
Eligió, para una orden en Borrador, un proveedor o un almacén que está dado de baja. Elija uno activo o reactive el que
quería.

**¿Qué significa "Proveedor no encontrado." / "Almacén no encontrado." al editar una orden? (404)**
El proveedor o el almacén indicado no existe o es de otra compañía. Vuelva a elegirlo de la lista.

**¿Puedo cambiar el proveedor o el almacén de una orden en Borrador?** Sí. En la ficha de la orden (o por el API, `PATCH
/api/v1/purchase-orders/{publicId}` con `supplierId` y/o `warehousePublicId`) mientras la orden está en Borrador. Las líneas
no cambian. Queda en la auditoría de la orden.

**¿Por qué un ajuste manual pide nota y el del conteo o el del recibo no?** Porque la nota explica un ajuste que decide una
persona. Los ajustes que hace el sistema ya dicen de dónde vienen (el conteo, el recibo, la recolección eliminada o la
migración, con su referencia en el Kárdex) y no la piden. **Sí** la pide la resolución de un faltante de compra con "Ajuste
manual" (es un ajuste manual de inventario): sin nota, 400 en `errors.notes`. Cerrar y Reordenar un faltante no mueven inventario y la
dejan opcional.

**¿Qué productos muestra el indicador "Unidades totales"?** Los productos **activos con existencia en mano mayor que cero**,
contando todas sus posiciones (también cuarentena, cruce de muelle y lo reservado). Por el API es `GET /api/v1/products?
activeOnly=true&onlyOnHand=true`; con almacenes en el filtro, la existencia se mide en esos almacenes. No es lo mismo que
`onlyAvailable` (disponible para recolectar: sin cuarentena ni cruce de muelle y restando lo reservado).

**¿Por qué al empacar a veces no aparece "Predeterminado de la compañía"?** Porque su compañía no tiene tipo de servicio (o
tipo de paquete) predeterminado. Entonces hay que elegirlo en el formulario; si se envía vacío, el API responde 400 "El tipo
de servicio es obligatorio." o "El tipo de paquete es obligatorio.". Los predeterminados los configura quien administra la
compañía (`PUT /api/v1/tenant/settings`, `defaultServiceType` y `defaultPackageType`, permiso `admin.tenant`) y cualquier
usuario con sesión los lee en `GET /api/v1/tenant/settings` (código o `null`).

## Lote 13 — Recibo (Lote 3 del plan de cambios): ciclo de estatus, encabezado editable, esperado en ciegos y filtros

Capítulos: [06 — Inventario y almacén, secciones 4, 5 y 7](06-inventario-y-almacen.md) y
[F6 — Almacén e inventario (pantallas)](frontend/f6-almacen-e-inventario.md). Los mensajes del API traen el código HTTP indicado; los
que se ven solo en pantalla van agrupados más abajo ("Mensajes que solo ve en la pantalla…"). Este lote cubre también Recolección y
empaque (dos paneles, barra arrastrable) y los ajustes del 2026-09-30 al Lote 12 (nota obligatoria, "Unidades totales", proveedor y
almacén de la orden en Borrador, predeterminados al empacar).

### Mensajes de error nuevos o cambiados

**Antes `POST /api/v1/receipts` ciego sin líneas respondía 400 "Indique al menos una línea."; ¿ya no?**
Ya no. Un recibo ciego o de devolución sin `lines` (y sin `confirm`) crea **solo el encabezado** y queda en **Esperado**; las
líneas se agregan después con `POST /api/v1/receipts/{publicId}/lines`. Si una integración dependía del 400 para detectar un
recibo vacío, revísela: ahora recibe 200 con el recibo creado. Si lo creó por error, bórrelo (`DELETE`, 204).

**¿Qué significa "Indique al menos una línea."? (400)**
Solo sale ahora con `confirm: true` (recibo en una llamada, la cola del aparato) en un ciego o devolución sin `lines`: no se
puede confirmar un recibo vacío. Mande las líneas escaneadas o quite `confirm` para crear solo el encabezado.

**¿Qué significa "La cantidad esperada solo se captura en recibos ciegos o de devolución; en uno con aviso de llegada u orden de compra viene del documento."? (400)**
Mandó `expectedQty` (o `clearExpected`) en un recibo contra un aviso de llegada o una orden de compra. Ahí lo esperado es lo
del documento y no se toca: capture solo lo recibido. El error viene en `errors.expectedQty` (captura de línea),
`errors["line.expectedQty"]` (línea extra) o `errors["lines[i].expectedQty"]` (alta con líneas).

**¿Qué significa "La cantidad esperada no puede ser negativa."? (400)**
La cantidad esperada de una línea de un ciego o devolución es menor que cero. Use 0 (no se esperaba nada) o un número
positivo; con más de 3 decimales sale "La cantidad admite como máximo 3 decimales." y en productos por serie debe ser entera.

**¿Qué significa "El producto de una línea del aviso de llegada o de la orden de compra no se puede cambiar."? (400)**
Intentó cambiar el producto (`productPublicId`) de una línea que viene del documento. Si llegó otra cosa, capture 0 en esa
línea y agregue una línea extra con el producto que llegó.

**¿Qué significa "La línea tiene asignaciones de cruce de muelle; cancélelas antes de cambiar el producto."? (409)**
La línea ya está asignada a una orden en un plan de cruce de muelle. Cancele esas asignaciones en el plan y vuelva a cambiar el
producto.

**¿Qué significa "El tipo de un recibo con aviso de llegada u orden de compra no se puede cambiar."? (400)**
El `PATCH` trae `type` distinto en un recibo con documento (su tipo es ASN). Solo los recibos sin documento cambian entre
ciego (`BLIND`) y devolución (`RETURN`).

**¿Qué significa "El almacén solo se puede cambiar en un recibo sin aviso de llegada ni orden de compra y sin líneas."? (400)**
El almacén de un recibo con documento lo fija el documento, y el de uno con líneas ya tiene mercancía capturada en sus
posiciones. Si se equivocó de almacén, borre las líneas (o el recibo) y vuelva a empezar en el almacén correcto.

**¿Qué significa "El transporte admite como máximo 80 caracteres."? (400)** — y **"La referencia admite como máximo 80 caracteres."? (400)**
El transporte o la referencia del encabezado (alta o `PATCH`) pasan de 80 caracteres (sin contar los espacios de los
extremos). Acórtelos. Para borrarlos mande `""`.

**¿Qué significa "El recibo REC-… ya fue confirmado; no se puede modificar."? (422)**
Además de las líneas, ahora también el `PATCH` del encabezado lo responde: un recibo Completado, Completado con diferencia o
Acomodado ya está en el Kárdex y queda congelado.

**¿Qué significa "El registro fue modificado por otro usuario; recargue e intente de nuevo."? (409, en el `PATCH` del recibo)**
El `rowVersion` que mandó es de una lectura vieja: alguien (o usted mismo, al guardar una línea) cambió el recibo después.
Vuelva a leer la ficha y mande el `rowVersion` nuevo.

**¿Qué significa "Muelle no encontrado."? (404)**
El muelle del alta o del `PATCH` no es del almacén del recibo (o no existe). Elija un muelle de ese almacén.

**¿Qué significa "Diferencia desconocida: 'X'. Use SHORT, OVER o NONE."? (400)**
El filtro `variance` de `GET /api/v1/receipts` trae un valor que no existe. Use `SHORT` (faltante), `OVER` (sobrante) o
`NONE` (sin diferencia); se puede repetir el parámetro para combinarlos.

**¿Qué significa "Fase desconocida: 'X'. Use OPEN, PENDING_PUTAWAY o DONE."? (400)**
El filtro `phase` de `GET /api/v1/receipts` trae un valor que no existe. Use `OPEN` (abiertos), `PENDING_PUTAWAY`
(confirmados con acomodo pendiente) o `DONE` (acomodados).

**¿Qué significa "El recibo no tiene líneas; agregue al menos una antes de confirmar."? (422)**
Intentó confirmar un recibo sin líneas. Ahora es posible tener uno así: un ciego o devolución nace solo con el encabezado (Esperado), y
un recibo al que se le borraron todas las líneas queda en Recibiendo con 0 líneas. Agregue al menos una línea y confirme; si el
recibo se creó por error, bórrelo. En la pantalla, el botón **Confirmar recibo** ni siquiera se habilita y dice "Agregue al menos una
línea para confirmar.".

**¿Qué significa "Escriba una nota que explique el ajuste."? (400)**
Vea la pregunta del Lote 12. Desde el 2026-09-30 aplica a **todo ajuste manual**: el modal de producto, el botón Ajustar del Kárdex,
`POST /api/v1/inventory/adjustments` y también **Resolver** un faltante de compra con "Ajuste manual". Escriba por qué se ajusta el
inventario (hasta 300 caracteres). Cerrar y Reordenar un faltante no mueven inventario y dejan la nota opcional.

**¿Qué significa "El tipo de servicio es obligatorio." / "El tipo de paquete es obligatorio."? (400) — y en pantalla "Elija el tipo de servicio." / "Elija el tipo de paquete."**
Al **Empacar** una recolección, su compañía no tiene un tipo de servicio (o de paquete) predeterminado, así que hay que elegirlo. Si el
predeterminado existe, la pantalla ofrece "Predeterminado de la compañía ({etiqueta})" y no lo pide. Quien administra la compañía puede
configurarlos (Configuración de la compañía).

**¿Qué significa "Posición no encontrada."? (404), "La posición de recepción debe estar en una zona STAGING o CROSSDOCK." (400) y "La posición de recepción está desactivada." (422)?**
La posición de recepción que eligió en el encabezado del recibo (o en una línea) no es del almacén del recibo, no está en una zona
STAGING o CROSSDOCK, o está desactivada. Elija otra, o déjela vacía para que use la primera posición de una zona STAGING. Si cambió el
almacén de un recibo, la posición anterior se limpia sola.

**¿Qué significa "Almacén no encontrado." al editar el encabezado de un recibo? (404)**
El almacén indicado no existe o es de otra compañía. Vuelva a elegirlo de la lista.

**¿Qué significa "Línea del recibo no encontrada." / "Producto no encontrado." al capturar una línea? (404)**
La línea no pertenece a ese recibo (por ejemplo, otra persona la quitó) o el producto elegido no existe o es de otra compañía. Recargue
el recibo y vuelva a elegir el producto.

**¿Qué significa "rowVersion inválido: se espera el valor base64 devuelto por la ficha."? (400)**
Una integración mandó `rowVersion` con un valor que no es el que devolvió la ficha del recibo. Use exactamente el `rowVersion` de la
última lectura o no lo mande.

**¿Qué significa "El aviso de llegada tiene un recibo abierto; elimínelo antes de cancelar."? (409)**
El aviso ya tiene un recibo (Esperado, Recibiendo o Discrepancia). Bórrelo desde Recibo y después cancele el aviso.

**Mensajes que solo ve en la pantalla de Recibo**

**¿Qué significa "Agregue al menos una línea para confirmar."?**
Está bajo el botón **Confirmar recibo** apagado: el recibo no tiene líneas guardadas. Capture una línea (producto y recibido).

**¿Qué significa "Hay líneas sin guardar: salga del campo o pulse Enter para guardarlas."?**
Escribió algo en una fila y todavía no se guardó (la fila se guarda al salir del campo o con Enter). Salga del campo o pulse Enter; si
el botón sigue apagado, revise que la fila tenga producto y cantidad recibida.

**¿Qué significa "Corrija las líneas marcadas antes de confirmar."?**
Alguna fila tiene un error (el mensaje está bajo su campo, por ejemplo "Indique la cantidad recibida." o un error del servidor). Corríjalo
y el botón se habilita.

**¿Qué significa "Guardando líneas…" bajo el botón Confirmar recibo?**
Hay una fila guardándose; espere un momento y el botón se habilita.

**¿Qué significa "El recibo ya está confirmado." / "El recibo ya está confirmado: su encabezado no se puede cambiar."?**
El recibo está Completado, Completado con diferencia o Acomodado: ya está en el inventario y no se modifica ni se vuelve a confirmar. El
encabezado se abre en solo lectura. Si algo salió mal, corríjalo con un ajuste de inventario.

**¿Qué significa "Escriba un número."?**
Una cantidad de la tabla de líneas tiene texto que no es un número. Use dígitos, con coma o punto para los decimales.

**¿Qué significa "Con aviso de llegada u orden de compra, el origen, el almacén y lo esperado vienen del documento."? / "El almacén solo se cambia en un recibo sin documento y sin líneas."?**
Son avisos del modal del encabezado: en un recibo con documento no se cambian el origen ni el almacén, y en un ciego o devolución el
almacén solo se cambia mientras no tenga líneas. Si se equivocó de almacén, borre las líneas (o el recibo) y vuelva a empezar.

**¿Qué significa "Elija primero el almacén."? / "Su usuario no puede consultar órdenes de compra."?**
Al crear un recibo contra un aviso o una orden de compra, la lista depende del almacén: elíjalo primero. Si además su usuario no tiene
permiso para ver órdenes de compra (`purchasing.view`) o el módulo Compras está apagado, no puede elegir una; pida el permiso o
reciba contra un aviso o en ciego.

**¿Qué significa "No hay recibos con estos filtros." / "Selecciona un recibo de la lista" / "Este recibo no tiene tareas de acomodo."?**
Son avisos de estado vacío: los filtros no dejaron ningún recibo, no hay ningún recibo elegido a la derecha, o el recibo elegido en
Acomodo pendiente no tiene tareas. Quite filtros o elija otro recibo.

**¿Qué significa "Recibo no encontrado." en la pantalla?**
El enlace (`?receipt=…`) apunta a un recibo que ya no existe (se borró) o que es de otra compañía. Elija uno de la lista.

**Mensajes que solo ve en Recolección y empaque**

**¿Qué significa "Máximo 100 líneas por recolección."?**
La rejilla llegó al tope de 100 líneas y **Añadir línea** se apagó. Recolecte lo capturado y haga otra recolección con el resto.

**¿Qué significa "Elija primero el almacén." en Recolección?**
El buscador de producto depende del almacén de la recolección. Elija el almacén en el panel Recolección y después los productos.

**¿Qué significa "Escriba para ver las órdenes" / "Ninguna orden con ese número." / "No se pudieron consultar las órdenes."?**
Es el filtro **No. de orden** de la lista: al escribir ofrece números de orden existentes. "Ninguna orden con ese número." es que no hay
coincidencias; "No se pudieron consultar las órdenes." es que no se pudo pedir la lista de sugerencias (por ejemplo, por falta de
permiso o de conexión). Puede escribir el número y aplicar el filtro de todos modos.

**¿Qué significa "Recolección no encontrada."?**
El detalle se pidió de una recolección que ya no existe o es de otra compañía. Cierre el detalle y elija otra de la lista.

### Preguntas frecuentes

**¿Qué pasó con el estatus `OPEN` del recibo?** Se retiró. Ahora un recibo abierto está en **Esperado** (solo encabezado),
**Recibiendo** (todo cuadra) o **Discrepancia** (alguna línea difiere); confirmado, en **Completado** o **Completado con
diferencia**; y al cerrar el acomodo, en **Acomodado**. Al actualizar la base, los recibos que estaban en `OPEN` pasaron a
Recibiendo (con líneas) o Esperado (sin líneas) con la nota "Lote 13: nuevo ciclo de estatus del recibo." en su historial.
Un filtro `status=OPEN` ya no trae nada: use `phase=OPEN`.

**¿Por qué mi recibo pasó solo de Recibiendo a Discrepancia?** Porque al guardar una línea lo recibido quedó distinto de lo
esperado. Cuando la corrige para que cuadre, vuelve solo a Recibiendo. Cada paso queda en el historial.

**Borré todas las líneas y el recibo no volvió a Esperado.** Es a propósito: un recibo nunca regresa a Esperado. Queda en
Recibiendo con 0 líneas; "Confirmar" responde 422 "El recibo no tiene líneas; agregue al menos una antes de confirmar."

**En un recibo ciego capturé lo esperado y confirmé con diferencia: ¿se hizo un ajuste?** No. En ciegos y devoluciones al
Kárdex entra **lo recibido** (un solo movimiento de recepción) y el recibo queda "Completado con diferencia" para que se vea.
Solo en recibos con aviso u orden de compra la diferencia se asienta como ajuste (`RECEIPT_VARIANCE`).

**¿Cómo veo los recibos que falta acomodar?** `GET /api/v1/receipts?phase=PENDING_PUTAWAY` (Completados y Completados con
diferencia); cada fila trae `pendingPutawayCount` con sus tareas de acomodo abiertas. Al cerrar la última pasan a Acomodado.

**La compañía apagó el estatus Discrepancia (o Completado con diferencia): ¿qué pasa?** El recibo se salta ese estatus: una
diferencia deja el recibo en Recibiendo y la confirmación lo lleva a Completado. Si apaga Recibiendo, el recibo se queda en
Esperado hasta confirmarse.

**¿Puedo buscar avisos por referencia o por fecha de llegada?** Sí: `GET /api/v1/asns?reference=…&expectedFrom=…&expectedTo=…`
(referencia sin distinguir mayúsculas; fechas inclusive; un aviso sin fecha no entra en un rango).

**¿Por qué mi recibo dice "Discrepancia"?** Porque alguna línea tiene lo recibido distinto de lo esperado. Contra un aviso de llegada o
una orden de compra, lo esperado es el del documento; en un recibo ciego o de devolución, es lo que usted escribió en **Esperado** (si
no escribió nada, no hay diferencia). Es solo una señal: no bloquea nada. Si fue un error de captura, corrija la cantidad y el recibo
vuelve solo a "Recibiendo"; si la diferencia es real, confirme y quedará "Completado con diferencia".

**Recibí menos de lo esperado: ¿qué entra al inventario?** Siempre **lo recibido**. Con aviso u orden de compra, el Kárdex registra la
recepción por lo esperado y un ajuste (`RECEIPT_VARIANCE`) por la diferencia, así que el neto es lo recibido, y el faltante de la orden
se resuelve después en Compras. En un ciego o devolución entra directamente lo recibido, sin ajuste; la diferencia solo queda marcada
en el estatus "Completado con diferencia".

**¿Cómo edito el encabezado de un recibo (transporte, referencia, posición, muelle)?** Con doble clic en el recibo de la lista o con el
lápiz del detalle. Se puede mientras el recibo esté abierto (Esperado, Recibiendo o Discrepancia); confirmado, se abre en solo lectura.
El tipo (Ciego ↔ Devolución) solo cambia si no tiene aviso ni orden de compra, y el almacén solo si además no tiene líneas.

**¿Por qué no hay "Añadir ítem" en un recibo contra una orden de compra o un aviso?** Porque ahí lo esperado es el del documento y no se
toca desde la web: solo se captura lo recibido (0 si no llegó). Un producto que llegó y no venía en el documento se registra por la app
de almacén o por el API (`POST /api/v1/receipts/{publicId}/lines`), como línea extra.

**En un ciego, lo esperado se copió solo y después ya no lo hizo. ¿Por qué?** Mientras usted teclea lo recibido en una fila nueva, la
pantalla copia el valor a **Esperado** para que la diferencia sea 0. Ya guardada la fila, lo esperado queda con ese valor y cambiar lo
recibido no lo mueve: se ve la diferencia. Si quiere que vuelva a coincidir, cambie también lo esperado.

**¿Por qué mi recibo nuevo aparece primero aunque tengo filtros que lo excluirían?** Porque al crearlo la pantalla lo deja primero y
elegido para que capture las líneas. Al cambiar cualquier filtro o el buscador, la lista vuelve a mostrar solo lo que cumple.

**¿Cómo encuentro los recibos con faltante?** En Recibo, filtro **Diferencia → Faltante** (o **Sobrante**, **Sin diferencia**). Para los
que todavía se pueden corregir, agregue el filtro de Estatus **Discrepancia**.

**¿Dónde quedó la cola de acomodo?** En **Recibo › Acomodo pendiente**: una lista de los recibos Completados o Completados con
diferencia que aún tienen tareas, y a la derecha las tareas del recibo elegido (asignar, iniciar, completar, cancelar, como íconos). La
dirección antigua `/warehouse/tasks` lleva ahí. Las tareas de reabasto están en Recolección y empaque › Reabasto.

**¿Por qué el botón "Confirmar recibo" está apagado?** Debajo dice el motivo: ya está confirmado, no tiene líneas, hay líneas guardándose o
sin guardar, o alguna fila tiene un error. Vea los mensajes de arriba.

**¿Cómo vuelvo la barra de Recolección y empaque a 60/40?** Enfóquela (Tab) y pulse **Enter**, o haga **doble clic** en la barra. Con el
teclado, ← y → la mueven de 5 % en 5 %, e Inicio y Fin la llevan a los extremos. La posición se recuerda en su navegador.

**No veo la barra: los paneles están uno debajo del otro.** Es a propósito: con la ventana de 900 px o menos (celular), o cuando no cabe
el ancho mínimo de los dos paneles, se apilan (primero Recolección y abajo la lista) y no hay barra. Al ensanchar la ventana vuelve.

**No veo el panel "Recolección", solo la lista.** Su usuario no tiene el permiso `warehouse.pick`; sin él la lista ocupa todo el ancho y
no puede recolectar, empacar ni eliminar.

**Abrí el detalle de una recolección y tenía líneas capturadas: ¿las perdí?** No. El detalle se abre en un modal encima de la pantalla y
el panel Recolección conserva lo capturado.

**¿Dónde están Empacar y Eliminar?** Como íconos al final de la fila de cada recolección (caja y papelera), y también como botones en el
detalle. Empacar exige `warehouse.pick` y `orders.create`, y solo aparece en recolecciones que se pueden empacar; Eliminar exige
`warehouse.pick` (y `orders.cancel` si ya está empacada).

**¿Por qué "Unidades totales" solo cuenta productos activos?** Desde el ajuste del 2026-09-30 la cifra suma la existencia en mano de los
productos **activos** y, al hacer clic, la tabla muestra los activos con existencia en mano mayor que cero; así la cifra y la tabla
coinciden. (Por el API: `GET /api/v1/inventory/balances?activeProductsOnly=true`.)

## Lote 14 — Transferencias y ajustes, Conteo cíclico, Kárdex, conciliación y "Necesita tu atención" (Lote 4 del plan de cambios)

Capítulos: [06 — Inventario y almacén, secciones 3 y 6](06-inventario-y-almacen.md), [07 — Pulso del día, sección 4](07-pulso-y-actividad.md) y
[F6 — Almacén e inventario (pantallas)](frontend/f6-almacen-e-inventario.md). Los mensajes del API traen el código HTTP indicado; los que se
ven solo en pantalla van agrupados más abajo ("Mensajes que solo ve en la pantalla…"). Este lote cubre el Kárdex (filtros compartidos,
resumen y detalle), Transferencias y ajustes (Subir/Bajar), la conciliación automática con descuadres, el Conteo cíclico en dos paneles con
"lo cambiado" y los estatus Pendiente / Contado / Concordancia / Diferencia, y "Necesita tu atención" en el Pulso.

### Mensajes de error nuevos o cambiados

**¿Qué significa "Movimiento no encontrado."? (404)**
El detalle de un movimiento (`GET /api/v1/inventory/transactions/{id}` o el enlace `?txn=` del Kárdex) pidió un número que no existe o que
es de otra compañía. Vuelva al Kárdex y abra el movimiento desde la lista.

**¿Qué significa "Cliente no encontrado."? (404)**
El filtro de dueño del Kárdex (`ownerClientPublicIds`) trae un cliente que no existe o es de otra compañía. Elija el dueño desde la lista
del filtro (`GET /api/v1/inventory/owners`); "Propio" no lleva cliente (`includeOwn=true`).

**¿Qué significa "La dirección debe ser IN (entradas) u OUT (salidas)."? (400)**
El filtro `direction` del Kárdex (o de su resumen) trae otro valor. Use `IN` para entradas u `OUT` para salidas (sin distinguir
mayúsculas), o quite el filtro. El error viene en `errors.direction`.

**¿Qué significa "Motivo de ajuste desconocido: 'X'." en el Kárdex? (400)**
El filtro `reasons` trae un código que no está en el catálogo de motivos de ajuste. Use los códigos del catálogo (`DAMAGE`, `LOSS`, `FOUND`,
`EXPIRED`, `PO_SHORTAGE`, `OTHER`, o los que agregue su compañía). Es el mismo mensaje que al capturar un ajuste con un motivo inexistente.

**¿Qué significa "La conciliación manual admite como máximo 200 productos a la vez."? (400)**
`POST /api/v1/inventory/reconciliation/run` recibió más de 200 productos en `productPublicIds`. Mande menos, o no mande ninguno para revisar
**todos** los productos de la compañía. El error viene en `errors.productPublicIds`.

**¿Qué significa "Descuadre no encontrado."? (404)**
El descuadre no existe o es de otra compañía (no se distingue). Vuelva a la lista de Kárdex › Conciliación: si otro usuario lo cerró, ya no
está entre los Pendientes; cambie el filtro de estatus para verlo.

**¿Qué significa "Tipo de descuadre desconocido: 'X'."? (400)**
El filtro `kinds` de `GET /api/v1/inventory/discrepancies` trae un tipo que no existe. Los tipos son `BALANCE` (saldo por posición) y
`PRODUCT_TOTAL` (total del producto).

**¿Qué significa "Indique la acción: REBUILD_BALANCE (corregir el saldo) o DISMISS (descartar)."? (400)**
`POST …/discrepancies/{publicId}/resolve` llegó sin `action` o con otra distinta. Mande `REBUILD_BALANCE` para corregir el saldo según el
Kárdex o `DISMISS` para descartar. En la pantalla, los botones "Corregir el saldo según el Kárdex" y "Descartar" ya mandan la acción.

**¿Qué significa "Escriba una nota que explique por qué se descarta el descuadre."? (400, también en pantalla)**
Descartar exige una nota: quien lo lea después debe saber por qué se dejó así. Escriba el motivo (hasta 500 caracteres) y vuelva a pulsar
**Descartar**. Corregir el saldo no la exige. El error viene en `errors.notes`.

**¿Qué significa "La nota admite como máximo 500 caracteres."? (400, también en pantalla)**
La nota de resolución de un descuadre pasa de 500 caracteres. Acórtela.

**¿Qué significa "El descuadre ya está cerrado; solo se consulta."? (422)**
Ya se resolvió, se descartó o se cerró solo (otra persona o una revisión automática lo cerró antes). Un descuadre cerrado no se vuelve a
resolver. Si el problema reaparece, la revisión abre uno nuevo; si quiere revisar el saldo ahora, pulse **Ejecutar conciliación**.

**¿Qué significa "Este descuadre es del total del producto; no se corrige por posición. Corrija los descuadres por posición o descártelo con una nota."? (422)**
El descuadre no es de una posición sino de la **suma de todo el producto**: no hay una posición cuyo saldo corregir. Corrija primero los
descuadres por posición de ese producto (si los hay; un descuadre del total solo se abre cuando ninguna posición del producto está
descuadrada) o descarte este con una nota que explique la causa.

**¿Qué significa "El Kárdex da {ledger} para {sku} en {bin}, menos que lo reservado ({reserved}); libere la reserva antes de corregir el saldo."? (409)**
Para corregir, el saldo tomaría lo que suman los movimientos, pero ese número es menor que lo que ya está **reservado** en la posición
(por ejemplo, para una recolección o un cruce de muelle): el saldo no puede quedar por debajo de lo reservado. Libere esa reserva
(cancele o complete lo que la usa) y vuelva a intentar. No se cambió nada.

**¿Qué significa "El Kárdex da un saldo negativo ({ledger}) para {sku} en {bin}; revise los movimientos antes de corregir el saldo."? (409)**
La suma de los movimientos de esa clave es negativa: hay más salidas que entradas, lo que no puede ser un saldo. Corregir el saldo lo
copiaría del Kárdex y sería un error. Revise los movimientos de esa posición (el detalle del descuadre muestra los últimos 20); si el
Kárdex está mal, hace falta una revisión de soporte. También puede descartar el descuadre con una nota.

**¿Qué significa "La conciliación chocó con otra revisión simultánea; intente de nuevo."? (409)**
Dos revisiones (por ejemplo, la automática y su botón **Ejecutar conciliación**) intentaron abrir el mismo descuadre al mismo tiempo; solo
puede haber uno abierto por clave. No pasó nada malo: espere unos segundos y vuelva a ejecutar.

**¿Qué significa "rowVersion inválido: se espera el valor base64 devuelto por la ficha."? (400)**
El `rowVersion` que mandó al resolver un descuadre o al confirmar un conteo no es el texto que devuelve la ficha. Mande el valor tal cual
lo recibió, o no lo mande (sin él no hay control de cambios simultáneos).

**¿Qué significa "El registro fue modificado por otro usuario; recargue e intente de nuevo." al resolver un descuadre o confirmar un conteo? (409)**
El `rowVersion` es de una lectura vieja: alguien más (o la revisión automática, que actualiza las cifras del descuadre) cambió el registro
después. Recargue la ficha y repita la acción con el `rowVersion` nuevo.

**¿Qué significa "El rango de "lo cambiado" admite como máximo 31 días."? (400)**
En "Conteo de lo cambiado", entre "desde" y "hasta" hay más de 31 días. Acorte el rango. Si no cambió las fechas y este mensaje sale, la
última generación de este almacén fue hace más de 31 días: la ventana por defecto se acota sola a 31 días atrás, así que el mensaje solo
sale con fechas escritas a mano. El error viene en `errors.fromUtc`.

**¿Qué significa "La fecha 'desde' no puede ser posterior a la fecha 'hasta'." en "lo cambiado"? (400)**
La ventana de "Conteo de lo cambiado" tiene el "desde" después del "hasta". Corrija el orden. El mismo mensaje sale en el Kárdex y en la
lista de descuadres.

**¿Qué significa "No hubo movimientos en {almacén} entre {desde} y {hasta}; no hay posiciones que contar."? (400)**
En esa ventana (fechas y horas de Puerto Rico) el almacén no tuvo movimientos en posiciones activas, sin contar los que salen de un
conteo. Amplíe la ventana (hasta 31 días), quite el filtro de zonas o espere a que haya movimiento. En la pantalla, la vista previa lo dice
antes de crear y el botón queda apagado.

**¿Qué significa "Las {n} posiciones con cambios ya tienen un conteo pendiente."? (400)**
Todas las posiciones con movimientos en la ventana ya tienen un conteo **Pendiente o Contado**. No se crean conteos repetidos: confirme
o elimine los que hay, o elija otra ventana.

**¿Qué significa "Hay {n} posiciones con cambios; se generan como máximo 200 a la vez. Acote el rango de fechas o las zonas."? (400)**
"Lo cambiado" genera hasta 200 conteos por vez y esa ventana tiene más posiciones. **No se creó ninguno.** Acorte el rango de fechas o
elija zonas, y repita: como la ventana por defecto arranca desde la última generación, puede ir generando por partes.

**¿Qué significa "Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano."? (400)**
Además del alta normal, ahora sale en "lo cambiado" cuando las posiciones de la ventana no tienen nada que contar (por ejemplo, con
"Incluir posiciones vacías" apagado y todas quedaron en cero). Encienda **Incluir posiciones vacías** o amplíe la ventana.

**¿Qué significa "Crear un conteo vacío (allowEmpty) solo aplica a un único producto, sin posiciones, zonas ni categorías."? (400)**
Se pidió `allowEmpty: true` en `POST /api/v1/cycle-counts` junto con posiciones, zonas, categorías, más de un producto o ningún producto.
`allowEmpty` es solo para abrir el conteo **vacío de un único producto** (lo usa la app para "Otra posición"). Quite `allowEmpty` o deje
exactamente un producto y ningún otro filtro.

**¿Qué significa "El conteo ya fue reconciliado; solo se consulta." ahora que hay Concordancia y Diferencia? (422)**
Vale para los **dos** estatus finales. Un conteo Concordancia o Diferencia ya asentó sus ajustes en el Kárdex y no se edita, no se captura,
no se vuelve a confirmar y no se elimina. Si contó mal, haga un ajuste manual o un conteo nuevo de la posición.

**¿Qué significa "El conteo ya se terminó; puede corregir la captura o reconciliarlo."? (422)**
Se pidió **terminar** (`POST …/finish`) un conteo que ya está **Contado** (lo hace la app de almacén a ciegas). En la web no hay ese paso:
use **Confirmar conteo y ajustar**.

**¿Qué significa "El conteo no tiene líneas."? (422)**
Se intentó terminar o confirmar un conteo sin líneas. Agregue lo encontrado o elimine el conteo si sobra.

**¿Qué significa "Solo se elimina un conteo abierto; este ya se terminó de contar."? (422)**
Solo un conteo **Pendiente** se elimina. Uno **Contado** ya se terminó de contar; confírmelo. Uno **Concordancia** o **Diferencia** responde
"El conteo ya fue reconciliado; solo se consulta.".

**¿Qué significa "Falta el permiso 'pulse.attention'."? (403)**
`GET /api/v1/analytics/attention` la pide. Pídale a su administrador el permiso "Ver la sección Necesita tu atención del Pulso".

**¿Qué significa "Indique el almacén: la compañía tiene más de uno." / "El almacén está inactivo." en el conteo? (400 / 422)**
Vale para el alta de un conteo y para "lo cambiado": con más de un almacén activo hay que elegir cuál; uno dado de baja no admite conteos.

### Mensajes que solo ve en la pantalla (sin código HTTP)

**¿Qué significa "Elija si el ajuste sube o baja el inventario."?**
En el modal de ajuste falta **Tipo de ajuste**: elija **Subir** (entra inventario) o **Bajar** (sale). Es obligatorio; el signo lo pone la pantalla.

**¿Qué significa "La cantidad debe ser mayor que cero." en el ajuste?**
La cantidad del ajuste se escribe **siempre en positivo**; la dirección (Subir o Bajar) dice si suma o resta. Escriba un número mayor que cero.

**¿Qué significa "No puede bajar más de lo disponible en la posición ({qty})." / "No puede transferir más de lo disponible en la posición ({qty})."?**
La cantidad pasa de lo que hay disponible (en mano menos reservado) en esa posición, que la pantalla muestra debajo como "Disponible en la
posición: N". Escriba una cantidad menor o igual. Si el producto es por lote, el tope es el del lote elegido.

**¿Qué significa "Seleccione el lote."?**
Al **bajar** un producto por lote hay que elegir cuál lote de los que hay en la posición sale. (Al **subir**, se escribe el número de lote:
"Indique el número de lote.").

**¿Qué significa "Elija el ítem a transferir." / "Elija al menos una serie."?**
En "Transferir", después de la posición de origen se elige el **ítem** (producto y lote de lo que hay ahí). Si el producto se controla por
serie, hay que marcar al menos una serie; la cantidad es el número de series marcadas.

**¿Qué significa "El saldo ya cuadraba; el descuadre se cerró solo."?**
Aviso, no error: usted pidió corregir, pero cuando el sistema lo revisó el saldo ya coincidía con el Kárdex (una revisión o un movimiento
lo arregló antes). El descuadre quedó en **Se corrigió solo** y no se tocó nada.

**¿Qué significa "Ese código no está en este conteo. Use "Agregar lo encontrado" si el producto está en la posición."?**
El escáner del conteo busca por **SKU, código de barras, lote o serie exactos** entre las líneas de **este** conteo. Si el producto está en
la posición pero no en el conteo, pulse **Agregar lo encontrado**.

**¿Qué significa "{n} líneas coinciden: elija la posición y el lote."?**
El código coincide con varias líneas del conteo (el mismo producto en varias posiciones o lotes). Elija cuál en la lista que se abre.

**¿Qué significa "Hay cantidades que no se pudieron guardar; corríjalas antes de confirmar."?**
Al pulsar **Confirmar conteo y ajustar** la pantalla guarda primero lo tecleado, y alguna fila falló (aparece marcada). Corrija esa
cantidad y confirme otra vez.

**¿Por qué el botón "Confirmar conteo y ajustar" está apagado?**
Debajo dice el motivo: `Faltan {n} línea(s) por contar.`, `El conteo no tiene líneas.`, `El conteo ya fue confirmado; solo se consulta.`
o `La web no confirma conteos a ciegas.` (su usuario no tiene `warehouse.count`, o el conteo es de la app a ciegas).

**¿Qué significa "Elija el almacén." en "Conteo de lo cambiado"?**
Con más de un almacén activo hay que elegir cuál antes de crear los conteos.

**¿Qué significa "Revisión automática: al día" / "Revisión automática: {count} en cola" / "Revisión automática apagada"?**
Está en Kárdex › Conciliación. **Al día**: no hay productos esperando revisión. **N en cola**: N productos se revisarán en unos segundos.
**Apagada**: el servidor la tiene desactivada (`Inventory:Reconciliation:Enabled`); solo funciona el botón **Ejecutar conciliación**.

**¿Qué significa "No aplican al Kárdex: …" / "No aplican a Saldos (solo cambian el resumen de movimientos): …" / "No aplican a Conciliación: …"?**
Los filtros del Kárdex son los mismos en las tres pestañas y se conservan al cambiar de pestaña, pero cada una aplica solo algunos. Los que no
aplica se ven atenuados y esta nota los nombra: siguen puestos, y volverán a valer al regresar a la pestaña donde sí aplican. En Saldos, los de
movimientos (fechas, tipo, dueño, motivo, dirección, "Solo manuales") solo cambian el **resumen**.

**¿Qué significa "Su usuario no puede consultar posiciones"?**
El filtro **Posición** busca en `GET /api/v1/warehouses/bins/search`, que exige `inventory.view`. Sin ese permiso el filtro se ve pero no lista.

### Preguntas frecuentes

**¿Qué es un descuadre y por qué no lo arregla un ajuste?**
Un descuadre es que el **saldo** de una posición no coincide con lo que suman los **movimientos** del Kárdex. **No es una diferencia física en
el estante** (eso lo resuelve un conteo). Cada movimiento escribe el saldo y el Kárdex juntos, así que casi nunca ocurre: viene de un arreglo
hecho directo en la base de datos, de una restauración, de la migración o de un error. Un ajuste o un conteo mueven el saldo **y** el
Kárdex en la misma cantidad, así que la diferencia sigue igual. Se corrige con **Corregir el saldo según el Kárdex** (el saldo toma lo que
dan los movimientos, sin crear un movimiento nuevo) o se descarta con una nota.

**¿Cuándo aparece un descuadre y cuánto tarda?**
Unos segundos después de cada movimiento del producto (la revisión corre sola en segundo plano), o al pulsar **Ejecutar conciliación**
(revisa los productos del filtro o todos), o al final de una migración. Si el servidor se reinicia mientras hay revisiones en cola, se pierden;
el siguiente movimiento de ese producto o **Ejecutar conciliación** las cubren.

**¿Un descuadre que descarté puede volver?**
Solo si las cifras cambian: mientras el Kárdex y el saldo tengan los mismos números que cuando lo descartó, no se reabre. Si cambian, se abre
uno nuevo. Un descuadre resuelto o "Se corrigió solo" que vuelve a ocurrir también abre uno nuevo.

**¿Qué significan los cuatro estatus de un descuadre?**
**Pendiente**: hay que revisarlo. **Resuelto**: alguien corrigió el saldo según el Kárdex. **Descartado**: alguien decidió dejarlo, con una
nota. **Se corrigió solo**: una revisión lo encontró cuadrado (o al corregir ya cuadraba). Los tres últimos son finales.

**¿Qué hago si el descuadre es del "total del producto"?**
Corrija primero los descuadres por posición de ese producto; si no hay ninguno y el total sigue mal, descarte con una nota que explique la
causa y avise a soporte. "Corregir" no está disponible para este tipo.

**Corregí un descuadre: ¿cómo verifico lo que hay en el estante?**
Después de corregir, la ficha ofrece **Crear conteo de esa posición** (necesita `warehouse.count.capture`): crea un conteo Pendiente de esa
posición. El saldo ya coincide con el Kárdex, pero eso no prueba que coincida con lo físico; el conteo sí.

**¿Qué cuenta "Conteo de lo cambiado"?**
Un conteo **por cada posición** que tuvo movimientos en una ventana: por defecto desde la última vez que se generó en ese almacén (la primera
vez, desde las 00:00 de hoy en hora de Puerto Rico) hasta ahora; puede cambiar las fechas (hasta 31 días). Cada conteo trae **todo lo que hay
en la posición** y, con "Incluir posiciones vacías", también lo que quedó en cero. **No cuentan** los movimientos que salen de otro conteo, ni
las posiciones inactivas ni las que ya tienen un conteo Pendiente o Contado (se saltan; la vista previa dice cuántas). Hasta 200 posiciones
por vez y todo o nada.

**Generé "lo cambiado" dos veces seguidas: ¿se duplican los conteos?**
No: la segunda vez la ventana arranca donde terminó la primera y, además, una posición con un conteo Pendiente o Contado se salta.

**¿Por qué mi conteo dice "Diferencia"? ¿Y por qué "Concordancia"?**
**Diferencia** significa que al confirmar el sistema **asentó al menos un movimiento** (un ajuste `COUNT_VARIANCE` en el Kárdex o, en productos
con serie, una baja, un alta o una transferencia): lo contado no coincidía con el saldo actual. **Concordancia**: no hubo nada que ajustar. Ojo:
se compara con el saldo **actual**, no con la foto: si el saldo cambió después de tomar la foto y usted contó lo que había, queda en
Concordancia aunque la línea diga que el saldo cambió. Los conteos ya cerrados con ajuste antes del Lote 14 pasaron a Diferencia.

**¿Por qué "Abierto" y "Reconciliado" ahora dicen "Pendiente" y "Concordancia"?**
Se renombraron por decisión del dueño. Los códigos no cambiaron (`OPEN` y `RECONCILED`) y se agregó `RECONCILED_VARIANCE` ("Diferencia"). Un
filtro del API con `status=RECONCILED` ya no incluye los conteos con Diferencia: pida los dos.

**¿Qué pasó con el paso "Terminar conteo"?**
En la web ya no existe: **Confirmar conteo y ajustar** lleva el conteo de Pendiente a Concordancia o Diferencia en un solo paso. El estatus
**Contado** queda solo para el conteo a ciegas de la app de almacén (que termina de contar y deja la confirmación a la web).

**¿Dónde quedó la pestaña "Tareas de conteo" y cómo asigno un conteo?**
Se quitó. Cada conteo de la lista de la izquierda tiene un ícono **Asignar** (persona con "+", necesita `warehouse.manage`) que muestra a
quién está asignado, y un ícono **Eliminar** (papelera; solo conteos Pendientes, necesita `warehouse.count`). Para elegir a quién, su usuario
también necesita `admin.users`; sin él sale "Su usuario no puede consultar el listado de usuarios.".

**¿Dónde quedó "Ajustes de inventario"?**
Salió del menú (decisión del dueño). Esa pantalla no mostraba ajustes: mostraba las **compras recibidas de forma incompleta**. Sus faltantes se
resuelven ahora **desde la ficha de cada orden de compra, pestaña Faltantes** (Compras), y la dirección `/warehouse/inventory-adjustments`
lleva a Compras. Los ajustes de inventario están en **Transferencias y ajustes** (menú Almacén, después de Recolección y empaque) y en el Kárdex.

**¿Por qué no puedo elegir "Encontrado" al bajar (o "Daño" al subir)?**
Los motivos dependen de la dirección: **Encontrado** solo al subir; **Daño**, **Pérdida** y **Vencido** solo al bajar; los demás valen en las
dos. Es una regla de la pantalla; cambie la dirección para ver el motivo.

**¿Por qué las fechas del Kárdex cambiaron?**
Los filtros "Desde" y "Hasta" son ahora **días de Puerto Rico** (medianoche local), no días UTC. Una integración que mandaba `from` y `to`
pensando en UTC verá movimientos de la noche del día anterior o del siguiente en los bordes. Lo mismo vale para la lista de descuadres y la
de conteos.

**¿Qué muestra el resumen de arriba del Kárdex y de Saldos?**
Con **los mismos filtros** de la tabla: Movimientos, Entradas (cuántos movimientos y cuántas unidades), Salidas e Internos (transferencias
que no salen de los almacenes o posiciones filtrados). En **Saldos** se agregan **En mano** y **Disponible**. Saldos no filtra por Dueño (el
API de saldos no lo tiene); el filtro de dueño solo cambia el resumen de movimientos.

**¿Qué muestra el detalle de un movimiento?**
Haga clic en una fila del Kárdex, de Ajustes o de Transferencias: fecha y hora, usuario, tipo, producto, dueño, categoría, cantidad, de → a,
lote, serie, motivo, origen y nota; el **documento de origen** con su estatus y un botón **Abrir** (solo si usted tiene el permiso y el módulo
de esa pantalla; una tarea de almacén abre el documento de su padre); y los **movimientos relacionados** (los del mismo documento o, si no
tiene, los del mismo asiento; hasta 200).

**¿Qué significa "Solo manuales"?**
Movimientos sin documento de referencia: los que alguien hizo a mano (ajustes y transferencias desde las pantallas). Sin el filtro, las
pestañas listan **todos** los movimientos, incluidos los que genera el sistema (conteo, recibo, acomodo, reabasto).

**¿Por qué no veo "Necesita tu atención"?**
Porque su usuario no tiene el permiso `pulse.attention`, o porque ese panel se ocultó en su Pulso o en el de la compañía ("Organizar mi Pulso").
Si el panel se ve y dice "Todo en orden", no hay descuadres pendientes **que usted pueda ver** (necesita `inventory.view` y el módulo de
almacén encendido). El panel muestra los 5 descuadres más antiguos; **Ver todos (N)** abre la lista completa en Kárdex › Conciliación.

**¿Por qué "Conteos abiertos" del Pulso cambió de cifra?**
Ahora cuenta el total real de conteos **Pendientes y Contados** (antes contaba una lista cortada en 200 y solo los "Abiertos"). Los
Concordancia y Diferencia ya no cuentan.

**Escaneé un código y el conteo no lo encuentra: ¿qué hago?**
El escáner busca el código exacto (SKU, código de barras, lote o serie) entre las líneas del conteo. Si el producto no está en la lista de esa
posición, pulse **Agregar lo encontrado**. Un lector que teclea el código y pulsa Enter se comporta igual que escribirlo a mano.

**¿Puedo confirmar un conteo sin contar todas las líneas?**
No: `Faltan {n} línea(s) por contar.` Una línea con 0 contado es una captura válida (cuente cero); una línea vacía no.

**¿Se puede contar a ciegas desde la web?**
No. El conteo a ciegas es de la app de almacén; la web muestra lo esperado y el botón de confirmar avisa "La web no confirma conteos a ciegas.".

## Lote 15 — Pulso del día: franja "Almacén hoy", gráficos de la compañía, filas fijas y días de Puerto Rico (Lote 5 del plan de cambios)

Capítulos: [07 — Pulso del día, sección 5](07-pulso-y-actividad.md) y [F7A — Pulso: Almacén hoy (pantallas)](frontend/f7a-pulso-almacen-y-actividad.md). Los
mensajes del API traen el código HTTP indicado; los que se ven solo en pantalla van agrupados más abajo. Este lote cubre la franja "Almacén
hoy", las filas fijas del Pulso, los indicadores en una fila por módulo, los gráficos siempre dibujados, los 2 gráficos de almacén de la
compañía, "Otras" y los días de Puerto Rico en todo el análisis.

### Mensajes de error nuevos o cambiados

**¿Qué significa "Los días deben estar entre 1 y 14."? (400)**
`GET /api/v1/inventory/pulse/days` recibió un `days` que no está entre 1 y 14 (por ejemplo `days=0` o `days=15`). Mande un número de 1 a 14 o
no lo mande (por defecto son 7). La pantalla del Pulso siempre pide 7, así que este mensaje solo lo ve quien llama al API a mano o una
integración. El error viene en `errors.days`.

**¿Qué significa "Los elementos por default de la plataforma no se editan ni se eliminan." con un gráfico de almacén? (403)**
Los dos gráficos "Valor de inventario por categoría" y "Movimientos de inventario por tipo" **ya no son de sistema** desde el Lote 15. Si
aún ve este mensaje con uno de ellos, la base de datos de su compañía no se actualizó (el `db-init` no corrió): pida a soporte que la
actualice. Con cualquier otro gráfico de sistema el mensaje sigue siendo correcto: no se editan ni se borran; para cambiarlo, cree uno propio.

**¿Qué significa "Solo el dueño puede editar o eliminar este elemento." con un gráfico? (403)**
El gráfico es de una persona (privado o compartido) y solo su dueño lo edita o lo borra. Los gráficos **de la compañía** (sin dueño, como los
dos de almacén) no dan este mensaje: los edita quien tenga `analytics.manage`. Si no tiene ese permiso, el servidor responde 403 sin
mensaje: pídalo a su administrador ("Crear vistas, indicadores y gráficos").

**¿Qué significa "Tipo de gráfico: BAR, DONUT o LINE."? (400)**
Se intentó guardar un gráfico con otro tipo. Solo hay barras, dona y línea; el pastel (`PIE`) no está habilitado (el dueño eligió la dona para
el valor de inventario). Elija uno de los tres. El error viene en `errors.chartType`.

### Mensajes que solo ve en la pantalla (sin código HTTP)

**¿Qué significa "¿Eliminar el gráfico {nombre}? Es de la compañía: una vez eliminado no se vuelve a crear."?**
Es la confirmación al eliminar uno de los gráficos de la compañía (los dos de almacén). Confirme solo si está seguro: no se vuelve a sembrar
en actualizaciones futuras (ver "Borré el gráfico de la compañía, ¿vuelve?"). Con un gráfico que no es de la compañía la confirmación es
solo "¿Eliminar el gráfico {nombre}?".

**¿Qué significa "Este gráfico no tiene datos en el rango configurado." (Pulso) o "Este gráfico no tiene datos con los filtros y el rango actuales." (Análisis → Gráficos)?**
El gráfico no tiene ningún punto: la fuente no trae filas con ese rango de fecha y esos filtros. Cambie el rango con **Rango** (o el rango por
defecto, si tiene permiso) o revise el filtro. Ya no se muestra una lista cuando hay pocos puntos: con uno, dos o tres puntos se dibuja el
gráfico; solo sin puntos sale este aviso.

**¿Qué significa "…" o "—" en un número de la franja "Almacén hoy"?**
"…" es que todavía está cargando. "—" es que la consulta falló (por ejemplo, un 403 al cambiar de compañía a una sin permiso); el resto del
Pulso sigue funcionando. Recargue la página o vuelva a entrar a la compañía.

**¿Qué significa el texto oculto "Hoy hubo conteos con diferencia." / "Hay productos bajo su mínimo."?**
Es lo que lee un lector de pantalla en la tarjeta que se ve en **naranja** (borde y número): "Conteos con diferencia" si hoy se cerró algún
conteo en Diferencia, "Productos bajo mínimo" si hay algún producto bajo su mínimo.

### Preguntas frecuentes

**¿Por qué el Kárdex de "Salida" (o de "Recibidas") suma distinto que la tarjeta?**
La tarjeta es un **neto** y el Kárdex filtra por **tipo de movimiento**:
- **Unidades de salida:** la tarjeta suma recolección y cruce de muelle y **resta las recolecciones que se eliminaron** ese día. En el Kárdex una
  recolección eliminada es un ajuste de entrada (motivo "Reversa de recolección") y el enlace filtra solo los tipos Despacho y Cruce de
  muelle, así que el Kárdex puede sumar **más** que la tarjeta.
- **Unidades recibidas:** la tarjeta suma las recepciones **más los ajustes por diferencia de recibo** (lo que de verdad llegó: si se esperaban
  10 y llegaron 8, cuenta 8). El enlace filtra solo el tipo Recepción; las diferencias salen en el Kárdex como ajustes aparte, así que puede
  sumar más o menos que la tarjeta. Para ver todo, quite el filtro de tipo y agregue el de Ajuste.

**¿Por qué la franja dice 12 y el indicador "Unidades recibidas" dice otro número?**
El **número grande** de la franja es lo de **hoy**. Lo que se compara con el indicador de "últimos 7 días" es el texto pequeño "7 días: N": los
dos cuentan los mismos días de Puerto Rico y las mismas unidades, y coinciden. Los indicadores "Conteos con diferencia" (30 días) y
"Productos bajo mínimo" del panel de indicadores tienen su propio período; no se ocultaron (decisión del dueño).

**¿Por qué cambió mi gráfico con muchas barras y ahora dice "Otras"?**
Desde el Lote 15, en barras, dona y pastel que **suman o cuentan**, si hay **más de 8 grupos** se muestran los 7 mayores y un último punto
**"Otras"** con la suma del resto, para que el total sea exacto y la dona no engañe sobre las proporciones. También cambió en barras
que ya existían con más de 8 grupos (por ejemplo "Cambios por usuario": antes mostraba solo los 8 mayores). Con promedio, mínimo o máximo no
se junta nada. Si necesita ver un grupo que quedó dentro de "Otras", abra el gráfico en Análisis → Gráficos y filtre.

**¿Por qué un indicador o gráfico de "últimos 7 días" dio distinto que antes?**
Porque los rangos ahora son **días de Puerto Rico**, no días UTC. "Últimos 7 días" es hoy y los 6 anteriores, desde las 00:00 de Puerto Rico
(04:00 UTC). Un movimiento hecho entre las 20:00 y las 24:00 de Puerto Rico ahora cuenta en su día (antes contaba en el día siguiente, en UTC),
y "hoy" cambia a la medianoche de Puerto Rico, no a las 20:00. Vale también para "Últimos 30 días", "Mes actual", el rango personalizado, los
gráficos agrupados por fecha, las exportaciones y la ventana "Hoy" de Actividad reciente. Los totales de un rango largo casi no cambian; los
de un solo día pueden cambiar. Siguen en UTC la validación de mínimo y máximo de fechas de los campos personalizados y el "hoy" de la ficha de
cliente y de los documentos y tarifas del chofer.

**¿Por qué la franja "Almacén hoy" ya no se queda fija al desplazarse?**
Desde el 2026-10-05 (decisión del dueño) solo queda fijo el encabezado (fecha con los botones de Organizar, saludo y chip). "Almacén hoy" se desplaza con el resto
del Pulso, esté donde esté. No hay nada que configurar.

**Borré el gráfico de la compañía, ¿vuelve? ¿Cómo lo recupero?**
**No vuelve**: es la regla del dueño. Ni renombrarlo ni borrarlo lo recrea una actualización posterior. Para tenerlo otra vez, cree uno a mano en
Análisis → Gráficos → Nuevo gráfico: "Valor de inventario por categoría" = fuente Inventario (saldos), agrupar por Categoría, suma de
"Valor a costo", tipo Dona, en dinero; "Movimientos de inventario por tipo" = fuente Movimientos de inventario, agrupar por Tipo, suma de
"Unidades movidas", tipo Barras, últimos 7 días. Use un nombre distinto del borrado (el nombre de uno borrado sigue reservado en la base; no
se probó qué responde el API si se repite). Un gráfico **renombrado** o **oculto** sigue existiendo: búsquelo por su nuevo nombre o muéstrelo con
Organizar.

**¿Quién puede editar o borrar los dos gráficos de almacén?**
Quien tenga el permiso `analytics.manage` ("Crear vistas, indicadores y gráficos"; hoy el administrador de la compañía). Aparecen en Análisis →
Gráficos con el chip "De la compañía" y los botones Editar y Eliminar. Quien tenga `analytics.dates` cambia su rango por defecto.

**¿Por qué mis dos gráficos de almacén no están en la primera fila?**
Se mueven a la primera fila (valor a la izquierda, movimientos a la derecha) solo si conservan el orden con que venían de fábrica **y** la
compañía nunca organizó sus gráficos. Si algún gráfico activo de la compañía tiene orden 0, se toma como que ya se organizó y se respeta el
orden que hay. Ajústelo con Organizar.

**¿Por qué el gráfico de movimientos por tipo ya no muestra el despacho en negativo?**
Ahora mide **unidades movidas** (siempre en positivo): el despacho es una barra más. Para ver el signo, cree un gráfico con el campo
"Cantidad" (con signo).

**¿Por qué las tarjetas "Conteos con diferencia" o "Productos bajo mínimo" están en naranja?**
Es la señal de atención: "Conteos con diferencia" si **hoy** se cerró algún conteo en Diferencia (y la barrita de hoy también va naranja);
"Productos bajo mínimo" si hay algún producto bajo su mínimo en este momento. Sin nada que atender van en violeta.

**Toco "Conteos con diferencia" y la lista trae más conteos que el número. ¿Por qué?**
El número cuenta los conteos cerrados **en Diferencia hoy** (y el texto pequeño, en los 7 días); el enlace abre el Conteo cíclico con el
estatus Diferencia, **sin fechas** (el filtro de fechas de esa lista es el de alta del conteo, no el de cierre, y dejaría fuera un conteo
creado antes y cerrado en la ventana). Verá también los conteos en Diferencia más antiguos.

**¿Por qué la salida de un día sale negativa?**
La reversa de eliminar una recolección **resta** el día en que se elimina. Si ese día no hubo salidas y se eliminó una recolección de otro día,
el neto es negativo. No es un error de datos.

**¿Por qué no veo la franja "Almacén hoy"?**
Porque su usuario no tiene `pulse.warehouse` con `inventory.view`, o el módulo de almacén (`WMS_LOTSERIAL`) está apagado en la compañía, o
alguien ocultó el panel en su Pulso o en el de la compañía ("Organizar"). Es el mismo permiso del panel "Almacén": si ve uno, debería ver el otro.

**Cambié el almacén de la franja y cambió también el del panel "Almacén". ¿Es un error?**
No: es el **mismo** almacén en los dos, y se recuerda por usuario en ese navegador. Cambiarlo en uno lo cambia en el otro; los enlaces de las
tarjetas llevan el almacén elegido.

**¿Cuándo cambia la franja de día?**
A la medianoche de Puerto Rico (04:00 UTC). La franja se vuelve a pedir cada 5 minutos, así que con la pantalla abierta el cambio de día
puede tardar hasta ese tiempo en verse; al recargar es inmediato.

**¿Por qué en mi Pulso no aparece la fila "Contabilidad"?**
"Tus indicadores" muestra una fila por módulo (Operación, Almacén, Contabilidad) y solo las que tienen algún indicador visible. Hoy solo "COD
por cobrar" es de Contabilidad; si ese indicador está oculto o su módulo apagado, la fila no sale.

**¿Por qué no puedo mover un indicador a otra fila?**
En **Organizar**, los indicadores se ordenan solo **dentro de su fila** de módulo (los botones ▲ y ▼ se apagan en los bordes de la fila). El
módulo de un indicador se cambia editándolo en Análisis → Indicadores.

**¿Qué es "Descuadres pendientes" y por qué no está en mi Pulso?**
Es un indicador nuevo (Análisis → Indicadores) que cuenta los descuadres Kárdex ↔ saldo en estatus Pendiente. Viene **apagado en el Pulso**
porque el panel "Necesita tu atención" ya los muestra; se enciende con "Mostrar en Pulso del día".

**¿Por qué mis gráficos con un solo punto ahora se ven y antes eran una lista?**
Regla del dueño: los gráficos **siempre** se dibujan como gráfico. Una dona con una sola rebanada, una barra sola o una línea de un solo día
se ven (la línea pinta cada punto cuando hay 12 o menos). Nunca hay más de 2 gráficos por fila en "Tus gráficos".

## Lote 16 — Recibo directo a posición (pedido nuevo del dueño del 2026-09-30, fuera del plan de cambios), sugerencia con cupo, app con posición destino y trabajo adicional del día

Capítulos: [06 — Inventario y almacén, secciones 1.4, 4.1, 5 y 9](06-inventario-y-almacen.md), [09 — App de almacén, secciones 4 y 9](09-app-almacen.md) y
[F6 — Almacén e inventario (pantallas)](frontend/f6-almacen-e-inventario.md). Los mensajes del API traen el código HTTP indicado; los que se ven solo en pantalla o en la app van agrupados
más abajo. Este lote cubre el **modo de recepción** del almacén (con acomodo o directo a posición), la **posición destino** de cada línea, las sugerencias con cupo, la **posición de recepción por
defecto**, la app con el paso de posición destino y, el mismo día, el selector de posiciones de Recolección, el formato de números, exportar Recibos con sus líneas, el encabezado y los
filtros de las exportaciones y las fechas como fecha en Excel y CSV.

### Mensajes de error nuevos o cambiados

**¿Qué significa "Modo de recepción desconocido: 'X'. Use PUTAWAY o DIRECT."? (400)**
Se mandó un `receivingMode` que no es `PUTAWAY` (Con acomodo) ni `DIRECT` (Directo a posición) al crear o editar un almacén, al crear un recibo o al editar su encabezado. Mande uno de los dos
(no distingue mayúsculas) o no mande el campo. El error viene en `errors.receivingMode`. La pantalla usa un desplegable, así que solo lo ve quien llama al API.

**¿Qué significa "Indique la posición destino de {sku}: el recibo entra directo a posición."? (400)**
Se intentó **confirmar** un recibo directo y la línea de ese producto recibió algo (mayor que 0) pero no tiene posición destino. Indique dónde se guarda: elija la posición en la columna
"Posición destino" (o use "Usar posiciones sugeridas") y confirme de nuevo. No lo pide un producto por lote sin lote, ni una línea con cruce de muelle. En una solicitud, el error viene en
`errors["lines[i].targetBinId"]`, donde `i` es la posición de la línea (desde 0).

**¿Qué significa "La posición {code} está en una zona {zoneType}; la posición destino debe ser de guardado."? (400)**
La posición elegida como destino está en una zona de **recepción** (`STAGING`) o de **cruce de muelle** (`CROSSDOCK`). La mercancía de un recibo directo debe quedar donde se guarda: elija una
posición de otra zona (picking, reserva, refrigerado o cuarentena). El selector de la pantalla ya no las ofrece. Si una posición de guardado se movió después a una zona así, el mismo mensaje sale
al confirmar.

**¿Qué significa "La posición {code} no existe en el almacén del recibo."? (400)**
Se mandó un `targetBinCode` que no coincide con ninguna posición del almacén del recibo (la app lo manda al confirmar; en la web no se escribe el código a mano). Revise el código (mayúsculas
y minúsculas valen igual) y que la posición sea **de ese almacén**. El error viene en `errors.targetBinCode` (en el alta, `errors["lines[i].targetBinCode"]`).

**¿Qué significa "Indique la posición destino por id o por código, no ambos."? (400)**
La línea trae `targetBinId` **y** `targetBinCode`. Mande solo uno. El error viene en `errors.targetBinId`.

**¿Qué significa "Posición no encontrada." al elegir el destino o la posición por defecto? (404)**
El id de la posición no existe, es de otro almacén o es de otra compañía. La posición destino y la posición de recepción por defecto **tienen que ser del almacén del recibo (o del almacén que se edita)**.
Vuelva a elegirla de la lista.

**¿Qué significa "Ya se capturó {sku} con destino {bin}; en un recibo con aviso u orden de compra cada línea entra a una sola posición."? (400)**
En un recibo **con aviso de llegada o con orden de compra**, cada línea del documento entra a **una sola** posición. El mismo producto llegó con dos posiciones distintas (por ejemplo, desde la app).
Deje todo el producto en una posición (y transfiera después la parte que deba ir a otra) o, si la mercancía se recibe en dos posiciones, use un recibo **ciego** o de devolución, donde sí se pueden poner dos
líneas del mismo producto. La app lo avisa **antes** de enviar (ver más abajo). El error viene en `errors["lines[i].targetBinCode"]`.

**¿Qué significa "La posición destino {code} está desactivada." o "La zona de la posición destino {code} está inactiva."? (422)**
La posición (o su zona) se dio de baja después de elegirla, o se intentó elegir una desactivada. Reactívela en Almacenes → Posiciones o elija otra. Se revisa al guardar la línea y otra vez **al confirmar**: un
recibo abierto con un destino dado de baja no se puede confirmar hasta corregirlo.

**¿Qué significa "El almacén no tiene una posición de recepción (zona STAGING); indíquela." al pasar un recibo a "Con acomodo"? (422)**
Es el mismo mensaje de siempre. Ahora sale también cuando se cambia un recibo **directo** abierto a "Con acomodo" y el almacén no tiene zona `STAGING` ni una posición de recepción por defecto, y el
encabezado no indica una. Cree una zona de recepción con una posición, fije la **Posición de recepción por defecto** del almacén, o deje el recibo en directo.

**¿Qué significa "La posición de recepción debe estar en una zona STAGING o CROSSDOCK." o "La posición de recepción está desactivada." al guardar un almacén? (400 / 422)**
Son los mismos mensajes de la posición de recepción de un recibo, ahora también para la **Posición de recepción por defecto** del almacén (Almacenes → Datos → Recepción). Elija una posición activa de una zona de
recepción o de cruce de muelle del mismo almacén. El error viene en `errors.defaultReceivingBinId`.

**¿Qué significa "El recibo {n} ya fue confirmado; no se puede modificar." al usar "Usar posiciones sugeridas" o cambiar el modo? (422)**
Es el mensaje de siempre (recibo ya confirmado), ahora también para el destino de las líneas, el modo del recibo y "Usar posiciones sugeridas": solo se hacen con el recibo abierto (Esperado, Recibiendo o Discrepancia).

**¿Qué significa "El registro fue modificado por otro usuario; recargue e intente de nuevo." al usar "Usar posiciones sugeridas"? (409)**
Alguien guardó el recibo mientras usted tenía la pantalla abierta. La pantalla vuelve a cargar el recibo: revise lo que cambió y pulse el botón otra vez.

**¿Qué significa "La cantidad excede lo disponible para cruce de muelle (0)." (409) o "El recibo de la línea no admite asignaciones (eliminado o sin putaway pendiente)." (422) en un recibo directo ya confirmado?**
Un recibo directo confirmado no tiene acomodo pendiente (la mercancía ya está en su posición destino), y el cruce de muelle sobre un recibo confirmado se hace contra el acomodo pendiente. Asigne el cruce
**mientras el recibo está abierto**. (Por el código; no se probó en un recibo real.)

**¿Qué significa "warehouse.receivingMode debe ser PUTAWAY o DIRECT." (código de salida 1) o "La posición de recepción por defecto {code} no existe en el almacén." (rechazo del almacén en el reporte) en `import-legacy`?**
El primero es un error de la configuración: `warehouse.receivingMode` solo admite `PUTAWAY` o `DIRECT`. El segundo es que `warehouse.defaultReceivingBin` no coincide con ninguna posición creada para el almacén (o no
es de una zona de recepción o de cruce): corrija el código y repita. Las dos claves solo se aplican **al crear el almacén**: `--update` no las pisa; en un almacén que ya existe se cambian en Almacenes → Datos.

### Mensajes que solo ve en la pantalla o en la app (sin código HTTP)

**¿Qué significa "Falta la posición destino en {n} línea(s)."?**
Está bajo el botón **Confirmar recibo** (apagado) de un recibo directo: hay {n} líneas que reciben algo y no tienen posición destino. Elija la posición de cada una o pulse **Usar posiciones sugeridas**.

**¿Qué significa "Excede el cupo de {bin}: caben {free}"?**
Es un **aviso naranja**, no un error: lo recibido en esa línea es más que el espacio libre de la posición (`cupo − existencia − lo que otras líneas del recibo ya destinan`). Se puede guardar y confirmar igual.
Ver "¿Puedo pasarme del cupo?".

**¿Qué significa "Sugerida: {bin} · {motivo}" bajo la posición destino?**
Es la primera posición que el sistema recomienda para esa línea y por qué ("Consolidar con el mismo producto", "Reserva vacía", "Cuarentena (devolución)", etc.). Es solo una pista: no se llena sola. Aparece solo si la
posición elegida no es esa.

**¿Qué significa "Se asignó posición a {n} línea(s); {m} sin sugerencia."?**
Es el aviso de **Usar posiciones sugeridas**: {n} líneas recibieron su posición sugerida y {m} no tenían ninguna posición de guardado donde cupieran. Elija a mano las {m}.

**¿Qué significa "Elija primero un producto" en la posición de Recolección y empaque?**
El selector de Posición solo ofrece las posiciones donde el producto tiene existencia disponible, así que necesita saber el producto. Elíjalo y aparecerán.

**¿Qué significa "{code} recibe directo a posición: aquí solo aparecen recibos anteriores al cambio o con cruce de muelle."?**
Es un aviso en **Recibo → Acomodo pendiente** cuando filtra por un almacén directo. Los recibos directos no generan tareas de acomodo, así que no aparecen aquí. Ver "¿Por qué un recibo directo no aparece en Acomodo pendiente?".

**¿Qué significa "¿Cambiar el modo de recepción?"?**
Es la confirmación al guardar un cambio de modo en la ficha del almacén. Dice cuántos recibos abiertos y cuántos con acomodo pendiente hay: **conservan su modo**; solo los recibos **nuevos** siguen el modo nuevo.

**En la app: "La posición no existe en este almacén.", "Esa posición es de recepción o de cruce de muelle; escanea dónde se guarda.", "Esa posición está desactivada; escanea otra."**
La app validó la posición escaneada **sin señal**, contra las posiciones que tiene guardadas del almacén. Escanee la posición donde de verdad se deja la mercancía. Si acaba de crearse una posición y la app aún no
la tiene, sincronice con señal ("Sincronizar ahora") y escanee de nuevo.

**En la app: "El aparato todavía no tiene las posiciones de este almacén. Sincroniza con señal e intenta de nuevo."**
El aparato no ha descargado las posiciones del almacén por defecto (la primera vez bajan todas, en Advance Depot unas 3.886). Con señal, pulse "Sincronizar ahora" y vuelva a escanear.

**En la app: "No se puede enviar así" / "Ya se capturó {sku} con destino {bin}; en un recibo con aviso u orden de compra cada línea entra a una sola posición."**
Es el mismo caso del 400 de arriba, detectado **antes de enviar**. Ver "¿Por qué la pistola no me deja enviar?".

### Preguntas frecuentes

**¿Cómo pongo un almacén en directo?**
Con `warehouse.manage`: Almacenes → elija el almacén → **Datos** → sección **Recepción** → **Modo de recepción** = "Directo a posición" → **Guardar** y confirme. (También se puede al crear el almacén, o con `PATCH` de
`receivingMode`.) Desde ese momento los recibos **nuevos** nacen directos: cada línea lleva su posición destino y no hay tareas de acomodo. Para volver, cambie el modo a "Con acomodo". El modo de un almacén
sin zona de recepción (como Advance Solutions) debe seguir siendo directo, o los recibos con acomodo darán "El almacén no tiene una posición de recepción (zona STAGING); indíquela.".

**¿Qué pasa con los recibos abiertos cuando cambio el modo del almacén?**
**Nada:** cada recibo conserva el modo con que se abrió, y las tareas de acomodo pendientes siguen su curso (hay que acomodarlas). Solo los recibos nuevos siguen el modo nuevo. Si quiere que **un** recibo abierto
cambie, edite su encabezado (lápiz) y cambie su "Modo de recepción" antes de confirmarlo.

**¿Puedo pasarme del cupo?**
Sí. El cupo de la posición **solo avisa**: la línea se marca con "Excede el cupo de {bin}: caben {free}" en naranja y se puede guardar y confirmar igual. Lo que no cabe queda físicamente en esa posición; transfiera
el sobrante a otra después (Transferencias y ajustes). Los recibos abiertos a la vez no se ven entre sí, así que dos recibos pueden pasar el mismo cupo. "Usar posiciones sugeridas", en cambio, **solo asigna donde cabe**.

**¿Por qué mi recibo directo no pide la posición de recepción?**
Porque la mercancía no pasa por ahí: entra directo a la posición destino de cada línea. Un almacén sin zona `STAGING` puede recibir así. La posición de recepción por defecto del almacén solo la usan los recibos con
acomodo y las líneas con cruce de muelle.

**¿Por qué un recibo directo no aparece en Acomodo pendiente?**
Porque no genera tareas: al confirmar, cada línea queda en su posición y el recibo pasa a **Acomodado** en el mismo momento (el historial deja Recibiendo → Completado → Acomodado). En un almacén directo, Acomodo
pendiente solo muestra recibos anteriores al cambio de modo, los que se pasaron a "Con acomodo" y los que tuvieron cruce de muelle.

**¿Por qué el Kárdex no muestra un movimiento de acomodo después de un recibo directo?**
Porque no hubo: la entrada (`RECEIPT`) ya se asentó en la posición final. En un recibo con acomodo hay una entrada a la posición de recepción y luego una transferencia a la posición final.

**¿Por qué Actividad reciente muestra un solo evento en un recibo directo?**
Porque el recibo se confirma y queda Acomodado en el mismo instante. Solo se muestra "Recibo … confirmado" (el evento de "acomodado" se omite si no hubo tareas). Con cruce de muelle o con acomodo, salen los dos.

**¿Por qué no me sugiere ninguna posición?**
Ninguna posición de guardado activa tiene espacio para esa cantidad (cupo en unidades), o el producto no tiene dónde consolidar y el almacén no tiene reservas ni picking. El sistema **nunca** sugiere recepción ni
cruce de muelle, y sugiere la **cuarentena** solo en devoluciones. Elija una a mano; se le avisa si no cabe. Desde este lote el cupo en unidades también se respeta en las **tareas de acomodo** de siempre: una
posición llena ya no se sugiere para acomodar.

**¿Por qué en una devolución la primera sugerida es la cuarentena?**
Es una decisión del dueño (D6): la mercancía devuelta se revisa antes de volver a vender. La primera sugerida es una posición de cuarentena, si el almacén tiene una. Puede elegir otra.

**¿Qué es la "Posición de recepción por defecto" y por qué Depot usa R1?**
Es la posición de recepción que usan los recibos con acomodo (y las líneas con cruce de muelle) cuando el encabezado no indica otra. Sin ella, el sistema toma la primera posición de una zona `STAGING` por código de zona;
en Advance Depot eso daba `S1` de "Embarque". Por decisión del dueño, en Depot es `R1` (zona `STG`). Se cambia en Almacenes → Datos → Recepción (con "La primera de recepción" se vuelve a la regla automática).

**¿Puedo repartir una línea en varias posiciones?**
No: una línea entra entera a **una** posición. Si llegó más de lo que cabe, reciba igual y transfiera el sobrante después. En un recibo **ciego o de devolución** puede poner dos líneas del mismo producto, cada una con
su posición. En un recibo con aviso u orden de compra, el mismo producto con dos posiciones da 400 (ver arriba).

**¿Por qué mi recibo de la app entró "Con acomodo" en un almacén directo?**
Porque se empezó con una app **anterior** a este lote (no manda posiciones ni modo). El servidor no lo pierde: lo recibe con acomodo y crea tareas. Actualice la app de los aparatos para que escaneen la posición destino.

**¿Por qué la pistola no me deja enviar?**
Hay tres causas en un almacén directo: (1) **Confirmar recibo** está apagado porque alguna línea no tiene posición destino (las líneas se agregan al escanear una posición válida); (2) sale "No se puede enviar así": el mismo
producto de un aviso u orden de compra quedaría en dos posiciones; deje todas sus líneas en una posición (quite una línea y vuelva a agregarla), y (3) "El aparato todavía no tiene las posiciones de este almacén": sincronice
con señal. Un recibo que el servidor rechaza después, ya en la cola, **no se puede editar**: por eso la app lo impide antes.

**¿Por qué la pista "Sugerida" no aparece en la pistola?**
Necesita señal. Sin señal la captura sigue igual: escanee la posición donde deja la mercancía. La pista sale de las sugerencias de acomodo del servidor (la primera para ese producto y cantidad) y no descuenta lo que otras líneas del
mismo recibo ya ocupan.

**¿Por qué el selector de Posición de Recolección y empaque solo muestra algunas posiciones?**
Desde el ajuste del 2026-09-30 solo ofrece las posiciones **donde el producto tiene existencia disponible**, con su cantidad ("P-01 · PCK · 9 disp."), en el orden FEFO del sistema (la primera va marcada "Sugerida").
Si elige un lote, solo las posiciones de ese lote. Sin producto el campo está apagado.

**¿Por qué los números ahora llevan coma?**
Porque así se escriben en Puerto Rico: coma para los miles y punto para los decimales (`61,023`; `1,250.5`). Antes, en español, el sistema escribía `61.023` (punto de miles) y una cifra de cuatro dígitos como `1250` sin
separador, y se confundían con decimales. El cambio es solo de presentación: los datos no cambiaron. El dinero lleva `$` y dos decimales (`$1,234.50`). Vale en la web y en la app; los números de documento y los identificadores no llevan coma. Es el mismo formato en inglés.

**¿Por qué el PDF o el Excel traen la compañía y una línea de "Filtros:"?**
Desde este lote, todo PDF de tabla y todo Excel llevan arriba la **compañía**, el título, "Generado el …" y una línea con los **filtros que tenía la pantalla** ("Filtros: Almacén ALM-01 (Almacén principal) ·
Estatus Recibiendo · Creado del 01/09/2026 al 30/09/2026"), para saber de dónde salió el archivo. El CSV no cambia.

**¿Por qué el PDF dice "Sin filtros"?**
Porque la pantalla tiene barra de filtros y no había ninguno elegido cuando exportó: el archivo trae **todo** lo que había. La línea de filtros solo nombra los que estén **elegidos** al exportar: para dejar constancia de un filtro, elíjalo antes de exportar. Las tablas que
no tienen barra de filtros, y las que están dentro de una ventana (modal), no llevan esa línea.

**¿Cómo abro las fechas del Excel?**
Son **fechas de verdad**, no texto: se pueden ordenar, filtrar por fecha y restar. En Excel llevan el formato `aaaa-mm-dd` (o `aaaa-mm-dd hh:mm` si tienen hora, en hora de Puerto Rico). Si una celda muestra un número
como `46295`, dé a la columna el formato de fecha. En el **CSV**, la fecha sale como `2026-09-30` o `2026-09-30 14:03:00`: Excel la lee como fecha según su configuración regional; si no lo hace, abra el archivo con
Datos → Desde texto/CSV y marque esa columna como fecha. El PDF muestra la fecha como texto legible.

**¿Por qué el Kárdex ya no exporta la columna Hora?**
Porque la columna **Fecha** del archivo ahora lleva la fecha **y la hora** del movimiento. Conteos y Descuadres exportan la fecha real.

**¿Por qué el Excel de Recibos tiene una fila por línea y ya no trae la columna "Líneas"?**
Porque la exportación de Recibos (y de Acomodo pendiente) ahora trae **cada recibo con sus líneas**: en Excel y CSV, una fila por línea repitiendo los datos del recibo (un recibo sin líneas ocupa una fila con las columnas de
línea vacías); en PDF, un bloque por recibo con la tablita de sus líneas ("Sin líneas" si no tiene). Como se ven las líneas, el conteo ya no hace falta.

**¿Por qué los encabezados del Excel no van en negrita ni quedan fijos al desplazarse?**
Es un límite de la biblioteca que genera los archivos (edición comunitaria de SheetJS): no escribe negrita ni paneles inmovilizados. La fila de encabezados lleva **autofiltro** (las flechas de filtro de Excel) para
ordenarla y filtrarla.

**¿Por qué el PDF de Recibos sale horizontal en tamaño carta y los demás en A4?**
Los PDF agrupados (recibo con sus líneas) usan hoja carta horizontal; las demás tablas, A4 vertical (horizontal con más de 5 columnas).

**¿Cómo vuelvo un almacén de "Directo a posición" a "Con acomodo"?**
Igual que al revés: Almacenes → Datos → Recepción → Modo de recepción = "Con acomodo". Necesita una posición de recepción (la del almacén o la primera zona `STAGING`) para recibir con acomodo. Los recibos directos
abiertos siguen directos.

## Lote F9 — Ajustes de la compañía (pantalla) y región y formatos en toda la web

Pantalla Sistema → Ajustes de la compañía (`/system/settings`, `admin.tenant`). Detalle en
[frontend/f9-ajustes-de-la-compania.md](frontend/f9-ajustes-de-la-compania.md). Los mensajes del servidor de Región y formatos
están arriba, en "Región y formatos (2026-10)" (lote 18).

### Mensajes de error nuevos o cambiados

**¿Qué significa "El nombre es obligatorio." al agregar un feriado? (400)**
El feriado llegó sin nombre al servidor. Escriba el nombre (por ejemplo "Día de Reyes") y vuelva a agregarlo. La pantalla lo
avisa antes con "Escribe la fecha y el nombre del feriado".

**¿Qué significa "'COD' depende de 'LTL_GROUND', que debe encenderse primero."? (409)**
Se intentó encender un módulo cuyo módulo requerido está apagado. Encienda primero el requerido (la pantalla lo indica con
"Primero activa: …" y deja el interruptor deshabilitado).

**¿Qué significa "'SYSTEM' es un módulo núcleo y no se puede apagar."? (409)**
Los módulos núcleo siempre están encendidos; la pantalla los marca "Núcleo — siempre activo" y no ofrece apagarlos.

**¿Qué significa "Debe ser ≥ 1." en el máximo de paradas por ruta? (400)**
El máximo de paradas por ruta por defecto tiene que ser 1 o más. La pantalla lo avisa antes con "Debe ser 1 o más.".

**¿Qué significa "Código de idioma de 2 letras."? (400)**
El idioma por defecto debe ser un código de 2 letras (`es`, `en`). La pantalla solo ofrece Español e English.

**¿Qué significa "BrandingJson no es JSON válido." o "BrandingJson demasiado grande (los logos van a blob storage)."?**
Eran los mensajes de antes del lote 19. Hoy la marca se valida con reglas propias (tamaño de 4096 caracteres, campos conocidos,
colores, contraste y matiz) y cada mensaje está en la sección "Lote 19 — Marca por compañía" de más abajo.

### Mensajes que solo ve en la pantalla (sin código HTTP)

**"Tiene que haber al menos un día laborable"** — se intentó apagar el último día laborable; deje al menos uno.

**"Escribe la fecha y el nombre del feriado"** — falta la fecha o el nombre del feriado nuevo.

**"Ya hay un feriado en esa fecha."** — ya existe un feriado en esa misma fecha; elimínelo primero si quiere reemplazarlo. Desde
el lote 19 esto lo decide el servidor (409) y la pantalla muestra su mensaje (antes la pantalla lo detectaba sola y el servidor
reemplazaba el feriado sin avisar).

**"El separador de miles y el decimal no pueden ser el mismo: se cambió el otro."** — es un aviso, no un error: al escoger un
separador que chocaba con el otro, la pantalla cambió el otro. Revise la vista previa y guarde.

**"Escriba un color hexadecimal, por ejemplo #1F6FE5."** — el color propio escrito a mano no es un hexadecimal de 3 o 6 dígitos.

**"Corrija las validaciones antes de guardar."** — la combinación de colores no pasa el contraste mínimo o los dos acentos se
parecen demasiado (menos de 40°); la lista marcada con ✕ dice cuál.

**"Debe ser 1 o más."** — máximo de paradas por ruta vacío, decimal o menor que 1.

**"El teléfono debe tener 10 dígitos: (###) ###-####."** — ver la pregunta del lote 12 (el número y la máscara salen de la
compañía).

### Preguntas frecuentes

**¿Por qué las fechas en español ahora salen mes/día/año y la hora con a. m./p. m.?**
Desde el lote F9 el formato es de la compañía (Región y formatos), no del idioma. Puerto Rico usa MM/DD/AAAA y 12 horas (el orden de
la fecha de Puerto Rico está pendiente de confirmar con el dueño: decisión 1 del lote 18). Para día/mes/año o 24 horas, un administrador lo cambia en Ajustes de la
compañía → Región y formatos.

**Cambié los formatos y otro usuario todavía ve los anteriores.**
Su pantalla vuelve a leer los ajustes cuando pasan 10 minutos y vuelve a la ventana o abre otra pantalla, o al cambiar de idioma
o de compañía. Recargar la página los trae de inmediato.

**¿Qué pasa con los teléfonos de proveedores ya guardados con otra máscara?**
Se muestran con la máscara nueva si tienen los dígitos que pide (por ejemplo, 10 en Puerto Rico); los que no, se ven tal cual
y hay que corregirlos al editarlos. Los que se guardan desde ahora llevan solo los dígitos.

**Cambié la región a Estados Unidos y la hora no cambió.**
Puerto Rico (AST) y el Este de EE. UU. (Nueva York, con horario de verano) tienen la misma hora de marzo a noviembre (UTC−4). En
invierno Nueva York va una hora detrás. El resto de los formatos de las dos regiones es igual.

**¿Dónde cambio la política de MFA o la duración de las sesiones?**
En Seguridad y auditoría (botón "Abrir Seguridad y auditoría" de la pestaña General); no se repite en Ajustes de la compañía.

**¿Dónde cambio el modo de recepción de un almacén?**
En la ficha del almacén (Almacenes → Recepción). La pestaña Operación solo lo resume y marca en rojo los almacenes con acomodo
sin posición de recepción.

**¿Puedo cargar el logo de mi compañía?**
Sí, desde el lote 19: Ajustes de la compañía → Marca → Logo (ver "Lote 19 — Marca por compañía" más abajo).

## Lote F10 — Seguridad y auditoría (pantalla), sesiones de toda la compañía y actividad paginada

Pantalla Sistema → Seguridad y auditoría (`/system/audit`, `admin.audit`). Detalle en
[frontend/f10-seguridad-y-auditoria.md](frontend/f10-seguridad-y-auditoria.md); backend en el capítulo 01, secciones 9 y 9.1.

### Mensajes de error nuevos o cambiados

**¿Qué significa "La sesión actual no se revoca desde la lista; use Salir."? (409)**
Se intentó revocar la propia sesión desde Sesiones activas (`DELETE /api/v1/audit/sessions/{id}`). La pantalla no ofrece ese
botón en la fila "Esta sesión"; para cerrarla use **Salir**. (En Mi cuenta → Sesiones el mensaje sigue siendo el del lote 1:
"…; use logout.")

**¿Qué significa "Sesión '<id>' no encontrada."? (404)**
La sesión ya no está activa: otro administrador la revocó, venció, el usuario salió, o es de otra compañía. Recargue la lista.

**¿Qué significa "Esta acción requiere reautenticación reciente (AAL2)."? (403, `aal2_required`)**
"Cerrar las demás sesiones" es una acción sensible. La web abre sola la ventana de contraseña (y código MFA) y la repite; solo
lo verá si llama al API a mano: haga `POST /api/v1/auth/reauth` y repita.

**Revocar una sesión responde 403 sin mensaje.**
Su usuario no tiene `admin.users` (revocar exige `admin.audit` y `admin.users`). En la pantalla los botones no aparecen sin
ese permiso. Queda un evento `PERMISSION_DENIED`.

**"Entre 1 y 365 días." / "Entre 5 y 240 minutos." en la política (400)**
Son los mensajes de siempre de `PUT /api/v1/tenant/settings` (capítulo 01, sección 11); ahora la pantalla los pone bajo el
campo. La duración debe ser un número entero de días de 1 a 365.

### Mensajes que solo ve en la pantalla (sin código HTTP)

**"Entre 1 y 365 días."** bajo una duración — vacía, decimal, menor que 1 o mayor que 365; no se manda nada hasta corregirla.

**"Se descargaron las primeras 10000 filas; acote la fecha para exportar el resto."** — la exportación tiene un tope de 10 000
filas; ponga una Fecha más corta y exporte por partes.

**"No hay actividad con estos filtros."** — ninguna fila cumple el tipo, la fecha y la búsqueda; use **Limpiar**.

**"No se pudieron leer las sesiones."** / **"No se pudieron leer los ajustes de la compañía."** — falló la lectura (red o
permiso); **Reintentar** o recargue.

### Preguntas frecuentes

**¿Qué diferencia hay entre "Evento" y "Alerta"?**
Los dos son eventos de seguridad. "Alerta" es un intento fallido o bloqueado (contraseña incorrecta, bloqueo de cuenta,
reutilización de un token), un permiso denegado o un bloqueo; "Evento", lo que salió bien (entradas, salidas, MFA, sesiones
revocadas a propósito). "Cambio" es la bitácora de cambios de los registros.

**Antes el total de la actividad cambiaba al pasar de página.**
Era un error del servidor (el total salía de las primeras filas leídas). Desde el lote F10 es el conteo real y las páginas llegan
hasta el final; la búsqueda también se hace sobre toda la bitácora, no sobre las primeras filas.

**Busqué "Login · Éxito" y no encontré nada.**
La búsqueda mira cada dato por separado (tipo, resultado, usuario, detalle, IP, entidad). Busque `Login` o `Éxito`, o use el
filtro Seguridad.

**¿Por qué la "Ubicación" de una sesión es un número (o "—")?**
Es la IP desde la que se abrió o renovó la sesión. Las sesiones abiertas antes de este lote no la tienen ("—") hasta que se
renuevan. No se convierte en ciudad (haría falta un servicio externo).

**Revoqué una sesión y el usuario perdió todas.**
Ya no pasa (decisión del dueño, 2026-10-03). Revocar una sesión (como administrador, desde Mi cuenta, "Cerrar las demás
sesiones" o logout) cierra solo esa: si el aparato revocado intenta renovarse recibe `401 Refresh token inválido.` y las demás
sesiones de la persona siguen vivas. Solo el reuso de un token ya rotado (posible robo) cierra todas las sesiones de la persona.
Si el usuario aún perdió varias, revise en Actividad si hubo un evento `refresh_reuse`: es un posible robo y es lo esperado.

**¿"Cerrar las demás sesiones" me saca también de mis otros aparatos?**
Sí: cierra todas las sesiones de la compañía menos la de este navegador, incluidas las suyas en otros aparatos y las de los
aparatos de almacén.

**Cambié la duración de la sesión y nadie tuvo que volver a entrar.**
La duración nueva vale para las sesiones que se abren o renuevan desde ese momento; las demás conservan su vencimiento.

## Lote 19 — Marca por compañía en el servidor: validación de colores, logos y feriado duplicado

Pestaña Ajustes de la compañía → Marca (`/system/settings?tab=brand`, `admin.tenant`). Detalle en
[frontend/f11-marca-por-compania.md](frontend/f11-marca-por-compania.md) y en el capítulo 01, sección 11.2. Los errores de la
marca salen junto a los colores; los de cada logo, debajo de su ranura.

### Mensajes de error nuevos o cambiados

**¿Qué significa "La marca es demasiado grande (máximo 4096 caracteres); los logos se suben aparte."? (400)**
El `brandingJson` pasa de 4096 caracteres. La marca es solo tema y tres colores (menos de 200 caracteres); los logos ya no viajan
ahí, se suben con su propio botón. Solo aparece si alguien llama al API a mano con otro contenido.

**¿Qué significa "La marca no es un JSON válido." o "La marca debe ser un objeto JSON."? (400)**
El texto no es JSON, o es JSON pero no un objeto (`{ ... }`). La pantalla siempre manda un objeto válido.

**¿Qué significa "La marca trae un campo desconocido: '<campo>'." ? (400)**
Se mandó un campo que no es de la marca. Solo existen `preset`, `useCustom` y `custom` (con `flow`, `money` y `neutral`). Si estaba
guardando los logos dentro de la marca, ya no se hace: use los endpoints de logos.

**¿Qué significa "Los colores de estado (ok, warn, danger, info) no se pueden personalizar: '<campo>'."? (400)**
Verde, ámbar, rojo y azul de información no se cambian a propósito (verde = bien, rojo = mal); quite ese campo.

**¿Qué significa "El campo '<campo>' tiene un tipo inválido."? (400)**
`preset` debe ser texto, `useCustom` verdadero o falso, `custom` un objeto y cada color un texto.

**¿Qué significa "El color 'custom.<flow|money|neutral>' no es hexadecimal (use #RGB o #RRGGBB)."? (400)**
El color no es un hexadecimal de 3 o 6 dígitos (el `#` es opcional; no se admiten espacios ni nombres como "rojo"). La pantalla lo
avisa antes con "Escriba un color hexadecimal, por ejemplo #1F6FE5.".

**¿Qué significa "El tema predefinido '<id>' no existe."? (400)**
Los temas son `teikem`, `marino`, `acero`, `carretera`, `granate`, `vino`, `bosque`, `selva`, `oliva`, `turquesa`, `indigo`,
`violeta` y `grafito`, en minúscula.

**¿Qué significa "El contraste del color de operación en modo oscuro es 1.34:1; el mínimo es 4.5:1."? (400)**
Con ese color, lo que se escribe o se pinta con él no se lee bien sobre el panel del tema. Dice cuál pieza (texto, texto atenuado,
color de operación o color de dinero), en qué modo (oscuro o claro), el contraste real y el mínimo (7:1 para el texto principal; 4.5:1
para el resto). Escoja un color más claro (modo oscuro) o más oscuro (modo claro); la pantalla muestra todos los contrastes en vivo.

**¿Qué significa "Los colores de operación y de dinero son demasiado parecidos: 4° de separación y el mínimo es 40°."? (400)**
Los dos acentos son «dos corrientes» (lo que sale a la calle y el dinero que vuelve) y tienen que distinguirse: sus matices deben
estar al menos 40° separados. Cambie uno de los dos.

**¿Qué significa "Ranura de logo '<slot>' no encontrada."? (404) y "Logo '<slot>' no encontrado."? (404)**
La primera: la ranura no es una de `lockup`, `lockup-inverted`, `mark`, `mark-inverted`. La segunda: la compañía no tiene logo en esa
ranura (leer o quitar). La interfaz entonces usa la otra variante o el logo de Teikem.

**¿Qué significa "Seleccione un archivo de logo."? (400)**
No llegó archivo (o llegó vacío). Escoja uno con el botón Subir.

**¿Qué significa "El logo supera el tamaño máximo de 512 KB."? (413)**
El archivo pesa más de 512 KB (524.288 bytes). Reduzca el PNG/JPG/WebP o simplifique el SVG. La pantalla lo avisa antes de enviarlo.

**¿Qué significa "Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP."? (415)**
El contenido del archivo no es de uno de esos formatos. Se revisa el contenido, no el nombre: un archivo renombrado a `.png` no
sirve. GIF, PDF, HTML y SVG guardados en UTF-16 tampoco se admiten. Si la petición no es `multipart/form-data` el 415 llega sin
mensaje.

**¿Qué significa "El contenido del archivo (image/png) no coincide con el tipo declarado (image/jpeg)."? (415)**
El navegador o el programa dijo que era un tipo de imagen y los bytes son de otro. Guarde el archivo de nuevo en el formato que
quiere.

**¿Qué significa "El archivo está dañado o incompleto y no se puede usar como logo."? (400)**
Es un PNG, JPG o WebP cortado (una descarga interrumpida, por ejemplo). Vuelva a exportarlo.

**¿Qué significa "La subida del logo llegó incompleta o mal formada. Vuelva a intentarlo."? (400)**
La petición se cortó a la mitad (sin red, por ejemplo). Reintente.

**¿Qué significa "El SVG no es un XML válido."? (400)**
El SVG tiene etiquetas sin cerrar o caracteres inválidos. Ábralo en un editor de imágenes y guárdelo de nuevo.

**¿Qué significa "El SVG no se acepta: declara DOCTYPE o entidades."? (400)**
Un SVG con `<!DOCTYPE …>` o `<!ENTITY …>` puede leer archivos del servidor o hacerlo gastar memoria; se rechaza. Exporte el SVG sin
DOCTYPE (opción «SVG 1.1/Tiny» sin DTD, o limpie el archivo con SVGO).

**¿Qué significa "El SVG no se acepta: contiene el elemento <script>, que puede ejecutar código o cargar contenido externo."? (400)**
El SVG trae un elemento activo (`script`, `foreignObject`, `iframe`, `object`, `embed`, `link`, `animate`, `set`…). Quítelo o
convierta el diseño a trazos y formas simples.

**¿Qué significa "El SVG no se acepta: el atributo 'onload' ejecuta código."? (400)**
Algún elemento tiene un atributo de evento (`onload`, `onclick`…). Quítelo.

**¿Qué significa "El SVG no se acepta: el atributo '<atributo>' contiene un enlace de script (javascript:)."? (400)**
Un enlace del SVG empieza con `javascript:` (o `vbscript:`). Quítelo.

**¿Qué significa "El SVG no se acepta: el atributo '<atributo>' apunta fuera del archivo (solo se permiten referencias internas #id)."? (400)**
Un `<image>`, `<use>` o `<a>` apunta a otra dirección (`https://…`, otro archivo). Incruste la imagen como `data:image/png;base64,…` o
use solo referencias a elementos del mismo SVG (`#id`).

**¿Qué significa "El SVG no se acepta: una hoja de estilos importa o referencia contenido externo."? (400)**
Un `<style>` o `style=""` usa `@import`, `url(https://…)` o `expression(…)`. Los SVG con fuentes web hacen esto: convierta los textos
a trazos.

**¿Qué significa "Ya hay un feriado en esa fecha."? (409)**
`POST /tenant/holidays` con una fecha que ya tiene un feriado activo. No cambia el que existe; elimínelo primero si quiere
cambiarlo. (Uno eliminado antes sí se puede volver a agregar.)

### Mensajes que solo ve en la pantalla (sin código HTTP)

**"Sin logo: se usa el de Teikem"** — esa ranura no tiene archivo.

**"Logo guardado" / "Logo quitado"** — confirmación de subir/reemplazar o quitar un logo (se aplica de inmediato, sin Guardar cambios).

### Preguntas frecuentes

**¿Los logos se guardan con "Guardar cambios"?**
No. Cada logo se sube o se quita al instante con sus botones; "Guardar cambios" es solo para los colores.

**Subí solo el lockup para fondo claro y en el tema oscuro se ve igual.**
Es a propósito: si falta una variante se usa la otra en los dos temas. Suba la invertida si el logo no se lee sobre fondo oscuro.

**Subí el lockup pero la barra colapsada sigue mostrando el símbolo de Teikem.**
La barra colapsada usa la marca cuadrada (`mark`); suba esa pieza. El lockup no se encoge para sustituirla.

**Un usuario sin `admin.tenant` ve los logos pero no los puede cambiar.**
Correcto: leer solo pide sesión (es la marca de la interfaz) y escribir pide `admin.tenant`.

**¿Dónde queda el archivo del logo y qué ve la bitácora?**
En la base de datos (tabla `TenantBrandLogo`). La bitácora de cambios (entidad `TENANT_LOGO`) registra quién y cuándo subió o quitó,
y el tipo y el tamaño, pero nunca el archivo.

---

## Lote 21 — Conteo cíclico por producto: corrección, vista previa, "Por revisar", cierre en bloque y posición provisional (servidor)

Capítulo: [06 — Inventario y almacén](06-inventario-y-almacen.md), sección 6, "Lote 21".

### Mensajes de error nuevos o cambiados

**¿Qué significa "El conteo ya fue reconciliado; solo se consulta." al pedir la vista previa? (422)**
La vista previa solo existe mientras el conteo está Pendiente o Contado. Uno ya confirmado (Concordancia o Diferencia) se consulta con su ficha.

**¿Qué significa "El conteo ya fue reconciliado; no admite posiciones nuevas." al crear una posición provisional? (422)**
La posición provisional se crea desde un conteo sin confirmar. Si el conteo ya se confirmó, cree la posición desde Almacenes (requiere `warehouse.manage`).

**¿Qué significa "Ya existe una posición con ese código en el almacén." al crear una posición provisional? (409)**
El código es único por almacén (también frente a las posiciones definitivas y otras provisionales). Use esa posición como línea del conteo o escriba otro código.

**¿Qué significa "La posición no está pendiente de revisión." al confirmar? (409)**
Esa posición no es provisional: ya se confirmó (o nunca lo fue). No hay nada más que hacer.

**¿Qué significa "Zona no encontrada." al crear una posición provisional? (404)**
La zona no es del almacén del conteo (o no existe). Escoja una zona del mismo almacén del conteo.

**¿Qué significa "La zona está inactiva; reactívela primero." al crear una posición provisional? (422)**
La zona está dada de baja. Escoja otra o pida a un supervisor (`warehouse.manage`) que la reactive.

**¿Qué significa "Indique la zona de la posición." / "Indique el código de la posición o su pasillo/rack/nivel/posición." / "El código de la posición solo admite letras, números, guion y guion bajo (máximo 40)."? (400)**
Son las mismas validaciones del alta de posición: falta la zona, falta el código (o sus partes) o el código tiene caracteres no admitidos.

**¿Qué significa "Se revisan como máximo 200 conteos por vez; acote por almacén o por ids." en el cierre en bloque? (400)**
Mandó más de 200 `ids`. Envíe menos o use `warehousePublicId` (el servidor mira hasta 200 y marca `truncated = true` si había más).

**¿Qué significa "El conteo de {sku} en {posición} ({contado}) es menor que lo reservado ({reservado}); libere la reserva antes de reconciliar."? (409)**
Es el mismo error de siempre al confirmar, y ahora también sale **antes**, en la vista previa (`lines[].error`), y como motivo `Errors` en el cierre en bloque. Libere la reserva o corrija la cantidad contada.

**¿Qué significa "La serie {serie} está capturada en más de una línea del conteo."? (400)**
Una misma serie del mismo producto aparece en dos líneas. En la vista previa llega como `blockingError`; en el cierre en bloque como `Errors`. Quite la serie de una de las líneas.

### Motivos de "omitido" del cierre en bloque (`skipped[].reasonCode`, HTTP 200)

**`WouldPost` — "Asentaría {n} movimiento(s); revíselo."**
Contra la existencia **actual** el conteo no cuadra (aunque con la foto sí cuadrara). Ábralo, vea la vista previa y corrija o confirme a mano.

**`Pending` — "Faltan {n} línea(s) por contar."**
Tiene líneas sin cantidad (por ejemplo, "Refrescar" borró las capturas de las líneas con foto vieja). Termine de contarlas.

**`Errors`**
Alguna línea daría error al confirmar (contado menor que lo reservado o una serie repetida). El texto de `reason` dice cuál.

**`Stale` — "La existencia cambió mientras se cerraba; revíselo."**
Cuadraba al mirarlo, pero un movimiento cambió la existencia antes de cerrarlo y ya no cuadra. No se cerró nada de ese conteo; vuelva a mirarlo.

**`NotCounted` — "El conteo todavía no se termina de contar."**
Está Pendiente y usted lo pidió por `ids` sin `includeOpen`. Termínelo o pase `includeOpen: true`.

**`AlreadyReconciled`, `NotFound` ("El conteo no existe."), `NoLines` ("El conteo no tiene líneas."), `Failed`**
Ya estaba confirmado; el id no existe o es de otra compañía; no tiene líneas; o falló por otra regla (el motivo viene en `reason`). Un fallo de un conteo no afecta a los demás.

### Preguntas frecuentes

**¿Cuál es la diferencia entre capturar, recapturar y corregir?**
La primera vez que se cuenta una línea queda como lo "capturado" (con quién y cuándo). La misma persona puede recapturar mientras el conteo está Pendiente y reemplaza su captura. Cualquier cambio de otra persona, o de cualquiera después de terminar el conteo (Contado), es una **corrección**: la cantidad vigente cambia, lo capturado se conserva y se anota quién corrigió. Si la corrección vuelve al valor capturado, desaparece.

**¿Corregir una cantidad mueve inventario?**
No. Una corrección no es un ajuste ni una transferencia: solo cambia lo que se reconcilia. El inventario se mueve únicamente al confirmar, por la diferencia entre lo contado vigente y la existencia actual.

**¿Dónde queda la evidencia de la corrección?**
En la línea (`capturedQty`, `capturedByName`, `capturedAtUtc`, `correctedByName`, `correctedAtUtc`, `wasCorrected`), en la bitácora de cambios del conteo y, al confirmar, en el motivo del movimiento del Kárdex: `Conteo CC-00001 · contó 3 (Ana Pérez, 2026-10-03 14:05) · corregido de 3 a 5 por Beto Ruiz (2026-10-03 15:00)`.

**¿Quien cuenta a ciegas puede ver lo que dice el sistema por la evidencia?**
No. Ve lo que él capturó y si alguien lo corrigió, pero `systemQty`, `currentQty`, `varianceQty`, `reconciledSystemQty` y `adjustedQty` siguen en `null`.

**¿La vista previa dice lo mismo que va a pasar al confirmar?**
Sí: usa el mismo cálculo que la confirmación, con la existencia actual. Lo único que puede cambiar es que entre la vista previa y la confirmación otro movimiento modifique la existencia; por eso la confirmación recalcula con los saldos bloqueados.

**¿Qué significa `matches`?**
El conteo cuadra: tiene líneas, ninguna sin contar, ninguna con error y, contra la existencia **actual**, no asentaría ningún movimiento. Si aún hay líneas pendientes, `matches` es `false` aunque lo contado hasta ahora coincida.

**El conteo cuadraba con la foto pero "Cerrar los que cuadran" no lo cerró.**
Porque se mide contra la existencia actual, no contra la foto: si una línea se movió desde la foto y ahora asentaría algo, el conteo queda para revisar (`WouldPost`).

**¿"Cerrar los que cuadran" puede dejar algo a medias?**
No. Cada conteo se cierra en su propia transacción: o termina en Concordancia o queda como estaba. Los demás no se afectan.

**¿Por qué la lista "Por revisar" no muestra un conteo que ya terminó el operario?**
Solo muestra los **Contados** (el operario pulsó Terminar). Un conteo Pendiente aparece únicamente con `includeOpen=true` y cuando ya tiene todas sus líneas capturadas.

**La posición que creó el operario no estaba en el sistema, ¿puede seguir contando?**
Sí. La posición provisional se usa de inmediato como línea del conteo y en el inventario. El supervisor la ve con `isProvisional = true`, y la confirma, la corrige o la desactiva.

**¿Una posición provisional con inventario se puede desactivar?**
Se aplica la regla de siempre: una posición con existencia (en mano o reservada) o con tareas abiertas no se desactiva (409 `La posición {código} tiene inventario; no se puede desactivar.`). Muévala primero o confírmela.

**¿Quién ve el origen "Por producto"?**
Los conteos creados con `productPublicIds` y sin posiciones ni zonas llevan `originCode = PRODUCT` ("Por producto"); se puede filtrar con `origins=PRODUCT` en `GET /api/v1/cycle-counts/page`.

## Lote 25 — Conteo abierto con varios productos (servidor, app y web)

Detalle en el [capítulo 06 §6.x](06-inventario-y-almacen.md), el [capítulo 09 §7.2](09-app-almacen.md#72-contar-varios-productos-en-un-conteo-lote-25) y
`docs/lote25-decisiones.md`.

### Mensajes nuevos del servidor

**400 — "El producto {sku} no tiene existencia en ninguna posición del almacén; indique la posición donde lo encontró."** (`errors.binId`)
`POST /cycle-counts/{id}/lines` sin `binId` de un producto que el sistema no tiene en ninguna posición. Mande la posición donde lo encontró.

**400 — "El producto {sku} está en varias posiciones ({códigos}); indique en cuál lo contó."** (`errors.binId`)
Sin `binId` y el producto está en varias posiciones: elija una (la web la elige en el campo Posición; la app, en la lista). `GET /cycle-counts/{id}/product-bins`
dice cuáles son.

**400 — "Crear un conteo vacío (allowEmpty) solo aplica a uno o ningún producto, o a una sola posición, sin otras posiciones, zonas ni categorías."** (`errors.allowEmpty`)
(Antes decía "a un único producto".) `allowEmpty` con más de un producto, o con zonas, posiciones o categorías.

### Preguntas frecuentes

**¿Cómo cuento varios productos en un solo conteo?**
En la app: Conteo → **Por producto** y vaya escaneando; al final **Terminar conteo**. En la web: **Nuevo conteo** → **Por producto** → déjelo sin producto →
**Crear conteo**, y en el detalle escanee cada producto (el que no está en el conteo abre **Agregar lo encontrado** con el producto puesto).

**¿Tengo que elegir la posición?**
No: si el sistema tiene el producto en **una sola** posición se usa esa (se puede cambiar). Con varias hay que elegir; sin ninguna, escanear dónde lo encontró.

**¿El conteo abierto muestra las cantidades del sistema?**
No a ciegas (sin `warehouse.count`): la lista de dónde está el producto no lleva cantidades.

**Escaneé dos veces el mismo producto.**
En la misma posición y lote se abre la línea ya contada para corregir la cantidad; no se suma ni se duplica. En otra posición es otra línea.

## Lote 26 — Convertir un producto a serie (Rentas R0)

Detalle en el [capítulo 06 §2.1](06-inventario-y-almacen.md#21-convertir-a-serie-lote-26-rentas-r0) y `docs/lote26-decisiones.md`.
Endpoint: `POST /api/v1/products/{publicId}/convert-to-serial` (`inventory.manage` + `inventory.adjust`, módulo WMS_LOTSERIAL).

### Mensajes nuevos del servidor

**400 — "Capture {n} número(s) de serie para {bin} (hay {m})."**
En la posición `{bin}` hay `{n}` unidades en mano y la solicitud trae `{m}` series (con `{m}` = 0 si la posición no vino). Capture
exactamente una serie por unidad en cada posición con existencia; vea las posiciones en Inventario → Saldos filtrando por el producto.
Si `{n}` es 0, quitó de la posición una serie que no corresponde: esa posición no tiene existencia del producto.

**409 — "El producto {sku} tiene unidades reservadas; libérelas antes de convertirlo."**
Alguna unidad está apartada (recolección sin empacar, cruce de muelle u otra reserva). Termine o elimine esa recolección (o espere a
que se despache) y vuelva a convertir.

**409 — "El producto {sku} tiene recibos, tareas, recolecciones o conteos abiertos; termínelos antes de convertirlo."**
Esos documentos moverían el producto sin series. Confirme o elimine el recibo, complete o cancele la tarea, empaque o elimine la
recolección, y reconcilie o elimine el conteo; luego convierta.

**422 — "El producto {sku} ya se controla por serie."**
Ya está convertido (o se creó con serie). No hay nada que hacer; capture las series en los movimientos.

**422 — "Solo se convierten a serie productos sin seguimiento; {sku} se controla por lote."**
La herramienta solo convierte productos sin seguimiento. Un producto por lote no se convierte.

**422 — "La existencia de {sku} en {bin} es {cantidad}; ajústela a unidades enteras antes de convertirlo."**
Hay una fracción en esa posición (por ejemplo 1.5) y no se le puede dar una serie a media unidad. Corrija la existencia con un ajuste o
un conteo y vuelva a convertir.

**422 — "La existencia de {sku} en {posición o almacén} no está en una posición sin lote; muévala o ajústela antes de convertirlo."**
Hay existencia sin posición (solo en el almacén) o registrada con lote. Pásela a una posición (transferencia) o corríjala con un ajuste
antes de convertir.

**403 — "Falta el permiso 'inventory.adjust'."**
Convertir mueve inventario: además de `inventory.manage` hace falta `inventory.adjust`. Pida al administrador que se lo dé.

**400 — "El motivo TRACKING_CONVERSION lo asigna el sistema."**
"Conversión a serie" es un motivo que solo usa la herramienta de conversión; un ajuste manual debe usar otro motivo.

### Preguntas frecuentes

**¿Por qué no puedo cambiar el seguimiento a "Serie" en la ficha del producto?**
Porque el producto ya tiene movimientos (409 "No se puede cambiar el tipo de seguimiento de un producto que ya tiene movimientos.").
Use **Convertir a serie**: cambia el seguimiento y da de alta las series por el Kárdex, en una sola operación.

**¿La conversión cambia las cantidades?**
No. En cada posición sale el saldo sin serie y entra una unidad por cada serie, con el motivo "Conversión a serie": el en mano y el
disponible quedan iguales. En el Kárdex verá esos ajustes (neto cero).

**¿Puedo deshacer la conversión?**
No: un producto con movimientos no vuelve a "sin seguimiento". Si se equivocó en un número de serie, corríjalo con un ajuste (baja de la
serie equivocada y alta de la correcta, con nota).

**¿Qué pasa si falla a mitad?**
Nada queda a medias: la conversión es una sola transacción; si algo falla (una serie repetida, una posición que no cuadra) no se escribe
ningún movimiento ni cambia el seguimiento.

## Lote 27 — Rentas R1: renta hasta el despacho, extensiones y cancelación

Detalle en el [capítulo 11](11-rentas.md) y `docs/lote27-decisiones.md`. Endpoints `/api/v1/rentals` (`rental.view` / `rental.manage` /
`rental.extend`, módulo `RENTAL_EQUIPMENT` "Rentas").

### Mensajes nuevos del servidor

**400 — "Indique la localidad del cliente donde estará el equipo."**
La renta necesita la localidad (consignatario) del cliente donde quedará el equipo. Elija una de las localidades del cliente; si no
existe, créela en la ficha del cliente.

**400 — "La localidad no pertenece al cliente de la renta."**
Eligió una localidad de otro cliente. Escoja una del mismo cliente de la renta.

**400 — "La fecha de recogido no puede ser anterior a la de inicio."**
La fecha de recogido (fin de la renta) debe ser el mismo día de inicio o después. Corrija una de las dos.

**400 — "Solo se rentan equipos propios; {sku} pertenece a un cliente."**
Ese producto es mercancía de un cliente 3PL. Solo se rentan equipos de la compañía (sin dueño).

**400 — "El producto {sku} no se controla por serie; solo se rentan equipos con número de serie."**
El producto no tiene seguimiento por serie. Conviértalo con **Convertir a serie** (Lote 26) y vuelva a agregar el equipo por su serie.

**400 — "La tarifa no puede ser negativa."** / **"Indique el monto de la tarifa."** / **"Indique la frecuencia de cobro: DAILY, WEEKLY, MONTHLY o ONE_TIME."** / **"Frecuencia de cobro desconocida: '{x}'. Use DAILY, WEEKLY, MONTHLY o ONE_TIME."**
La tarifa de un equipo lleva frecuencia (diaria, semanal, mensual o "Fija") y un monto de 0 o más. Es solo un dato: no se cobra todavía.

**400 — "Indique el cliente de la renta."** / **"Indique la fecha de inicio de la renta."** / **"Indique la fecha de recogido."**
Faltan datos obligatorios del alta.

**400 — "El cliente de la renta no se cambia; cancele la renta y cree otra."**
El cliente es fijo. Si se equivocó de cliente, cancele la renta (Borrador o Programada) y cree otra.

**400 — "El campo '{campo}' no se puede modificar."**
El número, el estatus, la fecha de recogido pactada, las fechas de despacho y cierre y los enlaces a envío y factura no se editan.

**400 — "Indique el producto del equipo."** / **"Indique al menos un número de serie."** / **"Una renta admite como máximo 200 equipos."**
Cada equipo se agrega con su producto y su número de serie; una renta lleva hasta 200 equipos.

**400 — "La nueva fecha de recogido debe ser posterior a la actual ({aaaa-mm-dd})."**
Una extensión solo mueve la fecha hacia adelante. La fecha entre paréntesis es la vigente: elija un día posterior.

**400 — "Indique el motivo de la extensión."** / **"Indique la nueva fecha de recogido."** / **"El motivo admite como máximo 300 caracteres."** / **"Indique el equipo (lineId) de la tarifa."**
Toda extensión lleva la nueva fecha y el motivo (queda en la bitácora); una tarifa nueva indica a qué equipo aplica.

**400 — "El número de contrato admite como máximo 80 caracteres."** / **"Las notas admiten como máximo 1000 caracteres."** / **"El costo de transporte estimado no puede ser negativo."** / **"Moneda desconocida: '{código}'."** / **"Los días deben ser 0 o más."**
Acorte el texto, use un costo de 0 o más, una moneda del catálogo, o un número de días de 0 o más en el filtro "por vencer".

**404 — "Renta no encontrada."** / **"Localidad no encontrada."** / **"Contacto no encontrado."** / **"Línea no encontrada."**
La renta, la localidad, el contacto o el equipo no existen en su compañía (o el equipo ya se quitó de la renta). Recargue la ficha.

**409 — "El cliente está dado de baja; solo se consulta su historial."**
No se crean, programan ni despachan rentas de un cliente dado de baja. Reactive el cliente o use otro.

**409 — "La serie {s} ya está en la renta {REN-n}."**
La serie está en otra renta abierta (Borrador, Programada o En renta). Use otra serie o quite la serie de esa renta (o cancélela) primero.

**409 — "La serie {s} no está disponible en {posición o almacén}."**
La serie no existe, no está Disponible (reservada, rentada, despachada, dada de baja) o no está en una posición recolectable del almacén
de origen (por ejemplo, en cuarentena o en otro almacén). Búsquela en el rastro de serie y muévala a una posición de picking o reserva
del almacén de la renta.

**409 — "Una de las series se acaba de agregar a otra renta; recargue e intente de nuevo."**
Otra persona agregó la misma serie a otra renta al mismo tiempo. Recargue y elija otra serie.

**409 — "Quite los equipos de la renta antes de cambiar el almacén de origen."**
Los equipos salen del almacén de origen: para cambiarlo, quite primero los equipos y vuelva a agregarlos desde el otro almacén.

**409 — "El almacén {código} ya tiene una zona RENT que no es de rentas; cámbiele el código para poder despachar rentas."** / **"El almacén {código} ya tiene una posición EN-RENTA fuera de la zona En renta; cámbiele el código para poder despachar rentas."**
El primer despacho crea la zona RENT y la posición EN-RENTA; si esos códigos ya los usa otra zona u otra posición, cambie su código en
Almacenes y vuelva a despachar.

**422 — "La renta {n} ya fue despachada; no se puede modificar."**
Después del despacho la renta no se edita ni cambia de equipos o tarifas. La fecha (y la tarifa) cambian con una **extensión**.

**422 — "La renta {n} está cancelada; solo se consulta."**
Una renta cancelada no se edita ni se programa. Cree otra.

**422 — "La renta no tiene equipos; agregue al menos uno."**
Agregue al menos un equipo antes de programar o despachar.

**422 — "Solo se programa una renta en Borrador; la renta {n} no lo está."** / **"Solo se despacha una renta Programada; programe la renta {n} primero."**
El orden es Borrador → Programar → Despachar.

**422 — "Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución."**
Una renta despachada (En renta) termina con su devolución, que llega con el bloque R2.

**422 — "Solo se extiende una renta Programada o En renta."**
En Borrador cambie la fecha con la edición normal; una renta devuelta o cancelada ya no se extiende.

**422 — "La renta ya tiene extensiones; la fecha de recogido se cambia con una extensión."**
Cuando ya hay extensiones, las fechas no se editan: use otra extensión para moverla.

**422 — "La localidad está dada de baja; elija otra."**
Reactive la localidad en la ficha del cliente o elija otra.

**422 — "La posición EN-RENTA está inactiva; no admite movimientos de inventario."**
Alguien desactivó la posición EN-RENTA del almacén. Reactívela en Posiciones para poder despachar.

**403 — sin `rental.view`, `rental.manage` o `rental.extend`** (evento `PERMISSION_DENIED`)
Ver rentas pide `rental.view`; crearlas, programarlas, despacharlas o cancelarlas, `rental.manage`; extenderlas, `rental.extend`. Pida el
permiso al administrador (el Operador de almacén ya los trae).

### Mensajes del inventario con una serie en renta

**409 — "La serie {s} no está disponible en EN-RENTA."** / **"La serie {s} no está disponible en el almacén (no existe, ya salió o está en otra posición o lote)."** / **"La serie {s} ya está en inventario."**
La serie está **En renta**: no se transfiere, no se ajusta, no se recolecta ni se recibe de nuevo por las pantallas normales. Vuelve al
almacén con la **devolución de renta** (R2).

### Preguntas frecuentes

**¿Por qué el producto sigue con las mismas unidades en mano después de despachar la renta?**
Porque el equipo rentado sigue siendo de la compañía (decisión D1): se mueve a la posición EN-RENTA y queda reservado. Por eso el **en
mano** no cambia y el **disponible** baja.

**¿Dónde veo los equipos que están rentados?**
En la posición **EN-RENTA** (zona RENT "En renta") de cada almacén, en las series del producto (estatus "En renta") y en la lista de rentas
filtrando por estatus "En renta".

**¿Qué es una renta "vencida"?**
Una renta Programada o En renta cuya fecha de recogido ya pasó (antes de hoy, en la hora de la compañía). No es un estatus: se calcula
(`isOverdue`). Use `overdue=true` en la lista; "por vencer en N días", `dueWithinDays=N`.

**¿La tarifa se cobra?**
Todavía no: solo se guardan las condiciones (fija o por tiempo, monto y moneda por equipo). El cobro llegará con Facturación.

**¿Necesito que otra persona apruebe una extensión?**
No (D4): basta el permiso `rental.extend`. Queda en la bitácora con quién, cuándo, la fecha anterior y la nueva, el motivo y la tarifa
nueva si cambió.

**Desactivé "Programada" en el pipeline de rentas y ahora no puedo programar.**
Los estatus de la renta se pueden renombrar y reordenar, pero no desactivar: vuelva a habilitarlo (422 `El estatus '{código}' no existe o
no está habilitado para esta compañía.`).

## Lote 28 — Rentas R2: devolución de renta, proceso del equipo devuelto y conteo cíclico (D7)

Detalle en el [capítulo 11](11-rentas.md) (secciones 5, 6 y 7), el capítulo 06 §6.y y `docs/lote28-decisiones.md`. Endpoints
`POST /api/v1/rentals/{publicId}/returns` (`rental.return`), `/api/v1/rental-returns` (`rental.view`) y `/api/v1/rental-processes`
(`rental.view` / `rental.maintenance`; dar de baja también `inventory.adjust`), módulo `RENTAL_EQUIPMENT` "Rentas".

### Mensajes nuevos del servidor

**400 — "Con el motivo 'Otro' describa la devolución en las notas."**
Con el motivo "Otro" las notas de la devolución son obligatorias: escriba qué pasó (o elija otro motivo).

**400 — "La posición de destino no puede ser de la zona En renta."**
El equipo devuelto (o el que termina su proceso) no puede quedar en EN-RENTA: elija una posición de picking, reserva o cuarentena. Si no
indica ninguna, la devolución usa la posición de donde salió el equipo.

**400 — "Indique el motivo de la devolución: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER."** / **"Motivo de devolución desconocido: '{x}'. Use END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER."**
Cada devolución lleva un motivo: fin del contrato, anticipada por daño, anticipada a pedido del cliente u otro.

**400 — "Indique al menos una serie que se devuelve."** / **"Indique el número de serie del equipo devuelto."** / **"El número de serie {s} está repetido."**
Los equipos se devuelven por su número de serie, cada uno una sola vez por devolución.

**400 — "Condición desconocida: '{x}'. Use GOOD, DAMAGED o INCOMPLETE."**
La condición del equipo es Buena (por defecto), Dañado o Incompleto.

**400 — "La fecha de devolución no puede ser futura."** / **"La fecha de devolución no puede ser anterior al inicio de la renta ({aaaa-mm-dd})."**
La fecha es la del día en que volvió el equipo (hoy por defecto, en la hora de la compañía) y no puede ser antes de que empezara la renta.

**400 — "El costo de recogido estimado no puede ser negativo."** / **"Las notas del equipo admiten como máximo 500 caracteres."**
El costo de recogido es un dato (0 o más); las notas de cada equipo, hasta 500 caracteres.

**400 — "La posición de destino debe ser del almacén {código} del proceso."**
Al terminar un proceso el equipo solo se traslada dentro de su almacén. Para llevarlo a otro almacén, termínelo y luego haga una
transferencia normal.

**400 — "Indique el estatus al que pasa el proceso."**
"Avanzar" necesita el estatus destino (por ejemplo `CLEANING` o `REPAIR`).

**403 — "Falta el permiso 'inventory.adjust'."** (al dar de baja)
Dar de baja saca el equipo del inventario: además de `rental.maintenance` hace falta `inventory.adjust`. Pídalo al administrador o pida a
quien lo tenga que lo dé de baja. Sin `rental.return` no se registran devoluciones y sin `rental.maintenance` no se avanza el proceso (403
`PERMISSION_DENIED`).

**404 — "Devolución de renta no encontrada."** / **"Proceso no encontrado."** / **"Posición no encontrada."**
La devolución, el proceso o la posición no existen en su compañía. Recargue la lista.

**409 — "La serie {s} no está en renta en {REN-n}."**
Esa serie no es un equipo despachado y sin devolver de esa renta: es de otra renta, ya se devolvió o nunca salió. Revise la ficha de la
renta (los equipos devueltos muestran su fecha de devolución).

**409 — "La serie {s} está en renta ({REN-n}); registre su devolución antes de reconciliar el conteo."**
En un conteo se capturó una serie que el sistema tiene en el cliente. Si el equipo de verdad volvió, registre la devolución de esa renta y
recapture el conteo; si se capturó por error, quítela de la captura y confirme.

**409 — "Uno de los equipos se acaba de devolver en otra operación; recargue e intente de nuevo."**
Otra persona registró la devolución del mismo equipo al mismo tiempo. Recargue la renta.

**422 — "Solo se registra la devolución de una renta En renta; la renta {n} no lo está."**
Una renta en Borrador o Programada todavía no salió (cancélela si ya no va); una Devuelta o Cancelada ya terminó.

**422 — "El proceso ya terminó; solo se consulta."**
El proceso está en "Lista" o "Dada de baja": ya no se avanza, ni se termina, ni se da de baja otra vez.

**422 — "La posición {bin} es de equipos en renta; no se cuenta."**
La posición EN-RENTA (zona RENT) no se cuenta: lo que está ahí está en los clientes. Cuente las demás posiciones; los conteos de todo el
almacén, por producto o de lo cambiado ya la saltan solos.

**422 — "El estatus '{código}' no existe o no está habilitado para esta compañía."** / **"Salto ilegal: de '{de}' solo se puede avanzar a '{siguiente}'."**
El paso está desactivado en la configuración del proceso, o el salto no está permitido: avance al siguiente paso habilitado, a Reparación
/ Esperando piezas, o termine el proceso.

### Preguntas frecuentes

**Devolví un equipo y el producto sigue "No disponible". ¿Por qué?**
Porque pasó por proceso: queda "En proceso", en mano pero reservado, hasta que el proceso llega a **Lista**. Si no necesitaba revisión,
la próxima vez márquelo sin proceso (`requiresProcess: false`).

**¿Puedo devolver solo uno de los equipos de una renta?**
Sí. Registre la devolución con esa serie (por ejemplo "Anticipada por daño"). La renta sigue En renta hasta que vuelva el último equipo;
con esa devolución pasa a **Devuelta**.

**¿Puedo devolver un equipo a otro almacén?**
Sí: indique una posición de ese almacén (`toBinId`). Lo que no se puede es dejarlo en la zona En renta.

**¿Quién decide si un equipo pasa por proceso?**
Quien recibe la devolución, equipo por equipo (por defecto sí).

**¿Puedo quitar o renombrar pasos del proceso?**
Sí, desde la configuración de estatus (`RentalProcessStatus`): renombrar, reordenar o desactivar Inspección, Limpieza, Pruebas,
Reparación o Esperando piezas. El inventario solo cambia al llegar a **Lista** (se libera) o **Dada de baja** (sale del inventario).

**¿Qué pasa con el equipo dado de baja?**
Sale del inventario con un ajuste de salida con motivo "Daño" y la referencia del proceso; la serie queda "Dada de baja" y no puede volver
a recibirse ni rentarse.

**¿Una devolución anticipada cambia la tarifa o el cobro?**
No: la renta solo guarda las condiciones (D3). La devolución marca si fue anticipada (`isEarly`) para los reportes de rentas.

**¿Por qué no puedo contar la posición EN-RENTA?**
Porque lo que está ahí está físicamente en los clientes (D7). Para corregir un equipo que en realidad volvió, registre su devolución.

## Lote 29 — Rentas R3: reportes, indicadores y aviso de rentas vencidas o por vencer

Detalle en el [capítulo 11](11-rentas.md) (sección 10) y `docs/lote29-decisiones.md`. Fuentes de Análisis `RENTAL`, `RENTAL_RETURN` y
`RENTAL_PROCESS`; vistas "Equipos en renta por cliente", "Rentas por vencer (7 días)", "Rentas vencidas", "Devoluciones de renta por
motivo" y "Equipos en proceso"; indicadores "Rentas por vencer (7 días)" y "Rentas vencidas"; gráfico "Devoluciones de renta por motivo";
aviso `RENTAL_DUE` en "Necesita tu atención". Todo pide `rental.view` y el módulo `RENTAL_EQUIPMENT` "Rentas" encendido.

### Mensajes nuevos o que cambian de motivo

**404 — "Indicador '{id}' no encontrado."** / **"Gráfico '{id}' no encontrado."** / **"Vista '{id}' no encontrado."** (de rentas)
El indicador, gráfico o vista usa una fuente de rentas y usted no tiene `rental.view`, o el módulo **Rentas** está apagado en la compañía.
Pida el permiso al administrador, o que el administrador de la compañía encienda el módulo (Administración › Módulos): al encenderlo
vuelve todo lo que había.

**404 — "Fuente de datos 'RENTAL' no encontrado."** (también `RENTAL_RETURN` o `RENTAL_PROCESS`)
Al crear o previsualizar una vista, indicador o gráfico sobre una fuente de rentas sin `rental.view` o con el módulo Rentas apagado. Mismo
remedio que el anterior.

### Preguntas frecuentes

**¿Dónde veo las rentas que vencen esta semana?**
En el Pulso, en "Necesita tu atención" (una fila por renta vencida o que vence en 7 días, las más vencidas arriba), o en Análisis con la
vista "Rentas por vencer (7 días)". Las ya vencidas, en la vista "Rentas vencidas".

**¿Por qué una renta Programada aparece como vencida?**
Porque su fecha de recogido ya pasó y el equipo sigue apartado para ella. Despáchela y extiéndala, o cancélela si ya no va.

**Registré la devolución y la renta sigue en el aviso. ¿Por qué?**
Porque todavía le queda algún equipo sin devolver (devolución parcial): la renta sigue En renta. Registre la devolución del resto o
extienda la renta.

**¿El aviso usa la hora de Puerto Rico?**
Usa el día de la zona horaria de la compañía (Ajustes de la compañía); por defecto, Puerto Rico. Una renta que se recoge hoy está "por
vencer" hasta la medianoche de la compañía, no la de UTC.

**Apagué el módulo Rentas y desaparecieron sus reportes. ¿Se borraron?**
No. Con el módulo apagado se ocultan; al encenderlo vuelven, con las vistas, indicadores y gráficos que la compañía haya creado sobre
rentas.

**¿Por qué los indicadores de rentas no salen en mi Pulso?**
Vienen apagados porque "Necesita tu atención" ya muestra cada renta vencida o por vencer. Encienda "mostrar en Pulso" en el indicador.

**En la web el aviso dice "RENTAL_DUE" y "Revisar" no abre nada.**
Las pantallas de Rentas todavía no están en la web (bloques F-R1 y F-R2). El servidor ya entrega el aviso con la renta, el cliente, la
fecha de recogido y los días; la web mostrará el texto y la pantalla de la renta cuando lleguen esos bloques.

**¿Cuenta una devolución como "anticipada" si después extendieron la renta?**
"Anticipada" compara la fecha de devolución con la fecha de recogido **vigente** de la renta (la de hoy, con sus extensiones), igual que la
lista de devoluciones.

## Lote 30 — Rentas RM: seguimiento de los productos al migrar (series de Advance Depot)

Detalle en el [capítulo 10](10-migracion-de-datos.md) (sección 6), `docs/migracion/depot-series-y-rentas.md` y `docs/lote30-decisiones.md`.
Son mensajes del comando `import-legacy` (línea de comandos del servidor): el error de configuración termina con código de salida 1; las
advertencias quedan en el reporte y la carga sigue.

### Mensajes nuevos

**"products.trackingType debe ser NONE, LOT o SERIAL." (código de salida 1)**
El JSON de configuración trae en `products.trackingType` un valor que no es `NONE`, `LOT` ni `SERIAL` (por ejemplo `"SERIE"`). No se leyó
ninguna fuente ni se escribió nada. Corrija el valor (mayúsculas o minúsculas da igual) y repita.

**"El ítem {sku} está marcado como de serie pero tiene saldo inicial ({n} unidades) y la fuente no trae sus números de serie; se crea con seguimiento NONE. Conviértalo después con "Convertir a serie"." (advertencia)**
Con `products.trackingFromColumns.serial` encendido (o `trackingType` en `SERIAL`), el ítem pedía serie pero tiene existencia que cargar, y
ni QuickBooks ni el WMS traen los números. Para no dejar existencia "de serie" sin series, se creó sin seguimiento con su saldo. Si es un
equipo de renta, cuente las unidades, anote sus números de serie y use "Convertir a serie" (capítulo 6 §2.1). Aparece también en el mapeo
"Candidato a Convertir a serie".

**"El ítem {sku} está marcado por lote pero tiene saldo inicial ({n} unidades) y la fuente no trae sus lotes; se crea con seguimiento NONE." (advertencia)**
Igual que el anterior, con la casilla `Lot` (opción `trackingFromColumns.lot`). No hay herramienta "Convertir a lote": el producto queda sin
seguimiento. Para Depot no se recomienda encender `lot` (la casilla viene marcada por defecto en casi todos los ítems).

**"El producto {sku} ya existe con seguimiento {actual}; la migración no cambia el seguimiento de un producto existente (en el origen está marcado {pedido}). …" (advertencia)**
La migración nunca cambia el seguimiento de un producto que ya existe (regla D25), ni con `--update`. Si el origen pide SERIAL y el producto
está sin seguimiento, el mensaje termina con "Si es un equipo con número de serie, use "Convertir a serie"." y el producto queda en el mapeo
"Candidato a Convertir a serie": esa es la vía.

### Preguntas frecuentes

**¿Puedo traer de la migración los números de serie de los equipos de Depot?**
No: ninguna fuente los tiene. QuickBooks solo marca por producto si es "de serie" (una casilla), y el WMS anterior no guarda series. Los
números se capturan en Teikem con "Convertir a serie", leyéndolos de cada equipo.

**¿Conviene recrear la base de Depot para que los productos queden con serie?**
No. Los productos con existencia se crearían igual sin seguimiento (no hay series que cargar) y recrear borra todo lo hecho en la base
(rentas, conversiones, ajustes). Use "Convertir a serie" sobre la base actual. La opción del importador solo sirve para crear con serie
los productos **nuevos sin existencia**.

**Encendí `trackingFromColumns.serial`, corrí el dry-run y no cambió nada.**
Es lo esperado sobre una base ya cargada: todos los productos ya existen y no cambian. El reporte le deja la lista "Candidato a Convertir a
serie" para trabajar con R0.

**¿Por qué un producto que en QuickBooks dice Serial quedó "sin seguimiento"?**
Porque tenía existencia y no había números de serie que cargarle (o porque la opción `trackingFromColumns.serial` está apagada, que es lo
normal en Depot y Solutions). Conviértalo con "Convertir a serie".

## Lote A4 — App de almacén: contar por producto

Detalle en el [capítulo 9 §7.1](09-app-almacen.md#71-contar-por-producto-lote-a4) y en `docs/mobile/loteA4-decisiones.md`. Los
mensajes con código HTTP vienen del servidor (Lote 21) y la app los muestra tal cual; los demás son de la app.

### Mensajes nuevos

**"Este producto se cuenta por número de serie; cuéntalo desde la web por ahora."**
El producto se controla por serie y la app todavía no captura series. No se abrió ningún conteo. Cuéntelo desde la web (Conteo
cíclico) o cuente sus posiciones con un usuario de la web.

**"El sistema no tiene existencia de este producto. Si lo encontraste en alguna posición, usa «Otra posición»."**
Ya no es un error: el sistema no tiene existencia de ese producto en el almacén del aparato, y la app abre el conteo **vacío** (el servidor
ya no responde el 400 de "los filtros no seleccionan inventario" a este caso). Toque **Otra posición**, elija la zona, escriba el código
de la posición donde lo encontró (y el lote si lo lleva), escriba la cantidad y **Confirmar**. La posición, si es nueva, queda "pendiente
de revisión" para el supervisor. Si en realidad no lo encontró en ningún lado, toque **Cancelar conteo**.

**"No se puede terminar un conteo vacío: agrega la posición donde lo encontraste con «Otra posición» o cancela el conteo."**
Tocó Confirmar en un conteo por producto que no tiene ninguna fila (producto sin existencia y no agregó ninguna posición). No se mandó
nada. Use **Otra posición** para registrar dónde lo encontró o **Cancelar conteo** si no lo encontró.

**"No se pudo abrir el conteo de ese producto (necesita señal)."**
Abrir el conteo por producto se hace en línea. Busque señal e intente de nuevo; después, anotar y confirmar funcionan sin señal.

**"{n} posiciones en blanco se toman como 0."**
No es un error: es el aviso de la regla "en blanco = 0". Si de verdad no encontró nada en esas posiciones, toque **Confirmar**. Si
le faltó anotar alguna, escriba la cantidad antes de confirmar. Desde el Lote A5 hace falta **al menos una** cantidad escrita: con
todas en blanco, Confirmar avisa "Escribe al menos una cantidad…" (sección Lote A5).

**"Hay cantidades que no son un número; corrígelas para confirmar."**
Algún espacio tiene algo que no es una cantidad (queda marcado en rojo). Corríjalo o bórrelo (en blanco = 0); Confirmar se activa.

**"Ninguna posición coincide con «…»."**
El buscador (aparece con más de 6 posiciones) no encontró ese texto en los códigos de posición ni en los lotes. Borre el texto
para ver toda la lista.

**"La posición … ya está en la lista: escribe la cantidad ahí."**
En "Otra posición" escribió una posición (y lote) que ya tiene su fila en la lista. Vuelva (Cancelar) y escriba la cantidad en esa
fila: mandar la misma línea dos veces haría que el servidor rechazara todo el conteo.

**"Ya existe una posición con ese código en el almacén." (409) y el botón "Usar …, que ya existe"**
La posición que escribió ya existe en el almacén (el aparato todavía no la tenía). Si es donde encontró el producto, toque **Usar …,
que ya existe**: entra a la lista como una fila más (sin "pendiente de revisión", porque no es nueva). Si se equivocó de código,
corríjalo.

**"Este producto lleva lote: escribe el número de lote."**
Para una posición nueva de un producto con lote, el servidor necesita saber de qué lote es lo encontrado. Escriba el número de lote
de la etiqueta; el vencimiento es opcional.

**"No hay zonas para elegir: sincroniza con señal e intenta de nuevo." / "Sin respuesta del servidor: se muestran las zonas guardadas en el aparato."**
"Otra posición" pide las zonas del almacén al servidor; sin respuesta usa las de las posiciones ya descargadas. Si no hay ninguna,
sincronice con señal. Crear la posición también necesita señal.

**"No se pudo crear la posición: necesita señal. Intenta de nuevo cuando haya señal."**
La posición nueva se crea en el servidor en el momento. Lo ya anotado en la lista no se pierde.

**"Indique el código de la posición o su pasillo/rack/nivel/posición." / "El código de la posición solo admite letras, números, guion y guion bajo (máximo 40)." (400)**
Escriba el código (por ejemplo `A-01-02`) o al menos una de sus partes, sin espacios ni símbolos.

**"La zona está inactiva; reactívela primero." / "El conteo ya fue reconciliado; no admite posiciones nuevas." (422), "Zona no encontrada." / "Conteo no encontrado." (404)**
Elija otra zona; si el conteo ya se reconcilió o se canceló en la web mientras contaba, ya no admite posiciones: avise al supervisor.

### Preguntas frecuentes

**¿Qué diferencia hay entre "Por posición" y "Por producto"?**
Por posición: escanea una posición y cuenta todo lo que hay en ella. Por producto: escanea un producto y la app le lista todas las
posiciones (y lotes) donde el sistema dice que está; anota cuánto hay en cada una. La app recuerda la última forma que usó.

**¿Qué pasa con las posiciones que dejo en blanco?**
Cuentan como 0 (no encontró nada ahí). La línea encima de Confirmar dice cuántas son; al confirmar se mandan como 0. Desde el Lote
A5 no se pueden dejar **todas** en blanco: escriba al menos una cantidad (0 si no hay nada).

**¿Por qué no veo cuánto espera el sistema?**
Igual que en el conteo por posición: solo lo ve quien tiene el permiso `warehouse.count`; con `warehouse.count.capture` el conteo es a
ciegas.

**Encontré el producto en una posición que no está en la lista.**
Toque **Otra posición** al final de la lista, elija la zona y escriba la posición (y el lote si lo lleva). Si la posición no existía,
se crea "pendiente de revisión" y el supervisor la confirma o la corrige en la web; usted sigue contando.

**Cerré la app (o se apagó el aparato) a mitad de un conteo por producto.**
Abra Conteo: la lista aparece igual, con lo que ya había escrito, aun sin señal. Termine con Confirmar o cancélelo.

**Abrí el conteo de un producto equivocado.**
Toque **Cancelar conteo** (necesita señal y el permiso `warehouse.count`). Sin ese permiso, avise al supervisor para que lo cancele
desde la web; no lo confirme en 0.

## Lote F12 — Web: "Por revisar", corrección del supervisor y vista previa del conteo

Pantalla: [F12 — Conteo cíclico por producto](frontend/f12-conteo-por-producto.md). Los mensajes con código HTTP vienen del servidor
(Lote 21, sección anterior) y la pantalla los muestra tal cual.

### Mensajes de error que se ven en la pantalla

**¿Qué hago con "El conteo de {sku} en {posición} ({contado}) es menor que lo reservado ({reservado}); libere la reserva antes de reconciliar."? (409)**
En la vista previa sale en la columna Error de esa posición y Confirmar queda deshabilitado ("Hay {n} línea(s) con error; corríjalas
antes de confirmar."). Libere la reserva (o corrija la cantidad si se contó mal) y vuelva a abrir la vista previa.

**¿Qué significa "El registro fue modificado por otro usuario; recargue e intente de nuevo." al confirmar? (409)**
Alguien cambió el conteo (una corrección, una línea nueva) después de que usted abrió la vista previa. La vista previa se recalcula
sola: revísela y confirme de nuevo.

**¿Qué significa "El conteo ya fue reconciliado; solo se consulta." en la vista previa? (422)**
Otro usuario lo confirmó, o lo cerró "Cerrar los que cuadran", mientras usted lo revisaba. Cierre la vista previa: el conteo ya está
en Concordancia o Diferencia.

**¿Qué significa "La posición no está pendiente de revisión." al confirmar una posición? (409)**
Ya estaba confirmada (por usted en otra pestaña o por otro supervisor). No hay nada más que hacer.

### Mensajes que solo ve en la pantalla (sin código HTTP)

**"Ningún conteo de esta página cuadra."**: el botón "Cerrar los que cuadran" está deshabilitado porque ningún conteo visible está
en "Cuadra". Cambie de página o de filtros.

**"Solo se eligen los conteos que cuadran."**: la casilla de un conteo con diferencia, errores o líneas faltantes no se puede marcar.

**"El comentario admite como máximo 500 caracteres."**: el comentario del cierre en bloque es más largo; acórtelo.

**"Hay {n} línea(s) con error; corríjalas antes de confirmar." / "Faltan {n} línea(s) por contar." / "El conteo no tiene líneas."**:
lo que impide confirmar según la vista previa.

**"Ninguna línea falla: todo cuadra contra la existencia actual."**: con "solo las que fallan" no queda ninguna; active "Ver todas".

### Preguntas frecuentes

**¿Por qué "Cerrar los que cuadran" no cerró conteos de otras páginas?**
Cierra solo los que cuadran de la página que se ve (o los que marcó), para que el aviso diga exactamente cuántos y cuáles se cierran.
Pase a la página siguiente o suba "Filas por página".

**¿Corregir una cantidad en la web mueve inventario?**
No. Es una corrección (queda "Corregido a Y" con su nombre y la hora); el inventario se mueve solo al confirmar el conteo, por la
diferencia contra la existencia actual que muestra la vista previa.

**Corregí una línea y ya no falla, pero sigue en la lista. ¿Es un error?**
No: la línea que se corrige en la sesión se queda a la vista para que vea el resultado. Al volver a abrir el conteo ya no aparece en
"solo las que fallan".

**¿Por qué la Varianza dice −2 y el Ajuste dice +1?**
La Varianza compara con la foto (lo que había al crear el conteo); el Ajuste compara con la existencia **actual**, que es lo que se
asentará. Si alguien movió inventario en esa posición después de la foto, los dos números difieren (la vista previa marca "Saldo
cambió").

**¿Por qué no veo la pestaña "Por revisar" ni la vista previa?**
Su usuario cuenta a ciegas (no tiene `warehouse.count`). Pida a un supervisor que revise y confirme.

**¿Puedo crear un conteo por producto desde la web?**
Sí (Lote F13): **Nuevo conteo** → pestaña **Por producto** → almacén y producto → **Crear conteo**. Crea el conteo con una línea por
posición y lote con existencia (origen Producto) y lo abre. También se puede desde la app de almacén ("Contar por producto").

**¿Por qué no encuentro un producto en "Nuevo conteo > Por producto"?**
El switch **Solo con existencia** nace encendido y la lista solo trae los productos con existencia en mano en el almacén elegido
(`GET /api/v1/products?warehousePublicId=…&onlyOnHand=true`). Apáguelo para ver todos los productos activos; si elige uno sin existencia,
el servidor responde 400 `Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.` junto al
selector. El switch no guarda su estado: al reabrir el modal vuelve a nacer encendido.

**"Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano." al crear por producto (400)**
El producto no tiene existencia en mano en ese almacén. El mensaje sale bajo el selector de producto y el modal sigue abierto. Elija
otro almacén o producto, o recíbalo primero. La web no abre conteos vacíos; para contar algo que el sistema cree que no existe use
"Contar por producto" en la app de almacén.

**"Elija el producto." al pulsar Crear conteo en la pestaña Por producto**
Falta elegir el producto de la lista (no basta con escribir el texto). No se envió nada al servidor.

**"La línea ya fue corregida por el supervisor; no se puede volver a capturar." (409)**
Alguien con solo `warehouse.count.capture` (por ejemplo el operario que contó la línea) intentó cambiar una línea que un supervisor ya
corrigió. La corrección no se tocó. Qué hacer: pida al supervisor (quien corrigió, o alguien con `warehouse.count`) que la vuelva a
corregir desde la web. En la app, la operación queda marcada "rechazada" con este mensaje y no se reintenta sola. En la captura en
lote **solo** responde 409 cuando TODAS las líneas del lote están corregidas: el mensaje agrega `Renglón(es) del lote: n (SKU)` y `No se
guardó nada.` (es verdad: nada se guardó). Si el lote traía además líneas libres, no hay error: responde 200, se guardan las libres y la
respuesta trae `skippedLines` con las omitidas (`reasonCode` `CORRECTED_BY_SUPERVISOR`, lo que se mandó y el valor del supervisor). Reenviar el mismo valor que ya tiene la línea no da error. En la app de almacén (Lote A6), Sincronización lo
explica en grande y ofrece "Actualizar el conteo": ver la sección **Lote A6** al final de esta página.

## Lote A5 — App de almacén: al menos una cantidad al contar por producto; en Despacho, la cantidad primero

Decisiones del dueño 4 y 5 del 2026-10-03 (`docs/decisiones-del-dueno-2026-10-03.md`). Detalle en el
[capítulo 9](09-app-almacen.md) (§6 Despacho, §7.1 Contar por producto) y en `docs/mobile/loteA5-decisiones.md`. Ninguno de estos
mensajes viene del servidor (no tienen código HTTP): son de la app y no se manda nada.

### Mensajes nuevos

**"Escribe al menos una cantidad. Si no hay nada de este producto, escribe 0 en una posición."**
Tocó **Confirmar** en un conteo por producto con **todas** las posiciones en blanco. No se mandó nada y el conteo sigue abierto. Qué
hacer: escriba lo que encontró en cada posición; si de verdad no hay nada de ese producto en ninguna, escriba **0** en una posición (las
demás en blanco se toman como 0) y toque Confirmar. Una fila de "Otra posición" con su cantidad también cuenta. Si abrió el producto
equivocado, use **Cancelar conteo** (o avise al supervisor si no tiene `warehouse.count`).

**"Escribe la cantidad primero y luego escanea la posición."**
En **Despacho** escaneó la posición de donde sale el producto sin haber escrito la cantidad. No se agregó ninguna línea (ni con 1).
Qué hacer: escriba la cantidad (el cursor ya está ahí) y vuelva a escanear la posición: la línea se agrega al instante con el aviso
verde "Agregado: …".

**"La cantidad debe ser un número mayor que 0. Corrígela y vuelve a escanear la posición."**
En **Despacho** la cantidad escrita es 0, negativa o no es un número. No se agregó nada. Corrija la cantidad y vuelva a escanear la
posición. Se acepta coma o punto para los decimales.

### Preguntas frecuentes

**¿Por qué ya no puedo confirmar un conteo por producto con todo en blanco?**
Para que un conteo no se cierre en 0 por un toque sin querer (por ejemplo, al abrir un producto por error). Si de verdad no hay nada,
escribir un 0 deja constancia de que se contó.

**En Despacho, ¿dónde quedó el botón "Agregar"?**
Ya no hace falta: con la cantidad escrita, escanear la posición agrega la línea. Si escribe la posición a mano (botón ⌨), **Aceptar**
hace lo mismo que escanear.

**Escaneé la posición, salió el aviso de la cantidad y la posición ya no aparece. ¿Tengo que escanearla otra vez?**
Sí: la posición no se guarda hasta que la línea se agrega, para que nunca quede una línea a medias. Escriba la cantidad y vuelva a
escanearla.

## Lote A6 — App de almacén: captura de conteo rechazada porque el supervisor ya corrigió una línea

Pendiente del cambio 2 de las decisiones del dueño del 2026-10-03 (`docs/decisiones-del-dueno-2026-10-03.md`). Detalle en el
[capítulo 9 §9.1](09-app-almacen.md) y en `docs/mobile/loteA6-decisiones.md`.

### Mensajes nuevos

**"Se guardaron las demás líneas de tu conteo." (app, sin código HTTP; Lote A7)**
Lo muestra Sincronización cuando el servidor respondió 200 al lote de captura pero omitió las líneas que el supervisor ya había corregido
(`skippedLines`). Las demás líneas sí se guardaron y el envío quedó como enviado; no es un error. Debajo salen las no guardadas:
`• SKU · posición (mandaste x → el supervisor dejó y)`. Qué hacer: nada con esas líneas (el supervisor ya tiene su valor); si falta contar
algo, cree un conteo nuevo o pida al supervisor que lo revise. Toque **Actualizar el conteo** para ver cómo quedó y **Descartar este aviso**
cuando ya lo leyó (el aviso no se va solo ni al cerrar la app).

**"La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote: n (SKU). No se guardó nada." (409, en la app)**
Ahora solo ocurre cuando **todas** las líneas del lote que mandó el aparato ya las corrigió el supervisor. En la app, **Sincronización → Con
error** muestra en su lugar la tarjeta roja `El supervisor ya corrigió todas las líneas de este envío.` con `No se guardó ninguna línea de
este envío porque todas ya las corrigió el supervisor.` y los renglones (`• Renglón 2: SKU-1 (mandaste 6)`). Qué hacer: `Si falta contar
algo, crea un conteo nuevo (escanea la posición otra vez) o pide al supervisor que lo revise.` La app no reabre un conteo ya enviado.
Antes, toque **Actualizar el conteo** para ver cómo quedó. Cuando termine, toque **Descartar este envío** (y descarte también el cierre del
mismo conteo si quedó con error).

**"El supervisor ya corrigió todas las líneas de este envío."** (app, sin código HTTP)
Es el título de esa tarjeta (antes decía "una línea de este conteo"): ver el mensaje anterior.

**"El cierre de este mismo conteo también quedó con error: el conteo no se terminó desde este aparato."** (app)
Detrás del lote rechazado la cola mandó el cierre del conteo, y el servidor también lo rechazó (422 `Faltan {n} línea(s)
por contar.` si el conteo seguía Pendiente, porque las cantidades del lote no se guardaron, o `El conteo ya se terminó; puede corregir la
captura o reconciliarlo.` si ya estaba Contado). El cierre no cambió nada en el servidor. Qué hacer: lo mismo que arriba;
la fila del cierre se descarta aparte con su propio "Descartar".

**"Sin señal: no se pudo actualizar el conteo. Inténtalo de nuevo cuando haya conexión."** (app)
Tocó **Actualizar el conteo** sin señal (o el servidor no respondió). No cambió nada; vuelva a tocarlo cuando haya conexión.

**"El conteo {número} sigue abierto (Pendiente)."** (app)
Resultado de **Actualizar el conteo**: el conteo todavía se puede capturar. Debajo se ve el resumen (`{total} líneas: {c} corregidas
por el supervisor, {s} sin contar.`) y cada línea con `Contado: n`, `Sin contar` o `Corregida por el supervisor (nombre) · Contado: n`.
Las líneas corregidas no se vuelven a capturar; pida al supervisor que capture o revise las demás.

**"El conteo {número} ya se terminó de contar (Contado): pide al supervisor que lo revise."** (app)
El conteo ya está Contado (alguien lo terminó). Desde la app no hay nada más que hacer: el supervisor lo revisa y lo reconcilia en la web.

**"El conteo {número} ya fue reconciliado: no admite más capturas. Puedes descartar este envío."** (app)
El conteo ya se cerró y asentó; lo que mandó este aparato ya no puede entrar. Descarte el envío. Si cree que faltó algo, avise al
supervisor para que abra un conteo nuevo.

**"El conteo ya no existe (se eliminó). Puedes descartar este envío."** (app; el servidor respondió 404 `Conteo no encontrado.`)
Alguien eliminó el conteo (o no es de esta compañía). Descarte el envío.

**"No se pudo saber de qué conteo es este envío; pide al supervisor que lo revise."** (app)
La fila de la cola no tiene la ruta esperada (no debería pasar). No hay botón de actualizar; pida al supervisor que lo revise.

**"Lo que dijo el servidor:"** (app)
Si la lista de renglones del mensaje no tiene el formato esperado (por ejemplo, un SKU con paréntesis), la tarjeta no intenta
interpretarla y muestra el mensaje del servidor tal cual.

### Preguntas frecuentes

**¿Se perdió lo que conté?**
No. Desde el Lote A7 las líneas que el supervisor no había corregido se guardan; solo se omiten las corregidas, y el aviso le dice cuáles
(con lo que usted mandó y lo que dejó el supervisor). En el caso residual del 409 (todas corregidas) no se guardó ninguna línea de ese
envío, pero lo que contó sigue en la tarjeta hasta que la descarte.

**¿Por qué no se guardaron las líneas que el supervisor corrigió?**
Para proteger la corrección: solo quien la corrigió o quien tiene `warehouse.count` puede volver a cambiarla. Las demás líneas del mismo
lote sí se guardan (decisión del dueño, segundo bloque, 2026-10-03; antes se rechazaba todo el lote).

**¿Puedo retomar un conteo que ya envié?**
No: el dueño decidió que la app no tiene esa función. Si falta contar algo, cree un conteo nuevo (escanee la posición otra vez) o avise al
supervisor.

**¿"Reintentar" sirve?**
Vuelve a mandar exactamente el mismo lote: falla igual mientras la corrección del supervisor siga ahí. Solo funcionaría si el supervisor
quitó su corrección o la dejó con el mismo número que usted mandó.

**¿"Actualizar el conteo" me muestra las cantidades esperadas?**
No. Solo lo contado vigente y quién corrigió, nunca lo que el sistema esperaba (igual que el conteo a ciegas).

## Lote F14 — Web: códigos de barras de productos y de posiciones para el conteo

Pantalla: [F14 — Códigos de barras para el conteo](frontend/f14-codigos-de-barras.md). Los reportes se arman en el navegador: no hay
mensajes nuevos del servidor (los de lectura de productos y posiciones son los de siempre).

### Avisos que se ven en el reporte o en la pantalla (sin código HTTP)

**"No se pudo generar el reporte. Intente de nuevo."** (toast)
Falló la lectura del listado o el armado del PDF. Intente de nuevo; si se repite, avise a soporte con la hora y los filtros usados.

**"El reporte incluye solo los primeros {n} productos (límite de lectura). Afine los filtros para ver el resto."** / **"…las primeras {n} posiciones…"**
El filtro devuelve más de 10 000 elementos (el mismo tope que Exportar y el Reporte de inventario). Imprima por partes: por categoría,
zona, pasillo o texto.

**"Se usaron {n} columnas porque hay códigos largos que no caben más angostos."** / **"Se usó una sola columna…"**
Algún código no cabe en 3 (o 2) columnas sin bajar de 0.25 mm por barra fina. Es informativo; para tener 3 columnas, deje fuera los
códigos largos con los filtros.

**"No caben como código de barras legible en el ancho de la hoja ({n}); salen en la lista sin código: …"** y el recuadro **"No cabe: demasiado largo para un código legible"**
El valor es tan largo que ni a todo el ancho de la hoja se imprime legible. Ese elemento sale en su grupo pero sin código: teclee el
valor a mano en el lector.

**"Omitidos porque tienen caracteres que el código de barras (Code 128) no admite ({n}): VALOR (no admite: É)"**
El valor tiene acentos, ñ, tabuladores u otros caracteres fuera de ASCII imprimible (los que no se ven se escriben como `U+0009`).
No sale en el reporte; teclee el valor a mano o cambie el código de la posición.

**"Hay valores sin código de barras (no caben u omitidos): vea los avisos al final del reporte."**
Remite a los dos avisos anteriores, que van en la última página.

**"Ningún producto cumple los filtros." / "Ninguna posición cumple los filtros."**
El filtro de la pantalla no deja nada; cambie los filtros.

### Preguntas frecuentes

**¿Qué lleva el código de barras de un producto: su código de barras o su SKU?**
El **SKU exacto**. La app del lector busca el producto por código de barras o por SKU, y el SKU es único y siempre existe.

**¿Por qué el reporte trae productos que no tienen existencia o están inactivos?**
Porque el reporte es exactamente lo que filtra la tabla. Use los indicadores del río (p. ej. *Unidades totales* = activos con existencia)
o los filtros para acotarlo.

**¿Cómo se agrupan las posiciones?**
Por el primer número del código (la primera secuencia de dígitos), comparado como número: 2 antes que 10, y 01 y 1 en el mismo grupo.
Los códigos sin dígitos van a "Otras posiciones" al final.

**¿Cuántos caben por hoja?**
Con códigos cortos, unas 24 posiciones (3 columnas × 8 filas); los productos con descripción larga ocupan un poco más. Con
"2 columnas", menos por hoja pero códigos más anchos.

**El lector no lee la hoja impresa.**
Imprima al 100 % ("Tamaño real", sin "Ajustar a la página"), en negro sobre papel blanco, y pruebe con "2 columnas".

## Productos por posición (informe; reemplaza a las hojas de posición del Lote 23 y del F15)

Detalle en el [capítulo 06 §1.5](06-inventario-y-almacen.md#15-productos-por-posición-informe), la pantalla en
[F15 — Productos por posición](frontend/f15-productos-por-posicion.md) y las decisiones en `docs/lote24-decisiones.md`. El PDF se arma en el navegador.

### Mensajes del servidor

**400 — "Se pueden pedir como máximo 200 posiciones por consulta; use skip para pedir las siguientes."** (`errors.take`)
`GET .../bin-products` con `take` mayor que 200. Pida de 200 en 200: `take=200&skip=0`, luego `skip=200`, `skip=400`… hasta llegar a `total`.
La pantalla nunca pide más de 200, así que no debería verse.

**404 — "Almacén no encontrado."** (`GET .../bin-products`)
El almacén no existe o es de otra compañía.

### Mensajes que se ven en la pantalla (sin código HTTP propio)

**"Son {n}: el PDF será grande y puede tardar un poco. Se imprimen todas; no hay límite."** (aviso, no error)
Desde 2026-10-10 ya **no hay tope** de posiciones por impresión: el aviso solo dice que el PDF será grande. Si prefiere imprimir menos, filtre por zona, por **Pasillo** o por texto en **Posición**, o marque casillas.

**¿Cómo se ve ahora el informe «Productos por posición»?**
En hoja carta, igual que «Códigos de barras»: varias celdas por página, un grupo por posición con su título y el código de barras de cada producto debajo.

**"No hay posiciones en lo que eligió."**
La opción elegida no tiene posiciones (p. ej. *Las posiciones marcadas (0)*). Elija otra opción o cambie los filtros.

**"No hay nada para imprimir: las posiciones elegidas no tienen productos. Active «Incluir posiciones vacías» para imprimirlas."**
Todas las posiciones elegidas están vacías y el interruptor está apagado: no se genera PDF. Enciéndalo si quiere una página *Sin productos* por posición.

**"Impresión cancelada."**
Pulsó **Cancelar** mientras se leían las posiciones. No se descargó nada.

**"No se pudo generar el PDF de productos por posición. {mensaje}"**
Falló la lectura o el armado del PDF. Si el servidor respondió (p. ej. 404 **"Almacén no encontrado."**), su mensaje sale al final. Vuelva a
intentar; si se repite, avise a soporte con la hora y lo que eligió.

**"Se generaron {n} página(s) de {m} posición(es)."** (más *"{k} posición(es) sin productos no se imprimieron."* y *"{j} producto(s) sin código de
barras legible: vea el aviso al pie de su página."*)
Todo salió bien. Las vacías omitidas son las que no tenían productos con el interruptor apagado.

**En la página: "Sin código de barras: Code 128 no admite Ñ"** / **"No cabe: demasiado largo para un código legible"**
Ni el código de barras del producto ni su SKU se pueden imprimir como Code 128 (acentos, ñ…) o son tan largos que no caben legibles. El producto sale
con su SKU y nombre (sin barras) y un aviso al pie de esa página lo lista; búsquelo a mano en el lector.

### Preguntas frecuentes

**¿Para qué sirve?**
Para imprimir los productos de una posición (o de todo un pasillo) con su código de barras y escanearlos desde el papel, sobre todo en racks altos
(hasta 30 pies) donde no se alcanza el producto.

**¿Qué código de barras lleva cada producto?**
El **código de barras del producto** (el de su ficha). Si no tiene, el **SKU**. La app del lector busca el producto por los dos.

**¿Cómo imprimo solo un pasillo?**
Escriba el pasillo en el filtro **Pasillo** (o en **Posición**), pulse **Productos por posición** → *Las posiciones del filtro actual* → **Generar PDF**.

**¿El papel se desactualiza?**
El sistema no lo controla (ya no hay estado de "hoja"): el PDF muestra lo que había al generarlo. Si mueve mercancía, vuelva a generarlo.

**¿Trae cantidades?**
No: dice qué productos hay (SKU, nombre y código de barras), no cuántos.

**Tengo más de 10 productos en una posición.**
La posición sale en varias páginas de 10 (11 productos = 10 + 1), cada una con el encabezado de la posición y *Hoja 2 de 2*. Péguelas juntas.

**¿Las marcas de las casillas se pierden al cambiar de página?**
No: se conservan al cambiar de página o de filtro mientras no cambie de almacén. **Quitar marcas** las borra.

**¿Por qué la exportación de la tabla trae más filas que la página?**
**Exportar** saca **todo lo filtrado** (hasta 10 000 filas), no solo la página que se ve.

## Lote F16 — Web: etiquetas de posición en Posiciones

Pantalla: [F16 — Etiquetas de posición](frontend/f16-etiquetas-de-posicion.md). El servidor no cambió: se lee el listado de posiciones
(`GET /api/v1/warehouses/{almacén}/bins`) y el PDF se arma en el navegador. Si el servidor responde con error, su mensaje sale tal cual al
final de *"No se pudieron generar las etiquetas."*.

### Mensajes que se ven en la pantalla (sin código HTTP propio)

**"Son {n}: el PDF será grande y puede tardar un poco. Se imprimen todas; no hay límite."** (aviso, no error)
Desde 2026-10-10 ya no hay tope de 500 etiquetas por PDF: se imprimen todas las que pida. **Generar PDF** sigue habilitado.

**"No hay posiciones en lo que eligió."**
La opción elegida no tiene posiciones (p. ej. *Las posiciones del filtro actual (0)*). Cambie los filtros o marque posiciones.

**"No hay posiciones para imprimir con lo que eligió."**
Al leer, ya no había posiciones (alguien las dio de baja mientras tanto). Recargue la lista y vuelva a intentar. No se genera PDF.

**"Se canceló; no se generó el PDF."**
Pulsó **Cancelar** mientras se leían las posiciones. No se descargó nada.

**"No se pudieron generar las etiquetas. {mensaje}"**
Falló la lectura o el armado del PDF. Si el servidor respondió (p. ej. 404 **"Almacén no encontrado."** o un 400 de un filtro), su
mensaje sale al final. Vuelva a intentar; si se repite, avise a soporte con la hora y lo que eligió.

**"Se generaron {n} etiquetas de 4 × 2 pulgadas."** (o *"Se generó 1 etiqueta de …"*)
Todo salió bien; el PDF se descargó con una página por posición.

**"{n} salieron sin código de barras (solo con el código en texto):"** seguido de *"No caben como código de barras legible en el ancho de
la hoja…"* y/o *"Omitidos porque tienen caracteres que el código de barras (Code 128) no admite…"*
El PDF ya se descargó; esas etiquetas llevan el código en texto y, en el lugar de las barras, *"No cabe: demasiado largo para un código
legible"* (más de ~30 caracteres en 4 pulgadas con la barra mínima de 0.25 mm) o *"Sin código de barras: Code 128 no admite Ñ"*. Si
necesita código de barras, renombre la posición con un código más corto o sin acentos. Pulse **Listo** para cerrar.

### Preguntas frecuentes

**¿En qué se diferencia de "Productos por posición"?**
La **etiqueta** identifica la posición (su código) y no cambia con lo que hay en ella. **Productos por posición** (F15) lista los productos que
tiene cada posición, con su código de barras, para escanearlos desde el papel.

**¿Cómo reimprimo una etiqueta?**
Igual que la primera vez: márquela (o filtre) → **Etiquetas de posición** → **Generar PDF**. No hay nada que "desmarcar" antes.

**¿Qué tamaño elijo?**
El del rollo de su impresora: 4 × 2 pulgadas (10 × 5 cm), 4 × 4 (10 × 10 cm) o 4 × 6 (10 × 15 cm). Cada página del PDF mide exactamente
una etiqueta de ese tamaño. Configure el mismo tamaño en el driver e imprima al 100 %.

**La etiqueta sale de lado o cortada.**
Revise que el tamaño de etiqueta del driver sea el mismo que eligió y que la escala sea 100 % ("Tamaño real"). Si sigue, genere el PDF con
**Orientación → Girar 90°** (gira la página completa, para impresoras que alimentan la etiqueta de lado).

**¿Por qué no salen todas las posiciones del almacén?**
Salen las del **filtro actual** de la tabla (todos los filtros, también el texto de **Posición**) o las **marcadas**. Quite los filtros
(**Limpiar**) para todas (sin tope).

**¿Las marcas se quitan al imprimir etiquetas?**
No: se quedan por si quiere reimprimir. **Quitar marcas** las borra.

**¿Recuerda el tamaño que usé?**
Sí, en ese navegador: la próxima vez el modal abre con el último tamaño y la última orientación.

## Lote A8 — App de almacén: lo que hay en una posición (Consultar)

Detalle en el [capítulo 9 §8.1](09-app-almacen.md#81-lo-que-hay-en-una-posición-lote-a8) y en `docs/mobile/loteA8-decisiones.md`. Ninguno de
estos mensajes trae código HTTP propio: los arma la app (si el servidor responde un error, sale su mensaje tal cual).

### Mensajes nuevos o cambiados

**"No hay un producto ni una posición con ese código."** (antes "No hay nada con ese código.")
Lo escaneado no es un producto sincronizado, no es una posición del almacén del aparato y la búsqueda libre (SKU, nombre, código de
barras, lote) no encontró nada. Qué hacer: revise que la etiqueta sea de este almacén (el aparato consulta su almacén por defecto) y que
se haya leído completa; si es un producto nuevo, sincronice con señal y vuelva a escanear.

**"No hay productos en esta posición."**
La posición existe y el sistema no tiene nada en ella (sin existencia en mano ni reservada). No es un error. Si usted ve producto ahí,
avise al supervisor o cuéntela (Conteo → Por posición).

**"La posición {posición} está desactivada."**
La posición existe pero está dada de baja. Escanee otra; si debería estar activa, avise al supervisor (se reactiva en la web, pestaña Posiciones de la
ficha del almacén).

**"Ningún producto coincide con «{texto}»."**
Lo escrito en **Buscar producto o lote** no coincide con el SKU, el nombre ni el lote de ningún producto de la lista. Borre o cambie el
texto.

**"Se muestran los primeros 1,000 renglones de esta posición; el resto, en la web."**
La posición tiene más de 1,000 renglones de saldo (producto × lote): caso muy raro. La lista muestra los primeros; vea la posición
completa en la web (Inventario, pestaña Saldos, filtrando por la posición).

### Preguntas frecuentes

**Escaneo la posición y no veo cantidades. ¿Está mal?**
No: las cantidades del sistema solo las ve quien tiene el permiso `warehouse.count` (la misma regla que el conteo a ciegas). Si su trabajo
las necesita, pida al administrador que se lo dé; la app lo toma la próxima vez que abra Consultar con señal.

**Me dieron (o me quitaron) `warehouse.count` y la app sigue igual.**
La app pregunta los permisos al abrir **Consultar** con señal. Salga a Inicio y vuelva a entrar a Consultar con señal.

**La lista no coincide con la hoja pegada en el rack.**
La lista es lo que el sistema dice **ahora**; la hoja es de cuando se imprimió. Si la hoja está vieja, pida que la reimpriman (web, Almacén →
Posiciones → **Imprimir las desactualizadas**). Si lo que ve físicamente no coincide con la lista, cuente la posición.

**Toco un producto de la lista y no pasa nada.**
Es a propósito: Consultar solo muestra. Para ver ese producto en todo el almacén, escanee (o escriba con ⌨) su código.

**¿Funciona sin señal?**
Solo con una posición que ya consultó antes en el aparato: muestra esa lista con el aviso "Datos de las {hora} (hace {N} min, sin señal
ahora)". La primera vez necesita señal.

## Lote A9 — App de almacén: pantalla chica (PIN, teclado en pantalla y calculadora)

Detalle en el [capítulo 9 §2.2 y §7.3](09-app-almacen.md#73-calculadora-de-cantidad-2026-10-05) y en `docs/mobile/loteA9-decisiones.md`. Este lote
**no agrega ni cambia mensajes**: solo cambia cómo se ven y se mueven las pantallas. El único texto nuevo es el nombre del botón de volver de la
calculadora para el lector de pantalla (**"Cantidad directa"**, el mismo texto que tenía el enlace).

**En la pantalla del PIN no veo completos "Volver" y "Entrar".**
Deslice la pantalla hacia arriba: la pantalla del PIN se desplaza. En aparatos bajos las teclas se achican para que todo quepa; si aun así no
cabe (por ejemplo con la letra del sistema muy grande), deslice.

**Toco "Sueltas" (o cualquier campo) con el teclado en pantalla y el teclado lo tapa.**
La pantalla debe subir sola hasta dejar el campo encima del teclado. Si no sube, deslice la pantalla hacia arriba con el teclado abierto: el
espacio de abajo permite llevar el campo arriba. Si pasa siempre en su aparato, avise a soporte con el modelo y la versión de Android (es un
punto de la lista de comprobación del lote).

**¿Dónde quedó "Cantidad directa" en la calculadora?**
Ahora es el botón con la **flecha ←**, arriba a la izquierda, junto al título "Calculadora". Hace lo mismo: vuelve al campo de la cantidad con
el total ya puesto. En la calculadora de una fila (conteo de un producto en todas sus posiciones) la flecha cierra sin cambiar la fila; para
pasar el total use **Usar {total}**.

**La calculadora de una fila ya no sale en una ventana.**
Es a propósito: ahora ocupa la pantalla en el mismo lugar (como "Otra posición"), para que el teclado no tape los campos. Al terminar
(**Usar {total}**, **Cancelar** o la flecha ←) vuelve a la lista.

**No encuentro el botón de la calculadora.**
Es el cuadro **azul** con el dibujo **blanco** de una calculadora, a la derecha del campo de la cantidad (y en cada fila del conteo de un
producto en todas sus posiciones).

## Lote F17 — Web: Rentas (lista, ficha, alta con equipos por serie, extensión) y "Convertir a serie"

Pantalla: [F17 — Rentas (web) y "Convertir a serie"](frontend/f17-rentas.md). El servidor no cambió; sus mensajes (capítulo 11 §9 y
capítulo 06 §2.1) se muestran **tal cual**, bajo su campo o arriba del formulario o del diálogo. Para no esperar al servidor, la web revisa
antes de enviar con **el mismo texto** los que siguen.

### Mensajes que la web revisa antes de enviar (mismo texto que el servidor)

**"Indique el cliente de la renta."** / **"Indique la localidad del cliente donde estará el equipo."** / **"Indique la fecha de inicio de
la renta."** / **"Indique la fecha de recogido."** (400 del servidor)
Falta ese dato en **Nueva renta**. La localidad se elige después del cliente (solo sus localidades propias).

**"La fecha de recogido no puede ser anterior a la de inicio."** (400)
Corrija la fecha de recogido (puede ser el mismo día del inicio).

**"El costo de transporte estimado no puede ser negativo."** / **"El número de contrato admite como máximo 80 caracteres."** / **"Las notas
admiten como máximo 1000 caracteres."** (400)
Corrija el dato marcado.

**"El producto {sku} no se controla por serie; solo se rentan equipos con número de serie."** (400)
El equipo elegido no tiene seguimiento por serie. Conviértalo con **Convertir a serie** en la ficha del producto y vuelva a elegirlo.

**"Solo se rentan equipos propios; {sku} pertenece a un cliente."** (400)
El producto es de un cliente 3PL; solo se rentan equipos de la compañía.

**"Indique la frecuencia de cobro: DAILY, WEEKLY, MONTHLY o ONE_TIME."** / **"Indique el monto de la tarifa."** / **"La tarifa no puede ser
negativa."** (400)
La tarifa está a medias: elija la frecuencia (Diaria, Semanal, Mensual o Fija) y un monto de 0 o más, o deje los dos vacíos (sin tarifa).

**"La nueva fecha de recogido debe ser posterior a la actual ({aaaa-mm-dd})."** / **"Indique el motivo de la extensión."** / **"El motivo
admite como máximo 300 caracteres."** (400)
Al **Extender**: la nueva fecha debe ser posterior al recogido vigente y el motivo es obligatorio.

**"Capture {n} número(s) de serie para {posición} (hay {m})."** (400)
En **Convertir a serie**, la caja de esa posición tiene {m} series y la posición tiene {n} unidades en mano. Escriba una serie por unidad.

**"El número de serie {s} está repetido."** (400) / **"El número de serie {s} excede 80 caracteres."** (400) / **"Una línea admite como
máximo 500 números de serie."** (400) / **"Las notas admiten como máximo 300 caracteres."** (400)
Corrija las series (no se repiten, ni entre posiciones ni con otras mayúsculas) o la nota.

**"El producto {sku} tiene unidades reservadas; libérelas antes de convertirlo."** (409)
Sale al abrir **Convertir a serie** si alguna posición tiene reservado. Empaque o elimine la recolección (o lo que reserve) y vuelva a
intentar.

**"La existencia de {sku} en {posición} no está en una posición sin lote; muévala o ajústela antes de convertirlo."** / **"La existencia de
{sku} en {posición} es {cantidad}; ajústela a unidades enteras antes de convertirlo."** (422)
La existencia no se puede convertir así: muévala a una posición sin lote o ajústela a unidades enteras.

### Mensajes solo de la web

**"Elija el almacén de origen."**
Nueva renta sin almacén de origen: la web lo pide para ofrecer las series disponibles de ese almacén.

**"La serie {s} ya está en esta renta."**
Escaneó (o escribió y Enter) una serie que ya eligió o que ya está en la renta.

**"La serie {s} no está disponible en {almacén}."**
La serie escaneada no está disponible en el almacén de origen (no existe, está reservada, en renta, en cuarentena o en otro almacén). Es el
mismo texto del 409 del servidor.

**"Una renta admite como máximo 200 equipos."**
Agregar esos equipos pasaría del tope de 200 por renta.

**"Elija al menos un equipo para la tarifa nueva."**
En **Extender** puso tarifa nueva y quitó todos los equipos de "Equipos con la tarifa nueva".

**"El comentario admite como máximo 500 caracteres."** / **"Escriba un número válido."**
Acorte el comentario de la confirmación o corrija el costo de transporte.

**"Su usuario no puede consultar localidades (permiso locations.read y módulo Catálogo)."** / **"Su usuario no puede consultar los
contactos del cliente (permiso clients.read)."** / **"Su usuario no puede consultar clientes."**
Para crear una renta hace falta consultar clientes y sus localidades (módulo Catálogo). Pida esos permisos al administrador.

### Preguntas frecuentes

**¿Dónde están las rentas en la web?**
Almacén → **Rentas** (antes del Kárdex), con el módulo Rentas encendido y `rental.view`.

**¿Por qué no me deja elegir un equipo?**
El selector ofrece solo productos **propios con serie** que tengan disponible en el **almacén de origen**, y de ellos las series
**disponibles** en zonas que se rentan (no cuarentena ni cruce de muelle) que no estén ya en la renta. Si el producto no tiene serie,
conviértalo primero.

**¿"Vencida" es un estatus?**
No: es un dato calculado con el día de la compañía. La lista lo muestra en la columna **Vencimiento** y se filtra con "Vencen en (días)" y
"Solo vencidas".

**Al programar o despachar sale un error. ¿Qué hago?**
El diálogo se queda abierto con el mensaje exacto del servidor (capítulo 11 §9): por ejemplo, *"La renta no tiene equipos; agregue al menos
uno."* (agregue equipos) o *"La serie {s} no está disponible en {posición}."* (la serie se movió o se reservó: quítela y agregue otra).

**¿"Convertir a serie" cambia mi inventario?**
No en cantidades: es un **movimiento neto cero** (salida de las unidades sin serie y una entrada por serie, con el motivo "Conversión a
serie"). Lo que cambia es que el producto, desde entonces, se controla por serie.

## Lote F18 — Web: devoluciones de renta, proceso de equipos y reportes de rentas

Pantalla: [F18 — Devoluciones de renta, proceso de equipos y reportes de rentas](frontend/f18-devoluciones-y-proceso-de-rentas.md). El
servidor no cambió; sus mensajes (capítulo 11 §9, "Devolución, proceso y conteo") se muestran **tal cual**, bajo su campo o arriba del
formulario o del diálogo, que no se cierra.

### Mensajes que la web revisa antes de enviar (mismo texto que el servidor)

**"Indique el motivo de la devolución: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER."** (400)
Elija el **Motivo** en Registrar devolución (Fin del contrato, Anticipada por daño, Anticipada a pedido del cliente u Otro).

**"Con el motivo 'Otro' describa la devolución en las notas."** (400)
Con el motivo Otro, escriba en **Notas** qué pasó.

**"La fecha de devolución no puede ser futura."** / **"La fecha de devolución no puede ser anterior al inicio de la renta ({aaaa-mm-dd})."** (400)
Corrija la **Fecha de devolución** (hoy por defecto, en el día de la compañía).

**"El costo de recogido estimado no puede ser negativo."** (400)
Deje el costo vacío o en 0 o más.

**"Indique al menos una serie que se devuelve."** (400)
Marque al menos un equipo en **Equipos que vuelven**.

**"Las notas admiten como máximo 1000 caracteres."** / **"Las notas del equipo admiten como máximo 500 caracteres."** (400)
Acorte las notas de la devolución o del equipo.

**"Indique el estatus al que pasa el proceso."** (400)
En **Avanzar**, elija el estatus en "Pasa a".

### Mensajes del servidor que se ven en estas pantallas

**"La serie {s} no está en renta en {REN-n}."** (409)
El equipo ya se devolvió (otra persona lo registró) o no es de esa renta. Cierre el diálogo: la ficha se recarga con lo que queda por devolver.

**"Uno de los equipos se acaba de devolver en otra operación; recargue e intente de nuevo."** / **"El registro fue modificado por otro
usuario; recargue e intente de nuevo."** (409)
Alguien cambió la renta o el proceso al mismo tiempo. Cierre el diálogo, recargue y vuelva a intentar.

**"La posición de destino no puede ser de la zona En renta."** (400)
Elija otra posición de destino (la web no ofrece la zona En renta; puede salir si se escribe el código de EN-RENTA).

**"La posición de destino debe ser del almacén {código} del proceso."** (400)
Al **Completar**, el traslado es solo dentro del almacén del proceso; para llevarlo a otro almacén use una transferencia después.

**"Salto ilegal: de '{de}' solo se puede avanzar a '{siguiente}'."** / **"Desde el lateral '{x}' solo se puede regresar a '{paso}' o
avanzar a '{siguiente}'."** / **"El estatus '{código}' no existe o no está habilitado para esta compañía."** (422)
En **Avanzar**, elija el paso que corresponde (el marcado "(siguiente)", o Reparación / Esperando piezas).

**"El proceso ya terminó; solo se consulta."** (422)
El proceso llegó a Lista o Dada de baja (quizá en otra pestaña). Recargue la cola: la fila queda atenuada, solo con Historial.

**"Falta el permiso 'inventory.adjust'."** (403)
Dar de baja también es un ajuste de inventario. Pida `inventory.adjust` al administrador.

**"Solo se registra la devolución de una renta En renta; la renta {n} no lo está."** (422)
La renta ya está Devuelta (o nunca se despachó). Recargue la ficha.

### Mensajes solo de la web

**"Escriba la serie {s} para confirmar la baja."**
En **Dar de baja**, escriba la serie del equipo (sin importar mayúsculas) para confirmar: la baja no se deshace.

**"Escriba un número válido."**
El costo de recogido no es un número.

**"Dar de baja exige además el permiso inventory.adjust (ajustes de inventario), que su usuario no tiene: pídalo a un administrador."**
Nota de la cola de proceso cuando su rol puede avanzar y completar (`rental.maintenance`) pero no dar de baja.

### Preguntas frecuentes

**¿Dónde registro que volvió un equipo rentado?**
En la ficha de la renta (En renta) → **Registrar devolución**. No use un recibo de devolución normal: el equipo nunca salió del inventario.

**¿Puedo devolver solo un equipo?**
Sí: desmarque los demás. La renta sigue En renta hasta que vuelve el último.

**Devolví un equipo y no aparece disponible.**
Pasó por proceso. En **Proceso de equipos** avance sus pasos y termínelo con **Completar** (queda Lista y disponible).

**¿Dónde veo qué equipos llevan más días en revisión?**
En **Proceso de equipos** ordene por "Días en proceso", o en Reportes de rentas la vista "Equipos en proceso".

**¿Los indicadores de rentas salen en el Pulso?**
Vienen apagados (el aviso de "Necesita tu atención" ya muestra cada renta vencida o por vencer). Enciéndalos en Análisis → Indicadores
con "Mostrar en mi Pulso"; mientras tanto se ven en **Reportes de rentas**.

## Acomodo repartido en varias posiciones (tarea 24)

**Acomodé 9 posiciones de 20 y me quedaron 5 sueltos. ¿Dónde están?**
Si escaneó una décima posición, esa recibió los 5 (alerta fija `{bin} recibe solo 5 (lo que quedaba), no 20.`). Si confirmó con solo las 9, los 5 son una
tarea de acomodo nueva en la lista de Acomodar: ábrala y acomódela en una posición.

**Me dice `Ya no hay unidades por acomodar: los 185 están repartidos. Confirma el reparto.` al escanear una posición más.**
Es lo esperado: con 185 y 20 por posición caben 9 de 20 y una décima con 5; ya no queda nada por repartir. Confirme el reparto.

**Me salió una alerta fija arriba, ¿qué hago?**
Es el aviso de que esa posición recibe solo lo que quedaba (por ejemplo 5 y no 20). Se queda a la vista aunque desplace la pantalla; ciérrela con la ✕ cuando la haya leído.

**Escaneé una posición equivocada en el reparto.**
Toque la **✕** del renglón de esa posición (`Quitar {bin}`); las cantidades de las demás se recalculan. Nada se ha movido hasta **Confirmar reparto**.

**Confirmé el reparto y una posición era de otro almacén (`Posición no encontrada.`).**
No se movió nada: el reparto es todo o nada. Escanee las posiciones correctas y confírmelo de nuevo.

**En el recibo directo repartí 45 de 20 en 20 y me quedaron 5 sin ubicar.**
Escanee una tercera posición: recibe los 5 (con la alerta fija). Si confirmó solo con 2, la captura sigue con `5` y el campo de posición: escanee dónde quedan
(o toque Cancelar para descartarlos de este recibo; las 2 líneas ya agregadas se quedan).

**Repartir en un recibo con aviso me muestra el conflicto de posición.**
Con aviso u orden de compra cada línea entra a una sola posición (`Ya se capturó {sku} con destino {bin}; en un recibo con aviso u orden de compra cada línea entra a una sola posición.`). Para repartir un producto en varias posiciones use un recibo ciego o de devolución.

**En Despacho pedí 50 y me sale "20 de P-01 y 30 de R-02". ¿Puedo cambiar de dónde saco?**
Sí: toque **Cambiar** en el renglón y escanee la posición de donde sacará. Se acepta si tiene existencia de ese producto; si no, dice
`{bin} no tiene {qty} disponibles de este producto (hay {have}). Escanea otra posición.`

**En Despacho no me sale el plan de salida.**
Solo sale cuando la cantidad no cabe en una posición y el producto **no** es por lote (con lote la posición la manda el vencimiento). Sin señal y sin
copia del orden de salida en el aparato tampoco hay sugerencia.

## Buscar en la lista (tarea 26)

**Toco "Buscar en la lista" y me dice `No hay coincidencias en este aparato. Sincroniza si falta algo.`**
El buscador mira lo que el aparato ya bajó. Si el producto o la posición son nuevos, toque **Sincronizar ahora** en Inicio y busque de nuevo; si no existen en
el sistema, se crean en la web.

**¿Elegir de la lista es lo mismo que escanear?**
Sí: entrega el código al campo igual que un escaneo y pasa por las mismas validaciones (por ejemplo, en Despacho con producto por lote la posición sigue
siendo la del lote que vence primero).

## Conteo informado al capturar (tarea 25)

**Cuento en la app y me dice `No coincide con lo esperado. Vuelve a contar y acepta de nuevo.` ¿Cuánto era lo esperado?**
No se dice a propósito: así el recuento es una cuenta real. Vuelva a contar y acepte; al segundo intento la línea se cierra y, si la compañía muestra el número, verá `Contaste {n} y se esperaba {m}`.

**Me dice `Esa línea ya se verificó; no se puede cambiar ni quitar.`**
La línea ya coincidió o ya se recontó. Su cifra quedó fija; si hay un error real, el supervisor la corrige en la web (Conteo cíclico → Por revisar).

**El servidor me responde `La cantidad no es la que se verificó en la línea; no se puede cambiar después de ver el resultado.` al enviar el conteo.**
Se intentó guardar una cantidad distinta de la que se verificó. Cierre ese conteo y avise al supervisor; solo quien tiene el permiso Contar puede corregir la línea.

**No me aparece ninguna verificación al contar.**
Revise (1) que haya señal, (2) que la compañía no esté en «Nadie» (Sistema → Ajustes → Operación), (3) que su usuario esté marcado «Sí» en Usuarios si la compañía está en «Solo los marcados» (o no esté en «No» si está en «Todos») y (4) que no tenga el permiso Contar (el supervisor ve lo esperado desde el inicio). Sin señal o sin permiso la app captura sin verificar.

**Me sale `No está habilitado ver lo esperado al contar.`**
La compañía o su usuario no tienen habilitado ver lo esperado. Pida al administrador que lo marque en Sistema → Usuarios («Ve lo esperado al contar: Sí») si corresponde.

**¿Cómo hago que todos los contadores vean el resultado al capturar?**
Sistema → Ajustes → Operación → «¿Quién ve lo esperado al contar?» = **Todos**, y que ningún contador tenga el permiso Contar (solo «Capturar conteo»). Para un grupo: **Solo los marcados** y «Sí» a esas personas.

**Escribí un margen y no lo acepta (`El margen de reconteo debe estar entre 0 y 100 %.`).**
El margen es un porcentaje de 0 a 100 con hasta 2 decimales.

**Soy supervisor y con la compañía en «Nadie» la app me cuenta a ciegas.**
Es lo esperado: «Nadie» cierra lo esperado al contar en la app para todos, también para quien tiene el permiso Contar (por ejemplo, durante una auditoría). Para reconciliar, la web sigue mostrando lo esperado. Cambie el ajuste en Sistema → Ajustes → Operación si quiere que el supervisor lo vea.

**En el reparto me sale `Cupo para 15: recibirá 20. Se puede confirmar igual.`**
Esa posición tiene un cupo máximo configurado (Ubicaciones) y, con lo que ya tiene, solo le caben 15; el reparto le asigna 20. Es un aviso: se puede confirmar igual (el cupo no bloquea). Si no quiere pasarse, quite esa posición con su ✕ y use otra, o baje la cantidad por posición. Sin señal o sin cupo configurado no se muestra el aviso.

## Quitar líneas (recibo y despacho)

**Agregué un producto que no era en un recibo (o en un despacho) de la app. ¿Cómo lo quito?**
En la lista de líneas, toque **Quitar** junto a esa línea y confirme con **Quitar** en el aviso `¿Quitar esta línea?`. Las demás líneas no cambian. Aplica mientras el recibo o el despacho siguen **abiertos en el aparato**; una vez confirmado/enviado ya no se edita desde la app.

**El recibo ya se confirmó y se recibió algo que no era.**
Desde la app no se deshace. En la web, un recibo sin documento (ciego o devolución) abierto permite quitar una línea con la papelera; las líneas que vienen del aviso u orden de compra **no se quitan** (se captura 0 en lo recibido) y las extra sí (`Quita una línea extra (solo abiertos)`). Un recibo ya confirmado se corrige con un ajuste de inventario.

## Calculadora de cantidad: fondo y conteo de la web (2026-10-07)

**¿Qué es el «Fondo» de la calculadora?**
Cuántas hay una detrás de otra. Una estiba de 5 filas × 3 columnas con 2 de fondo son 5 × 3 × 2 = 30. Es opcional: en blanco vale 1. Para estibas distintas use **+ otro bloque**, cada uno con su fondo.

**La calculadora dice `Escriba números enteros en filas, columnas y fondo (el fondo, 1 o más) y un número en sueltas.`**
Filas, columnas y fondo solo admiten números enteros (el fondo, 1 o más); las sueltas pueden tener decimales. Revise que no haya letras, decimales ni un 0 en el fondo.

**¿Dónde está la calculadora en el conteo de la web?**
Conteo cíclico → elija el conteo → en cada línea el botón **∑** (o «Calculadora» dentro de la ventana de Cantidad contada). Las líneas con serie se cuentan por lista de números de serie y no la llevan.

## Reparto por posición en la web (2026-10-07)

**¿Cómo reparto 185 unidades de 20 en 20 en la web?**
Acomodar: al completar la tarea active «Repartir por posición», escriba 20 y elija las posiciones (9 completas y una décima con 5). En un recibo directo abierto use **Repartir** en la línea. Una posición de más recibe el resto y no se admiten más.

**Sale `Con {per} por posición caben {max} posición(es) para {pending}; no hay más unidades por repartir.`**
Eligió más posiciones de las que caben. Quite las filas sobrantes (✕) o suba la cantidad por posición.

**En el despacho dice `Elija el producto y escriba la cantidad para sugerir de dónde sacarla.`**
La acción **Sugerir posiciones** necesita producto y cantidad en la línea y que la posición esté vacía.

**Dice `No alcanza la existencia: faltan {short}. Baje la cantidad.`**
La existencia disponible del almacén (y del lote, si lo indicó) es menor a lo pedido; revise el saldo o baje la cantidad.

**El reparto del recibo se quedó a medias.**
Se guarda línea por línea; revise las líneas del recibo, corrija la que falló y repita **Repartir** sobre el resto.

## Sesión, bloqueo y compañías de la app (2026-10-07)

**Abrí la app y me pide el PIN aunque ya había entrado.**
Es a propósito: al abrir la app siempre se pide el PIN del usuario que dejó la sesión (así quien llegue detrás no usa su sesión). Lo capturado no se pierde. Si no es su usuario, toque «Cerrar sesión».

**¿Para qué sirve el candado?**
Bloquea al instante sin cerrar la sesión: para dejar el aparato un momento. Con el PIN sigue en la misma pantalla de Inicio.

**Una compañía quedó en la lista y da error; no la puedo sacar.**
Si el servidor ya no reconoce el aparato (`El aparato no está registrado o fue desactivado.`, 401), la app la quita sola al intentar entrar. Si prefiere quitarla antes, deslice su fila a la izquierda → «Quitar».

**Al quitar una compañía dice que tiene capturas sin sincronizar.**
Esas capturas viven solo en este teléfono; si confirma, se pierden. Cancele, sincronice con señal («Sincronizar ahora») y vuelva a quitarla.

## Posición sugerida y listado de posiciones en la app (2026-10-07)

**Toqué «Sugerida: X» y no se agregó la línea.**
Es a propósito: pone la posición en el campo y enciende **Aceptar**; al tocar Aceptar (o escanear la posición) se agrega la línea. Escribir la cantidad antes es obligatorio en Despacho.

**Dice `No alcanza sola: faltan N. Marca más posiciones abajo.`**
La cantidad es mayor que lo que hay (Despacho) o cabe (Recibo) en la posición sugerida. En el listado de abajo marque las posiciones; el contador «Tomado X de Y» debe llegar al total para usar el botón.

**No me deja marcar otra posición.**
Ya se tomó el total (Tomado X de X). Desmarque una o suba la cantidad.

**Cambié la cantidad y las marcas cambiaron.**
Es lo esperado: las marcas se recortan para no pasarse del total, empezando por las del final del listado.

**¿Por qué no sale el listado en un producto con lote?**
Con lote la posición es la del lote que vence primero y no se escoge.

## Conteo cíclico en la app: retomar, terminar con faltantes y conteos abiertos (2026-10-07)

**Escaneé otra vez la misma posición y no se abrió otro conteo.**
Es a propósito: si esa posición ya tiene un conteo abierto tuyo (o sin asignar) se **retoma** con lo que ya contaste (`Retomaste el conteo CC-… de …: ya llevas n de m contados.`). Antes se abría uno nuevo cada vez.

**`Esa posición la está contando {Nombre} ({CC-#####}).`**
Otra persona tiene abierto el conteo de esa posición. Que lo termine o lo guarde, o que un supervisor lo dé de baja o lo reasigne en la web (Conteo cíclico).

**¿Por qué en la web veo la lista completa de líneas del conteo y no solo lo que conté?**
Un conteo por posición trae **todas** las líneas que el sistema espera en esa posición; lo que contaste aparece como «Contado» y el resto como «Pendiente».

**Toqué «Terminar esta posición» y me preguntó «Faltan N producto(s) por contar».**
El conteo solo se cierra con todas las líneas contadas. Elige **Seguir contando**, **Guardar y seguir después** (el conteo queda abierto para retomarlo) o **Dejar en 0 y terminar** (lo que falta se cuenta como 0 y queda como diferencia para el supervisor).

**Guardé y seguí después, pero ahora no me deja hacer otra cosa.**
Es a propósito: mientras tengas un conteo abierto, las demás acciones avisan `Termina o cancela el conteo en curso antes de usar esto.` En **Conteo → Conteos abiertos** toca **Continuar** para terminarlo (o cancelarlo si tienes `warehouse.count`).

**`Tienes abierto el conteo CC-… (…). Termínalo o guárdalo antes de contar otra cosa.`**
Estás intentando abrir otra posición o «Por producto» con un conteo abierto. Retoma ese (escanear su misma posición también lo retoma) y termínalo.

**Un conteo abierto aparece en la lista pero ya no existe / no puedo continuarlo.**
Si un supervisor lo dio de baja, al tocar **Continuar** sale `Conteo no encontrado.` y se quita de la lista.

**`El servidor no aceptó terminar el conteo: …`**
El cierre fue rechazado en el servidor (por ejemplo, una línea ya corregida por el supervisor). El mensaje dice el motivo; también queda en Sincronización.

**«Por producto»: la posición donde ya conté ese producto ya no aparece.**
Es a propósito: solo se ofrecen las posiciones que aún no contaste. Para corregir una cantidad ya contada, toca ✎ en su línea de la lista. Para contar el mismo producto en otra posición, toca el producto en la lista (se pone en el campo de escaneo) y da Aceptar.

## Nombre del remitente de los correos (2026-10-08)

**¿Por qué el correo dice «Advance Solutions» y no «Teikem»?** (y de dónde sale el «via Teikem» que muestra Gmail)
El nombre del remitente es el de la compañía a la que pertenece el correo (la aplicación no le agrega nada más; el «via Teikem» que Gmail pone al lado lo pone Gmail porque el dominio que firma el correo no es el de la dirección del remitente, ver abajo): el código de verificación del primer ingreso usa la compañía con la que se está entrando; «Olvidé mi contraseña» usa la
compañía del usuario **si pertenece a una sola**; el aviso de «verificación en dos pasos reiniciada» usa la compañía del administrador que la reinició.

**Un usuario que está en varias compañías recibe el correo como «Teikem».**
Es a propósito: sin saber cuál elegir, se usa el nombre general (`Brevo__FromName`).

**¿Se puede cambiar la dirección de correo por compañía?**
No: la dirección es una sola (`Brevo__FromEmail`). Cambiarla por compañía exigiría autenticar un dominio por cada una en Brevo.

**Gmail muestra «vía …» junto al remitente.**
Es la autenticación del dominio (DKIM/SPF) en Brevo, no el nombre. Se corrige agregando los registros del dominio del remitente en el DNS (ver `deploy/README.md`).

## Recuperar la contraseña (2026-10-07)

**Olvidé mi contraseña.**
En el login toque «¿Olvidó su contraseña?», escriba su correo y use el enlace que le llega (vale 60 minutos, una sola vez). Si no llega, revise correo no deseado; por seguridad la pantalla responde lo mismo aunque el correo no exista.

**Dice `El enlace no es válido o venció. Pida uno nuevo.`**
El enlace ya se usó, pasó una hora, se pidió otro enlace después (el último invalida al anterior) o se copió incompleto. Pida uno nuevo con «Pedir otro enlace».

**Dice `Demasiados intentos; espere un minuto e intente de nuevo.`**
El límite es de 5 solicitudes por minuto desde la misma conexión. Espere un minuto.

**Un administrador me puso una contraseña temporal; ¿cuánto dura?**
10 minutos desde que se la pusieron. Entre con ella y el sistema le pedirá elegir la suya. Si pasan los 10 minutos sin entrar, vale otra vez su contraseña de siempre.

**Dice `No puede ponerse una contraseña temporal a sí mismo.`**
Un administrador no puede ponerse la temporal a sí mismo; para cambiar la suya use Mi cuenta, o pida a otro administrador que se la ponga.

**La temporal se entregó pero ya pasaron más de 10 minutos.**
Vuelva a ponerla (Sistema → Usuarios → Contraseña temporal). Es nueva y cuenta otros 10 minutos.

**¿Por qué la temporal no llega por correo?**
Es a propósito: la entrega el administrador en persona. El correo con enlace es solo para «Olvidé mi contraseña».

## Cancelar un conteo con permiso de solo capturar (2026-10-08)

**Le quité `warehouse.count` a un contador para que no vea lo esperado y ya no podía cancelar.**
Corregido: cancelar pide el permiso de capturar (`warehouse.count.capture`), igual que capturar y terminar. Ya no hace falta `warehouse.count`.

**Dice `Solo se elimina un conteo abierto; este ya se terminó de contar.`**
Un conteo terminado ya no se cancela; el supervisor lo reconcilia desde la web.

## Varios almacenes en un mismo aparato (2026-10-07)

**¿Puedo usar el mismo aparato en más de un almacén de la misma compañía?**
Sí. En Inicio toque **«Cambiar»** (junto a «Almacén: …») y elija el almacén. Todas las pantallas pasan a trabajar con ese almacén. Cada compañía
registrada en el teléfono recuerda el suyo.

**No me aparece el botón «Cambiar».**
Solo aparece si la compañía tiene más de un almacén activo y el usuario tiene `inventory.view`. Si es un aparato nuevo, abra Inicio con señal
una vez para que baje la lista de almacenes.

**«Termina o cancela el documento en curso antes de cambiar de almacén.»**
Hay un recibo, despacho o conteo abierto, y ese documento es de su almacén. Termínelo o cancélelo y cambie después.

**Cambié de almacén y volvió al de antes.**
Desde la web se cambió el almacén por defecto del aparato (manda la web) o el almacén elegido se dio de baja. Elíjalo otra vez.

**Recibir me pide otro modo (directo o con acomodo) después de cambiar.**
Es normal: el modo de recepción es de cada almacén (Almacenes → modo de recepción en la web).

## Daños: llegaron dañados o se dañaron en el almacén (2026-10-08)

**¿Cómo registro algo que llegó dañado en un recibo?**
Lo más fácil: **en la propia línea del recibo**. En la app, en la pantalla de la cantidad recibida marque **Vinieron unidades dañadas**, escriba cuántas y la razón (y, si es *Otra*, escríbala); después diga dónde se dejan (la sugerida es la de cuarentena) o toque **Dar salida**. En la web, ficha del recibo abierto → ícono **Unidades dañadas** de la fila. Las dañadas cuentan dentro de lo recibido (100 recibidas, 10 dañadas → 90 buenas). Al confirmar el recibo se crea el `DAN-#####` y lo dañado sale de lo bueno. También puede reportarlo después: web (ficha del recibo → **Reportar daño**, o Productos e inventario → pestaña Daños) o app (Inicio → Daño → «Llegó dañado en un recibo»); ahí reciba solo lo bueno.

**Dice `La cantidad dañada no puede ser mayor que la cantidad recibida.`**
Las dañadas van dentro de lo recibido: escribió más dañadas que recibidas, o bajó lo recibido por debajo de lo ya declarado como dañado. Corrija una de las dos cantidades.

**Dice `Escriba la razón del daño cuando la causa es Otro.`**
Escogió la razón *Otra*: escriba en el cuadro qué pasó. Con las otras razones no hace falta.

**¿Qué pasa con lo dañado si el recibo es «con acomodo»?**
Entra con el resto a la posición de recepción y, al confirmar, sale de ahí hacia la posición que indicó (la de cuarentena por defecto) o se descarta. La tarea de acomodo solo lleva las unidades buenas. Si no hay posición de cuarentena y no indicó otra, se queda en la posición de recepción pero ya no se acomoda: es el daño en espera de decidir.

**¿Lo dañado se cuenta en el conteo cíclico? ¿Se puede vender?**
Se **cuenta** (es existencia física de esa posición) pero **no se puede despachar**: en una zona de Cuarentena ya queda fuera de la asignación y, si lo dejó en otra posición (guardado, recepción), el sistema lo **reserva** y baja el disponible; la lista de Daños dice «reservado (no se despacha)». Al desecharlo o recuperarlo la reserva se libera.

**Vienen a recoger lo dañado de cuarentena. ¿Qué hago?**
En la pestaña Daños use **Dar salida** sobre ese daño y escoja el **destino final** (en la app, al dar salida a lo dañado al recibir o desde el tile Daño, también pregunta **¿A dónde va?**): *Tirado*, *Devuelto al proveedor*, *Donado* o *Vendido como saldo*. No es un despacho: baja el inventario con un ajuste de motivo Daño y queda el destino en el Kárdex y en la lista. Si necesita otro destino, agréguelo en Sistema → Catálogos (Destino final de lo dañado).

**Dice `Indique a dónde va lo que sale (tirado, devuelto al proveedor, donado…).`**
Al darle salida a lo que está en cuarentena hay que escoger el destino final. Elíjalo en la lista y vuelva a dar salida.

**¿Puedo dejar lo dañado en una posición que no sea de cuarentena?**
Sí, cualquier posición activa del almacén (en la app: escanee o escoja de la lista; en la web: «Posición donde queda»). El daño queda «En cuarentena» en esa posición hasta que lo desecha o lo recupera desde la pestaña Daños.

**¿Y si se dañó en el almacén?**
Reporte con origen «Se dañó en el almacén» y la posición donde estaba. Cuarentena la mueve a la posición de cuarentena; dar salida la baja del inventario con un ajuste de motivo Daño.

**Dice `El almacén no tiene una posición de cuarentena activa; cree una en una zona de tipo Cuarentena.`**
Falta la zona. En Almacén → Posiciones cree una zona de tipo Cuarentena con al menos una posición activa, o desbloquee la existente. (Al reportar un daño, o al declararlo en un recibo, también puede indicar cualquier otra posición activa.)

**Dice `Inventario insuficiente de {sku} en {posición}: disponible {x}, solicitado {y}.`**
Reportó más unidades de las que hay disponibles en esa posición (lo reservado no cuenta). Revise la cantidad o la posición.

**Dice `Solo se resuelve un daño que está en cuarentena.`**
Ese daño ya se desechó o se recuperó (o se desechó de una vez). No se resuelve dos veces.

**Dice `Los productos con serie todavía no se reportan aquí; use un ajuste de inventario con motivo Daño.`**
Por ahora los productos con serie no pasan por Daños: use Transferencias y ajustes → Ajustar → Bajar, motivo Daño, con la serie.

**¿Dónde veo cuánto se ha dañado y por qué?**
En Productos e inventario → pestaña **Daños** (filtros por estatus, origen y búsqueda). Para gráficos o indicadores use la fuente de datos «Daños» en Vistas e informes.

**Lo mandé a cuarentena por error.**
En la pestaña Daños, **Recuperar** (elija una posición de guardado): la existencia vuelve y el daño queda como Recuperado.

**¿Por qué no sale el tile Daño en la app?**
Su usuario no tiene el permiso «Reportar y resolver daños» (`warehouse.damage`). Pídalo al administrador (Sistema → Roles y usuarios).

## Etiquetas de producto (Productos e inventario, 2026-10-09)

Detalle en [Etiquetas de producto](frontend/f17b-etiquetas-de-producto.md).

**¿Cómo imprimo etiquetas adhesivas de mis productos?**
Productos e inventario → filtre la tabla → **Etiquetas de producto** → tamaño (4 × 2, 4 × 4 o 4 × 6) → **Generar PDF**. Sale una etiqueta por producto con el código de barras del SKU, el SKU en grande y el nombre.

**¿Hay un máximo de etiquetas de producto por PDF?**
No (desde 2026-10-10): se imprimen todos los productos del filtro. Con muchos, el PDF tarda más en generarse.

**Dice `No hay productos para imprimir con los filtros actuales.`**
El filtro no devuelve ningún producto: quite o cambie los filtros.

**Dice `{n} salieron sin código de barras (solo con el código en texto):`**
Esos SKU tienen caracteres que el código Code 128 no admite o son demasiado largos para el tamaño: la etiqueta sale con el SKU en texto. Elija un tamaño más grande o corrija el SKU.

**La etiqueta sale de lado o cortada.**
Revise que el tamaño del driver sea el mismo que eligió y la escala 100 %; si sigue, genere el PDF con **Orientación → Girar 90°**.

## Empaque del producto (2026-10-09)

Detalle en [Empaque del producto](06-inventario-y-almacen.md#empaque-del-producto-2026-10-09).

**¿El inventario se lleva en cajas?**
No. Siempre en la unidad base del producto. El empaque (Caja de 12) solo ayuda a contar y a leer cantidades.

**¿Cómo cuento por cajas?**
Defina el empaque del producto (Editar producto → Empaque y Unidades por empaque). En la calculadora del conteo o de Recibir, elija **Caja** en el bloque, o use **Cajas sueltas**; el total sale en unidades.

**Dice `Las unidades por empaque deben ser un número mayor que 0 (hasta 3 decimales).`**
Escriba un número mayor que 0 en *Unidades por empaque*.

**Dice `Indique la unidad del empaque (caja, barril, paquete…).`**
Puso las unidades por empaque pero no eligió el empaque: elíjalo o borre la cantidad.

**Dice `Indique cuántas unidades trae el empaque.`**
Eligió el empaque pero no cuántas unidades trae: escriba la cantidad (Caja = 12).

**Dice `La unidad del empaque no puede ser la misma que la unidad base.`**
El empaque debe ser distinto de la unidad base (p. ej. base Galón, empaque Barril).

**Dice `Unidad de empaque desconocida: '{code}'.`**
Esa unidad no existe en el catálogo *Unidad de medida*. Agréguela en el catálogo o elija otra.

**No me sale el selector de Caja en la calculadora de la app.**
El producto no tiene empaque o la app no ha sincronizado desde la actualización: sincronice y reintente.

## Transferir desde el aparato (2026-10-10)

Detalle en [Transferir](09-app-almacen.md#transferir-2026-10-10).

**No me sale el botón Transferir (ni «Mover» en Consultar).**
Su usuario no tiene el permiso `warehouse.transfer`. El *Operador de almacén* y quien ya puede ajustar inventario lo tienen; si usa un rol propio, pídale al administrador que se lo agregue (Sistema → Roles).

**Dice `Solo se pueden mover {qty} (lo reservado no se mueve).`**
Parte de lo que hay en esa posición está reservado para un despacho. Mueva solo lo disponible o espere a que se despache.

**Dice `La posición {bin} es de cuarentena, en renta o de cross-dock: no se transfiere desde aquí…`**
Esas posiciones tienen su propio flujo: lo dañado con *Daño*, lo rentado con Rentas y el cross-dock con su pantalla.

**Dice `Desde el aparato solo se transfiere dentro del mismo almacén.`**
Para mover entre almacenes use la web (Transferencias y ajustes).

**Dice `{sku} lleva número de serie: por ahora se transfiere desde la web.`**
Los productos con serie se mueven desde la web (Transferencias y ajustes), eligiendo las series.

**Dice `Transferir necesita señal…`**
La transferencia se hace en el servidor para poder confirmar la existencia: reintente con señal.

## Ajustar cantidad desde el aparato (2026-10-10)

Detalle en [Ajustar cantidad](09-app-almacen.md#ajustar-cantidad-2026-10-10).

**No me sale «Ajustar» en Consultar.**
Su usuario no tiene el permiso `warehouse.adjust`. Ninguna plantilla de rol lo trae: el administrador crea un rol con ese permiso (Sistema → Roles) y se lo asigna.

**¿Un ajuste sirve para pasar mercancía de una posición a otra?**
No. El ajuste cambia solo la cantidad de **una** posición. Para mover use *Transferir* (o *Mover* en Consultar).

**Dice `Escriba una nota que explique el ajuste.`**
La nota es obligatoria en todo ajuste manual: explique el motivo en pocas palabras.

**Dice `Solo se puede bajar hasta {qty} (lo reservado no sale).`**
Parte de lo que hay está reservado para un despacho: solo se puede bajar lo disponible.

**Dice `La posición {bin} es de cuarentena, en renta o de cross-dock: no se ajusta desde aquí…`**
Esas posiciones tienen su propio flujo (Daño, Rentas, Cross-dock).

## Señal débil (app, 2026-10-10)

**¿Por qué Consultar me muestra datos y dice «Actualizando…»?**
Porque el aparato muestra primero lo que ya tiene guardado y, en paralelo, le pregunta al servidor. Cuando llega la respuesta, la pantalla se actualiza sola y el indicador pasa a «✓ Al día».

**Dice `Sin conexión o con señal débil: se muestran los datos del aparato.`**
El servidor no respondió a tiempo. Lo que ve es lo último que el aparato sincronizó (la hora está debajo). Acérquese a una zona con mejor señal o toque **Sincronizar ahora** en Inicio.

**Dice `Datos del aparato de las {hora} (hace {n} min)`.**
Son saldos guardados en el aparato, no confirmados ahora con el servidor. Para operar con certeza espere el «✓ Al día».

**No me sale nada al escanear, pero el producto existe.**
Si el aparato nunca ha bajado los saldos de ese almacén, espera al servidor como antes. Sincronice una vez con buena señal (Inicio → **Sincronizar ahora**) y la próxima vez saldrá al instante.

**¿Puedo transferir, ajustar o reportar un daño sin señal?**
Sí. La operación se guarda en el aparato («Guardado en el aparato. Se envía solo en cuanto haya señal.»), el saldo que ve el aparato ya la refleja y sube sola en la siguiente sincronización. Ver cuántas faltan por enviar en Inicio → Sincronización.

**Dice `El servidor no aceptó la operación`.**
El servidor revisó la operación al recibirla y no pudo aplicarla (por ejemplo, la existencia ya no estaba porque otra persona la movió, o la posición cambió de zona). El aparato deshizo el movimiento en sus saldos. Entre a **Sincronización**, lea el motivo y **reintente** (si ya se puede) o **descarte**.

**Hice una transferencia sin señal y el saldo de otro aparato no cambió.**
Es normal: el otro aparato lo verá cuando la operación llegue al servidor y él sincronice. Hasta entonces dos aparatos pueden ver cifras distintas.

## Posiciones y Productos e inventario (2026-10-10)

**¿Qué es «3 productos» en la columna Producto de Posiciones?**
Es un enlace: la posición tiene más de un producto. Al tocarlo se abre la ventana **Productos en {posición}** con SKU, producto, lote, en mano y disponible.

**Exporté Posiciones y una posición sale varias veces.**
Es a propósito: una posición con más de un producto sale en una línea por producto (columna SKU, producto y lo que tiene ese producto en esa posición).

**¿Cómo veo qué productos hay en una posición desde Productos e inventario?**
Use el filtro **Posición** (escriba parte del código, por ejemplo `A-01`): salen los productos que tienen existencia en alguna posición cuyo código **contiene** ese texto. Con el filtro puesto, **En mano, Reservado y Disponible son SOLO las de las posiciones que coinciden** (si el texto coincide con varias posiciones, la suma de ellas), y el contador del panel dice cuántos productos hay ahí. Sin el filtro, las cantidades son las totales del producto. El filtro también va en «Filtros aplicados» de los reportes y de Exportar; no aplica al Reporte de ajustes.

## Aparato: no veo Transferir ni Ajustar (2026-10-10)

**Instalé la APK y en el menú no sale Transferir (o en Consultar no sale Mover / Ajustar).**
Los botones dependen de permisos del usuario, no de la versión de la APK: **Transferir y Mover** piden `warehouse.transfer` (lo traen «Operador de almacén» y «Admin de compañía»; `inventory.adjust` lo implica) y **Ajustar** pide `warehouse.adjust` (solo «Admin de compañía»; para un operario se crea un rol propio en Sistema → Roles con ese permiso). **Si el usuario ya tiene el permiso (p. ej. «Admin de compañía» con todos), lo más probable es que el APK instalado sea una compilación vieja:** desde 2026-10-10 Inicio muestra al pie «Versión 1.0.0 (código) · sello» (fecha y commit del APK; en el CI, `ci-<número> <commit>`). Si no sale esa línea, el APK es anterior: instale el nuevo. Revise (1) que el **API ya esté desplegado** con la base actualizada (`db-update`), (2) que el rol del usuario tenga el permiso y (3) abrir la app con señal una vez: el aparato lee los permisos del servidor al abrir la pantalla. La corrección del 2026-10-10 (b) en `Diseño/logistica-db-update.sql` agrega los dos permisos a los roles «Operador de almacén» y «Admin de compañía» que ya existían en cada compañía (antes quedaron fuera porque el sembrado solo propaga los códigos nuevos de su corrida).


## Lote F-A1 — Clientes: lista, alta y ficha (2026-10-10)

Mensajes de la pantalla Catálogo → Clientes (los del servidor salen tal cual, debajo del campo o en el diálogo). Capítulo: [fa1-clientes.md](frontend/fa1-clientes.md).

**`Ya existe un cliente con ese código.` (409) al crear.**
El duplicado es por **código** (no por nombre). Escriba otro código o déjelo vacío para que se genere del nombre (agrega `-2`, `-3`… si hace falta). Revise con **Mostrar inactivos**: un cliente dado de baja conserva su código.

**`El nombre es obligatorio.` / `No puede exceder 200 caracteres.` (400).**
El nombre es lo único obligatorio del alta. Razón social admite 250 y la identificación fiscal 50.

**`El código no puede exceder 30 caracteres.` (400).**
Acórtelo o déjelo vacío.

**`El límite de crédito no puede ser negativo.` (400) / `El límite de crédito no se puede dejar vacío; escriba 0 si no aplica.` (en pantalla).**
Use 0 o más. Una vez que el cliente tiene límite, el campo no puede quedar vacío (el servidor no sabe borrarlo; vacío significaría «no cambiar»).

**`El título no puede exceder 200 caracteres.` (400) / `Ya existe un contrato con ese número.` (409) al crear con contrato inicial.**
El título del contrato inicial admite 200 caracteres (vacío = «Contrato marco»). Si el 409 del contrato aparece, repita el alta; si sigue, avise a soporte (el número se arma del código del cliente).

**`El registro fue modificado por otro usuario; recargue e intente de nuevo.` (409) al guardar el perfil.**
Otra persona guardó el mismo cliente antes. La ficha se vuelve a leer sola y lo que usted escribió se conserva: revise y pulse **Guardar** otra vez.

**`El punto de recogido debe ser un almacén activo del cliente (tipo PICKUP o BOTH).` (400).**
Elija uno de la lista (solo se ofrecen los válidos); si el almacén se desactivó mientras tanto, vuelva a abrir la ficha. «Dirección corporativa» quita el punto propio.

**`Teléfono inválido.` / `Correo inválido.` / `El valor es obligatorio.` (400).**
El teléfono lleva los dígitos de la máscara de la compañía (p. ej. 10 en Puerto Rico); el correo, `usuario@dominio.com`.

**`El nombre del contacto es obligatorio.` (400), `No puede exceder 150 caracteres.` (nombre) / `No puede exceder 80 caracteres.` (puesto).**
Escriba el nombre de la persona; el puesto es opcional.

**`Ya existe un contacto principal activo para este cliente.` (409).**
Dos personas intentaron quedar como principal a la vez. Repita: marcar una persona como principal quita la marca a la anterior.

**`El patrón es obligatorio.` / `El patrón no puede exceder 40 caracteres.` / `El patrón debe incluir al menos un '#' para el consecutivo.` / `Carácter no permitido en el patrón: 'x'. Use letras, dígitos y - _ / . # @.` (400).**
Un patrón válido tiene de 1 a 40 caracteres, al menos un `#` y solo letras sin acento, dígitos y `- _ / . # @`. Para volver al del sistema deje el campo vacío. El mismo motivo se ve en rojo bajo el patrón mientras escribe.

**`Salto ilegal: de 'X' solo se puede avanzar a 'Y'.` / `El registro ya está en ese estatus.` / `'X' es terminal: no admite más transiciones.` (422) al cambiar el estatus.**
El estatus sigue el modelo de la compañía (Ajustes → Operación). Use el botón que ofrece la ficha; desde un estatus terminal no se avanza.

**`Cliente no encontrado.` (404).**
El cliente de la dirección (`?client=`) no existe o es de otra compañía. Elija uno de la lista.

**«El nombre no se cambia desde aquí».**
Es a propósito: el nombre se muestra pero el perfil no lo edita (el servidor no lo permite en el perfil).

**¿Por qué no veo Nuevo cliente, Guardar, Dar de baja o Agregar teléfono?**
Faltan permisos: crear pide `clients.create`; editar, estatus y baja piden `clients.update`; teléfonos y correos piden `contacts.manage` **y** `clients.update`. Con solo `clients.read` la ficha es de consulta.

**Di de baja un cliente y ya no está en la lista.**
La lista muestra solo activos. Encienda **Mostrar inactivos**, ábralo y use **Reactivar**. Un cliente de baja no se puede elegir en órdenes nuevas, pero su historial se conserva.

**¿Dónde cambio la dirección, el SLA o «Cliente desde» de un cliente?**
Las direcciones se administran en Localizaciones (en el perfil son de solo lectura). El SLA (por tipo de servicio, en horas) y «Cliente desde» (inicio del contrato) pertenecen al contrato y se editarán en el bloque de contratos.

**¿El estado «Activo/Inactivo» de la maqueta es el estatus?**
Son dos cosas: el **estatus** (etapas configurables, p. ej. Activo → En revisión) y la **baja** (Inactivo). La lista muestra las dos.
