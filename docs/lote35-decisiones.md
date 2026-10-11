# Lote 35 — Señal débil, bloque S5: conteo por posición sin señal y espacio libre sin servidor (2026-10-11)

Cierra la deuda de señal débil del plan (P0.1, `docs/mobile/senal-debil-decisiones.md`): faltaban **abrir un conteo sin señal** y la **capacidad en Acomodar/Recibir**.
Servidor + contrato + app + manual; las pruebas con SQL real de la app corren con `better-sqlite3` como el resto.

## Decisiones del dueño que se aplicaron (2026-10-11)

- Si al volver la señal la posición ya tiene un conteo abierto **de otro contador**: lo contado sin señal **se suma a ese conteo** (no se termina; lo sigue su dueño).
- La existencia contra la que se compara es **la del servidor al sincronizar, avisando** si hubo movimientos entre contar y sincronizar.
- Alcance: **solo conteo por posición** (el conteo por producto y el «conteo abierto» siguen necesitando señal para abrirse).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Servidor: conteo sin señal | `POST /api/v1/cycle-counts/offline-bin` (`warehouse.count.capture`): abre o retoma el conteo de la posición (también uno de otro contador: se suma, `joined`), captura las líneas con la regla de `PUT /lines/batch`, y lo termina solo si es suyo; si no se puede terminar NO falla (`finishMessage`); `movedSinceCounted` y comentario en el historial si hubo movimientos desde `countedAtUtc` | `CycleCountService.CaptureOfflineAsync`, `CycleCountsController`, `CycleCountContracts.cs`, `CycleCountRules.cs` |
| Servidor: cupo en la sincronización | `SyncBinDto.MaxCapacityQty` en `GET /sync/bins` | `SyncService.cs` |
| App: espacio libre local | Migración local **v12** (`bin.max_capacity_qty`, `bin.capacity_known`; borra la marca de agua de las posiciones para bajarlas completas una vez); `findBinByCode` calcula `freeQty` = cupo − saldos locales y solo pregunta al servidor si la posición no está o aún no trae el cupo | `kernel/db/schema.ts`, `kernel/sync/download.ts`, `kernel/warehouse/binLookup.ts` |
| App: conteo sin señal | Al escanear la posición espera 5 s al servidor; si no responde abre el conteo en el aparato (`countId` 0, a ciegas, lo esperado sale de los saldos locales); terminar / guardar para después encola UNA fila `countOffline`; cancelar solo descarta localmente; avisa joined / no se pudo terminar / movimientos / rechazo | `features/count/countApi.ts`, `countLogic.ts`, `localCount.ts`, `app/count.tsx`, `kernel/sync/outbox.ts` |
| Contrato | `web-app/openapi.json` (ruta `/cycle-counts/offline-bin`, `CountOfflineRequest`, `CountOfflineResultDto`, campo del cupo) y `schema.d.ts` de la web y de la app | `web-app/`, `app-almacen/` |
| Pruebas | Servidor: `CountOfflineTests` (9: abre y termina, posición vacía con lo hallado, retoma lo propio, se suma al de otro sin terminar, no se pudo terminar, movimientos, reloj adelantado, sin terminar, validaciones), `SyncRulesTests` (cupo), mapa de seguridad (161). App: `countOfflineScreen` (4: abre en el aparato, encola la fila, aviso de sumado, cancelar), `countApi` (6), `countLogic`, `localCount`, `binLookup` (5), `download` | `tests/Teikem.Tests/`, `app-almacen/src/` |
| Manual | App `09-app-almacen.md` §7.7 y §5.x; servidor `06-inventario-y-almacen.md` §6.s; 4 preguntas en `faq.md`; `docs/mobile/senal-debil-decisiones.md` bloque S5 | `docs/` |

## Cómo se probó

- Servidor: **3405 pruebas verdes**. App: **685 pruebas jest verdes**, `tsc` y `lint` limpios.
- Contra el API local (SQL Server local): `offline-bin` sin posición → 400 `Indique la posición.`; renglón de otra posición → 400 `Cada renglón del conteo debe ser de la posición indicada.`;
  conteo válido → 200 `created: true, finished: true`, estatus `COUNTED`; el **mismo envío con la misma clave** devuelve la respuesta guardada y solo existe **1** conteo de ese producto.
- **Maestro en el emulador Android** (APK de desarrollo compilado aquí, API y SQL Server locales): los recorridos nuevos `05a-conteo-sin-senal` (red cortada con `adb`: escanea la posición, abre el conteo EN EL APARATO, cuenta 48 y termina) y `05b-conteo-enviado` (con red otra vez: sincroniza y queda «Todo enviado») **pasan**, y en la base el conteo llegó como **COUNTED con 48 unidades y el comentario «Contado sin señal.»**. Los recorridos 01 al 03 siguen pasando. Se agregaron también al script del CI (`scripts/e2e-mobile-maestro.sh`).

## Decisiones a revisar

1. **5 segundos de espera** al abrir el conteo antes de contarlo en el aparato (`ONLINE_START_TIMEOUT_MS`). Con señal muy lenta puede abrir «sin señal» aunque el servidor termine creando el conteo;
   al enviar, el servidor **retoma ese mismo conteo** (no se duplica). Se ajusta en una constante.
2. **Lo esperado sin señal sale de los saldos del aparato**, que pueden estar atrasados: si el servidor tiene líneas que el aparato no contó, el conteo **no se termina** (queda abierto con lo contado guardado y la app avisa).
3. **El espacio libre del aparato no descuenta lo reservado por otras líneas del mismo recibo** (el servidor sí): puede decir que cabe y el servidor avisar que no; es solo un aviso, como ya era.
4. **Un conteo hecho sin señal no tiene número** hasta que llega (lo da el servidor); en Inicio/Conteo aparece sin número mientras tanto.
5. **Falta probar en el Zebra físico** (lector, batería, señal real). Los recorridos de Maestro cubren el flujo en el emulador.
