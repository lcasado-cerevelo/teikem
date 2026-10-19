# Ambiente de TEST (`test.advancelogisticspr.com`) — decisiones (2026-10-19)

Pedido de Luis: un tercer ambiente en el mismo servidor Windows (ya con producción y staging) con **todo lo que hay en `master`**, que diga TEST por todos lados
(cinta roja arriba y etiqueta al pie como staging), con certificado de Let's Encrypt que se renueve solo, las apps móviles apuntando a él, el portal del cliente
publicado en `test-portal.advancelogisticspr.com`, un instalador simple y una página para bajar las apps de test.

## Qué se construyó

| Pieza | Dónde |
|---|---|
| Instalador de test (un comando) | `deploy/windows/instalar-test.ps1`: llama a `instalar.ps1` con sitio `TeikemTest`, carpeta `C:\inetpub\teikem-test`, dominio, base `Teikem_Test`, etiqueta TEST, Let's Encrypt, CORS del portal y la página de descargas; luego crea el sitio estático del portal (`TeikemTestPortal`, `C:\inetpub\teikem-test-portal`, URL Rewrite para la SPA) y su certificado. Rechaza el sitio/carpeta de producción. |
| Let's Encrypt en el instalador | `deploy/windows/letsencrypt.ps1` (win-acme: lo busca, si no lo descarga a `C:\Tools\win-acme`; `--source iis --siteid --host --installation iis --setuptaskscheduler`) y las opciones `-LetsEncrypt -CorreoLetsEncrypt` de `instalar.ps1`. El correo se recuerda en `letsencrypt-correo.txt` del sitio. |
| `instalar.ps1` | Opciones nuevas `-Dominio`, `-DbNombre`, `-Etiqueta`, `-LetsEncrypt`, `-CorreoLetsEncrypt`, `-OrigenesExtra`, `-ConDescargas`. Sin ellas se comporta igual que antes (producción y staging no cambian). `wwwroot\descargas` queda fuera del `robocopy /MIR` y los APK subidos se conservan. |
| Paquete | `crear-paquete-windows.ps1 -Test`: además del API y la web, exporta `app-cliente` para la web (`expo export --platform web`, `APP_VARIANT=test`, API `https://test.advancelogisticspr.com`) a `portal\`, arma `descargas\` con `deploy/descargas-test/index.html`, logo, favicon, los APK más recientes de `F:\Download\TeikemApp` y `versions.json`; incluye `letsencrypt.ps1` e `instalar-test.ps1`. Sale `teikem-test-AAAAMMDD-HHMM.zip`. |
| API | `Program.cs`: los archivos estáticos sirven `.apk` como `application/vnd.android.package-archive`. La etiqueta TEST ya la soportaba (`Hosting:EnvironmentLabel` → `/health` → cinta de la web). |
| Web (`web-app`) | Sin cambios: la cinta (`EnvironmentRibbon`) muestra la etiqueta que diga `/health` (franja roja arriba, píldora abajo con el nombre de la base, `[TEST]` en el título). |
| App del almacén | Variante `test` en `app.config.ts` (paquete `com.teikem.almacen.test`, nombre «Teikem Almacén TEST», esquema `teikem-almacen-test`, iconos `assets/test` verdes con cinta TEST); `EnvironmentBanner` y `buildLabel` dicen TEST; `npm run brand:icons:test`. DataWedge toma el perfil del sufijo del paquete (`TeikemAlmacenTest`). |
| App del cliente | Variante `test` (y `staging` con iconos propios), nombre «Teikem Clientes TEST», `EnvironmentBanner` (franja arriba, `[TEST]` en el título web) y `EnvironmentFooter` (al pie) en `_layout.tsx`, textos `environment.notProduction` es/en, plugin de firma `withAndroidReleaseSigning` (copiado de la app del almacén), `extra.buildStamp`. |
| Scripts de APK | `construir-apk.ps1 -Test` y `-App cliente` (contadores `apk-versioncode-cliente[-staging|-test].txt`, nombres `TeikemTest-*`, `TeikemClientes*-*`); `actualizar-aparatos.ps1 -Test`, `-App cliente`. `scripts/brand-icons.mjs test|staging [almacen|cliente]`. |
| Página de descargas de test | `deploy/descargas-test/index.html`: cinta TEST, tarjetas Almacén TEST y Clientes TEST, instrucciones de instalación; lee `versions.json`. Se sirve en `https://test.advancelogisticspr.com/descargas/`. |
| Documentos | `deploy/windows/README.md` («Test en el mismo servidor»), `docs/mobile/actualizar-apk.md`, `deploy/descargas-apk/README.md`. |

## Cómo se probó
- `jest` de `EnvironmentBanner` en las dos apps (variantes staging, test, producción, desarrollo) y de i18n del cliente; `tsc -b` y `oxlint` de las dos apps sin errores (solo avisos previos).
- `expo export --platform web` de `app-cliente` con `APP_VARIANT=test`: el bundle lleva `appVariant:"test"`, la URL `test.advancelogisticspr.com` y el título «Teikem Clientes TEST».
- `dotnet build` de `Teikem.Api` en Release: 0 errores.
- Los seis scripts PowerShell pasan el analizador sintáctico. **No se corrió el instalador contra el servidor real** (eso lo hace Luis con el paquete).
- Iconos generados: `app-almacen/assets/test`, `app-cliente/assets/{staging,test}`.

## Decisiones tomadas (para revisar)
1. **win-acme** como cliente de Let's Encrypt en Windows: se busca uno ya instalado (PATH, `C:\Tools\win-acme`, `C:\Program Files\win-acme`, `C:\win-acme*`); si el servidor usa otro proceso (p. ej. Certify The Web), los sitios nuevos igual quedan con su enlace https y la tarea de win-acme solo renovará los suyos.
2. **El portal es un sitio IIS estático aparte** (`TeikemTestPortal`), no una carpeta del API: así vive en su propio dominio como manda el diseño del portal. Necesita el módulo **URL Rewrite** (el instalador lo descarga de Microsoft si falta; si no se pudiera, usa la página de error 404 → `index.html`).
3. **CORS**: el API de test admite el origen `https://test-portal.advancelogisticspr.com` (lo agrega el instalador a `Cors:Origins`).
4. **`Auth:PasswordReset:WebBaseUrl`** es una sola para la web del staff y el portal (el enlace de «confirmar computadora» del portal y el de recuperar contraseña del staff salen de la misma opción). En test no se pone; si hace falta, decidir cuál dominio lleva (o separar la opción del portal, que sería un cambio del API).
5. **Brevo**: test usa las variables del sistema igual que staging, así que **envía correos reales**; para evitarlo, dejarlas vacías al instalar.
6. **Base `Teikem_Test`**: el instalador la lleva al día con `db-update` (si está vacía la crea completa). Para empezar con datos reales, `restaurar-staging.bat` sirve ajustando destino, grupo de aplicaciones y carpeta de llaves (ver README).
7. **Color de test**: fondo **verde** (`#047857`) en los iconos de las apps para distinguirlo del morado de staging; la cinta y la etiqueta son rojas como en staging.
8. Las variantes `staging`/`test` del portal del cliente comparten el `EnvironmentBanner` nuevo; producción sigue sin nada.
9. Los APK de test **no se construyeron** en esta sesión (piden la contraseña de la llave de firma y compilan Android): `construir-apk.ps1 -Test` y `-App cliente -Test` antes de crear el paquete; si no están, la página de descargas lo dice y apaga el botón.
