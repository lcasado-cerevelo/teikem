# Teikem — reglas del proyecto (las hereda todo agente)

> ## ⛔ EN PRODUCCIÓN DESDE 2026-10-09
> - **`Diseño/logistica-db-estructura.sql` y `Diseño/logistica-db-seed.sql` están CONGELADOS: no se editan, jamás.** Un cambio de estructura **o de datos** va en un archivo **nuevo e idempotente** en `Diseño/cambios/NNNN-descripcion.sql` (ver [Diseño/cambios/README.md](Diseño/cambios/README.md)). `FrozenSqlTests` falla si se tocan.
> - **Recrear la base (`db-reset`, `scripts/recrear-base*.ps1`) está PROHIBIDO fuera de desarrollo.** En producción solo `db-update` (con simulación y respaldo).
> - La rama `Depot-Implementation` guarda el estado de la puesta en producción de Advance Depot, para dar soporte a su operación.

Plataforma de logística multi-tenant. Backend .NET 8 (ASP.NET Core + EF Core 8 + SQL Server). La referencia única de
funcionalidad es `Diseño/logistica-funcionalidades-maestro.md`; el esquema vive en `Diseño/logistica-db-estructura.sql`
y `Diseño/logistica-db-seed.sql`. Se construye por lotes (ver `docs/lote1-decisiones.md` para el formato de cierre de un lote).

## Convenciones que no se rompen
- **`TenantId` sale del principal (JWT `tid`), nunca del request.** Toda entidad con `TenantId` implementa `ITenantScoped`
  (o `IOptionallyTenantScoped` si `NULL` = global) y recibe el filtro global de `TeikemDbContext` automáticamente.
- **Defensa en profundidad**: filtro de tenant (datos) + `[RequirePermission("recurso.accion")]` (acción) + resolver de
  pertenencia `IOwnedEntityResolver` (recurso) para asociaciones polimórficas (`ContactPoint`, `CustomFieldValue`).
- **Catálogos por FK, nunca strings sueltos**: valores de clasificación en `LookupCode` (`Entity` + `InternalCode`),
  estatus en `StatusCode`. Etiquetas multilingües en JSON `{"es":..,"en":..}` resueltas con `MultilingualText`.
  Ids de catálogo se resuelven con `ILookupCache` (LookupCode) o consultando `db.StatusCodes` (StatusCode); no se hardcodean.
- **Cambios de estatus solo vía `StatusService.TransitionAsync`** (valida etapa activa, entradas laterales, escribe
  `EntityStatusHistory`, dispara `IStatusTransitionEffect`). Acciones por estatus se guardan con `StatusService.EnsureAllowedAsync`.
- **Auditoría automática**: entidades con `[AuditEntity("CODIGO_ENTITYTYPE")]` generan `AuditLog` desde el interceptor.
  Secretos con `[SensitiveData]`; timestamps técnicos con `[NotAudited]`. Eventos de acceso van a `ISecurityEventWriter`.
- **Soft delete (`IsActive = 0`), nunca DELETE** de registros con historial. PK `INT IDENTITY` + `PublicId` para exposición externa.
- **Scripts SQL — CONGELADOS desde la producción (2026-10-09)**: `Diseño/logistica-db-estructura.sql` y `Diseño/logistica-db-seed.sql` **no se
  editan nunca** (la prueba `FrozenSqlTests` lo impide). Un lote que necesite tablas, columnas o datos nuevos crea un archivo nuevo e **idempotente**
  `Diseño/cambios/NNNN-descripcion.sql` (siguiente número; `COL_LENGTH`/`OBJECT_ID` para estructura, `MERGE` para datos; nunca se edita uno ya
  publicado). `db-init` y `db-update` los aplican solos, en orden, después de estructura y seed. Nada de migraciones EF. Las entidades se mapean
  1:1 en `src/Teikem.Infrastructure/Persistence/Configurations`.
- **Recrear la base está PROHIBIDO fuera de desarrollo**: `db-reset` (el API lo rechaza si el ambiente no es Development) y
  `scripts/recrear-base.ps1` / `recrear-base-depot.ps1` solo se corren en la máquina de desarrollo, contra una base local. Jamás en producción.
- **Rama `Depot-Implementation`**: foto de lo que se puso en producción para Advance Depot (base, API, web y app); se usa para dar soporte a
  su operación. El trabajo sigue en `master`.
- **Permisos sembrados desde código** en `PermissionCatalog` (y espejados en el seed). Módulos por tenant: los endpoints
  de un módulo llevan `[RequireModule(ModuleKeys.X)]`.
- **Fuentes de datos para vistas/indicadores/gráficos**: cada módulo registra sus `IDataSource` en `DependencyInjection`
  (clave = código `EntityType`), con `DateField` si representa actividad de un período.
- **Excepciones de dominio** (`NotFoundException`, `ValidationException`, `ConflictException`, `StatusRuleException`,
  `ForbiddenException`) → el middleware las traduce a ProblemDetails. Los servicios devuelven DTOs de `Contracts/`.
- Identificadores en inglés (como el SQL); comentarios, documentos, mensajes de error y commits en español.

- **Manual funcional obligatorio por lote** en `docs/manual/`: un capítulo por módulo (`docs/manual/NN-<modulo>.md`) escrito para el
  usuario final y el soporte: cada funcionalidad (qué hace, quién puede: permiso y módulo, cómo se usa: pantalla/endpoint),
  campos y validaciones con el **mensaje de error exacto y el código HTTP**, estatus y transiciones (de → a, quién, efectos,
  qué bloquea), y casos frecuentes. Las preguntas y respuestas se acumulan en `docs/manual/faq.md` (cada mensaje de error
  del lote aparece ahí con qué hacer). Índice en `docs/manual/README.md`. Un lote no se cierra sin su capítulo y su FAQ.

## Estructura
- `src/Teikem.Domain`: entidades, constantes de catálogo, `PermissionCatalog`.
- `src/Teikem.Infrastructure`: DbContext, configuraciones, interceptores, runner SQL, DSL, motor de análisis, servicios, seeders.
- `src/Teikem.Api`: Program, policies (`[RequirePermission]`, `[RequireModule]`, `[RequireAal2]`), middleware, controladores `/api/v1/*`.
- `tests/Teikem.Tests`: pruebas unitarias xunit de lógica pura.
- `scripts/smoke.sh`: prueba de humo end-to-end; `.github/workflows/ci.yml`: build + test + db-init + smoke con SQL Server 2022.

## Frontend web (`web-app/`)
- React 18+ con TypeScript y Vite; cliente del API **generado** desde `web-app/openapi.json` (`npm run api:types`); nunca DTOs a mano.
- El contrato del kit y los patrones de pantalla están en `web-app/KIT.md`: quien construye una pantalla lee ese archivo, no el núcleo.
- Textos de interfaz con `t('clave')` en `src/kernel/i18n/{es,en}.json`; identificadores en inglés; comentarios en español.
- Permisos y módulos siempre con `<Can perm>` / `<ModuleGate module>` usando los códigos exactos del API.
- **Ordenar una tabla paginada por el servidor ordena TODO lo que dice el filtro**, no solo la página en pantalla (`DataTable` lee la consulta completa con `exportRows`, hasta 10 000 filas, y ordena y pagina ahí): toda tabla paginada por el servidor debe llevar `exportRows`.
- Full responsive (360 px en adelante), sin scroll horizontal de página; convenciones de interfaz del documento maestro (ordenar columnas, buscador libre `QBox`, chips sin envolver, idioma sin reiniciar).
- Compuerta antes de cualquier revisión: `npm run check` (tipos generados, tsc, oxlint, vitest, build). Recorridos Playwright en `web-app/e2e` contra el API real.
- Cierre de un lote de frontend: `docs/frontend/loteFN-decisiones.md` + capítulo del manual de pantallas en `docs/manual/frontend/` con capturas.

## Cómo verificar
```
docker compose up -d sqlserver          # o, sin Docker (contenedor de Claude Code): scripts/dev-sqlserver.sh
dotnet build Teikem.sln && dotnet test Teikem.sln
dotnet run --project src/Teikem.Api -- db-init
dotnet run --project src/Teikem.Api &  scripts/smoke.sh http://localhost:5000
cd web-app && npm ci && npm run check && npx playwright test   # frontend (API arriba en :5000)
```
Si el entorno no puede descargar el SDK (hosts bloqueados), el árbitro es el CI de GitHub Actions al hacer push.
Un lote no se da por terminado sin CI verde, sin `docs/loteN-decisiones.md` (qué se construyó, cómo se probó, decisiones a revisar)
y sin su capítulo del manual funcional + FAQ en `docs/manual/`.
