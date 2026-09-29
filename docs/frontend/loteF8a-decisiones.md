# Lote F8a — decisiones (revisión de 2 lentes y corrección de hallazgos)

Fecha: 2026-09-28. Cierra la fase de revisión y corrección posterior a la construcción del lote F8a (menú completo de
la maqueta, Pulso por paneles, Roles y usuarios + PIN, Aparatos móviles, Catálogos de valores, Indicadores y
Gráficos, marca — construidos según `docs/frontend/loteF8-plan.md`/`.json`, ya commiteados). Este documento cubre
**qué se corrigió y por qué**; el detalle pieza por pieza, con archivos y líneas exactas, vive en
`docs/frontend/loteF8a-hallazgos-plan.md` (documento de trabajo de los 9 paquetes).

## Cómo se probó

- Revisión con 2 lentes independientes (agentes separados): A "conformidad con la especificación y el kit" (contra
  `loteF8-plan.md` y `KIT.md`), B "correctness, seguridad e impacto en la app móvil" (`app-almacen/`). Cada hallazgo
  de la lente A/B quedó agrupado en 9 paquetes de trabajo por área de código.
- Cada paquete se cerró con: `dotnet build` + `dotnet test` (2062 pruebas) limpios, `npm run check` de `web-app/` en
  verde (379 pruebas — 46 nuevas o reescritas desde el inicio de esta revisión), y `scripts/smoke.sh` completo
  (**SMOKE OK**) al menos una vez tras los cambios de backend de mayor riesgo (paquetes 1, 2 y 3). Los paquetes 4, 5
  y 6 se verificaron contra la API real a mano (creando datos de prueba y comparando la respuesta exacta) porque la
  base de desarrollo, tras varias corridas de `smoke.sh` en la misma sesión, acumuló demasiado en el log de
  actividad para que el script complete sin tocar la base (ver más abajo).
- `web-app/openapi.json` y `schema.d.ts` (de `web-app/` y de `app-almacen/`) se regeneraron cada vez que cambió un
  contrato del API.

## Decisiones tomadas con Luis durante la revisión

1. **M1 (riesgo de que un operador quede sin poder entrar a un aparato).** Este lote le agrega `analytics.view` +
   4 permisos `pulse.*` a la plantilla "Operador de almacén". El PIN de un aparato exige que quien lo asignó cubra
   los permisos actuales del operador (`PinService.AssignerStillCoversAsync`, ya existía desde el Lote 8A); un
   cambio de plantilla puede romper esa cobertura para un PIN ya asignado. Se decidió **no** aflojar esa regla de
   seguridad general. En su lugar: (a) diagnóstico de solo lectura en el seed (bloque 5c, ver Paquete 1) que avisa
   por `PRINT` en el log de `db-init` qué PIN quedarían sin cobertura tras desplegar; (b) seguimiento anotado en
   `docs/lote8A-decisiones.md` (decisión 28) para quien vuelva a tocar esa regla.
2. **S3 (alcance de "Editar usuario").** Un admin de compañía no puede editar nombre/estado de un usuario que
   pertenece a más de una compañía (esos campos son de la cuenta, no de la membresía); esa edición queda reservada
   al administrador de plataforma. Implementado como 403 en `UserAdminService.UpdateUserAsync`.
3. **Alcance de `devices.manage` en la pantalla de Usuarios.** Hoy es inalcanzable en la práctica (`GET /users`
   exige `admin.users` en el controlador, así que alguien con solo `devices.manage` no puede ver la lista donde
   vive la acción de PIN). Se dejó **documentado, sin resolver**: requiere decidir cuánto debería ver alguien con
   solo ese permiso (¿la lista completa de usuarios? ¿algo más acotado?), que es una decisión de diseño de acceso a
   datos y no un bug de una línea.

## Hallazgos ALTA resueltos (con causa de fondo, no solo el síntoma)

- **S1** — `analytics.view` alcanzaba para leer fuentes restringidas (auditoría, eventos de seguridad, correos de
  usuarios) vía la vista previa de informes, porque esa ruta no aplicaba la regla "§2.2 — lo que no puedes leer no
  se lista" que sí se aplicó desde el principio a indicadores/gráficos/Pulso. Corregido en `AnalyticsService.cs`
  (`GetDataSourcesAsync`, `GetReportsAsync`, `CreateReportAsync`, `LoadReportAsync`, `PreviewReportAsync`). Nueva
  comprobación en `scripts/smoke.sh` (usuario con `analytics.view` sin `admin.audit` no ve ni puede previsualizar
  `AUDIT_LOG`/`SECURITY_EVENT`) — verificada a mano contra la API real, pendiente de correr en una corrida limpia
  del smoke completo (ver "Pendiente" abajo).
- **A1/A2 (motor de Indicadores/Gráficos)** — el switch de compañía y el editor pisaban la descripción del otro
  idioma y podían borrar un filtro `or`/`not` al guardar; Editar/Eliminar/el switch de compañía se mostraban a un
  dueño sin `analytics.manage`. Causa de fondo en ambos casos: el API nunca devolvía la descripción en los dos
  idiomas (solo resuelta en uno) y el campo `canEdit` del DTO no miraba el permiso, solo la propiedad — arreglado
  agregando `Descriptions` a `AnalyticsDefinitionDto` y `CanEditWithManageAsync` en el backend.
- **S2** — el alta de usuario sin contraseña generaba una temporal que el `POST /users` devolvía sin tipo (`object`
  crudo, invisible para Swagger): el frontend no tenía cómo saber que existía y la perdía. Nuevo
  `UserCreateResponseDto`; la pantalla la muestra una vez, con botón copiar, en un modal que no se cierra con
  Esc/clic afuera.

## Hallazgos MEDIA/BAJA — resumen por paquete

Ver `docs/frontend/loteF8a-hallazgos-plan.md` para el detalle completo. En resumen:

- **C1/C3** — cambiar "Rango" o editar una definición ya refresca el valor calculado (antes quedaba con el dato
  viejo); el editor ya no descarta las comparticiones por rol al tocar el buscador de personas.
- **Aparatos móviles** — el alta ya no pide ni manda un código técnico oculto: el servidor genera uno legible
  ("AP-XXXXXX") cuando no se manda uno, resolviendo de raíz el hueco entre el plan/mock (que no pedían el campo) y
  el API (que antes lo exigía). El código de registro y la contraseña temporal ya no se pierden cerrando el modal
  por accidente.
- **Catálogos de valores** — "Restaurar" un valor propio desactivado no tenía función real: `GET {entity}` excluía
  siempre los inactivos sin importar `includeDisabled`. Corregido, con un nuevo campo `IsActive` en el DTO. Las
  acciones de fila (Editar/Ajustar/Nuevo valor) ahora se decen por `TenantId` real del valor, no por `IsSystem` de
  la fila — un valor sin dueño es de un dominio global aunque no venga marcado así.
- **Pulso / Organizar** — "Organizar el de la compañía" partía del Pulso *personal* de quien lo abre, no del real
  de la compañía. Nuevo `GET /analytics/pulse?scope=company` (exige `pulse.organize_company`) para el estado real.
- **Cuenta propia** — el spinner de "PIN de la app" ya no gira para siempre si `GET /me/pin` falla.
- **Reautenticación** — se quitaron los `reauth()` preventivos de Roles/Usuarios/PIN: el cliente del API ya pide
  reautenticación sola ante un 403 `aal2_required` y reintenta; pedirla de antemano era redundante y, para "Quitar
  PIN", de plano incorrecto (el servidor no la exige ahí).
- **Pulido de UI** — `.rowbtn` (sin estilo) → `btn sm`; `p.help` fuera de un campo ya tiene estilo (se amplió la
  regla del kit en `base.css`); chips manuales → componente `Chip`; el filtro "entre" ya no desborda a 360 px.

## Huecos entre el plan/mock y el API actual (no son bugs; quedan anotados para decidir después)

- **Compartir "solo por correo"** (para quien no tiene `admin.users`): no está implementado; `ShareDto` no tiene
  un campo de correo. Requiere un cambio de contrato si se quiere.
- **Switch de compañía "con `analytics.manage` o dueño"**: el servidor solo permite al dueño (`EnsureEditable`);
  el plan describía también la ruta por permiso.
- **Quitar PIN propio "con contraseña actual"**: el endpoint `DELETE /me/pin` no acepta cuerpo hoy: quitarlo no
  pide contraseña (fijarlo sí).

## Qué quedó deliberadamente sin tocar

- Migrar `PinModal`/`PinTab` a `Form`/`Field` + zod (hoy usan inputs sueltos, ya probados): puro estilo interno,
  sin nada visible para el usuario. Se puede hacer como un paquete aparte, pequeño y de bajo riesgo, si se quiere
  esa consistencia.
- Paneles de Pulso "desconocidos" que quedan intercalados en el orden al organizar: hoy no puede pasar (el registro
  tiene exactamente 4 claves conocidas); se resuelve en el lote que agregue un panel nuevo.

## Cierre de los tres pendientes (2026-09-29, autorizado por Luis: "Sí, seguí con las tres")

1. **Corrida limpia de `scripts/smoke.sh`.** Se reinició `TeikemDev` (contenedor Docker desechable) y se corrió
   `db-init` de cero; **SMOKE OK**, corrida completa sin ningún fallo (352 líneas, cero `FAIL` reales — el único
   match de la palabra era parte del texto "LOGIN/FAILURE").
2. **Capítulo del manual + capturas + FAQ.** Nuevo capítulo
   [`docs/manual/frontend/f8a-menu-sistema-analisis-y-marca.md`](../manual/frontend/f8a-menu-sistema-analisis-y-marca.md)
   con capturas reales (`img/f8a-*.png`) del menú completo, la marca, Pulso por paneles (organizar mío y de la
   compañía), Indicadores y Gráficos, Roles/Usuarios + PIN (incluida la contraseña temporal y el bloqueo por
   multi-compañía), Aparatos (código autogenerado), Catálogos (ajustar/restaurar) y Mi cuenta → PIN de la app;
   nuevas preguntas en `docs/manual/faq.md` ("Lote F8a — frontend: menú, Sistema y Análisis") e índice actualizado
   en `docs/manual/README.md`.
3. **Recorrido de Playwright** (`web-app/e2e/f8a.spec.ts`, 11 pruebas — las 10 del plan más el paso móvil aparte):
   corrido contra la API real de punta a punta, en verde. Incluye la receta "usuario solo almacén" (rol + usuario
   por API, verificado el menú/Pulso acotado y "Sin permiso" en `/system/users`), el ciclo de PIN (asignar, rechazo
   por trivial, quitar, suspender), Aparatos, Catálogos y el paso móvil (360 px, sin scroll horizontal).

**Hallazgo nuevo, encontrado y corregido al escribir el recorrido (no estaba en los 9 paquetes anteriores):**
reautenticación (AAL2) invisible cuando se dispara desde dentro de otro modal ya abierto (por ejemplo, al asignar
un PIN sin haber reautenticado hace poco). `ReauthProvider` no usa portal (se monta cerca de la raíz de la app),
mientras que `Modal` sí (`createPortal` a `document.body`); con el mismo `z-index` (50), el modal que disparó la
acción queda **por encima** del de reautenticación en el DOM, bloqueando el clic — la persona ve el botón
"Guardar" quedarse en "Cargando…" sin ninguna manera de continuar salvo recargar la página (y perder lo que
escribió). Corregido con una clase `.scrim.reauth{z-index:90}` (por encima de cualquier modal y del toast) en
`web-app/src/styles/base.css` y `web-app/src/kernel/auth/ReauthProvider.tsx`. Verificado con el propio recorrido
(el paso de PIN dispara este camino).

## Seguimiento (2026-09-29): iconos de fila, MFA por compañía/usuario y despacho manual pendiente

Tres decisiones más de Luis sobre lo discutido en la revisión, ejecutadas la misma sesión.

### 1. Iconos en las acciones de fila (en vez de botones de texto)

La maqueta aprobada (`Diseño/teikem-mockups.html`, clase `.rowbtn`) siempre definió las acciones de fila como
icono solo (con `title` de tooltip), no como botones de texto — el mini-mock de F8a (Aparatos/PIN/Catálogos) se
había desviado de eso. Corregido para volver al estándar de la maqueta:

- `RowAction<T>.icon?` (nuevo campo, `web-app/src/kernel/ui/DataTable.tsx`): con él, el botón es solo ícono
  (28×28, `.rowbtn`) con `aria-label`/`title` = `label`; sin él, sigue siendo el botón de texto de siempre (por
  compatibilidad, aunque hoy todas las acciones de fila ya llevan uno).
- Iconos nuevos en `web-app/src/kernel/ui/actionIcons.tsx` (`IconEdit`, `IconTrash`, `IconKey`, `IconPower`,
  `IconRotateCcw`, `IconRefreshCw`, `IconShield`, `IconLogOut`) — no en `app/icons.tsx`, que es solo del shell.
- Aplicado en las 4 tablas de Sistema: Roles (editar/eliminar), Usuarios (editar, cerrar sesiones, PIN, MFA),
  Aparatos (activar/desactivar, nuevo código) y Catálogos (ajustar, restaurar, editar, desactivar).
- `KIT.md` actualizado (tabla de `RowAction<T>`): toda acción de fila nueva lleva `icon`.
- Como el nombre accesible viene del mismo `label` (antes visible, ahora en `aria-label`), ninguna prueba
  existente (`getByRole('button', {name...})`, en Playwright o Vitest) se rompió.

### 2. MFA: política de la compañía y exigirlo a una persona en particular

Verificado primero (Luis pidió confirmarlo antes de tocar nada): `Tenant.MfaRequired` ya existía completo de
punta a punta desde antes de este lote (columna, servicio, `PUT /api/v1/tenant/settings` con `admin.tenant`, y ya
bloqueaba el login) — solo la pantalla "Ajustes de la compañía" que lo expondría en la UI sigue pendiente
(`/system/settings` es un placeholder). No existía ningún control para exigir o resetear el MFA de una persona en
particular sin tocar la política de toda la compañía. Se construyó:

- **Default de `Tenant.MfaRequired` → `true`** (antes `false`), en la entidad (`Tenant.cs`) y en
  `Diseño/logistica-db-estructura.sql`: toda compañía nueva exige MFA a todos por default. `ProvisioningService`
  admite `TenantProvisionRequest.MfaRequired` para que quien aprovisiona lo apague explícitamente si hace falta
  (lo usa `scripts/smoke.sh` para su tenant de prueba, que no enrola TOTP). El tenant demo de desarrollo
  (`DemoTenantSeeder`) lo apaga siempre, para no bloquear las pruebas automatizadas ni el desarrollo local.
- **`UserTenant.MfaRequired`** (columna nueva): exige MFA a una persona puntual en la compañía activa, aparte de
  la política general. `AuthService.LoginAsync` bloquea con `mfa_required` si el TOTP ya está confirmado, o si lo
  exige el tenant, o si lo exige esta membresía en particular (un admin de plataforma no tiene fila de membresía y
  no se evalúa contra ella).
- **`PUT /api/v1/users/{id}/mfa`** `{required: bool}` y **`DELETE /api/v1/users/{id}/mfa`** (resetear: quita el
  TOTP confirmado y los códigos de recuperación de otro usuario que perdió su dispositivo) — ambos `admin.users`
  + AAL2, en `UserAdminService.SetMfaRequiredAsync` / `AuthService.AdminResetMfaAsync`.
- **UsersTab**: columna MFA con chip "Pendiente" cuando se exige pero todavía no está activo; acciones "Exigir
  MFA"/"Ya no exigir MFA"/"Restablecer MFA" (con `ConfirmDialog`).
- Pruebas nuevas: `tests/Teikem.Tests/AuthMfaTests.cs` (5, con `WmsFixture`) — el portón por tenant, por membresía,
  el toggle de `SetMfaRequiredAsync` y el reset de TOTP. El camino de login **exitoso** (más allá del portón) no
  se pudo probar con esta fixture: `IssueAsync` usa `ExecuteUpdateAsync` para `LastLoginUtc`, que el proveedor
  InMemory no traduce — se verificó a mano contra la API real (curl) en su lugar. `scripts/smoke.sh` gana una
  sección nueva ("MFA por usuario") con el mismo ciclo.
- Verificado en la interfaz real (`web-app`): crear el usuario, exigirle MFA, ver el chip "Pendiente", el toast
  "MFA updated" y el modal de reautenticación (AAL2) abriéndose correctamente encima del de Usuarios.

### 3. Despacho manual (pendiente, es un lote nuevo)

Luis rechazó usar "Ajustar" (ajuste de inventario) como salida manual de almacén sin documento — es
semánticamente un despacho, no una corrección — y pidió una operación propia: sin documento obligatorio, "a
ciegas" (como el conteo a ciegas de la app), y la MISMA operación de backend para la app de Almacén y para la
web (no dos implementaciones separadas). **No se construyó todavía** — es un lote nuevo, no un hallazgo de una
línea: hace falta decidir el nombre exacto, si necesita numeración propia (como los recibos/recolecciones), el
permiso y en qué pantallas vive. Queda anotado aquí para cuando se especifique como su propio lote.
