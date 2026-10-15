# Lote 50 — Motor de trabajos programados (P2.2, 2026-10-14)

Segundo lote de P2 (cimientos). Hecho en el contexto principal, sin agentes (orden de Luis). **No forma parte del cherry-pick a Depot-Implementation** (los avisos no dependen de este motor).

## Qué se construyó
- **Servicio propio** (decisión D5: sin Hangfire/Quartz): `ScheduledJobWorker` (BackgroundService, cada 30 s) + `ScheduledJobRunner` + trabajos en código (`IScheduledJob`). **Por compañía**: cada trabajo corre como la compañía sin usuario («Sistema»).
- **Sin duplicar con varias copias del API**: el trabajo se reclama con un `UPDATE` atómico (`ExecuteUpdate` con toda la condición en el WHERE); el reclamo vence a los `MaxRuntime` del trabajo si la copia se cae. Un fallo no detiene el ciclo: se anota y se reintenta a 1, 2 y 5 minutos (3 fallas seguidas) antes de volver al horario.
- Tres trabajos: `JOB_RUN_PURGE` (3:00 a. m., conserva las últimas 100 corridas por trabajo), `INVENTORY_SWEEP` (2:00 a. m., `SweepAsync` con SCHEDULED; sin efecto si el módulo de inventario está apagado) y `JOB_TEST` (apagado).
- Tablas `ScheduledJob` y `ScheduledJobRun`, EntityType `SCHEDULED_JOB` y permiso `jobs.manage` (Admin de compañía + roles ya clonados) en la sección **2026-10-14 (f)** de `logistica-db-update.sql`. Las filas por compañía las crea el propio sistema (no el SQL), sin pisar cambios del administrador.
- API `/api/v1/jobs` (lista, ficha, PATCH de interruptor/horario con rowVersion, run, runs) y pantalla **Sistema → Trabajos programados** (lista, interruptor, cambiar horario, Ejecutar ahora, historial).
- Manual: capítulo 13, pantalla `frontend/l50-trabajos-programados.md` (5 capturas), FAQ «Lote 50».

## Pruebas (solo el área)
- xunit: `ScheduledJobsTests` (horario puro: validación con mensajes exactos, próxima corrida diaria/intervalo, reintentos; sección del SQL; permiso solo Admin; auditoría; API por reflexión: todo `jobs.manage`, módulo SYSTEM, sin DELETE) y `ScheduledJobRunnerSqlTests` contra **SQL Server real** (base desechable): filas por compañía sin pisar cambios, **8 intentos concurrentes de 2 copias → corre exactamente una vez**, falla que no detiene al otro trabajo y reintenta, reclamo vencido tomado por otra copia (y uno vivo no), trabajo apagado que corre solo cuando se pide, aislamiento entre compañías, limpieza que conserva 100. Más SQL/auditoría/permisos/aislamiento/catálogos: 472 en verde. `db-update --dry-run`: 111 lotes sin error.
- vitest: `jobRules.test.ts` y `JobsScreen.test.tsx` (13), `tsc -b` limpio. Playwright `lote50-trabajos-programados.spec.ts` (escritorio y móvil 360 px) contra el API real en verde.

## Decisiones por omisión (a revisar con Luis)
1. La **hora diaria** usa la zona por omisión de la plataforma (Puerto Rico, `LocalDay.DefaultZone`), no una zona por compañía (aún no existe ese ajuste por compañía).
2. «Ejecutar ahora» **no corre dentro de la petición**: marca el pedido y el worker lo toma en ≤ 30 s (`Jobs:PollSeconds`).
3. El historial conserva **100 corridas por trabajo** (limpieza nocturna).
4. Sin PublicId en `ScheduledJob`: las rutas usan la clave del trabajo (única por compañía).
5. El tiempo máximo de cada trabajo (2, 5 y 30 min) es también la vigencia de su reclamo; un trabajo que lo supere se da por fallido.
