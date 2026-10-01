# Instalar Teikem en Windows Server con IIS (API + web)

Un solo sitio de IIS (`Teikem`) sirve la web en `/` y el API en `/api`, con su propio grupo de aplicaciones. Una sola dirección
(`https://teikem.advancelogisticspr.com`), sin CORS. La base de datos no se toca: se usa la que ya subió.

## Lo que hace el instalador (`instalar.ps1`)

| Paso | Detalle |
|---|---|
| IIS y .NET | Comprueba IIS y el **.NET 8 Hosting Bundle** (si falta, lo descarga de Microsoft y lo instala). |
| Configuración | Pregunta dominio y datos de la base (SQL Server). Genera la **llave que firma las sesiones**. Guarda todo en `appsettings.Production.local.json` dentro de la carpeta del sitio, legible solo por Administradores, SYSTEM y el grupo de aplicaciones. |
| Brevo | Si existen las **variables de entorno del sistema** `Brevo__ApiKey` y `Brevo__FromEmail`, las usa tal cual (no las pide ni las copia). Si no, las pregunta. |
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

## Actualizar a una versión nueva

Repita los pasos 1 a 3 con el .zip nuevo. Al preguntar si conserva la configuración, responda que sí (el valor por defecto): los
archivos se reemplazan (con `app_offline.htm` mientras tanto) y la configuración, la llave de sesiones y el certificado no se tocan.

## Detalles

- **Variables de Brevo:** el proceso del sitio las lee al arrancar; si las cambia en el sistema, haga `iisreset` (o reinicie el grupo
  de aplicaciones `Teikem`).
- **Base de datos:** el instalador usa autenticación de SQL Server. Para autenticación de Windows, edite la cadena en
  `appsettings.Production.local.json` (`Integrated Security=True`, sin usuario ni clave) y dé acceso en SQL Server a `IIS APPPOOL\Teikem`.
- **Estructura de la base:** el API **no** la modifica al arrancar. Si una versión nueva cambia la estructura, hay que aplicar el cambio
  antes de actualizar.
- **Seguridad:** el API se niega a arrancar en Producción con la llave de desarrollo; la configuración del servidor nunca va en el
  paquete (el paquete no trae ningún `appsettings.*.json` aparte del base). Cambiar la llave de sesiones (`Jwt:SigningKey`) cierra todas las sesiones.
- **Si el API no arranca:** Visor de eventos > Registros de Windows > Aplicación (origen *IIS AspNetCore Module V2*), o ponga
  `stdoutLogEnabled="true"` en `web.config` y mire `logs\`.
- Los primeros usuarios que entren pasan por el **primer ingreso** (correo, contraseña propia y MFA).
