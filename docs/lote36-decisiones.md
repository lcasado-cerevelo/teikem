# Lote 36 — Cierre de P0.3: deuda técnica del backend (2026-10-11)

Cierra los puntos de P0.3 de `docs/plan-de-desarrollo-pendiente.md` que quedaban (el alcance de datos y cerrar una ruta ya estaban hechos: Lotes 32 y 33). Cada punto se verificó antes de tocarlo.

## Decisiones del dueño que se aplicaron (2026-10-11)

- **D39** — un cliente dado de baja **no admite nada nuevo, tampoco cotizar** (409 `El cliente está dado de baja; solo se consulta su historial.`).
- **D44** — invitar al portal el correo de alguien que es usuario de portal de **otro cliente** de la misma compañía **se queda en 409** (una persona no cambia de cliente por invitación).

## Qué se hizo, punto por punto

| Punto del plan | Resultado | Dónde |
|---|---|---|
| `[SkipIdempotency]` declarativo (era una lista fija de rutas) | Atributo `SkipIdempotencyAttribute(motivo)`; el middleware lo lee del endpoint. Marcados **los mismos** controladores/acciones de antes: `AuthController`, `MeController`, `DevicesController`, `DeviceEnrollmentController`, `UsersController` (los dos), `UserPinsController`, `PlatformController` y las acciones `Invite` y `ResendInvite` de portal. Se quitaron `ExcludedPathPrefixes`, `ExcludedClientPathSuffixes` e `IsExcludedPath` de `IdempotencyRules`. La prueba `IdempotencyOptOutTests` fija el conjunto exacto por reflexión y verifica que todo lo que colgaba de los prefijos viejos sigue excluido y que ninguna otra escritura lo está | `Api/Auth/SkipIdempotencyAttribute.cs`, `IdempotencyMiddleware.cs`, controladores, `tests/.../IdempotencyOptOutTests.cs` |
| Ventana AAL2 a 15/30/60 | El servidor solo admite 15, 30 o 60 minutos (400 `La ventana de reautenticación debe ser de 15, 30 o 60 minutos.`). **Un valor viejo ya guardado se conserva** (solo se valida un valor nuevo), así que una compañía con 45 min sigue guardando sus otros ajustes. Antes aceptaba 5–240 | `Domain/Security/Aal2WindowRules.cs`, `TenantService.cs`, `Aal2WindowRulesTests` (11) |
| `orders.credit_override` en roles clonados (sin prueba) | Prueba con el `PermissionSeeder` **real**: roles `TenantAdmin` y `Billing` ya clonados por dos compañías lo reciben; `Dispatcher`, un rol propio y los demás no; segunda corrida sin duplicar | `CreditOverridePropagationTests` (10) |
| Tablas obsoletas de Rentas | **Script de solo lectura** para que Luis lo corra en producción (`scripts/revisar-tablas-viejas-de-rentas.sql`: dice por tabla «No existe», «VACÍA» o «TIENE N filas: NO retirar»). En las bases locales no existen. **No se borra nada**: si salen vacías se retiran después con una sección nueva de `update.sql`; si tienen filas, no se tocan | `scripts/` |
| D39 (cliente de baja) | `RateService.QuoteAsync` ahora exige cliente activo (el resto de lo nuevo ya lo exigía desde el Lote 3, ajuste A). Pasos nuevos en `smoke.sh`: cotizar, invitar y servicio especial con un cliente de baja → 409 | `RateService.cs`, `scripts/smoke.sh`, manual 02 |
| D44 (reinvitar desde otro cliente) | Sin cambio de código: se queda en 409 como está | — |
| Zona horaria (contradicción lote18/19 vs 20) | **Ya estaba resuelta** (Lote 20). Se buscó en `src/` y no queda `DateTime.UtcNow.Date` ni `DateOnly.FromDateTime(DateTime.UtcNow)`; solo un comentario y `LegacyImportReport.GeneratedAt = DateTime.Now` (hora de un informe de migración, no una regla de negocio) | — |
| `Contract.CodCommissionPct` sin exponer | **Se expone con la remesa COD (P8)**, que ya lo trae en su alcance; exponerlo antes no tiene dónde usarse | plan P8 |
| `docs/frontend/diseno-frontend.md` obsoleto | Sección «Estado real (2026-10-11)» al principio: qué existe, qué lotes se hicieron, qué falta, reglas que no se rompen | `docs/frontend/diseno-frontend.md` |

## Cómo se probó

- Pruebas del servidor: **3408 verdes**.
- Humo (`scripts/smoke.sh`) en base nueva con `db-init`: **SMOKE OK**, con los pasos nuevos de cliente de baja (cotizar, invitar y servicio especial → 409). De paso se corrigió `PRDAY` en `smoke.sh`: en el bash de Windows `TZ=America/Puerto_Rico` no se respeta (devolvía la fecha UTC) y entre las 8 p. m. y la medianoche de PR un paso de tarifas fechadas fallaba; ahora usa la zona POSIX `AST4`.

## Decisiones a revisar

1. **Ventana AAL2:** una compañía que ya tenía 5, 10 o 45 min la conserva; para cambiarla tiene que escoger 15, 30 o 60. Si prefieres migrar a la fuerza los valores viejos al más cercano, es una sección de `update.sql`.
2. **Tablas viejas de Rentas:** falta que corras el script en producción (es solo lectura).
3. `LegacyImportReport.GeneratedAt` usa `DateTime.Now` (hora local del servidor): irrelevante para negocio; se puede pasar a UTC si quieres.
