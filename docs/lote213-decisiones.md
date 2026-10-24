# Lote 213 — App móvil, base: la app del chofer crece para el personal

## Decisión de Luis (2026-10-23/24)
**Una sola app** («app móvil») para choferes y personal, en vez de dos. Lo que ve cada quien lo deciden sus permisos; el usuario de tipo **«Chofer (solo app)»** ve exactamente lo de hoy (sus manifiestos, envíos, avisos, chat, perfil) y nada más, aunque alguien le diera otros permisos (doble candado). Alcance acordado de la v1 del personal: ver mucho, escribir poco; almacén solo consulta; nada de contabilidad ni facturación; asignar en la calle con un permiso propio (Lote 215).

## Qué se construyó
- **Carpeta**: `app-choferes` → `app-movil` (commit aparte, con historial). Los identificadores Android por marca y Firebase no cambian.
- **Permiso `mobile.use`** («App móvil: usarla», categoría Transporte): en las plantillas Chofer y Despachador (TenantAdmin lo trae todo) y, en el SQL, a los roles ya clonados que tienen `driverapp.use` o `trips.dispatch` y a los TenantAdmin (sección 2026-10-24 (m)). Un chofer activo entra aunque su rol aún no lo traiga.
- **Sesión multicompañía para el personal**: `POST /driver-app/auth/login` y `/company-tokens` aceptan a quien tiene `mobile.use` (o es chofer) en alguna compañía; por compañía devuelven **los permisos efectivos** e **`isDriver`**, y la sesión dice **`userKind`** y **`driverOnly`**. Sin acceso en ninguna: «Su usuario no tiene acceso a la app móvil en ninguna compañía.» (403).
- **Avisos emergentes por usuario**: `dbo.UserPushDevice` para el personal (los choferes siguen en `DriverDevice`); `POST /api/v1/mobile/device` (con `mobile.use`, sin módulo) registra el teléfono y decide dónde guardarlo; los mensajes del chat llegan al teléfono por cualquiera de los dos.
- **App**: `useAccess()` (permisos por compañía, `isDriver`, `driverOnly`, `staff`); **inicio del personal** con «Necesita tu atención» (los mismos códigos y textos de la web, por compañía) y «Pulso del día» (indicadores con permiso), y debajo «Mi trabajo de chofer» si además es chofer; la pestaña Manifiestos solo para choferes; el registro del teléfono va a `mobile/device`.
- **Web**: los **correos** de contactos y personas del cliente son enlaces `mailto:` (misma regla que los teléfonos). CI: trabajo `mobile-app` (tsc, pruebas puras, export web).

## Cómo se probó
- Servidor: 3 pruebas nuevas de acceso (`AuthMfaTests.MobileApp.cs`: personal con `mobile.use` entra y recibe permisos; sin permiso ni chofer se rechaza; «solo chofer» se marca y un chofer entra sin el permiso nuevo) y 1 de push por usuario (registro idempotente en `UserPushDevice`, el aviso del chat le llega). 640 pruebas del grupo tocado en verde; suite completa al cierre del commit.
- App: `tsc`, lint de lo tocado, pruebas puras y `expo export`. **No probado en teléfono.**

## Decisiones a revisar
- «Necesita tu atención» en la app es **solo lectura** en esta versión: no tiene a dónde ir hasta que existan las pantallas de órdenes (214) y rutas (215).
- El inicio del personal se muestra si tiene `pulse.attention` o `analytics.view` en alguna compañía; con otros permisos pero sin esos dos, la app se ve como la del chofer (sin tarjetas) hasta que lleguen las pantallas del 214-216.
- La app conserva los endpoints `/driver-app/auth/*` (compatibilidad con las apps instaladas); el nombre interno «driver-app» ya no significa «solo choferes».
- Los textos de «atención» en la app son una copia de los de la web (12 códigos); un código nuevo se verá con su código y su cantidad hasta que se copie.
