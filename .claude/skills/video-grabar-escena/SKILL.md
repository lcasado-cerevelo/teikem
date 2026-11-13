---
name: video-grabar-escena
description: Receta para grabar UNA escena de un video de Teikem con Playwright, acompasada a la duración del audio, con cursor visible, clics resaltados y verificación mirando cuadros. Úsalo al grabar o ensayar una escena.
---
# Grabar una escena

Antes: skill `video-entorno-seguro` (debe pasar completo) y `docs/video/REGLAS.md` (R1, R4, R5, R7, R8).

## Lo que ya se sabe de esta máquina (medido con Playwright 1.63)
- `recordVideo` graba **webm VP8 a 25 fps reales, ~1.1 Mbps**. El texto de 12 a 14 px se lee, algo blando al ampliar. Tema claro y oscuro salen igual.
- **`recordVideo.size` debe ser IGUAL al viewport**: si es mayor, el contenido queda arriba a la izquierda sobre gris; sin `size` sale 800×450. `deviceScaleFactor: 2` no sube la resolución.
- El video arranca ~0.6 s antes de que la página esté lista (1.5 s en frío): se recorta o se marca (el editor lo recorta).
- Playwright **no dibuja el cursor**: se inyecta un cursor falso. **No usar `showActions`**: rotula en inglés y alarga la corrida ~6 veces.
- Alternativa de más calidad si una escena de prueba no convence: `page.screencast.start({ onFrame })` (JPEG q100 a ~60 cuadros por segundo con marca de tiempo) y armar el video con ffmpeg.
- Probar **zoom 1.25** de la página en la escena 1 para que el texto se lea mejor.

## Receta
1. **Entorno** (R1): API en `localhost` con `scripts/api-para-pruebas.ps1 -Base Teikem_video` (sin correo, sin onboarding, `Files__Root` con la copia de archivos); Vite con `VITE_API_URL` a ese API; `WEB_URL` y `API_URL` en el entorno de Playwright.
2. **Spec propio** `docs/video/<id>/escenas/Enn-<clave>.spec.ts` y config propia `web-app/e2e/video.config.ts` (no tocar `playwright.config.ts`): `viewport 1920×1080`, `video: { mode: 'on', size: { width: 1920, height: 1080 } }`, `locale: 'es-PR'`, `workers: 1`, `trace: 'off'`.
3. **Sesión fuera de cámara**: iniciar sesión en un contexto previo (`login(page)` de `web-app/e2e/staffHelpers.ts`), guardar `storageState` y abrir el contexto de grabación con él; la contraseña nunca sale en cámara. Estado inicial por API (`apiHeaders`, `ok`).
4. **Acompasar al audio**: leer `tomas/Enn.json` (`audioSeg`). Una función `paso(nombre, msObjetivo, accion)` ejecuta la acción, **espera el resultado visible** (`expect(...).toBeVisible()`) y completa el tiempo restante con una pausa; nunca solo tiempo. Pausa de lectura de 1.2 s en cada pantalla nueva.
5. **Ritmo y claridad**: cursor falso que sigue `mousemove`; antes de cada clic, resaltar el elemento ~300 ms (contorno inyectado, clase con prefijo `vid-`); escribir con `pressSequentially(texto, { delay: 60 })`; desplazamientos en pasos cortos; esperar a que se vayan los toasts antes de la imagen importante; sin movimientos bruscos del ratón.
6. **Duración**: si la toma dura menos que el audio, se alarga la pausa final del último paso; si dura más, **la escena falla** y se pide acortar el guion. Nunca se acelera el video.
7. **Al terminar**: copiar la toma a `F:/Download/TeikemVideo/<id>/tomas/Enn-<clave>.webm`, extraer **tres cuadros** (inicio, medio y final) con ffmpeg a `cuadros/Enn-<clave>/`, **abrirlos con Read** (regla de pantallas), medir lo que importe (alto de filas, miniaturas) y anotar en `tomas/Enn-<clave>.json`: `{ audioSeg, tomaSeg, intentos, cuadrosVistos: [...], hallazgos: [...], estado }`.
8. **Recursos**: la corrida de Playwright, el API y Vite se lanzan con `guardia-recursos.ps1 -Archivo` (un `.ps1` que hace `cd web-app; npx playwright test --config e2e/video.config.ts --workers=1`); si la guardia lo mata (código 4) no se reintenta en bucle: se informa.
9. **Fallos**: máximo 2 intentos; no reintentar en bucle. Esta máquina tiene caídas nativas esporádicas (0xC0000005): repetir con 1 worker; no es del código. Antes de reintentar una escena que cambió datos, volver al punto de partida (instantánea, ver `video-datos`).

## Esqueleto del spec
```ts
import { test, expect } from '@playwright/test'
import { apiHeaders, ok } from '../../../web-app/e2e/staffHelpers'

test.use({ viewport: { width: 1920, height: 1080 }, video: { mode: 'on', size: { width: 1920, height: 1080 } }, locale: 'es-PR' })

test('E0N - clave', async ({ page }) => {
  // storageState ya cargado por la config; estado inicial por API si hace falta
  await page.goto('/warehouse/products')
  // paso('Lista de productos', 6000, async () => { ... })
})
```
(Ajustar la ruta de importación a la ubicación real del spec; el cursor falso y `paso()` viven en un auxiliar `web-app/e2e/video/ayudas.ts` que se crea con la primera escena.)
