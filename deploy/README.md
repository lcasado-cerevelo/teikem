# Instalar Teikem en un servidor Linux (Docker) y construir el APK

> **¿Su servidor es Windows Server con IIS?** Use [windows/README.md](windows/README.md) (instalador `instalar.ps1`). Esta guía de Docker
> es para un servidor Linux.

Un paquete con todo: el API, la web y un servidor (Caddy) que atiende HTTPS con certificado **Let's Encrypt automático**
(se pide solo y **se renueva solo**). La base de datos no va aquí: se usa la que ya subió.

```
Internet ──► Caddy (80/443, HTTPS) ──► /        la web (archivos estáticos)
                                   └─► /api/*   el API (ASP.NET Core, solo dentro de la red de Docker)
                                                  └─► SQL Server (el que ya tiene)
```

Una sola dirección para todo: `https://teikem.advancelogisticspr.com` (la web en `/` y el API en `/api`). No hay CORS.

## Lo que necesita antes

| Qué | Detalle |
|---|---|
| Servidor Linux | Ubuntu 22.04/24.04 (o Amazon Linux 2023), con acceso por SSH y `sudo`. |
| DNS | Un registro **A** de `teikem.advancelogisticspr.com` hacia la **IP pública** del servidor (sin esto Let's Encrypt no puede emitir el certificado). |
| Puertos | **80 y 443** abiertos hacia Internet (en AWS: grupo de seguridad de la instancia) y sin otro programa usándolos. |
| Base de datos | Que el servidor **llegue** al SQL Server (servidor, puerto, usuario y contraseña). Use un usuario propio de Teikem, no `sa`. |
| Correo | `Brevo__ApiKey` y `Brevo__FromEmail` como **variables de entorno del servidor** (ver abajo). |

## 1. Instalar Docker (si no lo tiene)

**No hace falta hacerlo a mano: `install.sh` lo instala si falta.** Si prefiere hacerlo usted, desde su PowerShell de Windows
(OpenSSH ya viene con Windows 10/11) entre al servidor y corra los comandos:

```powershell
ssh -i "C:\ruta\llave.pem" ubuntu@IP-DEL-SERVIDOR
```

Ubuntu / Debian:

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER
exit            # y vuelva a entrar por SSH para que tome el grupo
docker --version && docker compose version
```

Amazon Linux 2023 (usuario `ec2-user`):

```bash
sudo dnf install -y docker
sudo systemctl enable --now docker
sudo usermod -aG docker ec2-user
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -SL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64 -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose
exit            # y vuelva a entrar
docker --version && docker compose version
```

> Si su servidor es **Windows Server**, este paquete no sirve tal cual (las imágenes son de Linux). Avise y se arma el instalador para IIS.

## 2. Crear el paquete (en su computadora)

```powershell
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\deploy\crear-paquete.ps1"
```

Deja `F:\Download\TeikemDeploy\teikem-deploy-AAAAMMDD-HHMM.tar.gz` (solo el código necesario, de lo que está en git) y le
imprime los comandos de los pasos siguientes.

## 3. Subirlo e instalar (en el servidor)

```powershell
scp -i "C:\ruta\llave.pem" "F:\Download\TeikemDeploy\teikem-deploy-AAAAMMDD-HHMM.tar.gz" ubuntu@IP-DEL-SERVIDOR:~/
ssh -i "C:\ruta\llave.pem" ubuntu@IP-DEL-SERVIDOR
```

```bash
mkdir -p ~/teikem && tar xzf ~/teikem-deploy-*.tar.gz -C ~/teikem
cd ~/teikem/deploy && bash install.sh
```

`install.sh` pregunta: el dominio, el correo para los avisos de Let's Encrypt, los datos de la base (servidor, base, usuario,
contraseña) y Brevo. Genera la llave que firma las sesiones, guarda todo en `deploy/.env` (solo legible por su usuario),
construye (la primera vez tarda varios minutos), levanta todo y comprueba que el API responda. Al final le dice qué abrir.

### Las llaves de Brevo (variables del servidor)

Los contenedores **no heredan** las variables del servidor, así que el instalador las copia a `deploy/.env` (solo legible por su
usuario). Las busca en el entorno de quien corre el script y, si no están ahí, en `/etc/environment`:

- Si encuentra `Brevo__ApiKey` y `Brevo__FromEmail` (y opcionalmente `Brevo__FromName`), **las usa sin preguntar**.
- `bash update.sh` las vuelve a copiar cada vez: si cambia la llave en el servidor, basta con actualizar.
- Si las definió solo para otro usuario (por ejemplo `root` o un servicio), no las verá: en ese caso las pide o las puede escribir
  en `deploy/.env`. Para comprobar qué ve el script: `printenv Brevo__ApiKey Brevo__FromEmail`.

## 4. Actualizar a una versión nueva

Repita los pasos 2 y 3, pero el último comando es:

```bash
cd ~/teikem/deploy && bash update.sh
```

La configuración (`.env`) y los certificados se conservan. Al subir una carpeta nueva encima, **no borre `deploy/.env`**.

## Certificado digital (Let's Encrypt)

- Lo pide y lo **renueva solo** Caddy (aprox. 30 días antes de vencer); los certificados viven en el volumen `caddy_data`.
- Redirige `http` a `https` y envía HSTS.
- Si algún día no pudiera renovar, Let's Encrypt avisa al correo que dio en la instalación.
- Problemas comunes: el DNS aún no apunta al servidor, el puerto 80 o 443 está cerrado, o se probó muchas veces (Let's Encrypt limita
  los intentos fallidos por hora). Mire el motivo con `docker compose logs web`.

## Operación

```bash
cd ~/teikem/deploy
docker compose ps                 # estado
docker compose logs -f api        # registro del API
docker compose logs -f web        # registro de Caddy / certificado
docker compose restart api        # reiniciar el API
```

- **Estructura de la base de datos:** el API **no** la modifica al arrancar. Si una versión nueva cambia la estructura, hay que
  aplicar el cambio a la base antes de actualizar (el instalador no corre `db-init`; ver `docs/` del lote correspondiente).
- **Seguridad:** el API no se publica fuera de la red de Docker; la llave de sesiones se genera en el servidor y el API se niega a
  arrancar en Producción con la llave de desarrollo. Cambiar `Jwt__SigningKey` cierra todas las sesiones.
- Los primeros usuarios que entren pasan por el **primer ingreso** (correo, contraseña propia y MFA).

## APK de la aplicación móvil

En su computadora (necesita Android Studio con SDK 36 y Node):

```powershell
powershell -ExecutionPolicy Bypass -File "F:\Visual Studio 2022\Projects\teikem\scripts\construir-apk.ps1"
```

- Deja `F:\Download\TeikemApp\TeikemAlmacen-<versión>.apk`, **firmado con su propia llave** y solo con HTTPS.
- La primera vez crea la llave en `%USERPROFILE%\.teikem\teikem-release.jks` y le pide una contraseña. **Guarde copia del archivo y de la
  contraseña**: sin ellos no se pueden publicar actualizaciones de la misma app (ni subirla a Play Store).
- El servidor (`https://teikem.advancelogisticspr.com`) viene escrito de fábrica en la pantalla de registro del aparato
  (cámbielo con `-ApiUrl` si usa otra dirección).
- Al terminar devuelve la carpeta `app-almacen\android` a la variante de desarrollo (para seguir probando en el emulador con HTTP);
  use `-SinRestaurar` para dejarla en producción.
- Instalar en un aparato: copie el APK al teléfono y ábralo (permita "instalar apps desconocidas"), o `adb install -r archivo.apk`.
- Para subir una versión nueva, aumente `version` (`package.json`/`app.config.ts`) y `android.versionCode` (`app.config.ts`) antes de compilar.
