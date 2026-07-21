# Plataforma de Logística — Manifiesto de entregables

Diseño de base de datos y funcionalidades para la plataforma de logística (13 módulos, multi-tenant) sobre .NET 8 / EF Core / SQL Server.

## Entregables vigentes

### Funcionalidades (referencia única)
| Archivo | Contenido |
|---|---|
| **`logistica-funcionalidades-maestro.md`** | **Documento maestro consolidado.** Todas las funcionalidades de los 13 módulos + 5 capas transversales, unificadas de los 4 documentos de diseño. **Empieza por aquí.** |

### Base de datos (standalone — fuera del repo de API)
| Archivo | Orden | Contenido |
|---|---|---|
| **`logistica-db-estructura.sql`** | 1º | ~121 tablas ordenadas por dependencias FK, incl. el ciclo COD, catálogo de módulos por tenant, Equipos en alquiler, y Compras (`Supplier`/`PurchaseOrder`) para el inventario propio que Advance revende + vista `vw_LotGenealogy`. Incluye la capa transversal de **campos personalizados e informes por entidad** y `AspNetUsers`/`AspNetRoles` mínimos (guarded). |
| **`logistica-db-seed.sql`** | 2º | Seed idempotente (MERGE): `CatalogDomain` (~100), `LookupCode`, `StatusCode` (con `StageKind`/color), `Permission` (28), 6 roles plantilla, `RolePermission`. |

### Documentos de diseño (detalle de modelado — DDL, FKs, decisiones)
| Archivo | Alcance |
|---|---|
| `logistica-fase1-v3-diseno-completo.md` | Capa transversal (catálogos, estatus, contactos) + módulos 1–3, 7 |
| `logistica-modulos-4-6-flota-wms-crossdock.md` | Módulos 4–6 |
| `logistica-modulos-restantes.md` | Módulos 7–13 |
| `logistica-seguridad-auditoria.md` | Seguridad, RBAC, MFA/AAL2, auditoría, multi-tenancy |

> Los `v1`/`v2` (`logistica-fase1-modulos-1-3.md`, `logistica-fase1-v2-diseno-completo.md`) quedaron **superados por el v3**. Se conservan solo como histórico.

## Orden de aplicación

**Entorno limpio (dev):**
```
1. logistica-db-estructura.sql
2. logistica-db-seed.sql
```

**Producción (con Identity vía EF migrations):**
```
1. Ejecutar la migración de ASP.NET Core Identity (crea AspNetUsers/Roles/etc. reales)
2. logistica-db-estructura.sql   (los CREATE de AspNet* van guarded; no chocan)
3. logistica-db-seed.sql
```

Ambos scripts son **idempotentes** (re-ejecutables sin duplicar). El seed usa `MERGE` por clave natural.

## Convenciones (no romper)
- La BD se entrega **SEPARADA** del repo de API. Nunca dentro de `Scripts/` del API.
- API y frontend siempre en **zips separados**.
- Identidad = ASP.NET Core Identity con llaves `int`. Permisos sembrados desde código (el tenant no los edita); roles = data del tenant.
- `TenantId` sale del principal autenticado, **nunca** del request.

## Próximos pasos (siguiente fase)
1. Generar el **proyecto base EF Core**: entidades + `DbContext` con filtro global de tenant + migración por capas.
2. Confirmar política de **retención/particionado** para `AuditLog`, `SecurityEvent` y `DriverLocationPing` (crecen rápido).
3. Confirmar `TierMode` por defecto de cada cliente conocido (Caguas = GRADUATED).
