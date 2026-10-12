# IP preferida: entrar sin código MFA desde la red de la localidad (diseño, 2026-10-12)

> **Estado: SOLO DISEÑO. No hay código.** Pedido de Luis (2026-10-12). Nada de esto está implementado. Decisiones de Luis incorporadas (2026-10-12); lo que sigue abierto está en la sección 10.

## 1. Qué se quiere
Una compañía tiene una o más **localidades** (almacén, oficina). Cada una sale a internet por la IP pública de su firewall. Quien se conecte desde la red de esa localidad (Wi-Fi o cable) **no debe escribir el código MFA al iniciar sesión**. Las acciones sensibles de administración (asignar permisos, etc.) **siguen pidiendo MFA**. Una lista **por compañía**, administrada solo por administración.

## 2. Cómo es hoy (lo que ya existe y se reutiliza)
- `AuthService.LoginAsync` ([AuthService.cs:139](../src/Teikem.Infrastructure/Services/AuthService.cs)): si el usuario tiene TOTP confirmado, o la compañía exige MFA (`Tenant.MfaRequired`, por defecto `true`), o la membresía lo exige (`UserTenant.MfaRequired`), responde `mfa_required` con un challenge y la sesión se emite recién en `VerifyMfaAsync` con `aal2At = ahora`. Sin MFA la sesión se emite con `aal2At: null`.
- **Acciones sensibles**: el filtro `[RequireAal2]` ([Policies.cs:95](../src/Teikem.Api/Auth/Policies.cs)) exige una reautenticación (contraseña **+ código MFA** si el usuario tiene TOTP) dentro de `Tenant.Aal2WindowMinutes`. Lo usan roles, permisos de usuario, MFA de otros usuarios, contraseñas temporales, módulos de la compañía, etc.
- La IP del cliente ya se toma de `HttpContext.Connection.RemoteIpAddress` (`TenantContextMiddleware`); **no** se usan cabeceras `X-Forwarded-For` ni hay proxy delante (IIS en proceso).
- Bitácora: `SecurityEvent` (tipos `LOGIN`, `MFA`, `REAUTH`, …; guarda IP y User-Agent).
- **Recuperar la contraseña por correo** (`POST /auth/forgot-password` y `POST /auth/reset-password`, `AuthService.Password.cs`): hoy el enlace del correo (un solo uso, vence en minutos) **basta** para poner la contraseña nueva; **no pide código MFA** en ese paso (el código se pide después, al iniciar sesión). Al terminar cierra todas las sesiones y levanta el bloqueo por intentos.
- La app del aparato (Zebra) no usa MFA (registro del aparato + PIN) y el portal de clientes tiene su propio ingreso: **no cambian**.

**Consecuencia clave:** como una sesión sin MFA nace con `aal2At = null`, **las acciones `[RequireAal2]` ya piden el código solas**. El requisito «un admin que asigne permisos sí debe dar MFA» se cumple sin tocar esos endpoints.

## 3. Regla de decisión (un solo punto: justo antes de emitir `mfa_required`)
En orden; la primera que aplica gana:

| # | Condición | Resultado |
|---|---|---|
| 1 | Administrador de plataforma (`IsPlatformAdmin`) | **Siempre** pide MFA (nunca se salta) |
| 2 | La membresía tiene `MfaRequired` (marca individual «siempre MFA») | Pide MFA (la marca individual no se anula por IP) |
| 3 | El usuario **no tiene TOTP confirmado** | Igual que hoy (inscripción obligatoria): la IP preferida salta el **código**, no la **inscripción** |
| 4 | Interruptor global encendido **y** la IP del pedido está en una entrada **activa y vigente** de la lista de **esta** compañía | **No pide código**; se emite la sesión con `aal2At = null` |
| 5 | Cualquier otro caso | Igual que hoy |

- La contraseña **siempre** se verifica, y los fallos siguen sumando al bloqueo de cuenta (5 fallos → 15 min). Solo cambia el segundo paso.

**Cómo se combinan los dos «MFA» que ya existen** (aclaración): la política de la compañía (`Tenant.MfaRequired`, por defecto sí) **no** impide la IP preferida: justamente es lo que la IP preferida relaja. En cambio la **marca individual** de una persona sí manda sobre la IP.

| Compañía exige MFA | Marca individual «siempre MFA» de la persona | Desde la red preferida | Desde otra red |
|---|---|---|---|
| Sí | **No** (lo normal) | **No pide código** | Pide código |
| Sí | **Sí** | Pide código | Pide código |
| No | No | No pide código (si ya tiene TOTP inscrito) | Pide código si tiene TOTP inscrito |

Es decir: para que alguien entre sin código desde la localidad, **la compañía puede seguir exigiendo MFA** y a esa persona simplemente **no** se le pone la marca individual. Quien tenga la marca individual la tiene «siempre», incluso en la red preferida (la regla 2). La regla 3 es otra cosa: si la persona **nunca inscribió** su MFA, primero tiene que inscribirlo (una sola vez); desde ahí ya aplican las filas de la tabla.
- La regla 3 evita que alguien entre siempre desde la oficina sin haber inscrito nunca su MFA: sin TOTP no podría pasar luego una acción sensible.
- Efecto fuera de la localidad: igual que hoy, pide el código.

### 3.1 Recuperar la contraseña por correo: siempre exige MFA, sin excepción por IP preferida (decidido por Luis, 2026-10-12)
La IP preferida **solo** relaja el segundo paso del **inicio de sesión**. **Nunca** aplica al restablecimiento de contraseña por correo: ese flujo es anónimo (se llega con el enlace del correo) y, sin esta regla, quien tuviera acceso al correo de alguien y estuviera dentro de la red preferida podría cambiar la contraseña y entrar sin código.

**Regla:** al **poner la contraseña nueva con el enlace** (`reset-password`) se pide además un **código MFA** (TOTP o código de recuperación) cuando **cualquiera** de estas es cierta:
- la persona **tiene un TOTP confirmado**, o
- la **compañía exige MFA** (`Tenant.MfaRequired`) en alguna de sus membresías activas, o
- alguna de sus membresías tiene la marca individual «siempre MFA».

(La recuperación no está atada a una compañía; si la persona pertenece a varias, basta que una lo exija.) Un administrador de plataforma siempre lo cumple si tiene TOTP.

**Cómo funciona:**
1. `forgot-password` no cambia: sigue sin pedir nada y responde igual exista o no el correo (sin enumerar usuarios).
2. En `reset-password`, si hace falta el código y no viene (o es inválido), el servidor responde un error específico (`mfa_required` / «Código MFA inválido») **sin gastar el enlace**: el enlace sigue siendo de un solo uso y solo se consume cuando todo es correcto. La página de restablecimiento muestra entonces el campo «Código MFA» y reenvía.
3. Los códigos MFA fallidos **cuentan para el bloqueo de la cuenta** (5 fallos → 15 min, igual que en el login), están limitados por el mismo límite de tasa que ya protege estos endpoints y quedan en la bitácora de seguridad (`PASSWORD_CHANGE`, etapa `reset_link`, con `mfa: verified` o `mfa_failed`).
4. Al terminar sigue igual: se cierran todas las sesiones y se levanta el bloqueo. Después, el inicio de sesión aplica las reglas normales de la sección 3 (aquí sí puede aplicar la IP preferida, porque ya se demostró el MFA en el restablecimiento).

**Caso sin MFA inscrito:** si la persona **nunca inscribió** su MFA (no hay TOTP que verificar), el código no se puede pedir; el enlace del correo sigue siendo el único factor, igual que en el primer ingreso (invitación por correo), y en el siguiente inicio de sesión se le exige inscribirlo (regla 3). Es un límite conocido: para cuentas todavía sin MFA la seguridad descansa en el correo.

## 4. Datos
Tabla nueva **`TenantTrustedNetwork`** (por compañía, `ITenantScoped`, con auditoría como el resto de las entidades):

| Campo | Notas |
|---|---|
| `TenantTrustedNetworkId`, `PublicId`, `TenantId` | |
| `Name` | «Almacén Bayamón» (obligatorio, máx. 100) |
| `Cidr` | una IP (`203.0.113.10`, se guarda como `/32`) o un rango (`192.168.10.0/24`); normalizado; IPv4 e IPv6 |
| `IsActive` | activar/desactivar sin borrar: **es lo que manda** cuando no hay fecha de vencimiento |
| `ExpiresAtUtc` | **opcional; en blanco = no se toma en cuenta** (la entrada vale mientras esté activa). Con fecha, deja de valer sola al llegar a ella |
| `Note` | opcional (máx. 300): p. ej. «IP fija del ISP, contrato #…» |
| `InternalOnlyConfirmedByUserId`, `InternalOnlyConfirmedAtUtc` | quién y cuándo marcó el recuadro «esta IP es solo de la red interna» al crear la entrada (obligatorio para guardarla; ver sección 5) |
| creación/cambio | usuario y fecha (los pone la auditoría) |

Límites y validaciones: el **máximo de entradas es un ajuste de cada compañía** (columna nueva `Tenant.TrustedNetworkMaxEntries`, entero opcional; **vacío = sin tope**; no está fijo en el código ni en la configuración del servidor). Si se baja por debajo de las entradas que ya hay, las existentes se conservan y solo se bloquean las altas nuevas (con el mensaje «Esta compañía llegó a su máximo de N entradas»). Se rechazan rangos que confíen en medio internet (IPv4 más ancho que `/16`, IPv6 más ancho que `/48`, y siempre `0.0.0.0/0` y `::/0`); sin duplicados; una IPv6 mapeada a IPv4 (`::ffff:1.2.3.4`) se normaliza a IPv4 antes de comparar. Una IP que **no** coincide jamás salta el MFA (falla hacia el lado seguro; p. ej. si el cliente entra por IPv6 y solo se cargó la IPv4).

Interruptor global (distinto del tope, que es por compañía): `Auth:TrustedNetworks:Enabled` en configuración del servidor, **apagado por defecto**. Con él apagado el comportamiento es exactamente el de hoy aunque haya entradas cargadas. Es lo que permite llevar el código a `master` sin cambiar nada hasta encenderlo (en staging primero).

El archivo de estructura/seed están congelados: la tabla va como sección nueva, idempotente, **al final** de `Diseño/logistica-db-update.sql`.

## 5. Quién lo administra y cómo
- **Pantalla:** Sistema → Ajustes → **Seguridad** (junto a la política de MFA de la compañía): lista con nombre, IP/rango, activa, vence, nota; alta, edición, apagar y borrar.
- **Máximo de entradas:** campo en el mismo panel («Máximo de entradas: [   ]  vacío = sin límite»), guardado en la compañía (`Tenant.TrustedNetworkMaxEntries`), con el contador «usa 3 de 10». Mismo permiso y mismo `[RequireAal2]`; el cambio también queda en la bitácora y en el correo de aviso (sección 6). Va en los contratos de la compañía (`TenantContracts`, leer y modificar) como el resto de los ajustes.
- **Permiso:** `admin.tenant` (el de los ajustes de la compañía) y **`[RequireAal2]`** en alta/cambio/borrado: quien cambia la lista debe haber dado MFA hace poco. Así una sesión abierta desde la oficina sin código no puede ampliar la lista por sí sola.
- **La pantalla ayuda a escribir bien la IP o el rango** (sin que nadie tenga que saber qué es CIDR):
  - **«Su IP actual es X»** con el botón **«Agregar esta IP»** (la guarda como una sola IP, la opción recomendada) y aviso si esa IP ya está cubierta por la lista.
  - Selector **«Una sola IP (recomendado)» / «Un rango de IPs»**. En «una sola IP» solo se escribe la IP; la pantalla muestra «Se guardará como `203.0.113.10/32` (una sola dirección)».
  - En «rango» se explica en una línea qué es el `/NN` y la pantalla da **vista previa en vivo**: «`192.168.10.0/24` = 256 direcciones, de `192.168.10.0` a `192.168.10.255`». Debajo, atajos con tamaños reales (`/32` = 1, `/29` = 8, `/28` = 16, `/27` = 32, `/26` = 64, `/25` = 128, `/24` = 256) para elegir sin calcular.
  - **Corrige y avisa:** `192.168.10.5/24` se ajusta a `192.168.10.0/24` con un aviso; una IP mal escrita se marca con el motivo («falta un número», «el número va de 0 a 255»).
  - **Avisos de riesgo:** rango ancho («cubre 65,536 direcciones; ¿seguro?»); rango **privado** (`10.x`, `172.16–31.x`, `192.168.x`: «no es una IP de internet; solo sirve si la conexión llega por red interna»); rango que **no incluye su IP actual** (para que sepa que usted mismo no quedaría cubierto).
  - **«Probar una IP»:** escribe una IP y dice si alguna entrada activa la cubre y cuál.
  - En la lista, cada entrada muestra cuántas direcciones cubre y una marca «Usted está aquí» si su IP actual entra en ella.
- **Recuadro de confirmación obligatorio al agregar una IP o rango:** «Confirmo que esta IP corresponde solo a la red interna de la empresa, no a una red de invitados ni a una red pública». Sin marcarlo no se guarda; quién lo marcó y cuándo queda en la entrada y en la bitácora. Al cambiar la IP o el rango de una entrada existente hay que volver a confirmarlo.
- Aviso permanente en la pantalla: toda persona que salga por esa red (incluido Wi-Fi de invitados si comparte la salida) entra sin código.

## 6. Bitácora y alertas
- Cada ingreso que saltó el MFA queda como `LOGIN` éxito con detalle `{ stage: "password", mfa: "skipped_trusted_network", network: "<nombre>" }` (IP y User-Agent ya se guardan).
- Alta, cambio y baja de entradas: nuevo tipo de evento `TRUSTED_NETWORK` (quién, qué IP, de dónde, y si confirmó que es solo red interna) y la auditoría normal de la entidad.
- **Correo a los administradores de la compañía cuando cambia la lista** (alta, cambio, activar/desactivar, baja y cambio del máximo de entradas): quién lo hizo, qué entrada (nombre, IP/rango), cuándo y desde qué IP. Se envía por el mismo servicio de correo que ya usa el sistema (Brevo); los destinatarios son quienes tienen el permiso de administrar la compañía (`admin.tenant`) con correo verificado, incluido quien hizo el cambio. Si el correo falla, el cambio **no** se revierte (se registra el fallo).
- La lista de eventos de seguridad ya se puede consultar y exportar; se agrega el filtro «salto MFA por red».

## 7. Seguridad: riesgos y cómo se cubren
| Riesgo | Mitigación |
|---|---|
| Falsificar la IP con cabeceras (`X-Forwarded-For`) | Solo `RemoteIpAddress`. Si algún día se pone un proxy/CDN delante, hay que configurar proxies de confianza **antes** de encender el interruptor (mismo aviso que ya tiene `DeviceRateLimits`) |
| La IP pública del proveedor cambia y la hereda otro cliente | La IP de la localidad es **fija** (confirmado por Luis). Si algún día cambiara, hay que actualizar la lista; vencimiento opcional; revisión periódica |
| Wi-Fi de invitados u otro dispositivo en la misma red | Separar la red de invitados a nivel de red (sección 11); recuadro de confirmación al agregar la IP; aviso permanente en pantalla; la contraseña sigue siendo obligatoria y con bloqueo; las acciones sensibles siguen pidiendo MFA |
| Un administrador con sesión sin MFA amplía la lista | `[RequireAal2]` en la administración de la lista; los permisos y roles también son `[RequireAal2]` |
| Se roba una sesión sin MFA | La sesión sin MFA no pasa las acciones `[RequireAal2]`; fuera de la localidad el código sigue pidiéndose en un login nuevo |
| Entrar sin haber inscrito nunca el MFA | Regla 3 (inscripción obligatoria) |
| Cuentas privilegiadas | Regla 1 (admin de plataforma siempre MFA) y regla 2 (marca individual «siempre MFA») |
| Cambiar la contraseña con solo el correo (sobre todo estando en la red preferida) | La recuperación por correo exige el código MFA cuando la persona o la compañía lo requieren, **sin excepción por IP preferida** (sección 3.1) |

## 8. Piezas a construir (cuando se apruebe)
1. **Base de datos:** sección nueva en `logistica-db-update.sql` (tabla + índice por compañía + columna `Tenant.TrustedNetworkMaxEntries`, nula).
2. **Dominio/infraestructura:** entidad, configuración EF, `TrustedNetworkService` (CRUD con validaciones y auditoría) y el **método único** `IsTrustedNetwork(tenantId, ip)` (lógica de CIDR pura, con pruebas).
3. **Login:** una sola llamada a ese método en `AuthService.LoginAsync`, en el punto de la sección 3; el resto de `AuthService` no se toca (minimiza choques con `master`).
3b. **Recuperación de contraseña con MFA (sección 3.1):** en `AuthService.Password.cs` (`ResetPasswordWithLinkAsync`): decidir si hace falta el código, verificarlo sin consumir el enlace, contar los fallos para el bloqueo, bitácora; el contrato de `reset-password` agrega un campo opcional `mfaCode`; en la web, el campo «Código MFA» en la página de restablecimiento. **Es independiente del resto**: no depende del interruptor global ni de la lista de IPs, y puede ir en su propio commit y entregarse primero.
4. **API:** controlador de la lista (`admin.tenant` + `[RequireAal2]`) y `GET` de «mi IP»; contrato en `openapi.json` + `schema.d.ts`.
5. **Web:** panel «Seguridad» con la lista y la ayuda de la sección 5 (IP actual, tipo de entrada, vista previa de CIDR, avisos, «Probar una IP»); lógica de CIDR compartida y probada; textos es/en.
5a. **Recuadro de confirmación** «solo red interna» (campo obligatorio en el alta, constancia en la entrada y en el evento de bitácora).
5b. **Correo:** plantilla es/en del aviso de cambio de la lista y su envío por el servicio de correo existente.
6. **Tipos de evento** de seguridad y filtro en la bitácora.
7. **Pruebas:** recuperación de contraseña (con TOTP / compañía que exige MFA / marca individual: sin código no cambia y el enlace sigue vigente; con código correcto cambia; código incorrecto suma al bloqueo; código de recuperación vale; sin MFA inscrito pasa solo con el enlace; desde la IP preferida **no** se exime); CIDR (IPv4/IPv6/mapeada/rangos prohibidos y normalización), matriz de login (las 5 reglas y la tabla de combinaciones de la sección 3), que `[RequireAal2]` siga exigiendo MFA tras un ingreso por IP, bloqueo de cuenta intacto, tope por compañía (con valor, vacío = sin tope, bajarlo por debajo de lo que ya hay), vencimiento en blanco vs. con fecha, correo de aviso, y que con el interruptor apagado nada cambia.
8. **Manual y decisiones:** capítulo de seguridad + FAQ + este documento pasa a «decisiones».

## 9. Ramas (para no dañar `master`)
Estado al 2026-10-12 (consultado en solo lectura): `master` tiene 77 commits que `Depot-Implementation` no tiene y Depot tiene 20 que master no tiene; los lotes 34–37 están en las dos. Una fusión completa de Depot hacia master solo choca en **3 archivos** (`logistica-db-update.sql`, `docs/manual/faq.md`, `docs/plan-de-desarrollo-pendiente.md`), todos de «agregar al final».

Plan: hacerlo en **`Depot-Implementation`** (producción corre de ahí) como **commits pequeños y aislados** (uno por pieza de la sección 8), con el interruptor **apagado**, probarlo en **staging** con la IP real de la localidad, y luego pasar esos commits a `master` con `cherry-pick`, corriendo la suite de master encima. El único choque esperable es la sección nueva al final de `logistica-db-update.sql` (se conservan las dos). No se crean ramas nuevas sin que Luis lo pida.

## 10. Decisiones
**Tomadas (Luis, 2026-10-12):**
1. Las cuentas con permisos de administración de la compañía **sí** saltan el código al iniciar sesión desde la red preferida: las acciones sensibles ya piden MFA aparte (`[RequireAal2]`). Quien se quiera excluir lleva la marca individual «siempre MFA».
2. **Vencimiento:** se incluye como campo opcional; **en blanco no se toma en cuenta**; manda activar/desactivar.
3. **Correo** a los administradores cuando cambia la lista: sí.
4. **«Agregar esta IP»** (mostrar la IP actual): sí.
5. La IP de la localidad es **fija** (ya la usan para otro servicio, un IPsec en AWS que expone una impresora a una localidad externa).
6. **Máximo de entradas por compañía: un ajuste de cada compañía** (no fijo en el código ni en el servidor); vacío = sin tope.
7. **Sin IPsec / VPN:** queda descartado para esto; el diseño es solo «red preferida» por IP o rango público.
8. **Recuperar la contraseña por correo exige MFA** cuando la compañía (o la persona) lo requiere, **indistintamente de la IP preferida** (sección 3.1).

**Cerrada:** la localidad **no tiene red Wi-Fi de invitados**, así que todo equipo que salga por el firewall de la localidad (cable o Wi-Fi) entra sin código; la contraseña sigue siendo obligatoria. Si algún día se agrega una red de invitados que salga por el mismo firewall, habría que separarla **a nivel de red** (otra salida o IP): el sistema solo ve la IP pública y no puede distinguir una red Wi-Fi de otra.

**Abiertas:** ninguna por ahora.

## 11. Clientes con red de invitados (otras compañías) y lista para su TI
El sistema solo ve la **IP pública** con la que sale la conexión: no distingue una red Wi-Fi de otra detrás del mismo firewall. Por eso la solución es de red, no de pantalla:

1. **Separar la red de invitados:** que salga a internet por **otra IP pública** (VLAN de invitados con otra IP de salida, o una línea de internet aparte). En la lista solo se carga la IP de la **red interna**. Un equipo de invitados sale por una IP que no está en la lista y recibe el pedido de código como cualquiera de afuera. No requiere cambios en Teikem.
2. **El producto lo hace explícito:** recuadro de confirmación al agregar la IP (sección 5), aviso permanente, y la función **apagada por defecto**: cada compañía la enciende cuando sus respuestas de abajo son buenas.
3. **Lo que ya acota el riesgo:** el invitado necesita además una contraseña válida (con bloqueo por intentos); y una sesión que entró sin código **no** pasa las acciones sensibles (permisos, roles, contraseñas temporales, módulos): esas siguen pidiendo MFA. El riesgo residual es «contraseña robada + estar físicamente en esa red».

**Lista de preguntas para el departamento de TI del cliente (va en el manual y se hace ANTES de activar):**
- ¿Cuál es la IP pública de salida de la red interna de cada localidad? ¿Es **fija** (contratada con el proveedor)? Si cambia, ¿quién avisa?
- ¿Existe red Wi-Fi de invitados (o de visitantes, o pública)? Si existe: ¿sale por **la misma** IP pública que la red interna? Si sí, hay que separarla antes de activar.
- ¿Hay otros equipos que no son de la empresa en la red interna (personales, de contratistas, de proveedores)?
- ¿Cuántas localidades hay y cada una sale por su propia IP?
- ¿Quién en la compañía administra la lista y con qué cuenta (debe tener MFA inscrito)?
- ¿Quién revisa la lista y cada cuánto? (Se recomienda cada trimestre; las entradas pueden tener vencimiento.)

## 12. Fase 2 opcional: «IP preferida + equipo conocido» (NO está en el alcance de la fase 1)
Para un cliente que **no pueda** separar la red de invitados. El código solo se salta si la IP coincide **y** el navegador/equipo es uno que ya validó MFA antes (marca firmada, por usuario y por equipo, que el usuario o la administración pueden revocar). Un portátil de invitados no cumple la segunda condición aunque comparta la IP.
- Costo: el primer ingreso de cada equipo nuevo siempre pide el código; hace falta el registro de equipos conocidos, su pantalla de revocación y expiración, y pruebas.
- Se activaría como **opción por compañía**, solo para quien la necesite. Se decide más adelante, con un cliente real que la requiera.
