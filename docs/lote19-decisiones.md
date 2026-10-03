# Lote 19 — Marca por compañía en el servidor: validación de colores, logos y feriado duplicado (2026-10-03)

Cierre del pedido «Región y formatos de la compañía», parte 3: la marca por compañía (Ajustes → Marca) deja de validarse solo en la
pantalla y de usar los logos de Teikem fijos. Pendientes de backend anotados en `docs/frontend/loteF9-decisiones.md` y en la
bitácora «Marca por compañía: temas de color y logo propio» del documento maestro («un tema inválido entrado por API dejaría la
interfaz ilegible para toda la compañía»; «los logos van a un almacén de archivos»). Número: `docs/lote19-decisiones.md` estaba libre
(el último era el 18). La pantalla de la web y su recorrido llevan el nombre F11 en `web-app/e2e` y en `docs/manual/frontend`.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Validación de la marca | `BrandingRules`: porta la derivación de la paleta y las comprobaciones de `brandTheme.ts` (13 temas, tono y saturación del neutro acotada a 0.45, contraste WCAG de texto ≥ 7:1 y atenuado/acentos ≥ 4.5:1 contra el panel derivado en oscuro y claro, matiz entre acentos ≥ 40°). Rechaza con 400 y mensaje exacto: tamaño (4096 caracteres), JSON mal formado, raíz que no es objeto, campos desconocidos, colores de estado, tipos, hexadecimales, tema inexistente, contraste (dice cuál y en qué modo) y matiz. `PUT /tenant/settings` la aplica a `brandingJson` (campo `brandingJson` en `errors`) | `src/Teikem.Domain/Tenancy/BrandingRules.cs`, `TenantService.cs` |
| Paridad con la web | `validateBrandingJson` (espejo en TS, mismos mensajes) y **vectores compartidos** `tests/shared/brand-vectors.json` (64 casos: los 13 temas, colores propios, frontera de tamaño y de matiz 38°–41°, 39 inválidos con los 10 códigos de error, y las 9 comprobaciones numéricas de cada caso que llega a los colores). Los lee xunit (`BrandingRulesTests`, copiado a la salida con `<None Link>`) y vitest (`brandVectors.test.ts`). Los valores esperados salieron de la implementación original en TS; la de C# coincide exacta (razones con dos decimales) | `tests/shared/`, `brandTheme.ts`, `BrandingRulesTests.cs` |
| Logos: reglas | `BrandLogoRules`: tipo por **contenido real** (firma de PNG con IHDR/IEND, JPEG con SOI/EOI, WebP con RIFF/VP8*, SVG como XML con `DtdProcessing.Prohibit`), tope de 512 KB, tipo declarado que no coincide → 415, SVG sin elementos activos (`script`, `foreignObject`, `iframe`, `object`, `embed`, `link`, `set`, `animate`…), sin atributos `on*`, sin `javascript:`/`vbscript:` (aunque lo disfracen), con `href`/`src` solo `#id` o `data:image/…;base64`, y hojas de estilo sin `@import`/`url()` externos | `src/Teikem.Domain/Tenancy/BrandLogoRules.cs` |
| Logos: almacenamiento | Tabla nueva `dbo.TenantBrandLogo` (por compañía y ranura; `Content VARBINARY(MAX)`, tipo, tamaño, SHA-256, quién y cuándo; `CHECK` de ranura y de tamaño) entre `TenantHoliday` y la capa 1B; entidad `TenantBrandLogo` (`ITenantScoped`, `ISoftDeletable`, `IAuditStamped`, `[AuditEntity(TENANT_LOGO)]` con `Content` `[NotAudited]`), configuración 1:1 y `EntityType` `TENANT_LOGO` en el seed | `Diseño/logistica-db-estructura.sql`, `…-seed.sql`, `Tenant.cs`, `TenancyConfigurations.cs` |
| Logos: servicio y endpoints | `BrandLogoService` (listar, leer, subir/reemplazar, quitar) y `BrandLogosController`: `GET /api/v1/tenant/brand/logos`, `GET/PUT/DELETE …/{slot}` con ranuras `lockup`, `lockup-inverted`, `mark`, `mark-inverted`. Escribir `admin.tenant`; leer solo sesión. Lectura con `ETag` (SHA-256) y `Last-Modified` (304), `Cache-Control: private, no-cache`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox`, `Content-Disposition: inline`. Subida `multipart/form-data` (campo `file`) con el tope **antes de leer el cuerpo** (`BrandLogoUploadLimit`: 413 por `Content-Length`, y tope del cuerpo sin longitud declarada). Nuevas `PayloadTooLargeException` (413) y `UnsupportedMediaException` (415) | `BrandLogoService.cs`, `TenantControllers.cs`, `Exceptions.cs`, `TenantContextMiddleware.cs` |
| Feriado duplicado | `POST /tenant/holidays` con una fecha que ya tiene feriado activo → 409 «Ya hay un feriado en esa fecha.» (`errors.date`) sin pisar el existente; uno quitado antes se reactiva | `TenantService.AddHolidayAsync` |
| Web | `BrandTab` sube/reemplaza/quita los 4 logos (vista previa sobre fondo claro u oscuro, tipo y tamaño, 400/413/415 del servidor bajo la ranura, solo lectura sin `admin.tenant`) y muestra el 400 de la marca junto a los colores; `TenantBrand` baja los logos con el token y `BrandLockup`/`BrandMark` los usan (variante por tema; la otra si falta; respaldo de Teikem); `CalendarTab` muestra el 409 del servidor (se quitó su aviso propio y `holidayOnDate`); `serializeBranding` ya solo escribe las 3 claves | `web-app/src/features/system/…`, `web-app/src/kernel/ui/…` |
| Contrato | `web-app/openapi.json` (del Swagger del API, solo agrega los 2 paths y `BrandLogoDto`), `schema.d.ts` de web y de `app-almacen` con `npm run api:types` | — |
| Humo | Bloque «marca por compañía» en `scripts/smoke.sh` (13 temas, 8 rechazos de marca, logos: subir/reemplazar/leer con cabeceras y 304, 400 con SVG activo/entidades/sin archivo, 413 también sin `Content-Length`, 415, 401/403, aislamiento con otra compañía, bitácora sin binario, quitar; feriado duplicado) | `scripts/smoke.sh` |
| Documentación | Manual funcional (capítulo 01, secciones 11 y 11.2), FAQ «Lote 19», capítulo de pantallas `frontend/f11-marca-por-compania.md` con capturas `f11-*`, índice, `KIT.md`, y los pendientes de `loteF9-decisiones.md` marcados como resueltos | `docs/manual/…`, `docs/frontend/loteF9-decisiones.md`, `web-app/KIT.md` |

## Cómo se probó (ejecutado en este entorno)

- `dotnet build Teikem.sln -c Release`: **0 errores** (5 advertencias que ya existían).
- `dotnet test Teikem.sln -c Release`: **2929 pruebas, 0 fallas.** Nuevas: `BrandingRulesTests` (72: los 64 vectores compartidos, los 13 temas,
  cada regla con su mensaje exacto, límite 4096/4097, orden de las comprobaciones, tope del neutro), `BrandLogoTests` (31: tipo por
  contenido, tipo declarado distinto, no-imágenes, vacío/512 KB exactos y 512 KB + 1 B, imágenes truncadas, 14 motivos de SVG
  rechazado con mensaje exacto, DOCTYPE/entidades, SVG inofensivo aceptado, servicio —subir/reemplazar/quitar/reactivar, errores
  400/413/415/404 sin guardar nada, aislamiento entre compañías—, auditoría sin binario, permisos del controlador, cabeceras de
  seguridad y filtro de tamaño) y `TenantBrandSettingsTests` (11: marca válida y vacía, 7 inválidas con mensaje exacto sin guardar
  nada, 4097 caracteres, feriado repetido 409 sin pisar, reactivar un quitado, otra compañía con la misma fecha).
- Base: SQL Server 2022 ya corriendo; `db-reset --yes` y `db-init` **dos veces**: terminaron bien (tabla `TenantBrandLogo` y `TENANT_LOGO` presentes).
- `scripts/smoke.sh` (API en Development, `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` como el CI, base recién
  reiniciada): **SMOKE OK**, 131 pasos, incluido el bloque nuevo. Antes de la corrida final el bloque falló dos veces por defectos
  reales que se corrigieron: el tope sin `Content-Length` daba un 400 genérico del enlace de modelos (ahora 413 con el formato de
  siempre) y mi archivo «de más de 512 KB» pesaba en realidad menos de 524.288 bytes.
- Web: `npm run check` (tipos generados, `tsc -b`, `oxlint`, vitest, build) **EXIT 0**: 111 archivos, **1103 pruebas** (nuevas: `brandVectors.test.ts`
  con los 64 vectores, `brandLogos.test.ts`, 3 en `Brand.test.tsx`, 6 de logos/marca y la del 409 en `TenantSettingsPage.test.tsx`).
  `app-almacen`: `npm run check` pasó (236 pruebas) con su `schema.d.ts` regenerado.
- Playwright (Vite con `VITE_API_URL=http://localhost:5000`, `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`, sin `playwright install`):
  `e2e/loteF11-marca.spec.ts` (proyecto `escritorio-f11`, nuevo, después de F9) **3 pasaron**: sube logos (claro, oscuro, marca), los ve en la
  barra lateral y cambia de variante con el tema, 415/400/512 KB junto a la ranura, recarga, quita; marca inválida por API (5 casos
  con mensaje exacto, la guardada no cambia) y logos por API (401, 413, 415, 400, cabeceras, contenido idéntico); tema Bosque
  aplicado, 360 px sin scroll horizontal y restauración. Además `loteF1.spec.ts` y `loteF9.spec.ts` (proyectos `escritorio`/`movil`, sin
  dependencias): 11 pasaron, 9 se omiten por diseño de esos archivos. **No** se corrió la suite Playwright completa. La compañía demo quedó
  sin marca ni logos (comprobado en la base).
- Capturas: solo las `f11-*` (barra lateral, logos, errores, tema); no quedó ninguna captura regenerada de otros lotes.
- **No se pudo comprobar aquí:** el CI de GitHub Actions (se verá al hacer push).

## Decisiones a revisar (valores por defecto tomados, aislados)

1. **Tope del JSON de la marca: 4096 caracteres** (`BrandingRules.MaxJsonChars`; la marca real mide menos de 200). Antes eran 200.000; los logos ya no viajan ahí. Si se quiere
   más, es una constante y una línea del vector compartido (`limits.maxChars`).
2. **Campos desconocidos = 400.** Una marca guardada antes con claves ajenas (la demo no tiene) se lee igual, pero la web ya solo escribe `preset`, `useCustom` y `custom`; un
   cliente viejo que reenvíe claves extra recibiría el 400.
3. **Nombres y significado de las ranuras:** `lockup` y `mark` son para fondo **claro**; `lockup-inverted` y `mark-inverted`, para fondo **oscuro** (la regla de la maqueta: tema oscuro → variante `-inv`).
   Si falta una variante se usa la otra; **la marca cuadrada no sustituye al lockup ni al revés** (sin `mark`, la barra colapsada muestra el símbolo de Teikem).
4. **512 KB = 524.288 bytes** (`512 × 1024`). El sobre del multipart tiene 16 KB de holgura antes de rechazar por la cabecera.
5. **Tipo declarado que no coincide con el contenido → 415** (estricto, aunque el contenido sea una imagen válida). Se tolera `application/octet-stream`, el alias `image/jpg` y un
   parámetro `; charset`. Si molesta, se relaja en `BrandLogoRules.Inspect`.
6. **Política de SVG conservadora:** se rechazan `set` y `animate` (pueden cambiar un `href`), toda referencia que no sea `#id` o `data:image/png|jpeg|webp|gif;base64`, y cualquier `DOCTYPE`. Un SVG con fuentes
   web (`@import`) o `<image>` externo se rechaza; hay que incrustarlo o convertir los textos a trazos. Se acepta `<style>` interno sin recursos externos.
7. **Quitar un logo = baja lógica y se libera el binario** (`IsActive = 0`, contenido vacío): la bitácora conserva quién y cuándo, pero el archivo no se recupera; subir otro reutiliza la fila.
8. **Los logos exigen sesión** (es la marca de la interfaz): la pantalla de entrada (login) y la de carga muestran el logo de Teikem porque aún no se sabe la compañía. Un logo propio también
   en el login pediría un endpoint público por compañía (dominio o código), fuera de este encargo.
9. **Caché de la lectura:** `private, no-cache` con `ETag` fuerte (SHA-256): el navegador revalida siempre (304 barato) y un cambio se ve de inmediato en esa sesión; no se usó `max-age`.
10. **Se informa el primer fallo, no todos** (igual que `TenantFormatRules`): en la marca el orden es tamaño → JSON → objeto → campos → tipos → hexadecimales → tema → contraste → matiz.
11. **Entidad de auditoría nueva `TENANT_LOGO`** (en lugar de reutilizar `TENANT`): el `EntityId` de la bitácora es el id del logo, no el de la compañía.
12. **Bases ya creadas:** la estructura y el seed cambiaron (tabla y valor de catálogo). Como en los lotes 16–18, una base existente se recrea (`db-reset --yes` / `scripts/recrear-base.ps1`); no hay scripts `ALTER`.
13. **Un cuerpo que no es `multipart/form-data` recibe 415 sin cuerpo** (lo rechaza el enrutamiento, porque el parámetro `IFormFile` hace que MVC infiera `Consumes`); la web nunca lo manda.
14. **Pruebas de la web:** jsdom y el `Request` de Node no se entienden con un `FormData` con archivos, así que vitest sustituye `FormData` por un registro; la codificación multipart real la cubren el humo y Playwright.

## Qué quedó fuera / pendientes

- **Logo propio en la pantalla de entrada** (ver decisión 8).
- **Paridad del JSON anidado muy profundo:** `JSON.parse` no limita la profundidad y `System.Text.Json` sí (64); un JSON de más de 64 niveles cabe en 4096 caracteres y daría `notObject` en la web y
  `malformed` en el servidor. Ambos lo rechazan; solo difiere el mensaje.
- **La suite Playwright completa y el CI de GitHub Actions** no se corrieron aquí (solo `loteF11`, `loteF1` y `loteF9`); el CI las corre al hacer push.
- Siguen pendientes (otros temas): conteo de recibos por almacén en una llamada, la app del Zebra y los servicios que aún cuentan «hoy» en UTC (ver `docs/lote18-decisiones.md`).
