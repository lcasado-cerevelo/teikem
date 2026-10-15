# Pasar a Depot-Implementation: trabajos programados, avisos, Kemi y mensajería

Escrita el 2026-10-15, al terminar el Lote 53. **Nada de esto está pasado a Depot-Implementation todavía**: la rama de Depot no se tocó (regla del dueño: ramas y cherry-pick solo cuando él lo ordene).
Los lotes se hicieron en `master` como commits **consecutivos, autocontenidos y sin mezclar** con lo demás de P2 (el Lote 49, los módulos de ventas/impuestos/facturación, **no** va).

## Qué va y en qué orden

| # | Commit | Qué es | Depende de |
|---|---|---|---|
| 1 | `221fd59` | **Lote 50** — trabajos programados por compañía (motor, 3 trabajos, API `/jobs`, pantalla Sistema → Trabajos programados, permiso `jobs.manage`, SQL sección «2026-10-14 (f)») | nada de lo nuevo |
| 2 | `a0d43a9` | **Lote 51** — avisos, servidor (cola, reglas, canales, registro, bandeja; permiso `notifications.manage`; SQL «2026-10-14 (g)») | nada de lo nuevo (no usa el motor de trabajos) |
| 3 | `1882235` | Arreglo del Lote 50: la prueba del menú (`navigation.test.ts`) cuenta Trabajos programados y su orden (60/70/80) | 1 |
| 4 | `b08c4a4` | **Lote 52** — pantalla Sistema → Reglas de aviso, campanita y Kemi (capa de presentación) | 2 y 3 |
| 5 | `cd64fb0` | **Lote 53** — mensajería interna con hilos (SignalR, presencia, pantalla de chat, Kemi con mensajes; permiso `messages.use`; SQL «2026-10-15 (h)») | 4 |
| 6 | `2959d18` | Mensajes: el campo «Para» busca personas y roles a la vez (reemplaza la lista de roles) | 5 |
| 7 | `eb24112` | **Lote 54** — Enviar avisos: avisos manuales a personas, roles o todos, no respondibles; permiso `notifications.send`; SQL «2026-10-15 (i)»; «Seleccionar todo» en el buscador | 6 |

Pasarlos **en ese orden**, uno por uno:

```bash
git checkout Depot-Implementation          # solo cuando Luis lo ordene
git cherry-pick -x 221fd59 a0d43a9 1882235 b08c4a4 cd64fb0 2959d18 eb24112
```

Si un paso queda a medias por conflictos: resolver, `git add`, `git cherry-pick --continue`. `-x` deja anotado de qué commit de `master` viene cada uno.

## Dónde van a chocar (comprobado con una simulación, sin tocar nada)

Depot-Implementation tiene 62 commits que `master` no tiene y `master` tiene 153 que Depot no tiene, así que **habrá conflictos de contexto** (no de fondo). Los archivos que chocan, por commit:

| Archivo | Por qué choca | Qué hacer |
|---|---|---|
| `Diseño/logistica-db-update.sql` (final del archivo) | Depot tiene otras secciones al final; las mías se pegan después | Conservar **todo** lo de Depot y **agregar al final** mis secciones `(f)`, `(g)` y `(h)` tal cual (son idempotentes, cada una con su fecha). No reordenar ni reescribir las de Depot. La sección `(e)` del Lote 49 **no** existe en Depot y no hace falta |
| `src/Teikem.Domain/Constants/PermissionCatalog.cs` | Depot tiene otros permisos en la lista | Agregar `JobsManage`, `NotificationsManage`, `MessagesUse` (constantes y filas de `All`) y dejar la función `WithMessaging(...)` que rodea a `RoleTemplates` (suma `MessagesUse` a **todas** las plantillas) |
| `src/Teikem.Domain/Constants/CatalogDomains.cs` | EntityType `SCHEDULED_JOB` y las constantes `JobScheduleKinds/JobStatuses/JobTriggers` al final | Agregarlos tal cual |
| `src/Teikem.Infrastructure/Persistence/TeikemDbContext.cs` | Los `DbSet` se anclan junto a los de impresión | Agregar los `DbSet` de trabajos, avisos y mensajería donde encajen |
| `src/Teikem.Infrastructure/DependencyInjection.cs` | Los registros se anclan al final de los servicios de Depot | Agregar los registros de Jobs, Notifications y Messaging y sus `using` |
| `tests/Teikem.Tests/{Fleet,Order,Trip,Wms}CatalogTests.cs`, `Analytics/PulseLayoutTests.cs` | **Conteo de permisos** (hoy en master: 80) | Poner el conteo de Depot **+ 4** (jobs.manage, notifications.manage, messages.use, notifications.send). No copiar el 80 |
| `web-app/src/app/routes.tsx` | Rutas del grupo Sistema | Rutas nuevas: `/system/notifications` (orden 60), `/system/announcements` (70), `/system/jobs` (80) y `/messages` (sin menú); Seguridad y auditoría queda en 90 y Ajustes en 100 en master. Si Depot tiene otras rutas en Sistema, **renumerar los `order`** para que queden 10, 20, 30… consecutivos en el orden del arreglo |
| `web-app/src/app/navigation.test.ts` | Cantidad de ítems del menú y orden | Recalcular con los ítems de Depot (+3: Trabajos programados, Reglas de aviso y Enviar avisos; la pantalla Mensajes no tiene ítem) y agregar sus títulos en su lugar |
| `web-app/src/kernel/i18n/{es,en}.json` | Claves nuevas: `jobs`, `notifications`, `messages` (arriba) y `nav.jobs` / `nav.notifications` | Agregar los bloques; el JSON debe seguir siendo válido |
| `web-app/openapi.json` y `web-app/src/kernel/api/schema.d.ts` | Contrato del API | **Regenerar**: dejar la versión de Depot, arrancar el API de Depot con estos lotes y bajar su swagger a `openapi.json`, luego `npm run api:types`. (Alternativa: aceptar las rutas y esquemas nuevos a mano y correr `npm run api:types`) |
| `docs/manual/README.md`, `docs/manual/faq.md` | Se agregan capítulos y secciones al final | Conservar lo de Depot y agregar lo mío |
| `web-app/src/app/AppShell.tsx`, `icons.tsx` (Lotes 52 y 53) | El shell de Depot puede ser distinto | Aplicar a mano: envolver con `NotificationsProvider` y `MessagesProvider`, agregar `<MessagesButton />` y `<NotificationBell />` en la cabecera, `<KemiSleepButton />`, `<KemiFloating />` y `<NewMessageHost />`; los íconos `IconBell` e `IconMessage` |
| `web-app/package.json` y `package-lock.json` (Lote 53) | Dependencia nueva `@microsoft/signalr` | Agregarla y correr `npm install` |

Los archivos **nuevos** (carpetas `src/Teikem.Domain/{Jobs,Notifications,Messaging}`, `src/Teikem.Infrastructure/{Jobs,Notifications,Messaging}`, `web-app/src/features/{notifications,messages}`, pruebas, capturas, manual) no chocan.

## Antes de dar por bueno el paso a Depot

1. `dotnet build Teikem.sln` y `dotnet test` (en especial `ScheduledJobsTests`, `ScheduledJobRunnerSqlTests`, `NotificationsTests`, `NotificationsSqlTests`, `MessagingTests`, `MessagingSqlTests`, `FrozenSqlTests` y los de conteo de permisos). Las pruebas «SqlServer» necesitan `TEIKEM_TEST_SQLSERVER`.
2. `db-update --dry-run` contra una **copia** de la base de Depot: las secciones `(f)`, `(g)` y `(h)` deben aplicar sin error (`Diseño/logistica-db-update.sql` es lo único que toca estructura; las reglas de producción se cumplen: solo se agrega, es idempotente, no se edita la estructura ni el seed congelados).
3. `cd web-app && npm run check` (tipos generados, `tsc`, lint, vitest, build).
4. **Servidor de producción**: la mensajería usa **WebSockets** (SignalR en `/hubs/messages`): en IIS, habilitar el *WebSocket Protocol*; detrás de un proxy inverso, permitir `Upgrade`/`Connection` en esa ruta. Sin ellos todo sigue funcionando con el respaldo de 30 s, solo que sin la entrega instantánea. Una sola instancia del servidor por ahora (ver `docs/manual/15-mensajeria.md` §7).
5. Configuración opcional (`appsettings`): `Jobs:Enabled`, `Jobs:PollSeconds`, `Notifications:Enabled`, `Notifications:PollSeconds`. El correo de los avisos usa la configuración de Brevo que Depot ya tenga; SMS y push quedan «no configurados».
6. Al activar en producción: el SQL agrega `messages.use` a **todos** los roles existentes y `jobs.manage` / `notifications.manage` a los administradores de compañía (plantilla y roles clonados). Los roles nuevos no reciben `messages.use` por omisión (decisión del dueño: los usuarios del portal de clientes solo lo reciben si él lo asigna).

## Lo que se decidió y conviene recordar

- Los trabajos programados son **por compañía** (permiso `jobs.manage`); el motor es propio y simple (sin Hangfire/Quartz) con reclamo atómico, así que varias copias del API no duplican corridas.
- Los avisos **no dependen** del motor de trabajos (su propio worker); SMS y push están como canales «no configurados» hasta que haya proveedor.
- Kemi, en estos lotes, es solo la **capa de presentación** (globos, opciones en botones, dormir en la cabecera). El «cerebro» (hacer ajustes, proponer rutas) es una fase posterior, con su diseño antes.
- Mensajería: hilos entre dos y grupos (por personas o por un rol «en foto»), en vivo por SignalR con respaldo por sondeo, presencia «en línea», sin chat «escribiendo…» ni adjuntos, mensajes que no se editan ni se borran.
