# app-cliente — Teikem Clientes (portal de clientes)

App del cliente (Expo / React Native, web y móvil) del portal de clientes ([docs/p10-portal-diseno.md](../docs/p10-portal-diseno.md)). Habla solo con `/api/v1/portal/*`, `/api/v1/auth/*` y `/api/v1/legal/*`;
el cliente del API se **genera** desde `web-app/openapi.json` (`npm run api:types`): nunca DTOs a mano.

## Correr
```
cd app-cliente
npm install --legacy-peer-deps
EXPO_PUBLIC_API_URL=http://localhost:5000 npm run web      # web en http://localhost:8081 (o npm run android / ios)
npm run check                                               # tipos generados, tsc, oxlint y jest
```
`.env.local` (no se sube) puede fijar `EXPO_PUBLIC_API_URL`. En desarrollo, el API debe tener `Auth:PasswordReset:WebBaseUrl` = la dirección de esta app (el enlace del correo de confirmar la computadora abre `/confirm-browser`) y el origen de la app en `Cors:Origins`.

## Estructura
- `src/app/` — rutas (Expo Router): `login`, `mfa`, `legal`, `lock`, `confirm-browser`, `(tabs)/home|orders|notices|settings`, `order/[id]`. `_layout.tsx` es la compuerta (sin sesión → login; con la huella activa y bloqueada → `/lock`; sin aceptar el documento legal → `/legal`).
- `src/features/` — pantallas por tema; `src/kernel/` — API, sesión, huella, idioma y el kit de interfaz.
- Textos en `src/kernel/i18n/{es,en}.json` (una prueba exige las mismas claves y parámetros en los dos).
- Web instalable: `public/manifest.json` y `src/app/+html.tsx`; el botón «Poner en mi escritorio» está en el login (`InstallPrompt.web.tsx`).
- La huella usa `expo-local-authentication` y solo existe en el teléfono.
