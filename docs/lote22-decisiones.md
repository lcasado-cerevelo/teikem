# Lote 22 — Segundo bloque de decisiones del dueño sobre el conteo cíclico (2026-10-03)

Implementa el [segundo bloque de decisiones](decisiones-del-dueno-2026-10-03.md#segundo-bloque-de-decisiones-2026-10-03): (A) servidor, un
lote con líneas corregidas guarda las libres; (B) app, aviso de lo no guardado; (C) web, switch "Solo con existencia". Rama
`claude/company-settings-screen-plan-uajc2i`. No cambia el esquema SQL (no hubo tablas ni columnas nuevas) ni los permisos.

## Qué se construyó

### A. Servidor
| Pieza | Qué hace | Dónde |
|---|---|---|
| Lote parcial | `PUT /api/v1/cycle-counts/{id}/lines/batch`: las líneas **no bloqueadas** se validan y guardan; las **bloqueadas por corrección** (otra persona ya corrigió y quien captura no es quien corrigió ni tiene `warehouse.count`) se **omiten** sin tocarlas. Un error de validación en una libre sigue anulando todo el lote (400). Si **todas** las líneas del lote están bloqueadas → 409 `La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote: n (SKU). No se guardó nada.` | `CycleCountService.CaptureBatchAsync` |
| Respuesta | `CycleCountDetailDto` gana al final `SkippedLines: IReadOnlyList<CountSkippedLineDto>?` (null si no se omitió ninguna). Cada una: `lineId`, `binCode`, `sku`, `lotNumber`, `sentQty` (lo que se mandó), `currentQty` (el valor vigente del supervisor), `reasonCode` = `CORRECTED_BY_SUPERVISOR`, `message`. Sobrevive a `CycleCountService.Blind`/`ForCaller`; no lleva ninguna cantidad esperada | `CycleCountContracts.cs`, `CycleCountRules.SkippedReasonCorrected` |
| Sin cambio | Reenviar el valor vigente de una línea corregida no cambia nada y no cuenta como omitida; quien corrigió y quien tiene `warehouse.count` siguen pudiendo; `PUT /lines` (una línea) y `AddLineAsync` siguen dando 409; `POST /finish` igual | — |
| Contrato | `web-app/openapi.json` regenerado del Swagger del API (solo el esquema `CountSkippedLineDto` y el campo `skippedLines`); `schema.d.ts` de web y app con `npm run api:types` | — |

### B. App de almacén (Lote A7, documentado en `mobile/loteA6-decisiones.md`)
`outbox.ts` lee `skippedLines` de un `countBatch` exitoso y guarda un aviso persistente en el kv del aparato (`countSkippedNotices`) sin
tocar el estado `sent`; Sincronización lo muestra en un bloque ámbar grande (`Se guardaron las demás líneas de tu conteo.` / `Estas no se
guardaron porque el supervisor ya las corrigió:` / `• SKU · posición (mandaste x → el supervisor dejó y)`) con **Actualizar el conteo** y
**Descartar este aviso**. La tarjeta roja de rechazo A6 queda para el 409 residual con textos nuevos y sin "retomar" (se crea un conteo nuevo).
Archivos: `features/count/{countSkipped.ts,SkippedLinesNotice.tsx}`, `kernel/sync/outbox.ts`, `kernel/db/kv.ts`, `app/sync.tsx`, i18n es/en.

### C. Web
Switch **Solo con existencia** (encendido de entrada, sin guardar) junto al selector de "Nuevo conteo > Por producto":
`ProductPickerInput warehousePublicId onlyOnHand`. Detalle en `frontend/loteF13-decisiones.md` (sección "Segundo bloque").

## Cómo se probó (resultados reales de esta sesión)

| Comando | Resultado |
|---|---|
| `dotnet build Teikem.sln -c Release` | **pasó**, 0 errores (5 avisos que ya existían) |
| `dotnet test Teikem.sln` | **pasó**: 2995 pruebas, 0 fallas. Nuevas (en `CycleCountByProductTests`): lote mixto guarda la libre y omite la corregida con el detalle exacto (`CountSkippedLineDto` completo); lote con todas bloqueadas → 409 con el mensaje y `No se guardó nada.`; el supervisor y quien corrigió siguen pudiendo (sin `skippedLines`); a ciegas la línea omitida se conserva sin ningún campo de lo esperado; reenviar el valor vigente no se omite; error de validación en una libre → nada se guarda; otra compañía → 404. Se ajustó `WmsContractsTests` (firma posicional del DTO) y se reemplazó la prueba "todo o nada" |
| `scripts/dev-sqlserver.sh` | SQL Server ya estaba corriendo |
| `db-reset --yes` + `db-init` dos veces | **pasaron** (base recreada, inicialización completa ambas veces) |
| API Release en Development (`Auth__Onboarding__Enabled=false`, `ASPNETCORE_URLS=http://localhost:5000`) + `scripts/smoke.sh` con `SMOKE_SQL` (sqlcmd local) y `SMOKE_MIGRATION_RUN` | **SMOKE OK** (exit 0), con el bloque nuevo 3c: lote mixto 200 con `skippedLines` (a ciegas, sin `systemQty`), la libre guardada y la corrección intacta; 409 con todas corregidas; lote sin corregidas sin `skippedLines` |
| `cd web-app && npm run check` | **pasó**: api:types, tsc -b, oxlint (solo avisos ya existentes), vitest 117 archivos / 1183 pruebas, build |
| `cd app-almacen && npm run check` | **pasó**: api:types, tsc -b, oxlint sin hallazgos, jest 63 suites / 335 pruebas (antes 60 / 316) |
| Playwright, Vite con `VITE_API_URL=http://localhost:5000`, `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`: F12 y F13 (`--no-deps --workers=1`) | F13 **2 pasaron** (escritorio y 360 px); F12 **pasó** (4 pasaron, 4 omitidos por proyecto) al correrlo solo. En una corrida previa de F13+F12 juntas, F12 "3. Cerrar los que cuadran" falló una vez y no se reprodujo al repetir F12 solo ni en la suite completa (posible interferencia entre corridas contra la misma base; no se investigó más) |
| Playwright suite completa `npx playwright test --workers=1` | **81 pasaron, 74 omitidos por proyecto, 0 fallaron** (7.8 min) |

**No se pudo**: probar en el Zebra ni compilar el APK (lo hace el job `android` del CI); no hay recorrido Maestro para el aviso. Falta ver el CI de
GitHub Actions en verde tras el push. Las capturas que regeneró Playwright de otros lotes se descartaron con `git checkout`; solo se
actualizaron `f13-por-producto.png` y `f13-sin-existencia.png` (muestran el switch).

## Decisiones para el dueño (valor más seguro)

1. **`currentQty` se manda también a ciegas** (decisión 5 del bloque): es el valor que dejó el supervisor, no lo esperado. Si prefiere ocultarlo,
   es una línea en `CycleCountService.Blind` y en `SkippedLinesNotice`.
2. **Varias líneas bloqueadas y una libre ya sin cambios**: si el lote trae una libre cuyo valor no cambia (reenvío idempotente) y el resto
   bloqueadas, responde 200 con `skippedLines` (la libre cuenta como "libre"); el 409 solo sale cuando no queda ninguna línea libre.
3. **`SentQty` en productos SERIAL** es la cantidad de series mandadas (no hay `countedQty`).
4. **El aviso de la app no caduca** y vive solo en Sincronización (sin marca en Inicio ni en la cuenta de "Con error").
5. **El switch de la web usa `onlyOnHand` (existencia en mano, incluye reservada)**, no `onlyAvailable` (disponible recolectable); así el selector
   ofrece exactamente lo que el servidor puede contar. Sin almacén elegido no filtra por almacén.
6. **El 409 residual de la app sigue reconociéndose por el texto exacto** del mensaje del servidor (decisión 4 del bloque); la prueba que compara
   el texto protege contra cambios.
7. **Aislamiento del texto "No se guardó nada."**: ya no es cierto para el lote mixto (no se usa ahí), pero un aparato con una versión vieja de la
   app que reciba el 200 con `skippedLines` simplemente no mostrará el aviso (la fila queda enviada y las libres sí se guardaron).

## Pendientes

1. Comprobación en el Zebra con la lista del Lote A7 (en `mobile/loteA6-decisiones.md`) y compilación del APK en el CI.
2. CI de GitHub Actions en verde tras el push (el árbitro del lote).
3. Opcional: marca del aviso en Inicio de la app y caducidad configurable; mostrar `skippedLines` también en la web si algún día la web captura por lote.
4. `package-lock.json` de la app desincronizado para `npm ci` sin `--legacy-peer-deps` (anterior a este lote).
