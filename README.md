# Teikem — Plataforma de logística multi-tenant

Backend .NET 8 (ASP.NET Core + EF Core + SQL Server) construido por lotes a partir del documento maestro
`Diseño/logistica-funcionalidades-maestro.md`. Este repositorio contiene:

| Carpeta | Contenido |
|---|---|
| `Diseño/` | Documento maestro y el **único** set de scripts SQL: `logistica-db-estructura.sql` (incluye las tablas de Identity) y `logistica-db-seed.sql`. La BD se entrega separada del API, según el README de Diseño. |
| `src/Teikem.Domain` | Entidades (mapeo 1:1 a las tablas), constantes de catálogo, catálogo de permisos (sembrado desde código). |
| `src/Teikem.Infrastructure` | `TeikemDbContext` (filtro global de tenant), interceptor de auditoría, runner de scripts SQL, motor de DSL/análisis y servicios de negocio. |
| `src/Teikem.Api` | ASP.NET Core: JWT, RBAC por policy, filtros de módulo y AAL2, controladores `/api/v1/*`, Swagger. |
| `tests/Teikem.Tests` | Pruebas unitarias de lógica pura (DSL, TOTP, etiquetas multilingües, rangos de fecha, agregados). |
| `web/` | Landing pública (sin relación con el API). |

## Arranque en desarrollo

```bash
docker compose up -d sqlserver                     # SQL Server 2022 en localhost:1433 (sa / Teikem_Dev_2026!)
dotnet run --project src/Teikem.Api -- db-init     # crea la BD y aplica: estructura → seed → seeders de código
dotnet run --project src/Teikem.Api                # https://localhost:5001/swagger
dotnet test                                        # pruebas unitarias
```

`db-init` registra cada script en `dbo.__SchemaVersion` por nombre + hash SHA-256: el seed se vuelve a aplicar si cambia (es MERGE); la estructura se aplica una sola vez sobre BD limpia (si cambia, recrea la BD de desarrollo).
Con `Seed:Demo:Enabled=true` (default en `appsettings.json`) se aprovisiona el tenant demo **Advance Logistics** con:

| Usuario                 | Contraseña           | Rol                                                  |
| ----------------------- | -------------------- | ---------------------------------------------------- |
| `admin@teikem.local`    | `Teikem_Admin_2026!` | TenantAdmin de Advance                               |
| `despacho@teikem.local` | `Teikem_Admin_2026!` | Dispatcher                                           |
| `soporte@teikem.local`  | `Teikem_Admin_2026!` | Administrador de plataforma (opera cualquier tenant) |

Flujo básico: `POST /api/v1/auth/login` → `GET /api/v1/me` (tenant activo, permisos efectivos, módulos encendidos) → resto de endpoints con `Authorization: Bearer <accessToken>`.
El idioma de las etiquetas sale de `Accept-Language` (o `X-Lang: en`).

## Estado: seis lotes construidos

| Lote | Módulo | Cierre | Manual |
|---|---|---|---|
| 1 | Plataforma y seguridad (catálogos, estatus, contactos, RBAC, auditoría, campos personalizados, vistas, indicadores, gráficos, módulos) | `docs/lote1-decisiones.md` | `docs/manual/01-plataforma-y-seguridad.md` |
| 2 | Clientes y contratos | `docs/lote2-decisiones.md` | `docs/manual/02-clientes-y-contratos.md` |
| 3 | Órdenes de transporte (con importador) | `docs/lote3-decisiones.md` | `docs/manual/03-ordenes-de-transporte.md` |
| 4 | Flota, choferes y mantenimiento | `docs/lote4-decisiones.md` | `docs/manual/04-flota-choferes-mantenimiento.md` |
| 5 | Trips y rutas | `docs/lote5-decisiones.md` | `docs/manual/05-trips-y-rutas.md` |
| 6 | Inventario y almacén (WMS, compras mínimas, cross-dock) | `docs/lote6-decisiones.md` | `docs/manual/06-inventario-y-almacen.md` |

Cada cierre documenta qué se construyó, cómo se probó (con el enlace a la corrida de CI) y las decisiones a revisar.
El manual funcional completo, con preguntas frecuentes, está en `docs/manual/` (índice en `docs/manual/README.md`).
Los planes de diseño aprobados de cada lote están en `docs/loteN-plan.md`.

## Prueba de humo end-to-end

Con SQL Server y el API arriba, `scripts/smoke.sh http://localhost:5000` recorre los seis lotes (112 pasos) creando datos con sufijo
de tiempo; es re-ejecutable sobre una BD persistente. Necesita `bash`, `curl` y `jq` (en Windows: Git Bash o WSL).
