# Lote F-F1 — Campos personalizados, administración de definiciones (2026-10-12)

Primer sub-lote de F-F (F-F1 este; **F-F2 Vistas e informes sigue pendiente**: `/analytics/reports` continúa como pantalla pendiente). Pantalla `/analytics/custom-fields` (Análisis → Campos personalizados, `admin.customfields`, módulo CUSTOM_FIELDS). Manual: [ff1-campos-personalizados.md](../manual/frontend/ff1-campos-personalizados.md); preguntas: sección «Lote F-F1» de [faq.md](../manual/faq.md); API: capítulo [01 §7](../manual/01-plataforma-y-seguridad.md). Referencia visual: `Diseño/teikem-mockups.html` (`camposScreen`, `renderCFEditorHtml`).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pantalla | Panel de entidades con contador (activos) y tabla de la entidad: Etiqueta, Clave, Tipo (+ lista y N opciones), Requerido, Único, Mostrar en listados, Estado, acciones; Incluir inactivos; nota de la maqueta | `features/customfields/CustomFieldsAdminScreen.tsx`, `customFieldsAdmin.css` |
| Alta/edición | Modal con los 8 tipos, clave (solo alta), etiquetas es/en, descripción, validación amigable por tipo, valor por defecto por tipo, Requerido/Único/Mostrar en listados, Orden | `CustomFieldDefinitionModal.tsx` |
| Lista de valores | Selector de listas del catálogo y «Nueva lista…» en línea (nombre + opciones por coma) con `admin.catalogs` | `CustomListPicker.tsx`, `listChoices.ts` |
| Reglas | Clave, validación, defecto, opciones, esquema zod, cuerpo del API, mapeo de errores | `kernel/custom-fields/definitionRules.ts` |
| Hooks | `useCustomFieldDefinitionsAdmin`, `useSaveCustomFieldDefinition`, `useSetCustomFieldDefinitionActive` | `kernel/custom-fields/api.ts` |
| Menú | `/analytics/custom-fields` pasó de `pending(...)` a pantalla (misma ruta, permiso, módulo y orden) | `app/routes.tsx` |
| Servidor | Defectos corregidos (abajo) | `CustomFieldService.cs` |

`openapi.json` y `schema.d.ts` no cambiaron (contrato igual).

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `definitionRules.test.ts` (25) | clave, validación por tipo (armar/leer, claves desconocidas, JSON roto), defecto por tipo (BOOL, MULTISELECT, DATETIME en hora de la compañía), opciones y listas, cuerpo del alta/edición, esquema con los mensajes exactos, mapeo de errores |
| `CustomFieldsAdminScreen.test.tsx` (21) | lista por entidad y contadores, cambio de entidad y vacío, Incluir inactivos, sin permiso; alta de TEXT, NUMBER, DATE, BOOL, DATETIME, SELECT, MULTISELECT y LOOKUP_REF; «Nueva lista…» (con Enter y errores), sin `admin.catalogs`; validación en cliente; 409 clave repetida, 400 lista inexistente/etiqueta, 500 como aviso; editar, opciones propias, 409 cambio de tipo, desactivar con confirmación y reactivar |
| `navigation.test.ts` | la ruta ya no es pendiente; `/analytics/reports` sigue pendiente |
| `e2e/loteFF1-campos.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio **1 de 1** y móvil (360 px, sin scroll horizontal en cada paso) **1 de 1**. Crea texto con regex, número y selección con lista nueva; clave repetida; ficha de un cliente creado por API (valor inválido, válido y recarga); editar; cambio de tipo 409; desactivar (desaparece de la ficha); Incluir inactivos y Reactivar. Capturas `ff1-*.png` (11) |
| `dotnet test` | 3571 de 3571 (6 nuevas en `CustomFieldDefinitionTests`) |
| `npm run check` | tipos, tsc, oxlint (sin avisos nuevos), vitest (197 archivos, 2504 pruebas), build |

## Defectos encontrados y arreglados

1. **Servidor: valores en una entidad sin resolver (INVOICE).** `PUT /custom-fields/values/{entityType}/{id}` omitía la verificación cuando no había resolver: aceptaba y guardaba valores de ids inexistentes. Ahora responde 400 `La entidad '…' aún no admite campos personalizados (su módulo no está construido).` y tampoco se pueden crear definiciones para esa entidad. Prueba xunit.
2. **Servidor: una definición podía apuntar a un dominio de estatus como lista** (Scope=Status): ningún valor la cumpliría. Ahora `La lista '…' no existe.` Prueba xunit.
3. **Pantalla:** el valor por defecto de un campo de lista aparecía antes de escoger la lista (selector vacío); ahora aparece cuando hay lista. La cifra del panel derecho parecía pertenecer al interruptor y se quitó. Capturas con el pie del modal cortado: alto corregido.

## Diferencias con la maqueta (todas con su razón)

1. **Entidades: 6, no 9.** Cliente, Orden, Chofer, Vehículo, Producto y Ubicación. La maqueta añade Almacén y Contrato (el servidor las soporta pero la web no tiene dónde llenarlas todavía) y **Factura** (el servidor no tiene resolver: no puede guardar valores; ahora lo rechaza).
2. **Tipos: 8 del API** (la maqueta tiene 6): se añaden Fecha y hora y Referencia a catálogo; «Booleano» se llama **Sí/No**.
3. **Interruptores `.sw` en vez de casillas** para Requerido, Único y Mostrar en listados (el kit no trae casilla suelta).
4. **«Único»** como columna de la tabla (la maqueta no la muestra) y columna **Estado** + acciones Desactivar/Reactivar en lugar del bote de basura: el servidor desactiva, no borra.
5. **Validación** (formato, longitudes, mínimo/máximo) y **Orden**, **Descripción** y **Valor por defecto por tipo**: la maqueta solo tiene un valor por defecto de texto.
6. **«Nueva lista…» en línea** como la maqueta, pero solo con `admin.catalogs`; sin él se explica qué pedir. El código de cada opción se arma de su etiqueta (sin acentos, mayúsculas, `_`).
7. **Tabla con el `DataTable` del kit** (orden por columna, pie con rango y Exportar, tarjetas bajo 720 px) en vez de `table.lst` con `sortTh`.
8. La nota inferior de la maqueta se conserva **al final** (como en la maqueta).

## Decisiones tomadas por default para presentar al dueño al final de P1

- **Entidades listadas:** solo las 6 que hoy tienen ficha con `CustomFieldsForm`. Almacén y Contrato se agregan con una línea cuando tengan ficha; Factura no se lista mientras no exista su módulo.
- **El servidor rechaza** (400) valores y definiciones de entidades sin resolver: cambio de comportamiento deliberado (antes aceptaba valores de ids inexistentes).
- **Contador por entidad = campos activos.** Con «Incluir inactivos» la tabla muestra más filas que el contador.
- **Etiqueta en inglés vacía = copia la española; Descripción se guarda igual en es y en en** (el DTO solo devuelve la descripción del idioma actual, no se puede editar cada idioma por separado).
- **Clave:** se recorta y pasa a minúsculas (como el servidor); una clave de un campo desactivado sigue ocupada (409): el manual lo explica.
- **Valor por defecto de BOOL:** casilla; sin marcar = sin valor por defecto (no «No»). MULTISELECT: códigos separados por coma. DATETIME: se escribe en la hora de la compañía y se guarda en UTC. No se valida contra la lista (como el servidor).
- **Fecha y fecha-hora: mínimo y máximo en días respecto a hoy** (es lo que evalúa el servidor); se escribe «Mínimo (días desde hoy)».
- **Texto no ofrece mínimo/máximo numérico** aunque el servidor lo aplicaría a un texto con forma de número.
- **Opciones propias heredadas** (definiciones hechas por API sin lista): se conservan al editar; escoger una lista las reemplaza. LOOKUP_REF siempre exige lista.
- **Nueva lista:** el nombre en inglés es el mismo que el español; sin descripción. Se puede crear una lista duplicada solo con otro nombre (409 del servidor si el nombre da la misma clave).
- **Reactivar sin confirmación** (reversible y sin efecto sobre datos); **Desactivar con confirmación** que explica que los valores se conservan.
- **La entidad elegida va en `?entity=`**; la primera (Cliente) es la omisión.
- **Una tabla sin paginación de servidor** (lista completa, el servidor no pagina): `DataTable` pagina y ordena en el cliente y exporta todo.

## Qué no se pudo verificar

- Edición de varias pestañas a la vez (otro usuario cambia el campo mientras se edita): el servidor no usa `rowVersion` en definiciones, gana la última escritura.
- Entidades Orden, Chofer, Vehículo, Producto y Ubicación en navegador: se probaron en vitest y la ficha de Cliente en el recorrido; las otras fichas usan el mismo `CustomFieldsForm`.
- Listas del sistema muy largas en el selector (más de cientos): solo con las del catálogo de la base de pruebas.
