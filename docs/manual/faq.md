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
que sí admite `effectiveFrom` pasado.

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
El `typeId` apunta a un tipo dado de baja (`POST /api/v1/special-service-types/{id}/deactivate`). Reactívalo con
`.../reactivate`, elige otro tipo, o crea el servicio con `newTypeName` (si coincide con el tipo inactivo, se reactiva solo).

**¿Qué significa "El tipo tiene tarifas vigentes en N cliente(s); ciérrelas antes de inactivarlo."?**
No se puede dar de baja un tipo de servicio especial mientras algún cliente tenga una tarifa abierta de ese tipo. Cierra esas
tarifas (`.../special-services/{id}/close`) y vuelve a intentarlo; el historial se conserva.

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
