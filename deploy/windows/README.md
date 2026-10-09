# Instalar Teikem en Windows Server con IIS (API + web)


> ## ⛔ EN PRODUCCIÓN DESDE 2026-10-09
> - **`Diseño/logistica-db-estructura.sql` y `Diseño/logistica-db-seed.sql` están CONGELADOS: no se editan, jamás.** Un cambio de estructura **o de datos** va en un archivo **nuevo e idempotente** en `Diseño/cambios/NNNN-descripcion.sql` (ver [Diseño/cambios/README.md](../../Diseño/cambios/README.md)). `FrozenSqlTests` falla si se tocan.
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
| HTTPS | **No instala ni emite certificados.** El sitio toma el certificado del proceso de Let's Encrypt que ya tiene el servidor. Cuando el sitio ya tiene su enlace HTTPS, el instalador (al volver a correrlo) activa la redirección de http a https. |

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
