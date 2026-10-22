# Lote 202 — Logo en la pantalla de entrada (2026-10-22)

## Qué se construyó
- `dbo.Tenant.CustomDomain` y `Subdomain` (índices únicos filtrados) en `logistica-db-update.sql`; `TenantDomainRules` (normalización y búsqueda por dirección).
- API pública sin sesión `GET /api/v1/public/brand` y `/logo/{slot}` (`PublicBrandService`): última compañía → dominio → subdominio → ninguna; solo nombre y logos; límite de 120/min por IP y ruta.
- `PUT /api/v1/platform/tenants/{publicId}/domains` (solo administrador de plataforma). `MeDto.TenantPublicId` para recordar la última compañía.
- Web: la pantalla de entrada carga la marca pública; la sesión recuerda la compañía al entrar y al cambiarla.

## Cómo se probó
`TenantDomainRulesTests` y `PublicBrandTests` (xunit), `publicBrand.test.ts` (vitest) y las pruebas de seguridad/tenant existentes.

## Decisiones a revisar
- El dominio/subdominio solo lo fija el administrador de plataforma y por API (no hay pantalla de plataforma todavía).
- `host` puede mandarlo la web (por si se sirve desde otro sitio que el API); solo cambia qué logo se ve, nunca da acceso.
- Aplica a la web del personal; el login del portal del cliente y las apps no lo usan todavía.
