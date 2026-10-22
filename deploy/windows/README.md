# Instalar Teikem en Windows Server con IIS (API + web)


> ## ⛔ EN PRODUCCIÓN DESDE 2026-10-09
> - **`Diseño/logistica-db-estructura.sql` y `Diseño/logistica-db-seed.sql` están CONGELADOS: no se editan, jamás.** Un cambio de estructura **o de datos** va en el archivo **`Diseño/logistica-db-update.sql`** (UN solo archivo para estructura y datos, **idempotente**; se agrega al final) (ver [Diseño/logistica-db-update.sql](../../Diseño/logistica-db-update.sql)). `FrozenSqlTests` falla si se tocan.
> - **Recrear la base (`db-reset`, `scripts/recrear-base*.ps1`) está PROHIBIDO fuera de desarrollo.** En producción solo `db-update` (con simulación y respaldo).
> - La rama `Depot-Implementation` guarda el estado de la puesta en producción de Advance Depot, para dar soporte a su operación.

Un solo sitio de IIS (`Teikem`) sirve la web en `/` y el API en `/api`, con su propio grupo de aplicaciones. Una sola dirección
(`https://teikem.advancelogisticspr.com`), sin CORS. La base de datos es la que ya subió: el instalador **la deja al día** con la
versión del paquete (agrega lo que falte; nunca borra datos), con simulación y respaldo antes (ver «Actualizar»).

## Lo que hace el instalador (`instalar.ps1`)

| Paso | Detalle |
|---|---|
| IIS y .NET | Comprueba IIS y el **.NET 8 Hosting Bundle** (si falta, lo descarga de Microsoft y lo instala). |
| Configuración | Pregunta dominio y datos de la base (SQL Server). Genera la **llave que firma las sesiones**. Guarda todo en `appsettings.Production.local.json` dentro de la carpeta del sitio, legible solo por Administradores, SYSTEM y el grupo de aplicaciones. |
| Brevo | Si existen las **variables de entorno del sistema** `Brevo__ApiKey` y `Brevo__FromEmail`, las usa tal cual (no las pide ni las copia). Si no, las pregunta. |
| Base de datos | **Simula** (`db-update --dry-run`) y muestra qué le falta a la base; pide confirmar que ya hay respaldo; aplica los cambios en una sola transacción y corre los datos de referencia y los permisos. Va **antes** de copiar los archivos: si falla, el sitio sigue con la versión anterior. |
| IIS | Crea el grupo de aplicaciones `Teikem` (sin código administrado, siempre encendido) y el sitio `Teikem` en `C:\inetpub\teikem` con el nombre de dominio en el puerto 80, y la regla de firewall para 80/443. |
| Comprobación | Llama a `/health` y, si no responde, muestra los últimos eventos del módulo de IIS. |
| HTTPS | Sin `-LetsEncrypt`, **no instala ni emite certificados**: el sitio toma el certificado del proceso de Let's Encrypt que ya tiene el servidor y, cuando ya tiene su enlace HTTPS, el instalador (al volver a correrlo) activa la redirección de http a https. Con `-LetsEncrypt -CorreoLetsEncrypt <correo>` (2026-10-19; así lo usa `instalar-test.ps1`) pide el certificado él mismo con win-acme (`letsencrypt.ps1`), deja la tarea de renovación automática y activa la redirección en la misma corrida. |

## Pasos

**1. En su computadora**, crear el paquete (compila la web y el API):

```powershell
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\deploy\windows\crear-paquete-windows.ps1"
```

Deja `F:\Download\TeikemDeploy\teikem-windows-AAAAMMDD-HHMM.zip`.

**2. Copiar el .zip al servidor** (Escritorio remoto: pegar el archivo, o una carpeta compartida) y descomprimirlo:

```powershell
Expand-Archive C:\Temp\teikem-windows-*.zip C:\Temp\teikem -Force
```

**3. En el servidor**, PowerShell **como Administrador**, dentro de esa carpeta:

```powershell
cd C:\Temp\teikem
powershell -ExecutionPolicy Bypass -File .\instalar.ps1
```

**4. Certificado:** que su proceso de Let's Encrypt agregue el sitio nuevo (`Teikem`, host `teikem.advancelogisticspr.com`).
Requisitos habituales: el DNS del dominio apunta a este servidor y el puerto 80 está abierto para la validación. El instalador
imprime el id del sitio por si lo necesita. **Cuando el sitio ya tenga HTTPS, vuelva a correr `instalar.ps1`** (conserve la
configuración): activa la redirección de http a https y deja todo listo.

## Actualizar a una versión nueva (API + web + base de datos)

Repita los pasos 1 a 3 con el .zip nuevo. Al preguntar si conserva la configuración, responda que sí (el valor por defecto). En orden:

1. El sitio queda en mantenimiento (`app_offline.htm`).
2. **Base de datos, simulación:** muestra la lista de lo que falta (tablas, columnas, índices, checks, FKs, vistas). No cambia nada.
   Si aparece **«REVISAR A MANO»**, es una diferencia que no se arregla sola (por ejemplo, una columna que ahora debe ser obligatoria y
   tiene datos vacíos): el resto sí se aplica; esa se revisa después.
3. **Respaldo:** el instalador pide confirmar que ya lo hizo. Ejemplo (en el SQL Server de la base):
   `BACKUP DATABASE [Teikem] TO DISK = N'C:\Respaldos\Teikem-antes.bak' WITH COMPRESSION`
4. **Base de datos, aplicar:** todo el esquema va en una transacción (si algo falla, la base queda como estaba y el sitio **no** se
   toca). Después corre el seed (datos de referencia) y los permisos nuevos.
5. Se reemplazan los archivos del sitio; la configuración, la llave de sesiones y el certificado no se tocan.

Opciones del instalador: `-SinBase` (no toca la base de datos) y `-RespaldoHecho` (no pregunta por el respaldo, para correrlo sin teclado).
Para ver el plan sin instalar nada: en la carpeta del paquete, `dotnet app\Teikem.Api.dll db-update --dry-run` con la cadena de conexión
en la variable `ConnectionStrings__Teikem`, `Jwt__SigningKey` (cualquier texto largo), `Database__RepoRoot` = la carpeta del paquete y `Database__DesignFolder` = `db`.

**Cómo funciona `db-update`:** el script de estructura no se puede volver a correr sobre una base que ya existe, así que se aplica a
una base **temporal vacía** (en el mismo servidor; hace falta permiso para crear bases y se borra sola al terminar), se compara con la
real y se aplica **solo lo que falta**. Nunca borra tablas ni columnas (lo que sobra se informa); solo retira una columna obsoleta
`NOT NULL` sin valor por defecto si su tabla está **vacía** (así lo hizo el cambio de Rentas con la tabla de cargos). Una columna que
solo se ensancha (texto más largo) o pasa a aceptar nulos se ajusta sola; cualquier otro cambio de tipo se avisa.

## Detalles

- **Variables de Brevo:** el proceso del sitio las lee al arrancar; si las cambia en el sistema, haga `iisreset` (o reinicie el grupo
  de aplicaciones `Teikem`).
- **Base de datos:** el instalador usa autenticación de SQL Server. Para autenticación de Windows, edite la cadena en
  `appsettings.Production.local.json` (`Integrated Security=True`, sin usuario ni clave) y dé acceso en SQL Server a `IIS APPPOOL\Teikem`.
- **Estructura de la base:** el API **no** la modifica al arrancar; la actualiza el instalador con `db-update` (ver arriba), siempre antes
  de copiar los archivos. El usuario SQL de la cadena de conexión necesita permiso para crear tablas y bases (la temporal de la comparación);
  si solo tiene permisos de datos, corra el instalador con un usuario con más permisos o use `-SinBase` y aplique el cambio por otro medio.
- **Seguridad:** el API se niega a arrancar en Producción con la llave de desarrollo; la configuración del servidor nunca va en el
  paquete (el paquete no trae ningún `appsettings.*.json` aparte del base). Cambiar la llave de sesiones (`Jwt:SigningKey`) cierra todas las sesiones.
- **Si el API no arranca:** Visor de eventos > Registros de Windows > Aplicación (origen *IIS AspNetCore Module V2*), o ponga
  `stdoutLogEnabled="true"` en `web.config` y mire `logs\`.
- Los primeros usuarios que entren pasan por el **primer ingreso** (correo, contraseña propia y MFA).

## Staging en el mismo servidor (`teikem.isaxpr.com`)

Mismo paquete, otro sitio: cada ambiente tiene su sitio y grupo de aplicaciones de IIS, su carpeta, su `appsettings.Production.local.json`
(con **su** cadena de conexión y **su** llave de sesiones), su carpeta de llaves de Data Protection y su base de datos. Nada se comparte con producción.

```powershell
cd C:\Temp	eikem      # el paquete descomprimido (el mismo .zip que producción, o uno más nuevo para probarlo primero)
powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -Sitio TeikemStaging -Carpeta C:\inetpub	eikem-staging
```

Responda: dominio `teikem.isaxpr.com`, nombre de la base `Teikem_Staging` (con su servidor, usuario y contraseña). Antes: el DNS de
`teikem.isaxpr.com` debe apuntar a este servidor; después, que el proceso de Let's Encrypt agregue el sitio `TeikemStaging` y se vuelve a
correr el mismo comando para activar la redirección a https. Para actualizar staging: el mismo comando con el .zip nuevo (conserve la configuración).

- La base `Teikem_Staging` se deja al día con `db-update` (igual que producción: simula, pide respaldo y aplica). Si está vacía, crea todo.
- Las variables de sistema de Brevo son las mismas: staging **sí envía correos reales**. Si no quiere eso, déjelas vacías al pedirlas y use otra llave solo para staging.
- **APK de staging:** `scripts\construir-apk.ps1 -Staging` (paquete `com.teikem.almacen.staging`, icono morado con cinta STAGING, servidor
  `https://teikem.isaxpr.com`); se instala junto a la de producción. Ver `docs/mobile/actualizar-apk.md`.

## Test en el mismo servidor (`test.advancelogisticspr.com`, 2026-10-19)

Tercer ambiente, junto a producción y staging, con **todo lo que hay en `master`**: es donde se prueba lo nuevo antes de que llegue a producción
(staging guarda la versión de producción). Un solo comando lo pone y lo actualiza:

| Qué | Dónde |
|---|---|
| API + web | `https://test.advancelogisticspr.com` — sitio IIS `TeikemTest`, carpeta `C:\inetpub\teikem-test`, base `Teikem_Test`. Cinta roja **TEST** arriba de todas las pantallas, etiqueta al pie y `[TEST]` en el título de la pestaña (igual que staging; lo pone `Hosting:EnvironmentLabel = TEST`). |
| Portal del cliente | `https://test-portal.advancelogisticspr.com` — sitio IIS `TeikemTestPortal`, carpeta `C:\inetpub\teikem-test-portal`: la web de `app-cliente` compilada apuntando al API de test, con la misma cinta TEST arriba y abajo. Es la **primera publicación del portal** (producción aún no lo tiene). |
| Descargas de las apps | `https://test.advancelogisticspr.com/descargas/` — página para bajar los APK de test (Almacén TEST y Clientes TEST) desde el teléfono. |
| Certificados | Let's Encrypt para los dos nombres, pedidos por el instalador con **win-acme** (lo descarga a `C:\Tools\win-acme` si el servidor no lo tiene) y renovados solos por su tarea programada. |

**1. En su computadora** (hace falta Node y el SDK de .NET; para los APK, Android Studio):

```powershell
# los APK de test (opcional, pero sin ellos la página de descargas dice que aún no se publicaron); piden la contraseña de la llave de firma
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\construir-apk.ps1" -Test
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\construir-apk.ps1" -App cliente -Test
# el paquete: API + web + scripts de la base + portal compilado + página de descargas con los APK más recientes de F:\Download\TeikemApp
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\deploy\windows\crear-paquete-windows.ps1" -Test
```

Deja `F:\Download\TeikemDeploy\teikem-test-AAAAMMDD-HHMM.zip`.

**2. Antes de la primera vez:** el DNS de `test.advancelogisticspr.com` y de `test-portal.advancelogisticspr.com` apunta a este servidor (registros A, como los de producción)
y el puerto 80 llega desde Internet (Let's Encrypt valida por http). La base `Teikem_Test` puede existir vacía (el instalador crea todo) o ser una copia de otra.

**3. En el servidor**, PowerShell **como Administrador**, en la carpeta del .zip descomprimido:

```powershell
Expand-Archive C:\Temp\teikem-test-*.zip C:\Temp\teikem-test -Force
cd C:\Temp\teikem-test
powershell -ExecutionPolicy Bypass -File .\instalar-test.ps1
```

La primera vez pregunta: servidor, usuario y contraseña de SQL Server (la base ya viene como `Teikem_Test`) y el correo para la cuenta de Let's Encrypt
(se recuerda en `C:\inetpub\teikem-test\letsencrypt-correo.txt`). Luego hace solo: base al día (`db-update`: simula, pide confirmar el respaldo, aplica),
sitio del API y la web con la etiqueta TEST y el origen del portal en CORS, página de descargas, certificado y redirección a https; después el sitio del portal
(instala el módulo URL Rewrite de IIS si falta, para que todas las rutas del portal abran `index.html`), su certificado y su redirección.

**Actualizar test:** repetir 1 y 3 con el .zip nuevo y responder que **sí** conserva la configuración. Los APK que ya estaban en `wwwroot\descargas\apk` se
conservan; los que trae el paquete los reemplazan. Opciones: `-SinBase`, `-RespaldoHecho`, `-SinPortal`, `-CorreoLetsEncrypt <correo>`.

- **Correo redirigido (2026-10-19):** test usa las mismas llaves de Brevo que producción, pero **nadie real recibe nada**: el instalador pone `Email:RedirectTo = teikem@cerevelo.com` y todo correo (clientes, consignatarios, staff) va a ese buzón, con dirección «más» por destinatario original (`teikem+cliente_suempresa.com@cerevelo.com`, que Google Workspace entrega en el mismo buzón), el asunto `[TEST → cliente@suempresa.com] …` y una franja roja al inicio del cuerpo diciendo para quién iba. Excepción: `Email:ExceptDomains = ["cerevelo.com"]`, para que los códigos de MFA y de primer ingreso del equipo lleguen a cada quien. Se cambia con `-CorreoRedirigir` y `-CorreoRedirigirExcepto` de `instalar-test.ps1` (o editando la sección `Email` de `appsettings.Production.local.json` del sitio y reiniciando el grupo de aplicaciones). `instalar.ps1` se niega a ponerla en el sitio de producción.
- **Direcciones de los enlaces de los correos (2026-10-22):** el instalador escribe `Auth:PasswordReset:WebBaseUrl = https://<dominio>` si falta (sin ella **no sale** el correo de «Olvidé mi contraseña», sin avisar) y, con `-UrlPortal <url>`, `Portal:WebBaseUrl` (a donde apunta el enlace del correo de la **invitación al portal**: la del usuario del cliente y la de su consignatario; sin ella esas invitaciones no salen). `instalar-test.ps1` lo pasa solo (`https://test-portal…`). Para un sitio ya instalado: volver a correr el instalador con `-UrlPortal`, o editar `appsettings.Production.local.json` y reiniciar el grupo de aplicaciones.
- (histórico) `Auth:PasswordReset:WebBaseUrl` (enlaces de los correos de recuperar contraseña y confirmar computadora) no la pone el instalador; si se necesita en test, agregarla a `appsettings.Production.local.json` de `TeikemTest` (una sola para la web y el portal; ver decisiones).
- Para copiar la base de producción sobre `Teikem_Test`: `scripts\restaurar-staging.bat` sirve cambiando `$Destino` a `Teikem_Test`, `$PoolIIS` a `TeikemTest` y `$LlavesStaging` a `C:\ProgramData\Teikem\keys-TeikemTest` (el destino debe llevar «test» o «staging» en el nombre).
- Las apps: `scripts\construir-apk.ps1 -Test` (Almacén TEST: paquete `com.teikem.almacen.test`, icono verde con cinta TEST, servidor `https://test.advancelogisticspr.com`) y `-App cliente -Test` (Clientes TEST: `com.teikem.cliente.test`). Se instalan junto a las de producción y staging. Ver `docs/mobile/actualizar-apk.md`.

## IP preferida: encenderla en el servidor

La lista de IPs preferidas (Seguridad y auditoría → Sesiones y MFA) está **apagada por defecto** en cada servidor. Para que de verdad se salte el código MFA al iniciar sesión, agregue a `appsettings.Production.local.json` del sitio (el de producción y/o el de staging) y reinicie el grupo de aplicaciones:

```json
"Auth": { "TrustedNetworks": { "Enabled": true } }
```

- Es un interruptor **por servidor** (no por compañía): con él apagado la lista se puede preparar pero no salta el código, y el panel lo avisa. Cada compañía decide qué IPs carga.
- La IP del cliente se toma de la conexión real. Si algún día se pone un proxy o CDN delante del sitio, configure primero los proxies de confianza (si no, todas las conexiones se verían con la IP del proxy) **antes** de encenderlo.
- La base se actualiza con el instalador (`db-update`: la tabla `TenantTrustedNetwork`, la columna `Tenant.TrustedNetworkMaxEntries` y el tipo de evento `TRUSTED_NETWORK`).
- Los avisos por correo a los administradores usan el mismo proveedor (Brevo) que el resto de los correos.
