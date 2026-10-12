# Lote F-B — Consignatarios: lista, ficha, alta/edición e importación por CSV (2026-10-11)

Pantalla **Catálogo → Consignatarios** (`/catalog/consignees`). Solo se tocó `web-app/` y `docs/`: el servidor y el contrato (`openapi.json`,
`schema.d.ts`) ya venían regenerados del lote 38 y **no se editaron** (`npm run api:types` no genera diferencias). Manual de pantallas:
[fb-consignatarios.md](../manual/frontend/fb-consignatarios.md); preguntas del FAQ pendientes de integrar: [_faq-fb.md](_faq-fb.md).
Referencia de la maqueta: `Diseño/teikem-mockups.html`, `consignatariosScreenHtml` y la importación (`renderConsigneeImportModalHtml`,
`renderConsImpTplEditorHtml`). Lote hermano: [loteFA1-decisiones.md](loteFA1-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/consignees/`) |
|---|---|---|
| Pantalla | Maestro-detalle en la rejilla `.md` (280 px + resto, una columna bajo 720 px con la lista arriba). Encabezado: filtro **Cliente** («Todos los clientes», «Compartidos (sin dueño)» y cada cliente), **Nuevo consignatario** (`locations.create`) e **Importar** (`locations.create`). Panel **Consignatarios** (contador, `QBox`, «Mostrar inactivos» = `includeInactive`); filas con número, nombre, chip Activo/Inactivo y «pueblo · cliente dueño» o «Compartido (todos los clientes)». Selección en la URL (`?consignee=`); sin ella, el primero. | `ConsigneeListScreen.tsx`, `consignees.css` |
| Ficha | «Ficha del consignatario» con **Editar** (`locations.update`), número, nombre, dirección, pueblo, teléfono, cliente dueño, **Estado** (interruptor con confirmación) y **Número de factura repetido** (interruptor que guarda al instante con `rowVersion`). Sección plegada **Opciones de entrega** (línea 2, estado, ZIP, país, ventana, minutos de servicio, notas). Debajo, `CustomFieldsForm entityType="LOCATION"`. | `ConsigneeDetailPanel.tsx` |
| Alta / edición | Modal con los campos de la maqueta y, plegado, **Opciones de entrega**. Zod espejo del servidor con los mensajes del manual 02 §2; `applyProblemDetails` (400 por campo, 409 con el mensaje del servidor); PATCH solo de lo cambiado con `rowVersion`. | `ConsigneeFormModal.tsx`, `consigneeRules.ts` |
| Importación | Modal «Importar consignatarios desde CSV» (plantilla → cliente dueño → archivo → vista previa del servidor → Importar/Cancelar → resultado). | `ConsigneeImportModal.tsx` |
| Editor de plantilla | Modal aparte: nombre, delimitador, ¿encabezado?, tabla Columna N → Campo destino, agregar/quitar columnas y valores por defecto. | `ImportTemplateEditorModal.tsx` |
| Hooks | `useConsignees`, `useConsignee`, `useConsigneeContacts`, `useClientOptions`, `useCreateConsignee`, `useUpdateConsignee`, `useSetConsigneeActive`, `useSaveConsigneePhone`, `useImportFields`, `useImportTemplates`, `useSaveImportTemplate`, `useSetTemplateActive`, `useValidateImportFile`, `useConfirmImport` (con `Idempotency-Key`), `useDiscardImport`. | `api.ts` |
| Kit | `TextInput type="time"` (ventana horaria) con prueba en `Form.test.tsx`; `IconPlus` e `IconUpload` (`icons.tsx`, exportados por el barril) con `icons.test.tsx`. Anotado en `KIT.md`. | `kernel/ui` |
| Ruta | `/catalog/consignees` pasó de `pending(...)` a pantalla real (mismo `perm` `locations.read`, módulo `CATALOG`, mismo orden 20). `navigation.test.ts` y `Placeholder.test.tsx` usan ahora Choferes (`/catalog/drivers`) como ejemplo de ruta pendiente. | `app/routes.tsx` |
| Textos | `consignees.*` en `es.json` y `en.json` (se reutiliza `nav.consignees`, que no contradice la pantalla). | `kernel/i18n/` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `npm run check` en `web-app/` | tipos generados sin cambios, `tsc -b`, oxlint (solo avisos que ya existían; ninguno en `features/consignees`) y build: **pasan**. vitest: **156 archivos / 1605 pruebas pasan** (corrida completa y también con `--maxWorkers=4`); con la máquina cargada, `npm run check` completo a veces falla por un worker que muere (código 3221225477) o por tiempos de espera de 5 s en `App.test.tsx`, `auth.test.tsx` y `CycleCountScreen.test.tsx` (pasan solas; ajenas a este lote) |
| `consigneeRules.test.ts` | esquema (obligatorios, topes, minutos, ventana, teléfono), requests de alta y PATCH, teléfono, lista y filtros, definición de plantillas, vista previa, paridad es/en de `consignees.*` |
| `ConsigneeListScreen.test.tsx` (20) | lista (solo DELIVERY, filas, buscador local, inactivos, filtro de cliente y «Compartidos», selección por URL), ficha, factura repetida, edición (PATCH mínimo, teléfono por ContactPoint, makeShared, 409), baja y reactivación, alta (request, validaciones, ventana, 400 por campo, dueño preseleccionado) y permisos |
| `ConsigneeImport.test.tsx` (17) | sin plantilla (archivo deshabilitado; crear «Formato estándar»), vista previa con errores y avisos, re-validar al cambiar de cliente, límite de 2 MB, 400, confirmar con `Idempotency-Key`, resultado, Cancelar (discard), 409, editor (validaciones, defaults, agregar/quitar columnas, PATCH), desactivar/reactivar y permiso del botón |
| `e2e/loteFB-consignatarios.spec.ts` | **escrito, NO ejecutado** (el dueño no quiere Playwright en esta sesión). Las capturas `docs/manual/frontend/img/fb-*.png` no existen todavía |

## Diferencias con la maqueta (todas con su razón)

Aprobadas de antemano:

1. **País y tipo.** El servidor exige país y tipo de localización. El tipo es fijo `DELIVERY` y no se muestra; el país arranca en `PR` (select del catálogo `Country`
   dentro de «Opciones de entrega»; el selector de pueblo lo llena solo si la localidad es de otro país).
2. **Opciones de entrega** (plegada, en el modal y en la ficha): línea 2, estado/región, ZIP, país, ventana horaria, minutos de servicio, notas de acceso y de entrega.
   La maqueta no los tiene; el servidor los guarda y la operación los necesita. Si un error cae en esa sección, se abre sola.
3. **«Pueblo» = ciudad (`city`)**, con `PostalLocalityPicker` (el mismo de Almacén): al elegir llena pueblo, ZIP, estado y país. En la maqueta era un select de pueblos fijos.

Otras (decisión del implementador):

4. **Lista de filas, no `DataTable`.** El encargo decía «ordena y pagina el DataTable con sortValue», pero la maqueta y F-A1 son una lista de filas de 280 px
   (`.unrow`) y el API no pagina. Se mantuvo la lista de la maqueta, ordenada por nombre como la devuelve el servidor, con buscador local (como la maqueta, que filtra
   número, nombre, dirección, pueblo, dueño y estado). No hay `onSort`/`onPage` ni `exportRows` porque no hay tabla paginada por el servidor.
5. **Dueño: nombre del cliente, no su código.** La maqueta pinta `c.id` (el código del cliente); el API entrega `clientName`, que es lo que se muestra en la lista, la ficha y
   los selectores.
6. **Alta con el filtro de cliente puesto** preselecciona ese cliente como dueño (la maqueta arranca siempre en «Compartido»). Es solo un valor inicial, se puede cambiar.
7. **Estado y baja.** El interruptor «Estado» de la maqueta cambiaba al instante; aquí pide confirmación (`ConfirmDialog`) porque una localización inactiva sale de los
   combos de captura. «Número de factura repetido» sí guarda al instante, como la maqueta. «Mostrar inactivos» no está en la maqueta: lo pidió el encargo.
8. **Teléfono.** La maqueta lo guarda en el consignatario; en el servidor es un `ContactPoint` de la localización (`LOCATION`). Se lee de `GET /contacts/LOCATION/{id}`
   (consulta secundaria: un 403 no saca de la pantalla) y se escribe con `contacts.manage` **y** `locations.update`. Sin ellos el campo del modal queda de solo lectura con una
   nota. Si el consignatario se guardó y el teléfono falló, el aviso lo dice («El consignatario se guardó, pero el teléfono no: …»); no se deshace nada.
9. **Alta con teléfono exige `locations.update`.** El teléfono del consignatario recién creado se escribe como `ContactPoint`, y el servidor pide el permiso de la entidad dueña
   (`locations.update`), no solo `locations.create`. Quien solo tiene `locations.create` crea sin teléfono.
10. **Importación: la vista previa la hace el servidor**, no el navegador (la maqueta parseaba el CSV en el cliente). El archivo se valida en cuanto se elige y otra vez si cambia
    la plantilla o el cliente dueño (el lote anterior se descarta). La tabla muestra, por fila, el estado (válida / con aviso / con errores), los valores mapeados por la
    plantilla y las **observaciones con los mensajes del servidor**; «Solo filas con errores o avisos» filtra; se pintan hasta 200 filas (el lote completo, hasta 5.000, vive en el
    servidor y **Importar** crea todas las válidas).
11. **Sin las «pistas» de encabezado.** La maqueta enseñaba en el editor «tu último archivo trae: …» leyendo el CSV en el cliente; ya no se lee el archivo en el navegador, así que
    no se muestra.
12. **Plantillas: desactivar/reactivar, no «Eliminar».** El servidor no tiene `DELETE`. El botón de la papelera de la maqueta pasa a «Desactivar esta plantilla» (con confirmación);
    el interruptor «Mostrar plantillas inactivas» las deja ver para **Reactivar**. Una plantilla inactiva no sirve para importar.
13. **Plantilla «Formato estándar».** La maqueta la sembraba con name, number, address, town, phone. Aquí, si la compañía no tiene ninguna plantilla, la pantalla **ofrece crearla**
    (botón «Crear “Formato estándar”») con las columnas `name, code, line1, city, contactPhone` (los nombres del API). No se siembra sola.
14. **Campo «Cliente» de la plantilla.** La maqueta tenía un campo `client`; el servidor tiene dos, `clientCode` y `clientName` (no pueden estar los dos). El dropdown «Cliente dueño de
    los consignatarios» es el destino por defecto y la columna del archivo, si la plantilla la trae, manda por fila (misma precedencia que la maqueta; la explica el texto de ayuda).
    Las plantillas se crean **generales** (sin cliente); el servidor admite plantillas por cliente (`clientPublicId`) y, si las hay, el selector las ofrece al elegir ese cliente
    (con un cliente dueño solo sirven las generales y las de ese cliente; sin dueño, solo las generales).
15. **Valores por defecto** (no están en la maqueta): filas campo + valor en el editor, solo con los campos que el servidor marca `defaultAllowed` (país, ciudad, minutos de servicio,
    teléfono…). Número, nombre, dirección y cliente no los admiten.
16. **Resultado de la importación.** La maqueta cerraba con un aviso «N consignatarios importados». Aquí el modal pasa a un resultado con creados / fallidos / omitidos y, si hay filas
    fallidas, su motivo; además sale el aviso. La lista se refresca sola.
17. **Ancho del modal.** La maqueta usa 680 px; el kit solo tiene 440 / 640 / 880 px. Se usa 640 (`size="md"`).
18. **Textos en inglés.** Los mensajes de validación en pantalla están en español (idéntico al servidor y al manual) y traducidos en `en.json`; los que manda el servidor salen siempre en español.
19. **Validaciones de la plantilla en el editor** (nombre, name+line1, ciudad, clientCode/clientName, campo repetido) se muestran de una en una: Zod no ejecuta las reglas que cruzan campos
    mientras otro campo (p. ej. el nombre) sigue inválido. El servidor las repite todas.

## Supuestos

- La lista usa `locationType=DELIVERY`: un consignatario de tipo `BOTH` (almacén y entrega) no aparece aquí, sino en Almacenes.
- El filtro por cliente manda `clientId` con `includeShared=false` (como la maqueta: solo los de ese cliente); «Compartidos» trae todos y filtra por `isShared` en el cliente.
- Los clientes del filtro y del dueño salen de `GET /clients` (permiso `clients.read`); sin él, solo se ofrece «Compartido» (y el dueño actual al editar).
- Un consignatario DELIVERY nunca es el «almacén por defecto» de un cliente, por eso el aviso de baja no menciona ese efecto (sí aplica a las otras localizaciones; ver manual 02 §2).
- La baja y la reactivación responden 204; el recibo se vuelve a leer por invalidación.
- La importación manda el archivo por `validate-file` (multipart); `confirm` va sin `rows` (todas las válidas) y con `Idempotency-Key` estable por lote (un reintento no duplica).

## Qué no se pudo verificar

- **Playwright**: el recorrido está escrito y no se ejecutó. Los selectores (`getByLabel`, `.cs-result`) y las capturas son de papel hasta correrlo contra el API real.
- La subida **multipart real** (en jsdom el `FormData` se sustituye por un registro, como en la prueba de logos); la codificación la cubrirá Playwright.
- El aspecto a 360 px y los colores de los chips de la vista previa (`consignees.css`) no se miraron en un navegador.
