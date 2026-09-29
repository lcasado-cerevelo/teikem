# Lote F8a — plan de trabajo sobre los hallazgos de revisión (2 lentes)

Este documento junta los hallazgos de la revisión de 2 lentes (A: conformidad con la especificación y el kit; B:
correctness, seguridad e impacto en la app móvil) del frontend web del lote F8a, organizados en 9 paquetes de trabajo.
Es un documento de trabajo — se va tachando/actualizando a medida que se resuelve cada punto. El cierre real del lote
(`docs/frontend/loteF8a-decisiones.md` + capítulo del manual) se escribe aparte, cuando todo esto quede resuelto.

Estado: **plan aprobado para discutir, ejecución NO iniciada.** Hay una persona más trabajando en este código en
paralelo — antes de tocar un paquete, confirmar que no choca con lo que esa persona está editando.

## Decisiones abiertas (bloquean el paquete correspondiente hasta resolverse)

- **M1**: **RESUELTO 2026-09-28** — documentar (ya anotado en `docs/lote8A-decisiones.md`, seguimiento de la
  decisión 28) + diagnóstico de solo lectura en el seed (implementado, ver Paquete 1). No se toca la regla de
  cobertura del PIN.
- **S3** (bloquea el paquete 3, parcialmente): **RESUELTO 2026-09-28** — un admin de compañía NO podrá editar
  (nombre/activo) a un usuario que pertenezca a más de una compañía; esa edición queda reservada al admin de
  plataforma (super admin). Falta implementar.

## Progreso

- **Paquete 1 (backend — seguridad y permisos): HECHO 2026-09-28.**
  - S1: `AnalyticsService.cs` ahora aplica §2.2 (fuente legible) en `GetDataSourcesAsync`, `GetReportsAsync`,
    `CreateReportAsync`, `LoadReportAsync` (nuevo parámetro `requireReadable`, usado por defecto en `GetReportAsync`
    y `RunReportAsync`; en `false` para `UpdateReportAsync`/`DeleteReportAsync`, igual que indicadores/gráficos) y
    `PreviewReportAsync`.
  - C4: el bloque 5b del seed ahora también propaga `analytics.view` a los roles de tenant ya clonados con nombre
    de plantilla (antes solo `pulse.%`).
  - M1: nuevo bloque 5c en `Diseño/logistica-db-seed.sql` — diagnóstico de solo lectura (PRINT, visible en el log de
    `db-init`) de `UserPin` que quedarían sin cobertura de su asignador tras este lote.
  - Verificado: `dotnet build` limpio, `dotnet test` (pruebas puras) en verde, `scripts/smoke.sh` completo → **SMOKE
    OK** contra una base con el esquema y seed actualizados (sin regresiones, incluida la sección de fuentes de
    datos/vistas del Lote 6).
  - Pendiente de este paquete: agregar una prueba unitaria/smoke específica para S1 (hoy solo se verificó
    manualmente + smoke general no cubre el caso "operador con analytics.view intenta preview de AUDIT_LOG") — se
    suma en el Paquete 9.

- **Paquete 2 (motor de Indicadores/Gráficos): HECHO 2026-09-28.**
  - Se descubrió que A1/C2 tenían una causa más profunda que un bug de frontend: `AnalyticsDefinitionDto` nunca
    devolvía la descripción en los dos idiomas (solo `description`, ya resuelta en uno) — el frontend no tenía cómo
    conservar el otro idioma. Se agregó `Descriptions` (dict es/en) al DTO (`AnalyticsContracts.cs`,
    `AnalyticsService.cs`), se regeneró `web-app/openapi.json` + `schema.d.ts` (y el de `app-almacen/`, ver M2).
  - A1/C2: `definitions.ts` ya no reconstruye `descriptions`/`filterJson` dentro de `commonUpsertFields` — los recibe
    ya resueltos de quien llama. `withShowInPulse` manda `descriptions: null` (no cambiar) y el `filterJson`
    original sin pasar por filas (evita que un filtro `or`/`not` se borre). Nueva `filterIsUnrepresentable` +
    aviso en `DefinitionEditor.tsx` cuando el filtro guardado no se puede editar como filas (se conserva tal cual
    hasta que el usuario agregue una fila a propósito).
  - A2: la causa real era que `AnalyticsDefinitionDto.CanEdit` solo miraba dueño/sistema, nunca `analytics.manage`
    (que el `[RequirePermission]` del PUT/DELETE sí exige) — nuevo `CanEditWithManageAsync` en `AnalyticsService.cs`.
    Sin cambios en el frontend: ya estaba gateado con `canEdit`, que ahora es correcto.
  - C1: `useSetMyDateRange`/`useSaveIndicator`/`useSaveChart`/`useDeleteIndicator`/`useDeleteChart` (`api.ts`) ahora
    también invalidan la query del valor/los datos de ESE elemento (antes solo la lista y el Pulso).
  - C3: `setShareValue` en `DefinitionEditor.tsx` ya no descarta las comparticiones por rol al tocar el selector de
    personas.
  - E2 + lente A #2: `IndicatorsPage.tsx`/`ChartsPage.tsx` distinguen cargando/error/sin datos en vez de mostrarlos
    igual.
  - Lente A #1: "Ponle un nombre al indicador."/"…al gráfico." (antes un genérico "Ponle un nombre.").
  - M2 (de paso, mismo cambio de esquema): regenerado también `app-almacen/src/kernel/api/schema.d.ts`.
  - Verificado: `npm run check` en verde (373 pruebas, 8 nuevas), `dotnet build` limpio, `scripts/smoke.sh` completo
    → **SMOKE OK** de nuevo tras los cambios de `CanEdit`/`Descriptions` (sin regresiones).

- **Paquete 3 (Roles y Usuarios): HECHO 2026-09-28**, salvo un punto que quedó documentado en vez de resuelto (ver
  abajo).
  - S2: el `POST /api/v1/users` devolvía `Task<object>` (una anónima `{user, temporaryPassword}`) — Swagger no la
    tipaba, así que el frontend no tenía cómo saber que existía. Nuevo `UserCreateResponseDto` en
    `SecurityContracts.cs`/`SecurityControllers.cs`; `CreateUserModal` ahora la muestra en un segundo paso del mismo
    modal (grande, monoespaciada, con "Copiar") antes de cerrar, cuando no se mandó contraseña. Corregido también el
    texto de ayuda, que prometía un flujo de invitación que no existe.
  - S3: `UserAdminService.UpdateUserAsync` ahora bloquea (403) que un admin de compañía edite nombre/activo de un
    usuario que pertenece a más de una compañía; solo el admin de plataforma puede.
  - S4: quitados los `reauth()` explícitos y preventivos de `RolesTab.tsx`, `UsersTab.tsx` (roles y permisos extra) y
    `PinModal.tsx` (asignar/restablecer y quitar PIN) — el cliente del API (`client.ts`) ya pide reautenticación sola
    ante un 403 `aal2_required` y reintenta la misma llamada; pedirla de antemano era redundante y molestaba con un
    prompt aunque la sesión ya fuera AAL2. Para "Quitar PIN" además era simplemente incorrecto: el servidor nunca la
    exige ahí.
  - Lente A #5: "Permisos extra" ya muestra la etiqueta del catálogo `PermissionCategory`, no el código crudo.
  - Lente A #12: la nota de Roles ya no hardcodea los nombres de las plantillas — los lista de los roles reales
    marcados `isTemplate`.
  - **No resuelto, documentado en su lugar**: hacer alcanzable la rama `devices.manage` de la columna/acciones de PIN
    sin `admin.users` — hoy es inalcanzable en la práctica porque `GET /api/v1/users` (toda la pantalla de Usuarios)
    exige `admin.users` en el controlador. Solucionarlo de verdad requiere decidir cuánto debería ver alguien con
    solo `devices.manage` (¿la lista completa de usuarios con correos? ¿solo para el propósito de PIN?) — es una
    decisión de diseño de acceso a datos, no un bug de una línea; lo dejo para que Luis decida el alcance antes de
    tocarlo.
  - Verificado: `npm run check` en verde (373 pruebas — 2 reescritas para probar el flujo reactivo real de AAL2 en
    vez de uno simulado), `dotnet build` limpio, `SMOKE OK` completo de nuevo.

- **Paquete 4 (Aparatos móviles): HECHO 2026-09-28.**
  - Lente A #6 / M3: en vez de solo corregir el comentario, se resolvió de raíz — `DeviceService.CreateAsync` ahora
    genera un código legible ("AP-XXXXXX") cuando el alta no manda `code` (antes 400 "El código del aparato es
    obligatorio."); con un código explícito (uso administrativo directo, como los aparatos ya sembrados/probados en
    `smoke.sh`) lo respeta tal cual. `devicesApi.ts` ya no tiene el generador de código técnico oculto ni el
    reintento por 409 — la pantalla manda solo Nombre y almacén, como pide el plan.
  - E5: `EnrollCodeModal` y el nuevo modal de contraseña temporal (Paquete 3) ya no se cierran con Esc/clic afuera
    (`dismissible={false}`) — solo con el botón, para no perder un código/contraseña que se muestra una sola vez.
  - M2: ya resuelto de paso en el Paquete 2 (regeneración de `app-almacen/src/kernel/api/schema.d.ts`).
  - Nuevas pruebas: `DeviceServiceTests.Device_without_a_code_gets_one_generated_by_the_server` (backend);
    `DevicesPage.test.tsx` ahora revisa el cuerpo real del POST (antes solo que hubiera un POST — el hallazgo T3).
  - Verificado: `dotnet test` completo (2062 pruebas), `npm run check` en verde (373 pruebas), `smoke.sh` — la
    sección de aparatos pasó limpia (confirmé a mano el `AP-XXXXXX` en la respuesta real), pero la corrida completa
    falló más adelante, en una sección sin relación ("actividad reciente"), por "Argument list too long" de `jq`:
    es un artefacto de haber corrido `smoke.sh` muchas veces hoy contra la MISMA base de desarrollo sin reiniciarla
    (el log de actividad acumulado ya no entra en un solo argumento de shell), no una regresión de este paquete. No
    reinicié esa base porque borrarla es una acción destructiva que prefiero pedir antes de hacer, aunque sea
    desechable — lo hago si Luis lo pide antes del cierre del lote, para una corrida 100% limpia.

- **Paquete 5 (Catálogos de valores): HECHO 2026-09-28.**
  - Lente A #7/E4 (Restaurar sin función real): la causa era que `GetValuesAsync` excluía los valores con
    `IsActive=false` sin importar `includeDisabled` — nunca había dato para ofrecer "Restaurar". Ahora
    `includeDisabled=true` también los incluye, y `LookupValueDto` trae `IsActive` para que la pantalla sepa
    distinguir "desactivado" de "deshabilitado por override". Nueva fila de acción "Restaurar" (`useRestoreValue`,
    ya existía sin usar) para un valor propio desactivado; chip "Inactivo" en la columna Habilitado.
  - S5 (Editar/Ajustar según TenantId real, no `isSystem` por fila): un valor sin dueño (`tenantId` null) es de un
    dominio global aunque no venga marcado `isSystem` — el servidor (`EnsureCanEditDomain`) decide por `TenantId`,
    no por `IsSystem`. `valueOrigin` (api.ts) y las acciones de fila en `CatalogsPage.tsx` ahora se deciden con eso,
    no con `r.isSystem` a secas. De paso corregí la prueba existente, que sin darse cuenta probaba "Nuevo
    valor/Editar" contra un dominio de sistema con un valor mal etiquetado como propio en el mock — un caso que no
    puede pasar en la BD real (`AddValueAsync` siempre pone `TenantId = domain.TenantId`).
  - Lente A #8: "Nuevo valor" ya no se ofrece en dominios globales (antes siempre daba 403).
  - Lente A #9/E3: "Nueva lista" avisa por fila ("Fila N: el código/la etiqueta es obligatorio(a).") en vez de
    descartar en silencio una fila con código sin etiqueta o viceversa.
  - Lente A #13: los 3 modales de Catálogos ahora tienen `onError` → toast, además del aviso que ya mostraba el
    formulario.
  - Cosmético: el título de "Nuevo valor — …" ya usa la etiqueta del dominio, no la clave técnica.
  - Verificado a mano contra la API real: desactivar → oculto → visible con `includeDisabled=true` (`isActive:
    false`) → restaurar → vuelve a aparecer activo. `npm run check` en verde (374 pruebas — 1 nueva, 1 corregida),
    `dotnet test` completo (2062), `smoke.sh` sin fallas nuevas (la única falla sigue siendo la ya explicada,
    acumulación de "actividad reciente" en la BD de pruebas, sin relación).

- **Paquete 6 (Pulso / Organizar): HECHO 2026-09-28.**
  - P1 (partía del Pulso personal del admin): `Pulse.tsx` le pasaba a `PulseOrganizer` el mismo `data` de
    `usePulse()` sin importar el `scope` — con Pulso personal propio, "Organizar el de la compañía" partía de ESE
    estado, no del real de la compañía. Nuevo `GET /api/v1/analytics/pulse?scope=company`
    (`AnalyticsService.GetPulseAsync(companyOnly: true)`, exige `pulse.organize_company`): la misma resolución sin
    la capa personal de quien consulta. `Pulse.tsx` lo consulta aparte (`usePulseCompany`) solo mientras se organiza
    en modo compañía, con su propio spinner/error mientras carga.
  - P2 (paneles desconocidos intercalados): revisado y **dejado como está, a propósito** — hoy no puede pasar (el
    registro de paneles tiene exactamente 4 claves, todas conocidas); diseñar para un panel hipotético futuro sin
    que exista todavía sería trabajo especulativo. Si un lote agrega un panel nuevo, se resuelve en ese lote.
  - Nueva prueba en `Pulse.test.tsx` que reproduce exactamente el hallazgo: mockea una respuesta distinta para
    `?scope=company` y confirma que el Organizador de la compañía muestra ESA, no la personal del admin.
  - Verificado: `npm run check` en verde (375 pruebas — 1 nueva), `dotnet build`/`dotnet test` limpios (2062), y a
    mano contra la API real que `GET pulse` y `GET pulse?scope=company` responden distinto contrato (confirmé el
    camino feliz; el smoke completo no llegó a la sección de Pulso por la misma falla de acumulación ya explicada,
    que ocurre antes en el script).

- **Paquete 7 (Cuenta propia): HECHO 2026-09-28.**
  - E1: `PinTab.tsx` mostraba el spinner para siempre si `GET /me/pin` fallaba (`isLoading || !data`, sin mirar
    `error`). Ahora, si hay error, se ve con `EmptyState` en vez de girar sin parar. Nueva prueba en
    `account.test.tsx`.
  - Puramente frontend, sin cambios de backend/esquema.
  - Verificado: `npm run check` en verde (376 pruebas — 1 nueva).

- **Paquete 8 (pulido de UI): HECHO 2026-09-28**, salvo un punto dejado a propósito (ver abajo).
  - `.rowbtn` (que no existía en ningún CSS) → `btn sm` en los 4 sitios (Editar/Eliminar de Indicadores y Gráficos,
    quitar fila de filtro).
  - `p.help` sin estilo fuera de `.f`: en vez de envolver cada aviso suelto, se amplió la regla del kit
    (`base.css`) de `.f .help` a `.help` — corrige de una vez los usos de `DefinitionEditor`/`CatalogsPage` y
    cualquier otro que ya existiera fuera de un campo, sin tocar los archivos de `warehouse/` (evité esos a
    propósito por la persona que está trabajando ahí).
  - `<span className="chip …">` a mano → componente `Chip` en `Pulse.tsx` (2) y `PulseOrganizer.tsx` (1).
  - `DefinitionEditor` ya tiene `onError` → toast (antes solo el aviso del formulario).
  - Filtro "entre" (`DefinitionEditor.tsx`): los dos `<input>` ya no pueden desbordar el grid a 360 px
    (`minWidth: 0` + `flex: 1`, en vez de su ancho de contenido por defecto dentro del flex).
  - **Dejado a propósito, sin tocar:** migrar `PinModal`/`PinTab` a `Form`/`Field` + zod. Es un cambio de estilo
    interno (ya funcionan y están probados con inputs sueltos + `applyProblemDetails` a mano) sin nada visible para
    el usuario — no lo justifiqué frente al riesgo de tocar código que ya pasa sus pruebas, dado lo que queda del
    lote. Si Luis quiere esa consistencia, es un paquete aparte, pequeño y de bajo riesgo.
  - Verificado: `npm run check` en verde (376 pruebas, sin cambios de backend en este paquete).

- **Paquete 9 (pruebas y documentación): HECHO 2026-09-28.**
  - T1/T2/T3 ya quedaron resueltos de paso en los paquetes 3 y 4 (reautenticación reactiva real, switch de estado
    que valida el toast, cuerpo del POST de aparato).
  - T4 — coberturas nuevas: "Nuevo usuario" sin contraseña muestra y protege la temporal (`system.test.tsx`);
    "Quitar PIN" de otro usuario sin reauth de más (`system.test.tsx`); el switch "Mostrar en Pulso del día" en
    Gráficos, que Indicadores ya tenía y Gráficos no (`ChartsPage.test.tsx`).
  - S1 — nueva comprobación en `scripts/smoke.sh`: un usuario con `analytics.view` sin `admin.audit` no ve
    `AUDIT_LOG`/`SECURITY_EVENT` en `data-sources` y no puede previsualizarlas (404). Verificada a mano contra la
    API real (creé un rol y un usuario de prueba con exactamente esos permisos) porque el smoke completo no llega
    a esa sección por la acumulación de la BD ya explicada.
  - Huecos plan/API documentados en `docs/frontend/loteF8a-decisiones.md` (compartir por correo, switch de
    compañía por permiso, quitar PIN propio con contraseña) en vez de "corregidos", porque son decisiones de
    producto/contrato, no bugs.
  - `docs/frontend/loteF8a-decisiones.md` escrito — cierre formal de esta fase de revisión (distinto del capítulo
    del manual + Playwright, que quedan como la siguiente fase).
  - Verificado: `npm run check` en verde (379 pruebas — 3 nuevas de este paquete), sintaxis de `smoke.sh` validada
    (`bash -n`).

---

## 1. Backend — seguridad y permisos

- **S1 (ALTA):** aplicar la regla "§2.2 — lo que no puedes leer no se lista" también en `preview`, `run` y
  `GET reports` de `AnalyticsService.cs` (hoy solo se aplica a indicadores/gráficos/Pulso). Sin esto, `analytics.view`
  deja leer auditoría, eventos de seguridad y correos vía vista previa de informes.
- **C4 (MEDIA):** el bloque 5b del seed debe propagar también `analytics.view` a los roles ya clonados de "Operador de
  almacén", no solo `pulse.*` — para que BD migrada se comporte igual que BD nueva.
- **M1 (ALTA condicional):** ver discusión abajo — decisión pendiente antes de tocar código.

Archivos: `src/Teikem.Infrastructure/Services/AnalyticsService.cs`, `src/Teikem.Api/Controllers/ExtensibilityControllers.cs`,
`Diseño/logistica-db-seed.sql`.

## 2. Motor de Indicadores/Gráficos (`definitions.ts` + `DefinitionEditor.tsx`)

- **A1/C2 (ALTA):** el switch de compañía y el editor pisan la descripción del otro idioma y pueden borrar un filtro
  `or`/`not`. Arreglo: mandar `descriptions: null` cuando no se tocó ese campo; no colapsar el filtro a `null` si
  `parseFilterRows` no lo puede representar (avisar en vez de borrar).
- **A2 (ALTA):** ocultar Editar/Eliminar/switch de compañía cuando el usuario no tiene `analytics.manage`.
- **C3 (MEDIA):** no descartar las comparticiones por `roleId` al tocar el selector de personas.
- **C1 (MEDIA):** invalidar también la query del valor/gráfico (no solo la lista) al cambiar "Rango" o editar.
- **E2 (MEDIA) + lente A #2:** distinguir "cargando" / "error" / "sin datos" en vez de mostrar los tres igual.
- **Lente A #1:** corregir el texto a "Ponle un nombre al indicador."

Archivos: `definitions.ts`, `DefinitionEditor.tsx`, `IndicatorsPage.tsx`, `ChartsPage.tsx`, `ChartVisual.tsx`, `api.ts`,
`es.json`/`en.json`.

## 3. Roles y Usuarios (`UsersTab.tsx`, `RolesTab.tsx`, `PinModal.tsx`)

- **S2 (ALTA):** mostrar la `temporaryPassword` que devuelve el API al crear usuario sin contraseña (una sola vez, con
  botón copiar) — hoy se pierde y la cuenta queda sin forma de entrar.
- **S3 (ALTA) — RESUELTO:** en "Editar usuario", si el usuario pertenece a más de una compañía, la edición
  (nombre/activo) queda bloqueada para admin de compañía; solo el admin de plataforma puede hacerla.
- **S4 (MEDIA):** reautenticación reactiva (solo ante 403 `aal2_required`, no siempre preventiva); quitar el reauth de
  "Quitar PIN" (el servidor no lo exige); hacer alcanzable la rama `devices.manage` sin depender de `admin.users`.
- **Lente A #5:** usar la etiqueta del catálogo `PermissionCategory` en vez del código crudo en "Permisos extra".
- **Lente A #12:** la nota de Roles debe usar los nombres reales de las plantillas.

Archivos: `UsersTab.tsx`, `RolesTab.tsx`, `PinModal.tsx`, `permissionGroups.ts`, y backend `UserAdminService.cs` (para S3).

## 4. Aparatos móviles

- **Lente A #6 / M3 (MEDIA):** decidir si el código lo genera el servidor (como dice el plan) o se documenta que el
  cliente lo genera a propósito; de cualquier forma, corregir el comentario que dice "nunca se muestra" (sí se
  muestra).
- **E5 (BAJA):** el código de registro no debe perderse con clic afuera/Esc.
- **M2 (BAJA):** regenerar `app-almacen/src/kernel/api/schema.d.ts`.

Archivos: `devicesApi.ts`, `DevicesPage.tsx`, `EnrollCodeModal.tsx`, `app-almacen/`.

## 5. Catálogos de valores

- **Lente A #7/E4 (MEDIA):** dar función real a "Restaurar" — necesita que `GET {entity}` acepte `includeDisabled` de
  verdad (cambio de backend) o mostrarlo solo cuando aplique.
- **Lente A #8 (MEDIA):** ocultar "Nuevo valor" en dominios globales (siempre da 403).
- **Lente A #9/E3 (MEDIA):** avisar en vez de descartar en silencio filas incompletas al crear una lista.
- **Lente A #13 (MEDIA):** enrutar los errores de dominio global al toast.
- **S5 (MEDIA):** mostrar Editar/Ajustar según `TenantId` real del dominio (como decide el servidor), no según
  `isSystem` del cliente.

Archivos: `CatalogsPage.tsx`, `system/api.ts`, backend `LookupService.cs` (solo si se toca lo de "Restaurar").

## 6. Pulso / Organizar

- **P1 (MEDIA):** "Organizar el de la compañía" debe partir del Pulso *de la compañía*, no del personal del admin.
- **P2 (BAJA):** decidir un lugar consistente para paneles desconocidos en el orden (al final, por ejemplo) en vez de
  intercalarlos.

Archivos: `PulseOrganizer.tsx`, `pulseLayout.ts`.

## 7. Cuenta propia

- **E1 (MEDIA):** que el spinner de "PIN de la app" en Mi cuenta muestre error si `GET /me/pin` falla, en vez de girar
  para siempre.

Archivos: `PinTab.tsx`.

## 8. Pulido de UI (BAJA, bajo riesgo, se puede hacer en cualquier momento)

- Agregar `.rowbtn` al kit o cambiar por `btn sm`.
- Estilizar `p.help` dentro de modales.
- Usar el componente `Chip` en vez de `<span className="chip">` a mano.
- Agregar `onError`→toast en `DefinitionEditor`.
- Migrar `PinModal`/`PinTab` a `Form`/`Field`+zod.
- Revisar que el filtro "entre" no desborde a 360px.
- Título de "Nuevo valor" con la etiqueta del dominio, no la clave técnica.

## 9. Pruebas y documentación

- Corregir T1 (probar el reintento real tras 403 `aal2_required`, no reauth preventivo simulado).
- Corregir T2 (que el mock del interruptor de estado pueda fallar y se valide el toast).
- Sumar cobertura T3/T4: alta/edición de usuario (incl. `temporaryPassword`), quitar PIN, error de `GET /me/pin`,
  invalidación tras cambiar rango, cuerpo de `ChartUpsertRequest`, switch "Mostrar en Pulso" en Gráficos.
- Documentar en `docs/frontend/loteF8a-decisiones.md` los huecos plan/API que no son bugs sino decisiones a tomar:
  - Compartir "solo por correo" (Lente A #3): no implementado, requiere cambio de API (`ShareDto` no tiene campo de
    correo).
  - Switch de compañía "con `analytics.manage` o dueño" (Lente A #4): el servidor solo permite dueño.
  - Quitar PIN propio con contraseña (Lente A #10): el endpoint `DELETE /me/pin` no acepta cuerpo.

---

## Discusión: M1 — riesgo de bloqueo de operadores en los aparatos

**El hallazgo:** este lote agrega `analytics.view` + 4 permisos `pulse.*` a la plantilla "Operador de almacén". El
login por PIN en un aparato (y cada refresh de esa sesión) exige que los permisos efectivos del operador quepan
dentro de los de quien le asignó el PIN (`PinService.AssignerStillCoversAsync`, revisado). Si alguien asignó ese PIN
con un rol propio que no tiene `analytics.view`, el operador queda bloqueado (403 en login, 401+revocación en
refresh) hasta que se le reasigne el PIN.

**Tu pregunta:** ¿`analytics.view` es lo que controla qué ve el usuario en el Pulso/dashboard? — **Sí, correcto.**
Controla qué *fuentes de datos* puede leer (auditoría, seguridad, órdenes, etc. según la fuente) y es el permiso que
habilita ver paneles del Pulso como "Actividad reciente"; sin él, aunque tenga `pulse.activity`, el panel no muestra
datos.

**Tu propuesta:** que no se pueda asignar un PIN a un usuario que tenga permisos que el que asigna no tiene.

**Ya existe — y por eso no cierra el hallazgo.** Ese control ya está en el código, desde antes de este lote:
`PinService.EnsureNotHigherAsync` (líneas 308-319), llamado en `SetForUserAsync` y `RemoveForUserAsync`: hoy mismo,
si intentas asignar o quitar un PIN a alguien con más permisos que los tuyos, el servidor responde 403
"No puede asignar ni quitar el PIN de un usuario con más permisos que usted."

**Por qué el hallazgo sigue vivo a pesar de eso:** ese control solo corre *en el momento de asignar* el PIN. El
problema de M1 no es una asignación nueva — es que este lote cambia la PLANTILLA del rol "Operador de almacén"
después de que el PIN ya fue asignado. Un PIN que era válido el día que se asignó (el asignador cubría al operador
en ese momento) deja de serlo el día que se despliega este lote, porque el operador ganó permisos nuevos sin que
nadie haya vuelto a tocar ese PIN. `AssignerStillCoversAsync` (el chequeo en cada login/refresh) es justamente la
red de seguridad para ese caso — y es la que va a empezar a rechazar logins el día del despliegue, en cualquier tenant
donde el PIN lo haya asignado alguien que no sea TenantAdmin (TenantAdmin tiene "todos los permisos" por diseño, así
que siempre cubre cualquier permiso nuevo automáticamente) ni admin de plataforma.

**Alcance real:** esto solo afecta a un tenant si, además de tener operadores con PIN, alguien creó un rol propio
(no TenantAdmin) con `devices.manage` o `admin.users` — un "Supervisor" a la medida, por ejemplo — y lo usó para
asignar esos PINs. No afecta al tenant demo de este entorno (los PINs los asigna el admin).

**Opciones reales para resolverlo (agregar el bloqueo en la asignación no es una de ellas, porque ya existe):**
1. **Documentar y avisar** (mínimo esfuerzo): dejar constancia en `loteF8a-decisiones.md` y en el manual de que,
   antes de desplegar este lote en un tenant en producción con roles propios usados para asignar PINs, hay que
   revisar (o re-otorgar `analytics.view` a) esos roles asignadores, o volver a asignar los PINs afectados después
   del despliegue.
2. **Diagnóstico en el propio script de upgrade (5b)**: agregar una consulta (sin escribir nada, solo `PRINT`/log) que,
   al correr `db-init`, liste los `UserPin` cuyo `UpdatedBy` dejó de cubrir al `UserId` tras el cambio — así quien
   despliega lo ve antes de que el operador se tope con el bloqueo.
3. **Aflojar la regla de cobertura** para que `pulse.*`/`analytics.view` no cuenten como "de más" en
   `AssignerStillCoversAsync` (tratarlos como permisos de solo lectura de bajo riesgo, no como una escalada real).
   Esto cambia el modelo de seguridad general del PIN (hoy es "cualquier permiso de más bloquea"), no solo este caso.

**Mi recomendación:** opción 2 (diagnóstico en el upgrade) + opción 1 (documentar). Es barato, no cambia el modelo de
seguridad del PIN para todos los casos futuros, y le da a quien despliega la visibilidad para actuar antes de que un
operador real se quede afuera. La opción 3 es más cómoda pero abre la puerta a "¿qué otros permisos también
deberían ser una excepción?" — prefiero no tocar esa regla general sin más necesidad.

**Pendiente:** tu decisión entre 1+2 (recomendado), 3, o ambas.
