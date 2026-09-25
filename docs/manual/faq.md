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
compañía (`admin.users`/`admin.roles`) que te lo asigne, si corresponde a tu función.

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
