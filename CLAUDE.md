# Teikem — reglas del proyecto (las hereda todo agente)

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
- **Un solo set de scripts SQL**: un lote que necesite tablas o columnas nuevas edita `Diseño/logistica-db-estructura.sql`
  (respetando el orden de FKs por capas) y `Diseño/logistica-db-seed.sql` (MERGE idempotente). Nada de migraciones EF ni
  scripts adicionales. Las entidades se mapean 1:1 en `src/Teikem.Infrastructure/Persistence/Configurations`.
- **Permisos sembrados desde código** en `PermissionCatalog` (y espejados en el seed). Módulos por tenant: los endpoints
  de un módulo llevan `[RequireModule(ModuleKeys.X)]`.
- **Fuentes de datos para vistas/indicadores/gráficos**: cada módulo registra sus `IDataSource` en `DependencyInjection`
  (clave = código `EntityType`), con `DateField` si representa actividad de un período.
- **Excepciones de dominio** (`NotFoundException`, `ValidationException`, `ConflictException`, `StatusRuleException`,
  `ForbiddenException`) → el middleware las traduce a ProblemDetails. Los servicios devuelven DTOs de `Contracts/`.
- Identificadores en inglés (como el SQL); comentarios, documentos, mensajes de error y commits en español.

## Estructura
- `src/Teikem.Domain`: entidades, constantes de catálogo, `PermissionCatalog`.
- `src/Teikem.Infrastructure`: DbContext, configuraciones, interceptores, runner SQL, DSL, motor de análisis, servicios, seeders.
- `src/Teikem.Api`: Program, policies (`[RequirePermission]`, `[RequireModule]`, `[RequireAal2]`), middleware, controladores `/api/v1/*`.
- `tests/Teikem.Tests`: pruebas unitarias xunit de lógica pura.
- `scripts/smoke.sh`: prueba de humo end-to-end; `.github/workflows/ci.yml`: build + test + db-init + smoke con SQL Server 2022.

## Cómo verificar
```
docker compose up -d sqlserver          # o, sin Docker (contenedor de Claude Code): scripts/dev-sqlserver.sh
dotnet build Teikem.sln && dotnet test Teikem.sln
dotnet run --project src/Teikem.Api -- db-init
dotnet run --project src/Teikem.Api &  scripts/smoke.sh http://localhost:5000
```
Si el entorno no puede descargar el SDK (hosts bloqueados), el árbitro es el CI de GitHub Actions al hacer push.
Un lote no se da por terminado sin CI verde y sin `docs/loteN-decisiones.md` (qué se construyó, cómo se probó, decisiones a revisar).
