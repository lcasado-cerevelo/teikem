# Lote 210 — Avisos emergentes (push) con Firebase

## Qué se construyó
- **App**: `expo-notifications`; al entrar, la app pide el permiso de notificaciones, crea el canal «Avisos», obtiene el token de FCM y lo registra en **todas** las compañías del chofer (`POST /api/v1/driver-app/device`). Con la app abierta el aviso se muestra igual; al tocarlo abre la pestaña de avisos. `google-services.json` está en `app-choferes/` (proyecto `teikem-test`, app `com.advancelogisticspr.app`); si una marca no está en el archivo (De la Paz), se construye sin push en vez de fallar.
- **Servidor**: `FcmPushSender` (API HTTP v1 de FCM; token OAuth2 firmado con la clave de la cuenta de servicio), `DriverPushService` (registro del teléfono en `DriverDevice` y envío) y el trabajo `DRIVER_PUSH_DISPATCH` (cada minuto manda los avisos de la bandeja que no han salido; un token rechazado por FCM se desactiva; sin Firebase configurado no hace nada y el aviso queda en la bandeja). Columna `DriverNotice.PushedAtUtc` (sección 2026-10-23 (g) del SQL; los avisos anteriores se marcan como enviados).
- Todo aviso nuevo sale por aquí: «Nuevo manifiesto» al despachar y los que escriba la oficina.

## Configuración del servidor (SECRETO, no va en el repo)
Generar en Firebase → Configuración del proyecto → Cuentas de servicio → «Generar nueva clave privada», guardar el archivo JSON en el servidor y apuntar a él:
`Push:Fcm:ServiceAccountJsonPath` = `C:\ruta\privada\firebase-teikem.json` (o `Push__Fcm__ServiceAccountJsonPath` como variable de entorno; también existe `Push:Fcm:ServiceAccountJson` con el contenido).
Sin esa clave el servidor no envía nada (los avisos siguen apareciendo en la bandeja).

## Cómo se probó
3 pruebas nuevas (`TripStatusEffectTests.Push`, con un FCM falso): registro idempotente y validado, envío único y token muerto desactivado, sin Firebase no se envía. **No se probó contra Firebase real ni en un teléfono**: falta la clave de la cuenta de servicio y una compilación de la app.

## Decisiones a revisar
- El archivo `google-services.json` solo trae la marca Advance; para De la Paz hay que registrar su paquete en Firebase y volver a descargarlo.
- iPhone: falta la llave APNs (cuenta de desarrollador de Apple).
- El proyecto se llama `teikem-test`; si será el de producción conviene renombrarlo (Luis ya lo renombró).
