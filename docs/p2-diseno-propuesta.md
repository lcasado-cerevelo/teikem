# P2 — Cimientos: diseño propuesto (para aprobación de Luis, 2026-10-14)

Propuesta escrita por mí (sin agentes), a partir de `docs/plan-de-desarrollo-pendiente.md` (P2.1, P2.2, P2.3) y de lo que ya existe en el código (`InventoryReconciliationWorker`, `ITransactionalEmailSender`/Brevo, `DemoTenantSeeder`, `PermissionCatalog`). **No se construye nada más hasta que Luis la apruebe** (el Lote 49 ya está hecho, ver abajo). Decisiones ya tomadas: D5 = servicio propio simple (sin Hangfire/Quartz); D16 = SMS/push sin proveedor ni cuenta (canal «no configurado»).

## Resumen de lotes

| Lote | Qué | Tamaño relativo | Estado |
|---|---|---|---|
| 49 | Módulos `SALES_ORDERS`, `TAX`, `BILLING` definidos y apagados (P2.3) | chico | **Hecho** (commit `c81ee81`). Lo hice antes de que me pidieras presentar el diseño primero: son solo tres filas de catálogo, sin pantalla; se revierte con un `git revert` si no te gusta. |
| 50 | Motor de trabajos programados (P2.2) + pantalla «Trabajos programados» (solo lectura, «Ejecutar ahora» y interruptor) | mediano | Propuesto |
| 51 | Avisos, servidor (P2.1 etapa a): eventos, reglas, canales, cola (outbox), log, bandeja del usuario, correo real | grande | Propuesto |
| 52 | Pantalla Sistema → Reglas de aviso + campanita de avisos en la web + **Kemi, capa de presentación** (parpadea con globo «te llegó un aviso → Ver», se duerme en una esquina del encabezado y despierta con un clic) | grande | Aprobado |
| 53 | **Mensajería interna corta**: un mensaje de un usuario a otro usuario o a un rol, que llega a la bandeja y a la campanita y que Kemi anuncia (reutiliza los avisos). Se presenta su diseño antes de construir. | mediano | Aprobado el alcance |

**Cherry-pick a Depot-Implementation:** los Lotes 51, 52 y 53 (avisos, pantalla/campanita/Kemi y mensajería) se pasan juntos a la rama Depot-Implementation; se trabajan con commits consecutivos, autocontenidos y sin mezclar con el motor de trabajos (50) ni con los módulos (49); al final se entrega `docs/cherry-pick-avisos-depot.md` con los hashes en orden y los archivos que suelen chocar.

Cada lote: pruebas del área, manual para el usuario final + FAQ + capturas, `docs/loteNN-decisiones.md`, SQL solo como secciones nuevas e idempotentes al final de `logistica-db-update.sql`, y reporte acumulado. Las decisiones por omisión van anotadas y te las presento al final.

---

## Lote 50 — Motor de trabajos programados

**Idea:** un solo servicio de fondo (`ScheduledJobWorker`, un `BackgroundService`) que cada 30 s mira una tabla y ejecuta lo que toca. Varias copias del API pueden correr a la vez y **un trabajo nunca se ejecuta dos veces**: cada copia intenta «reclamar» el trabajo con un `UPDATE` atómico y solo la que lo logra lo corre.

**Decisión aprobada: los trabajos son POR COMPAÑÍA** (tablas con `TenantId`, permiso `jobs.manage`, pantalla en Sistema; el worker corre cada trabajo como la compañía sin usuario). Donde abajo diga «plataforma», léase «compañía».

**Tablas nuevas:**
- `dbo.ScheduledJob`: `JobKey` (PK), `Name`, `Description`, `ScheduleKind` (`INTERVAL` | `DAILY`), `IntervalSeconds`, `DailyAt` (hora local, en la zona de la plataforma), `IsEnabled`, `NextRunUtc`, `LastStartedUtc`, `LastFinishedUtc`, `LastStatus` (`OK` | `FAILED` | `RUNNING`), `LastError`, `ConsecutiveFailures`, `LockedBy`, `LockedUntilUtc`, `RowVersion`.
- `dbo.ScheduledJobRun`: historial (`RunId`, `JobKey`, `StartedUtc`, `FinishedUtc`, `Status`, `Message`, `Error`, `InstanceId`); se conservan las últimas 100 corridas por trabajo (lo limpia un trabajo del propio motor).

**Reclamar sin duplicar:** `UPDATE ScheduledJob SET LockedBy=@yo, LockedUntilUtc=@ahora+arriendo, LastStatus='RUNNING' WHERE JobKey=@k AND IsEnabled=1 AND NextRunUtc<=@ahora AND (LockedUntilUtc IS NULL OR LockedUntilUtc<@ahora)`: si afecta 1 fila, soy yo. Mientras corre renueva el «arriendo» (para que otra copia lo tome si esta se cae a medias).

**Trabajos en código:** interfaz `IScheduledJob` (clave, nombre, horario por omisión, `RunAsync`). Se registran en DI como las fuentes de datos. Al arrancar, un sembrador inserta la fila de cada trabajo **si falta** (no pisa lo que el administrador cambió del horario ni del interruptor).

**Fallos:** una falla no detiene nada: se guarda el error, `ConsecutiveFailures` sube y la siguiente corrida se reintenta con espera creciente (máximo 5 min por intento, tope 3 intentos seguidos y luego vuelve a su horario normal). El resultado queda a la vista.

**Trabajos iniciales:** (1) `JOB_RUN_PURGE` (limpia el historial), (2) `INVENTORY_SWEEP` (el barrido completo diario de Kárdex ↔ saldo que el Lote 14 dejó pendiente; usa el `SweepAsync` con `SCHEDULED`), (3) un trabajo de prueba apagado.

**Configuración:** `Jobs:Enabled` (por omisión sí; los comandos de consola y las pruebas lo apagan, como el worker de conciliación) y `Jobs:PollSeconds`.

**Pantalla y API (plataforma):** Sistema → «Trabajos programados»: lista (nombre, horario, último resultado, próxima corrida), **Ejecutar ahora**, interruptor y cambio de horario; historial de corridas. Solo el administrador de plataforma (`admin.platform`), porque los trabajos son de toda la plataforma, no de una compañía.

**Pruebas:** cálculo de «próxima corrida» (puro; diaria en la zona horaria de la plataforma, intervalos); reclamo con **dos instancias simuladas** contra SQL Server real (solo una gana); falla que no detiene el ciclo y reintenta; arriendo vencido lo toma otra copia; el sembrador no pisa cambios del administrador; permisos del API por reflexión.

---

## Lote 51 — Avisos (servidor)

**Tablas nuevas** (por compañía salvo el catálogo de eventos): 
- `NotificationEvent` (global; **catálogo sembrado desde código**: cada módulo registra sus eventos con código, módulo, etiqueta es/en y variables disponibles, como `IDataSource`).
- `NotificationRule` (compañía): evento, nombre, activa sí/no, alcance opcional (almacén), **destinatarios** (roles, usuarios concretos, correos sueltos; los teléfonos se guardan pero no se usan hasta que haya SMS), `RowVersion`, auditada.
- `NotificationRuleChannel`: por regla y canal (`EMAIL`, `SMS`, `APP`, `PUSH`): interruptor, asunto y cuerpo (plantilla es/en con variables `{numero}`, `{cliente}`…).
- `NotificationOutbox`: cola. Se escribe **en la misma transacción** de la operación de negocio; un aviso que falla **nunca** deshace la operación.
- `NotificationLog`: evento, regla, destinatario, canal, estado (`PENDING`/`SENT`/`FAILED`/`SKIPPED`), error y fecha. **Idempotente**: índice único (cola, regla, destinatario, canal).
- `UserNotification`: bandeja por usuario (título, texto, enlace, evento, leído, fecha) y `UserNotificationPref` (silenciar eventos opcionales).
- `TenantChannelSetting`: configuración de canales por compañía (vacía por omisión).

**Servicios:** `INotificationPublisher` (anota en la cola dentro de la transacción); `NotificationWorker` (servicio de fondo, cada 10 s): toma lo pendiente, busca las reglas activas de esa compañía y evento, arma los destinatarios, genera el texto, y para cada canal llama a su `INotificationChannelSender`. **EMAIL** usa el envío transaccional que ya existe (Brevo). **SMS y PUSH** son la versión «no configurado» (sin proveedor ni cuenta): el canal aparece apagado en la pantalla, no se puede prender en una regla y, si llegara un evento, el registro queda `SKIPPED` con la razón. **APP** escribe en la bandeja `UserNotification` (no necesita proveedor). Reintentos acotados (3, con espera de 1, 5 y 30 min). Se ejecuta como la compañía sin usuario («Sistema»), igual que la conciliación.

**Eventos:** el catálogo sale con un evento de prueba (`TEST`) para el «envío de prueba» y se deja registrado el contrato para que cada módulo agregue los suyos. Los publicadores reales (recibo con diferencia, daño reportado, conteo por revisar, faltante de compra, devolución con daño; luego los de ventas en P4) se conectan **en lotes aparte**, uno por módulo, para no tocar muchos servicios cerrados a la vez.

**Permiso:** `notifications.manage` (categoría ADMIN) para el administrador de la compañía, propagado a los roles ya clonados. Los avisos **no son un módulo**: van bajo `SYSTEM`.

**API:** eventos, reglas (alta, cambio, interruptor), canales de una regla, envío de prueba, consulta del log, y la bandeja del propio usuario (lista, contador, marcar leído, silenciar). Mensajes con texto y código HTTP exactos.

**Pruebas:** idempotencia (el mismo evento dos veces no avisa dos veces), canal apagado no envía, correo caído (reintenta, no deshace la operación, queda en el log), sin fuga entre compañías (pruebas de aislamiento), validación de correos y teléfonos, plantillas con variables que faltan, permisos por reflexión, y el canal no configurado (`SKIPPED`).

---

## Lote 52 — Pantalla «Reglas de aviso» y bandeja en la web

- **Sistema → Reglas de aviso:** eventos agrupados por módulo; por cada uno, la regla con su interruptor; por canal, su interruptor (los no configurados aparecen apagados con la explicación «Este canal todavía no está configurado en Teikem»); destinatarios (roles, usuarios, correos); plantilla por canal en español e inglés con las variables como fichas; **Enviar prueba** por canal; y la pestaña **Registro** del log con filtros.
- **Campanita de avisos** en la barra superior de la web: contador de no leídos, lista, marcar leído, silenciar un evento opcional. (La pantalla Avisos y el contador del aparato del almacén son la etapa b y quedan fuera.)
- Como siempre: textos es/en, 360 px, permisos con `<Can>`, recorrido Playwright con capturas y manual/FAQ para el usuario final.

---

## Decisiones que necesito de Luis (con mi recomendación)

1. **Trabajos de la plataforma, no de cada compañía** (pantalla solo para el administrador de plataforma). *Recomiendo sí.*
2. **Trabajos iniciales:** limpieza del historial, barrido diario de inventario y uno de prueba apagado. *Recomiendo esos tres.*
3. **Alcance de eventos en el Lote 51:** solo el motor y el evento de prueba; los publicadores reales por módulo en lotes aparte. *Recomiendo aparte, de a uno.*
4. **Destinatarios:** roles, usuarios y correos sueltos; teléfonos guardados pero sin uso hasta que haya SMS. *Recomiendo sí.*
5. **Reintentos de avisos:** 3 intentos (1, 5 y 30 min). *Recomiendo esto.*
6. **Campanita en la web** dentro del Lote 52 (y la pantalla Avisos del aparato queda para después). *Recomiendo incluir la campanita.*
7. **Módulos del Lote 49:** ya definidos y apagados, sin encender ninguna compañía todavía. *¿Confirmas o lo revierto?*
