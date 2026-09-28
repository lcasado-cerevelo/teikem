# Texto de arranque del lote F8a (para pegar en una sesión nueva, en local)

Eres Claude Code trabajando en el repositorio `lcasado-cerevelo/teikem` (rama `claude/great-davinci-t2nvtl`; haz `git pull` antes
de empezar). Lee primero `CLAUDE.md`, `web-app/KIT.md` y `docs/frontend/loteF8-plan.md` (v3): es el diseño de detalle **aprobado**
del lote F8a y la única fuente de lo que hay que construir. El `.json` hermano (`docs/frontend/loteF8-plan.json`) es la entrada
del skill `fe-implementar`.

## Qué es F8a

Control de punta a punta por permisos: menú completo de la maqueta (`Diseño/teikem-mockups.html`) con pantalla "pendiente" y
permiso/módulo en cada ítem; Pulso del día por paneles, cada uno con su permiso `pulse.*`, ordenable por compañía y por usuario;
pantallas de Indicadores, Gráficos, Roles y usuarios (con PIN de la app), Aparatos móviles (mínimo) y Catálogos de valores; marca
Teikem (`Logos/`) en la web y en la app móvil. Prioridad de negocio: que Luis pueda configurar un usuario que **solo vea
almacén** (receta en §0 del plan).

## Entorno local (Windows, Visual Studio 2022)

- SQL Server local; `dotnet run --project src/Teikem.Api -- db-init` recrea el esquema y el seed (`Diseño/logistica-db-*.sql`).
- API en `http://localhost:5000` (`dotnet run --project src/Teikem.Api`); comprueba `http://localhost:5000/health`. Si solo
  responde `https://localhost:5001`, cambia `web-app/.env.development` (`VITE_API_URL`) y avisa en el reporte.
- Usuarios demo: `teikem+admin@cerevelo.com` / `Teikem_Admin_2026!` (admin), `teikem+dispatch@cerevelo.com` (despacho),
  `teikem+support@cerevelo.com` (plataforma).
- Frontend: `cd web-app && npm ci && npm run check` (regenera tipos desde `openapi.json`, tsc, oxlint, vitest, build);
  Playwright: `npx playwright test` con el API arriba.
- App móvil: `cd app-almacen && npm ci && npm run check` (el lint es **oxlint** vía `npm run lint`, nunca `npx expo lint`).
- Sin `gh`; GitHub por MCP. Commits en español; no pongas identificadores de modelo en commits ni en archivos.

## Orden de ejecución (no te lo saltes)

1. **P1 backend** (`docs/frontend/loteF8-plan.md` §2 y §3 P1) con el agente `implementer` o el skill `lote-implementar`: permisos
   `pulse.*` en `PermissionCatalog` + plantillas + seed SQL (categoría `PULSE`), registro `PulsePanels`, columna
   `PulseSortOrder`, tabla `PulsePanelSetting`, `GET /analytics/pulse` por paneles con la regla de lectura por fuente de datos,
   `PUT /analytics/pulse/layout?scope=mine|company`, `DELETE …/layout/mine`, pruebas xunit, `scripts/smoke.sh`, manual y FAQ.
   Verifica: `dotnet build && dotnet test`, `db-init` en BD limpia, smoke verde. Commit y push.
2. **En paralelo con 1**: **P8** (marca en la app móvil: `scripts/brand-icons.mjs` + assets + lockup en Registrar/Entrar) y el
   **mini-mock** `docs/frontend/mock-f8a-sistema.html` de las tres pantallas sin maqueta (Aparatos, PIN en Usuarios y Mi cuenta,
   Catálogos de valores), con el mismo `<style>` de la maqueta y un botón "Móvil 360 px" como `docs/frontend/mock-pulso-almacen.html`.
   **Para y muéstrale el mini-mock a Luis antes de codificar esas tres pantallas.**
3. Con el API de P1 corriendo: `cd web-app && npm run api:types` (regenera `openapi.json` y `schema.d.ts`). Commit.
4. **Frontend** con el skill `fe-implementar` y `docs/frontend/loteF8-plan.json` (piezas P0, P7, P2, P3, P4, P5, P6): núcleo
   primero (P0, P7, P2), pantallas en paralelo, `npm run check` como compuerta, revisión de 2 lentes, Playwright
   `web-app/e2e/f8a.spec.ts` (los 10 pasos del plan, proyectos `escritorio` y `movil`), capturas para el manual.
5. Cierre: CI verde en GitHub Actions (jobs `build-test`, `frontend`, `mobile`, `android`; `android-e2e` es informativo),
   `docs/frontend/loteF8a-decisiones.md`, `docs/manual/frontend/f8a-menu-sistema-analisis-y-pulso.md`, actualización de
   `docs/manual/README.md` y `docs/manual/faq.md`. Reporte final a Luis con la receta "usuario solo almacén" probada de verdad
   (crea el rol y el usuario y adjunta las capturas).

## Reglas que no se negocian en este lote

- Todo ítem de menú y toda ruta llevan `perm` (y `module` cuando aplica) con los códigos exactos del API; ningún grupo ni ítem
  se pinta sin permiso. `perm` admite `"a|b"`.
- Todo panel del Pulso pasa por el registro `PulsePanels` y su permiso `pulse.*`; ningún panel se monta "a mano" en `Pulse.tsx`.
- Un indicador o gráfico solo se muestra a quien puede leer su fuente de datos (regla §2.2 del plan), en el Pulso y en las
  pantallas de Análisis.
- Nombres y orden del menú exactamente como la maqueta (7 grupos, 41 ítems); Almacén conserva la partición de F6 y "Recepción"
  pasa a "Recibo".
- Textos con `t('clave')`; identificadores en inglés; comentarios, mensajes y commits en español; mensajes de error del API tal
  cual (el manual los lista con su código HTTP).
- Nada de clientes/consignatarios, ríos de Operación, asistente, logo por compañía, impresoras ni integraciones: quedan como
  pantalla pendiente.

Cuando termines cada paso, di qué hiciste, cómo lo verificaste (comandos y resultado real) y qué falta.
