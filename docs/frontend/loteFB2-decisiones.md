# Lote F-B2 — Importar consignatarios: Excel, «(No usar)», columnas unidas, cliente por defecto y persona de contacto (2026-10-13)

Mejoras de la pantalla **Catálogo → Consignatarios** (lote F-B). Servidor: [lote45-decisiones.md](../lote45-decisiones.md). Manual de pantallas:
[fb2-importacion-consignatarios.md](../manual/frontend/fb2-importacion-consignatarios.md); FAQ «Lote 45 / F-B2».

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/consignees/`) |
|---|---|---|
| Modal de importación | Título «Importar consignatarios (CSV o Excel)», archivo `.csv`/`.xlsx`; al escogerlo, primero **revisa** (`inspect-file`) y después valida con la hoja leída. **Hoja del libro** cuando hay varias (cambiarla revalida y descarta el lote anterior). **Cliente por defecto**: sale de la plantilla (al abrir, si la pantalla no trae cliente, y al escoger otra plantilla) y **Recordar este cliente en la plantilla** (encendido) cuando difiere; va como `rememberClient` al confirmar. Vista previa con las columnas de los **campos** (sin «ignore», sin repetir el nombre unido), el teléfono con el formato de la compañía y la columna **Cliente** de cada fila. | `ConsigneeImportModal.tsx` |
| Editor nuevo de plantillas | `ConsigneeTemplateEditorModal` (880 px): una fila por columna del archivo con **En el archivo** (encabezado + 3 muestras) si hay archivo revisado; **(No usar)** como valor vacío del campo; **Usar también en otro campo** (varios campos por columna; «(No usar)» en el primero quita los demás); **Cliente por defecto**; sección **Campos con varias columnas** por cada campo en 2+ columnas: orden con ↑/↓, **Separador** por hueco (máx. 10) y **Resultado** con la primera fila del archivo que tenga algo en esas columnas (sin archivo, «[Columna N]»). Todas las filas viajan (las vacías como `ignore`). | `ConsigneeTemplateEditorModal.tsx` |
| Reglas puras | `consigneeTemplateValuesOf`, `syncJoins`, `moveJoinPart`, `joinPreview` (mismo criterio que el servidor), `consigneeTemplateIssues`/`consigneeTemplateSchema` (espejo de `ValidateColumns`: repetido en campos de formato propio, 5 columnas por campo), `consigneeTemplateColumns`, `buildConsigneeTemplateCreate/Patch` (`defaultClientPublicId` / `clearDefaultClient`), `templateSummary` («1+2. Nombre», «3, 5. (No usar)»), `templateFields`, `isExcelFile`, `IMPORT_FILE_ACCEPT`; persona de contacto en el esquema, el alta, el PATCH y la búsqueda. | `consigneeRules.ts` |
| API | `useInspectImportFile`, `sheet` en `useValidateImportFile`, `rememberClient` en `useConfirmImport` (invalida las plantillas), `keepPreviousData` en `useImportTemplates` (al cambiar de cliente la lista no parpadea). | `api.ts` |
| Persona de contacto | Campo del alta/edición, fila en la ficha y en la lista («pueblo · cliente · persona»), buscador. | `ConsigneeFormModal.tsx`, `ConsigneeDetailPanel.tsx`, `ConsigneeListScreen.tsx` |
| Textos | `consignees.*` nuevos y cambiados en es/en (paridad probada). | `kernel/i18n` |
| Contrato | `openapi.json` y `schema.d.ts` de la web y de la app regenerados. | — |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `tsc -b`, `oxlint` (0 errores; avisos del mismo tipo que los existentes), `vite build` | pasan; `app-almacen`: `tsc -b` pasa con el `schema.d.ts` nuevo |
| vitest (`--maxWorkers=3`) | completo a media pieza: **218 archivos / 2.748** en verde; corrida final solo del área (regla de Luis del 2026-10-13): `features/consignees` **82 en verde** (rules 32, importación 24, lista 22, selector 4) |
| `consigneeRules.test.ts` | columnas del request («ignore», dos campos por columna, parte y separador), `syncJoins`, `moveJoinPart`, `joinPreview`, reglas con mensajes del servidor, plantilla del API ↔ editor con el ancho del archivo y el cliente por defecto, resumen, Excel, persona de contacto, paridad es/en |
| `ConsigneeImport.test.tsx` | revisar antes de validar (campos del multipart), selector de hoja y revalidar con la hoja, CSV sin selector, cliente de la plantilla, «Recordar» y su cuerpo, editor con encabezados y muestras, «(No usar)», dos campos por columna, unión con ↑ y separador (cuerpo exacto), formato repetido, editar una plantilla unida y quitar el cliente |
| `ConsigneeListScreen.test.tsx` | persona de contacto en la ficha, en la fila, en el buscador y en el PATCH |
| Playwright `e2e/loteFB2-importacion.spec.ts` (escritorio y móvil 360 px, `--workers=2`, API propio :5112 y Vite :5177 sobre la base desechable `Teikem_fb2`) | **2 en verde**: libro .xlsx de dos hojas (ZIP y teléfono como números), editor con encabezados, «(No usar)», número también al nombre, « - », cliente por defecto, vista previa (00901, aviso del segundo teléfono), importar, ficha con la persona; CSV Windows-1252 con ñ, número repetido, «Recordar» → la plantilla queda con el otro cliente. Capturas `fb2-*` |
| Regresión Playwright `loteFB-consignatarios` y `loteFC3-ordenes-rapida-importacion` | **8 en verde** (FB actualizado a los textos nuevos; además acepta el estado vacío en una base recién creada) |

## Decisiones

1. **Editor propio para consignatarios.** El editor compartido con órdenes (`ImportTemplateEditorModal`) se queda para las plantillas de órdenes sin cambios; las de
   consignatarios usan `ConsigneeTemplateEditorModal` (filas con varios campos y uniones no caben en el modelo «una columna = un campo»).
2. **Se guardan todas las filas del editor** (las vacías como «(No usar)»): lo que se ve es lo que queda. Una plantilla nueva sin archivo arranca con 5 columnas.
3. **El archivo se revisa al escogerlo** (dos llamadas: revisar y validar): así el editor tiene encabezados y muestras sin leer el archivo en el navegador.
4. **Recordar el cliente**: interruptor visible solo cuando el cliente difiere del de la plantilla, encendido por omisión.
5. **Vista previa por campos**: un campo unido sale una vez con el resultado; se agregó la columna **Cliente** porque ahora cada fila puede tener el suyo.
6. **Separador**: input propio (no del kit) dentro de la sección de uniones, con `maxLength` 10; el espacio por omisión se explica en la ayuda.
7. **Flechas ↑/↓ en texto** (sin ícono nuevo del kit).

## Defectos encontrados y arreglados

- El editor de una plantilla con cliente por defecto parpadeaba (la lista de plantillas se vaciaba al cambiar de cliente): `keepPreviousData`.
- Con la plantilla con cliente por defecto, la lista de plantillas de ese cliente podía cambiar la plantilla elegida: se fija al aplicar el cliente.
- El smoke tenía mensajes viejos de «muelle» (renombrado a «plataforma» el 2026-10-12) y 11 motivos de ajuste (ya son 12): corregidos.
