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
El código de un almacén (o de una zona o un muelle) es inmutable una vez creado. Si lo escribiste mal, da de baja
el almacén y crea uno nuevo con el código correcto (solo si sigue vacío).

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
conteo y reversa de una recolección, respectivamente). Un ajuste manual capturado por un usuario debe usar otro
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
elimínalo (solo si sigue `OPEN`) antes de crear uno nuevo contra el mismo aviso.

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
No se puede cancelar una orden de compra mientras tiene un recibo `OPEN` (una recepción a medio capturar) contra
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
no debe verlas, quítele `analytics.view` o no le comparta reportes de esas fuentes.

## Lote 8A — Backend de la app de almacén: aparatos, PIN, idempotencia, sincronización y operaciones atómicas

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
contraseña; si la olvidó, cámbiela primero.

**¿Por qué no aparezco en la lista de usuarios del aparato?**
La lista muestra solo usuarios internos activos, con membresía activa en la compañía del aparato, con **PIN definido** y
con `inventory.view`, ordenados por nombre. Defina su PIN en Mi cuenta y pida el permiso si le falta. Si tiene PIN pero
no `inventory.view`, el login por aparato responde 403 `Falta el permiso 'inventory.view'.`.

**¿Qué significa "No puede asignar ni quitar el PIN de un usuario con más permisos que usted." (403)?**
Con `devices.manage` (o `admin.users`) puede asignar el PIN de otros, pero solo de usuarios cuyos permisos sean un
subconjunto de los suyos: con ese PIN se entra como esa persona en un aparato sin contraseña ni MFA. Pida a un
administrador con esos permisos que lo haga, o que la persona defina su PIN en Mi cuenta.

**Al asignar el PIN de otro usuario recibo 403 `aal2_required`.**
Asignar el PIN de otro exige una reautenticación reciente (AAL2), igual que cambiar roles o permisos: confirme su
contraseña (`POST /api/v1/auth/reauth`) y repita.

**¿Qué significa "La clave de idempotencia ya se usó con otro contenido." (409)?**
La cabecera `Idempotency-Key` identifica UNA operación: repetirla con el mismo cuerpo devuelve la respuesta guardada
(con `Idempotent-Replayed: true`); con otro cuerpo o en otra ruta es un error. Genere una clave nueva por operación.

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

**¿Qué significa "Falta el permiso 'devices.manage' o 'admin.users'." (403)?**
Para asignar o quitar el PIN de otro usuario (`PUT`/`DELETE /api/v1/users/{id}/pin`) hace falta uno de esos dos
permisos. Pida que se lo asignen o que otro administrador haga el cambio.

**Quise asignar el PIN de un usuario y me da 404 "Usuario no encontrado." aunque el usuario existe.**
El usuario es de otra compañía, es una cuenta de portal, o es el administrador de plataforma: ninguno de los tres
tiene PIN de aparato (el administrador de plataforma nunca entra por esta vía, así que ni siquiera es visible
aquí).

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

**¿Qué significa "El conteo admite como máximo 1000 líneas; acote los filtros."?**
Un conteo cíclico sin zonas ni posiciones toma todo el saldo en mano del almacén; si supera 1000 líneas, hay que
acotarlo eligiendo zonas o posiciones específicas antes de crearlo.

**¿Qué significa "Indique la posición." / "Indique la cantidad contada."?**
Al agregar o capturar una línea de conteo a mano, faltó la posición o la cantidad contada.

**¿Qué significa "Indique el lote por su id o por su número, no ambos."?**
Al agregar una línea de conteo con lote, hay que elegir un lote existente **o** escribir un número nuevo, no las
dos cosas a la vez.

**¿Qué significa "Faltan {n} línea(s) por contar."?**
"Terminar conteo" exige que todas las líneas ya tengan una captura (cantidad o series); revisa cuáles faltan en la
tabla (las que dicen "Pendiente").

### Recolección y empaque

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
