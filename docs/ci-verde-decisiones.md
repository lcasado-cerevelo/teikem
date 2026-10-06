# CI en verde — decisiones (2026-10-03)

Dos pasos del CI fallaban igual en la rama base (`claude/great-davinci-t2nvtl`, run 160) antes de los lotes recientes. No eran
regresiones de esos lotes.

## 1. Recorrido Playwright (job `build-test`)

**Causa raíz (distinta de la hipótesis inicial).** El recorrido no fallaba por la pantalla de elegir compañía ni por los datos que
deja el smoke. Fallaba porque el navegador no podía hablar con el API: Vite (arrancado por `playwright.config.ts`) lee
`web-app/.env.development`, que desde el checkpoint del 2026-09-29 (commit 37cfc19) dice `VITE_API_URL=https://localhost:5001`
(perfil https de `launchSettings.json`). En el CI el API solo escucha en `http://localhost:5000`, así que el login mostraba
"No se pudo conectar con el servidor" (`net::ERR_CERT_AUTHORITY_INVALID` / conexión rechazada en la traza), la URL se quedaba en
`/login` y `page.waitForURL(url => url.pathname !== '/login')` agotaba los 60 s. Localmente nadie lo veía porque los lotes F12-F14
documentan que se corre con `VITE_API_URL=http://localhost:5000` exportado a mano.

**Cambio.**
- `web-app/playwright.config.ts`: `webServer.env.VITE_API_URL = VITE_API_URL ?? API_URL ?? 'http://localhost:5000'`. El recorrido
  usa el mismo API que las pruebas por request (`API_URL`) sin depender de variables a mano.
- `.github/workflows/ci.yml`: el paso de Playwright exporta también `VITE_API_URL: http://localhost:5000` (explícito).
- `.env.development` NO se toca (sigue sirviendo al desarrollo local con el perfil https).
- Se descartaron (a) cambiar el helper de login y (b)/(c) recrear la base o limpiar el smoke: la hipótesis de las compañías extra
  se verificó y es falsa (tras el smoke, el administrador entra directo al Pulso de Advance Logistics y `f6.spec.ts` pasa).
- `web-app/e2e/lote14.spec.ts` (prueba 6): el clic en la fila del Kárdex que abre el detalle del movimiento se reintenta con
  `expect(...).toPass()` si la fila se repinta justo en ese momento. Se vio fallar una vez ese clic (el diálogo no abrió) en una
  base sucia tras varias corridas; no se debilita ninguna aserción.

**Cómo se reprodujo y verificó.** `db-reset --yes` + `db-init` x2, API Release (Development, `Auth__Onboarding__Enabled=false`, :5000),
`scripts/smoke.sh` con `SMOKE_SQL`/`SMOKE_MIGRATION_RUN` (SMOKE OK) y `npm run e2e -- --workers=1` con `API_URL` y Chromium de
`/opt/pw-browsers`:
- sin el cambio: `f6.spec.ts` (escritorio) falla en `waitForURL` con el aviso "No se pudo conectar con el servidor";
- con el cambio, base limpia + smoke: **81 pasaron, 74 omitidos por proyecto, 0 fallaron (8.0 min)**.

## 2. Fixtures de Maestro (job `android-e2e`)

**Causa raíz.** `GET /api/v1/warehouses/{id}/bins` devuelve una página `{ total, skip, take, items }` (Lote 1) y el script usaba
`jq '.[0].id'`. Los demás listados que usa el script (almacenes, aparatos, ubicaciones) siguen siendo arreglos.

**Cambio (`scripts/e2e-mobile-fixtures.sh`).** Un filtro `ROWS` tolera arreglo o página para todos los listados, y la posición se
toma por código exacto (`search` es "contiene"). Probado contra el API local con base limpia: la primera corrida termina bien, la
segunda recorre la rama del 409 ("dispositivo ya existía") y también termina bien; con `GITHUB_ENV` escribe
`E2E_ENROLL_CODE`, `E2E_CONSIGNEE_LABEL` y `E2E_SERVER_URL`.

## 3. Hallazgo en el paso de Maestro (corregido por lectura, NO ejecutable aquí)

`reactivecircus/android-emulator-runner` ejecuta **cada línea** de `script:` en un shell aparte (`sh`). El bloque anterior
dependía de `set -euo pipefail`, `export PATH` y un arreglo bash (`ENV_ARGS=(...)`) entre líneas: habría fallado aunque las
fixtures pasaran. Ahora el recorrido vive en `scripts/e2e-mobile-maestro.sh` y el workflow solo hace
`script: bash scripts/e2e-mobile-maestro.sh`. El script valida las tres variables `E2E_*` y, con `trap`, deja la red del emulador
encendida al terminar aunque 04a falle.

### Lo que podría fallar en los flujos Maestro (sin emulador, no verificado)
Leídos los cinco YAML contra el código: los textos y `testID` que usan existen (`server-url-input`, `enroll-code-input`,
`dispatch-qty`, `dispatch-from-bin`, "Escribe la cantidad primero y luego escanea la posición.", "Agregado: {qty} {sku} desde {bin}",
"Sincronizar ahora", "Todo enviado", etc.). Pendientes inciertos:
- `03-despacho.yaml`: al fallar la posición sin cantidad, `dispatch.tsx` enfoca la cantidad (teclado `decimal-pad` en pantalla);
  luego se toca el `ScanField` de posición, que no abre teclado (`showSoftInputOnFocus` falso). Si Android deja el teclado numérico
  abierto, podría tapar el aviso rojo (`assertVisible`) o el verde "Agregado: …". No se pudo deducir con certeza; no se cambió.
- El flujo 01 depende de que el usuario "Administrador Advance" aparezca en la lista del aparato y de que el PIN 2846 de las
  fixtures funcione (se probó el `PUT /me/pin`, no la pantalla).
- `android-e2e` sigue con `continue-on-error: true`.

## Comandos corridos (resultado real)
- `dotnet build Teikem.sln -c Release`: 0 errores. `dotnet test`: 2995 pasaron, 0 fallaron.
- `web-app`: `npm run check`: código de salida 0 (117 archivos de pruebas vitest pasaron; solo avisos de oxlint ya existentes).
- `app-almacen`: `npm run check`: código de salida 0 (63 suites, 335 pruebas).
- Smoke: SMOKE OK. Playwright serie completa tras el smoke: 81 pasaron, 0 fallaron. `lote14.spec.ts` solo (escritorio): 7 pasaron.
- Fixtures de Maestro: 2 corridas, ambas bien.
- NO se pudo correr: el emulador Android + Maestro, ni el CI de GitHub.

## Decisiones para el dueño
- Se dejó `.env.development` apuntando a https://localhost:5001 (desarrollo local con el perfil https); el recorrido Playwright fuerza
  :5000 por su cuenta. Si prefiere que `npm run dev` use http :5000 por defecto, es un cambio de una línea.
- La prueba 6 de `lote14.spec.ts` es sensible al estado de la base (el "Conteo de lo cambiado" cuenta posiciones con cambios);
  en el CI arranca limpia, pero repetirla varias veces seguidas en una base sucia puede fallar en la vista previa.

# CI en verde — segunda ronda (2026-10-06, master 78e8b31)

## 4. Job `mobile`: pruebas jest por encima de 5 s en el runner
**Qué fallaba.** `Exceeded timeout of 5000 ms` en `countOpenProductsScreen.test.tsx` ("abre el conteo vacío al primer escaneo…"),
`receiveDirectScreen.test.tsx` y una tercera, desde varias corridas atrás (también en commits anteriores a rentas).
**Por qué.** El CI corre jest con la caché de transformación vacía (checkout nuevo): la primera prueba de cada pantalla paga la
transformación con Babel de todo lo que la ruta carga, y ese tiempo cuenta dentro de la prueba. Reproducido aquí con
`npx jest --no-cache --maxWorkers=2` y la configuración anterior: la misma prueba falla por los 5 s (1 fallida, 421 pasaron);
con la caché caliente tarda menos de 1 s, por eso localmente nadie lo veía. Ninguna prueba fija un tiempo propio y el job no
limita workers.
**Cambio.** `app-almacen/package.json` → `jest.testTimeout: 60000` (global). No se salta ni se recorta ninguna prueba. Se eligió
60 s y no 30 s porque el runner llegó a 16–19 s (≈4× lo medido aquí en frío) y la prueba más lenta aquí en frío ya ronda los 5 s.
**Verificado.** `--no-cache` con el cambio: 74 suites, 422 pruebas, 0 fallaron; `npm run check` completo: código 0, 422 pruebas.

## 5. Job `build-test`, paso del smoke: corte silencioso en "aparatos y sincronización (Lote 8A)"
**Qué se buscó.** La corrida 220 falló en el smoke entre las 03:36 y 03:39 UTC (día UTC ≠ día de Puerto Rico). Se reprodujo como en
el CI: base nueva (`TeikemCI*`), `db-init` dos veces, API en :5000 (Development, `Auth__Onboarding__Enabled=false`) y el smoke con
`SMOKE_SQL` y `SMOKE_MIGRATION_RUN`.
**Lo que se encontró.** Una de las corridas murió justo después del encabezado "aparatos y sincronización (Lote 8A)" **sin ningún
mensaje FAIL**. La bitácora de seguridad de esa base mostró que los 10 PIN incorrectos en paralelo respondieron los 10 con 423
(cero 401). Eso es válido para el servidor (`PinService.VerifyForLoginAsync` relee el bloqueo tras contar: si el 5.º fallo se asienta
antes de que los otros relean, todos ven el bloqueo) y la aserción del smoke lo admite ("a lo más 4 × 401"), pero
`N401=$(grep -o 401 <<<"$CPP8" | wc -l)` con `set -euo pipefail` sale con código 1 cuando grep no encuentra nada y el script se corta
sin mensaje. Es una carrera: salió 1 vez en 10 corridas aquí; un runner con otro reparto de hilos la puede ver más seguido.
**Cambio.** `scripts/smoke.sh`: `… | wc -l || true` en los dos conteos (conserva el 0). No se toca la aserción.
**Fechas en la ventana 00:00–04:00 UTC.** Se revisaron todos los usos de fechas (`PRDAY`, `TODAY`, `YESTERDAY`, `dplus`, `D0`,
`TODAYPR`, `date -u` de órdenes y recolecciones, rentas R1/R2/R3 con `DaysToPickup`, vencidas y "devolución no futura"). No se
encontró ninguno que falle en la ventana:
- reloj real: dos corridas completas entre 03:50 y 04:00 UTC (rentas R1–R3 cayeron a las 04:00:07, justo fuera);
- reloj simulado con libfaketime (API, `db-init`, `date` y `jq`; bash no, porque los subshells borran el semáforo de libfaketime):
  SMOKE OK a las 01:30–01:33 UTC (sin `SMOKE_SQL`: el reloj de SQL Server no se desplaza) y SMOKE OK a las 03:33–03:36 UTC del día
  siguiente con `SMOKE_SQL` (desfase hacia adelante, para que los `SYSUTCDATETIME()` del humo queden en el pasado del API).
- `db-init` dos veces no afecta: todas las corridas fueron sobre base nueva con las dos pasadas, como el CI.
**No comprobable aquí.** No hay log del paso en GitHub: que la corrida 220 muriera exactamente por este corte es la explicación más
probable (falló sin que la ventana horaria reproduzca nada), no una certeza.

## 6. Playwright tras el smoke
Base nueva + `db-init` x2 + smoke (SMOKE OK) y `npm run e2e` con `CI=true`, `API_URL`/`VITE_API_URL=http://localhost:5000` y el
Chromium de `/opt/pw-browsers`: **91 pasaron, 84 omitidos por proyecto, 0 fallaron (6.5 min)**. Las capturas regeneradas se
descartaron.

## Resumen de corridas (resultado real)
- Smoke sobre base nueva, reloj real: 5 corridas completas SMOKE OK, 1 corte silencioso (el del punto 5, antes del cambio) y 1 que
  falló solo en la migración por correrse aquí con `dotnet <dll>` desde otra carpeta (falta de appsettings; en el CI usa
  `dotnet run --project`, no aplica).
- Smoke con reloj simulado: 2 SMOKE OK (01:30 y 03:33 UTC); las corridas descartadas fallaron por la propia simulación
  (semáforo de libfaketime, reloj de SQL Server sin desplazar), no por el humo.
- NO se pudo correr: el CI de GitHub.
