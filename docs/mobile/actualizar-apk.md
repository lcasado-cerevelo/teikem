# Actualizar la app del almacén sin desinstalar (2026-10-09)

Android conserva la configuración de la app (servidor, registro del aparato, PIN, datos sin enviar) si el APK nuevo se instala **encima**, siempre que:
1. tenga el **mismo paquete** (`com.teikem.almacen`),
2. esté firmado con la **misma llave** (`%USERPROFILE%\.teikem\teikem-release.jks`),
3. tenga un **código de versión mayor** que el instalado.

## Pasos
1. `scripts\construir-apk.ps1` — el mismo que crea el APK. Sube solo el código de versión (el primero que sale es el 2; los aparatos instalados traen el 1; se recuerda en `%USERPROFILE%\.teikem\apk-versioncode.txt`) y usa la misma llave. Deja `TeikemAlmacen-<versión>-c<código>.apk` en `F:\Download\TeikemApp`.
2. Instalar encima, de cualquiera de las dos formas:
   - **Por USB, varios aparatos:** `scripts\actualizar-aparatos.ps1` (depuración USB activada). Hace `adb install -r` a cada aparato conectado.
   - **A mano:** copiar el APK al aparato, abrirlo y aceptar «Actualizar».
3. Abrir la app: la base local se migra sola (v9) y la primera sincronización baja los productos completos.

## Nunca
- **No desinstalar** antes: se pierde la configuración y lo que no se haya enviado.
- No firmar con otra llave (Android lo rechaza; la única salida sería desinstalar).
- Si el APK instalado se hizo con otra llave o no se conserva esa llave, la única vía es desinstalar e instalar de nuevo.
