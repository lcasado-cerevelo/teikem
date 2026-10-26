# Publicador de TEST y app móvil de TEST — decisiones (2026-10-26)

Pedido de Luis: un publicador que deje listo todo lo que se sube a TEST (API, web del personal, portal del cliente), el APK de la app móvil en TEST, solo el APK del almacén si cambió, y la página de descargas con la app móvil nueva. La base de datos la hace Luis.

## Qué se construyó

- **`scripts/publicar-test.ps1`** (el publicador): revisa rama y commits pendientes; para cada app móvil (almacén, clientes, móvil del personal) mira si cambió su código desde el último APK de test que hay en `F:\Download\TeikemApp` (ignora tipos generados del API, pruebas, documentos y capturas) y ofrece compilar solo las que cambiaron; arma el paquete con `deploy/windows/crear-paquete-windows.ps1 -Test`; imprime la lista de lo que le toca a Luis. Opciones `-SinApk`, `-Forzar almacen,cliente,movil`, `-CarpetaApk`.
- **App móvil del personal (`app-movil`) con variante TEST**: marca `advance-test` en `app.config.ts` (paquete `com.advancelogisticspr.app.test`, «Advance App TEST», esquema `advanceapptest`), versión y código de versión por variables de entorno, firma con la llave propia (plugin `plugins/withAndroidReleaseSigning.ts`, el mismo de las otras dos apps) y sello de compilación.
- **`scripts/construir-apk.ps1 -App movil [-Test]`** y **`scripts/actualizar-aparatos.ps1 -App movil [-Test]`**: sin `-Test` es la app de producción (`com.advancelogisticspr.app`, `TeikemMovil-*.apk`); con `-Test`, `TeikemMovilTest-*.apk` apuntando a `https://test.advancelogisticspr.com/api/` (se escribe al compilar con `EXPO_PUBLIC_BASE_URL`). Sin variante staging. La versión sale de `app.config.ts` (1.3.0), contadores `apk-versioncode-movil[-test].txt`.
- **Página de descargas de TEST** (`deploy/descargas-test/index.html`): tarjeta nueva «Advance App TEST»; el paquete copia `TeikemMovilTest-*.apk` como `apk/teikem-movil-test.apk` y escribe `movil` en `versions.json`.
- Documentación: `deploy/windows/README.md`, `docs/mobile/actualizar-apk.md`.

## Cómo se probó

- `expo config` con `APP_VARIANT=advance-test`: nombre, paquete, esquema, versión y código de versión salen como se espera; `tsc` y lint de `app-movil` sin errores nuevos.
- `construir-apk.ps1 -VerCodigo` para móvil (1.3.0, código 2) y almacén (código 4, igual que antes); `-App movil -Staging` se niega con su mensaje.
- Los scripts PowerShell pasan el analizador sintáctico. La detección de cambios del publicador se probó en seco contra los APK de `F:\Download\TeikemApp`.
- **No se compiló ningún APK ni se armó el paquete** (piden la contraseña de la llave de firma y cargan mucho la máquina); lo hace Luis con `scripts\publicar-test.ps1`.

## Qué cambió desde los últimos APK de test (2026-10-21)

- **Almacén**: cambios chicos pero reales: un arreglo en «despacho de órdenes de venta» (un hook de React estaba después de un `return` temprano) y el estilo de los botones grandes (texto centrado, relleno). El publicador la marca como cambiada y la ofrece.
- **Clientes**: muchos cambios (portal con almacén, Kemi, contraseñas, etc.): hay que recompilarla.
- **Móvil del personal**: no existía APK de test; es la primera.

## Decisiones a revisar

1. La app móvil de TEST usa los mismos iconos que la de producción (solo el nombre dice TEST); las del almacén y clientes llevan iconos verdes con cinta. Falta dibujar los de la móvil.
2. **Avisos emergentes**: `google-services.json` solo trae las apps Android de producción; el paquete `.test` no está en Firebase, así que la app de TEST funciona sin avisos emergentes hasta darlo de alta.
3. La app móvil de TEST no muestra cinta ni franja «TEST» dentro de la app (solo el nombre y el paquete).
4. El publicador no sube nada al servidor: arma el `.zip` y la lista de pendientes.
