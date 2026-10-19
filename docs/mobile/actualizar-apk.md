# Actualizar la app del almacén sin desinstalar (2026-10-09)

Android conserva la configuración de la app (servidor, registro del aparato, PIN, datos sin enviar) si el APK nuevo se instala **encima**, siempre que:
1. tenga el **mismo paquete** (`com.teikem.almacen`),
2. esté firmado con la **misma llave** (`%USERPROFILE%\.teikem\teikem-release.jks`),
3. tenga un **código de versión mayor** que el instalado.

## Pasos
1. `scripts\construir-apk.ps1` — el mismo que crea el APK. Sube solo el código de versión (el primero que sale es el 2; los aparatos instalados traen el 1; se recuerda en `%USERPROFILE%\.teikem\apk-versioncode.txt`) y usa la misma llave. Deja `TeikemAlmacen-<versión>-c<código>.apk` en `F:\Download\TeikemApp`. Opciones: `-VerCodigo` (solo muestra versión y código que saldrían, sin compilar), `-Version 1.1.0` (versión visible; por defecto la de `package.json`), `-CodigoVersion <n>` (fija el código; debe ser mayor que el último, salvo `-PermitirBajar`).
2. Instalar encima, de cualquiera de las dos formas:
   - **Por USB, varios aparatos:** `scripts\actualizar-aparatos.ps1` (depuración USB activada). Hace `adb install -r` a cada aparato conectado.
   - **A mano:** copiar el APK al aparato, abrirlo y aceptar «Actualizar».
3. Abrir la app: la base local se migra sola (v9) y la primera sincronización baja los productos completos.

## Nunca
- **No desinstalar** antes: se pierde la configuración y lo que no se haya enviado.
- No firmar con otra llave (Android lo rechaza; la única salida sería desinstalar).
- Si el APK instalado se hizo con otra llave o no se conserva esa llave, la única vía es desinstalar e instalar de nuevo.

## APK de staging (2026-10-12)
`scripts\construir-apk.ps1 -Staging` hace la app del ambiente de pruebas: paquete **`com.teikem.almacen.staging`** (Android la trata como otra app, así que
queda **instalada al lado** de la de producción), nombre «Teikem Almacén STAGING», icono morado con cinta STAGING, servidor de fábrica
`https://teikem.isaxpr.com` y la palabra STAGING al pie de Inicio. Misma llave de firma; contador de código propio
(`%USERPROFILE%\.teikempk-versioncode-staging.txt`); sale como `TeikemStaging-<versión>-c<código>.apk`. Para subirle la versión: igual que
producción (cada compilación sube el código; `-Version` cambia el nombre visible). Instalar encima por USB: `scriptsctualizar-aparatos.ps1 -Staging`.
En los Zebra, cada app crea su propio perfil de DataWedge (`TeikemAlmacen` y `TeikemAlmacenStaging`), así que no se pisan.
Los iconos salen de `npm run brand:icons:staging` (en `app-almacen`).

## APK de test (2026-10-19)
`scripts\construir-apk.ps1 -Test` hace la app del ambiente de **TEST** (`https://test.advancelogisticspr.com`, lo que hay en `master`): paquete **`com.teikem.almacen.test`**
(otra app más: queda instalada al lado de la de producción y la de staging), nombre «Teikem Almacén TEST», icono **verde** con cinta TEST, la palabra TEST al pie de Inicio y la franja roja
«TEST · AMBIENTE DE PRUEBAS» arriba de todas las pantallas. Misma llave de firma; contador propio (`%USERPROFILE%\.teikem\apk-versioncode-test.txt`); sale como
`TeikemTest-<versión>-c<código>.apk`. Perfil de DataWedge propio en los Zebra (`TeikemAlmacenTest`). Instalar encima por USB: `scripts\actualizar-aparatos.ps1 -Test`.
Los iconos salen de `npm run brand:icons:test` (en `app-almacen`).

## APK del portal del cliente (2026-10-19)
El mismo script construye la app del cliente (`app-cliente`) con `-App cliente`: producción `TeikemClientes-*.apk` (`com.teikem.cliente`, servidor `https://teikem.advancelogisticspr.com`),
`-Staging` (`com.teikem.cliente.staging`, morado) o `-Test` (`com.teikem.cliente.test`, verde, `https://test.advancelogisticspr.com`; sale como `TeikemClientesTest-*.apk`). Misma llave de firma;
contadores propios (`apk-versioncode-cliente[-staging|-test].txt`). Instalar por USB: `scripts\actualizar-aparatos.ps1 -App cliente [-Staging|-Test]`.
Los iconos de las variantes: `node scripts/brand-icons.mjs staging cliente` y `node scripts/brand-icons.mjs test cliente` (desde la raíz; dejan `app-cliente/assets/{staging,test}`).

Los APK de test se publican para el teléfono en `https://test.advancelogisticspr.com/descargas/` (los recoge `deploy\windows\crear-paquete-windows.ps1 -Test` de `F:\Download\TeikemApp`).
