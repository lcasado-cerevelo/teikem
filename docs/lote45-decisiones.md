# Lote 45 — Importación de consignatarios mejorada: Excel, «(No usar)», columnas unidas, cliente por defecto y persona de contacto (servidor de F-B2) (2026-10-13)

Pedido de Luis sobre la importación del Lote 38. La pantalla es el lote de frontend F-B2 (`docs/frontend/loteFB2-decisiones.md`). Manual: capítulo 02 §2.2 y
`docs/manual/frontend/fb2-importacion-consignatarios.md`; FAQ «Lote 45 / F-B2». **No** incluye el catálogo de códigos postales ni la inferencia pueblo/ZIP/estado
(lote siguiente, cuando Luis entregue la fuente).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Excel .xlsx | `ImportFileReader` lee CSV o .xlsx (ExcelDataReader 3.9.0, **MIT**, sin dependencias en net8.0; la única librería agregada). Una hoja (la pedida o la primera). Celdas a texto: entero sin «.0», formato de ceros (`00000`) rellena, fecha → `yyyy-MM-dd`, fecha y hora → `yyyy-MM-dd HH:mm`, solo hora → `HH:mm` (redondeo al segundo), sí/no → `true/false`. Filas vacías fuera, celdas vacías del final fuera. `.xls` → 400; libro dañado → 400; 5.000 filas / 2 MB y además 64 MB descomprimido (defensa contra un zip inflado). | `Services/ImportFileReader.cs` |
| Codificación del CSV | BOM (UTF-8/UTF-16) → esa; si no, UTF-8 si los bytes son válidos; si no, **Windows-1252** (archivo real de Luis: 1.401 filas en cp1252 sin BOM). `CodePagesEncodingProvider` se registra una vez en el constructor estático del lector (también lo pide ExcelDataReader). | `ImportFileReader.DecodeText` |
| Revisar el archivo | `POST /locations/import/inspect-file` (multipart, `locations.create`): formato, hojas, hoja leída, filas, columnas con encabezado y 3 muestras, y las 3 primeras filas. No guarda nada. | `ConsigneeImportController`, `ConsigneeImportService.InspectFile` |
| Hoja | `validate-file` acepta `sheet`. | ídem |
| «(No usar)» | `field = "ignore"` en `ColumnsJson`: se guarda (el editor recuerda todas las columnas) y `Map` lo salta; la fila sigue. | `ConsigneeImportFields.Ignore`, `ConsigneeImportMapping` |
| Columnas unidas | `ImportColumn`/`ImportTemplateColumnDto` ganan `Part` y `Separator` (opcionales, al final). Un campo de texto en varias posiciones se une en orden de `Part` (luego posición) con el separador de cada parte (null = espacio); las vacías no dejan separador; todas vacías → default. La misma posición puede alimentar varios campos. Reglas: «No usar» + campo → 400; mismo (posición, campo) → «La posición N está repetida.»; campos de formato propio (`SingleColumn`) siguen con «El campo X está repetido.»; máx. 5 partes; separador máx. 10. Al guardar, `NormalizeColumns` numera las partes y quita `Part`/`Separator` donde no aplican. Las plantillas de órdenes no cambian. | `ConsigneeImportMapping`, `ImportTemplateService.SerializeColumns` |
| Cliente por defecto | El cliente del lote es el cliente de todo el archivo y **la fila con su propio cliente válido manda** (antes «La fila indica el cliente 'A', pero el lote es del cliente 'B'.»; el mensaje desaparece). `ImportTemplate.DefaultClientId` (activo; en una plantilla de cliente, ese mismo) en alta/PATCH (`defaultClientPublicId`, `clearDefaultClient`) y en el DTO (`defaultClientPublicId`, `defaultClientName`). `confirm` con `rememberClient: true` lo guarda (`ImportTemplateService.RememberDefaultClientAsync`, en la misma transacción que cierra el lote). | `ConsigneeImportService.ResolveOwner`, `ImportTemplateService` |
| Persona de contacto | `Location.ContactName` (NVARCHAR(150) NULL) en `LocationUpsertRequest`, `LocationPatchRequest` (vacío la borra), `LocationDto`, búsqueda de `/locations` y campo de importación `contactName` (admite default). | `LocationService`, `ConsigneeImportFields.ContactName` |
| Teléfonos | `NormalizePhone`: `787-555-0101`, `787 555-0101`, `7875550101`, `(787)555-0101`, `+1 787…` → 10 dígitos (como el alta manual). Dos o más números en la celda → el primero y aviso «La celda del teléfono trae más de un número; se tomó el primero (N).». Sin 10 dígitos reconocibles, queda igual y decide la regla de ContactPoint. | `ConsigneeImportMapping.NormalizePhone` |
| ZIP | PR/US con 3 o 4 dígitos → 5 con ceros (`901` → `00901`). Se hace antes de los obligatorios: ahí entrará la inferencia por catálogo del lote siguiente (comentario en `ValidateRow`). | `ConsigneeImportMapping.NormalizePostalCode` |
| SQL | Sección «2026-10-13 — Importación de consignatarios, mejoras (Lote 45, F-B2)» al final de `Diseño/logistica-db-update.sql`: `Location.ContactName` y `ImportTemplate.DefaultClientId` (FK `FK_ImportTemplate_DefaultClient`), con `COL_LENGTH`. Sin permisos nuevos (nada que propagar). Espejo EF en `ClientConfigurations` / `OrderConfigurations`. | — |
| Contrato | `openapi.json` (fusión: solo cambia lo que cambió; trae también deriva anterior del servidor no regenerada: multi-ajuste, borrar posiciones, productos) y los `schema.d.ts` de la web y de la app. | — |

## Hallazgos del archivo real de Luis (datos sintéticos en las pruebas; nada personal en el repositorio)

1. Windows-1252 sin BOM → detección de codificación (arriba).
2. 17 columnas, 10 vacías o «Column2…Column11» → «(No usar)» guardado en la plantilla.
3. Teléfonos en formatos mezclados y a veces dos en una celda → `NormalizePhone` + aviso.
4. Números de consignatario repetidos (4 de 1.401) → el mensaje existente ya dice el número y la fila: «El código X se repite en la fila N del archivo.» (no se cambió un mensaje publicado).
5. ZIP vacío en ~23 % de las filas → no es error (lo inferirá el lote del catálogo).
6. **Pendiente de Luis (no se hizo):** el archivo mezcla dos disposiciones de columnas en las mismas filas.

## Cómo se probó

- `dotnet test` (con `TEIKEM_TEST_SQLSERVER`): suite completa **3.848 en verde, 0 omitidas** a media pieza; corrida final solo del área (regla de Luis del 2026-10-13: `--filter` importación, consignatarios, plantillas, `ImportFileReader`, `ImportMapping`, `OrderCatalog`, `SchemaDiffer`, `FrozenSql`, `Location`, aislamiento): **288 en verde**. Nuevas:
  `ConsigneeImportMappingTests` («No usar», uniones con orden/separador/vacías, límites, `NormalizeColumns`, ZIP, teléfonos —10 casos—, persona de contacto),
  `ConsigneeImportImprovementsTests` (lector real de .xlsx con un libro OpenXML escrito en la prueba: hojas, celdas número/fecha/hora/sí-no, formato `00000`, línea física,
  límite de filas, `.xls` y libro roto; codificaciones UTF-8 con/sin BOM, UTF-16 y Windows-1252; servicio con Excel y hoja elegida, CSV cp1252 con columnas genéricas,
  dos teléfonos y número repetido; `InspectFile`; cliente por defecto guardado/listado/quitado y sus mensajes; `RememberDefaultClientAsync`; `ContactName` en
  `LocationService`; modelo EF; sección SQL). Cambiada: la fila con otro cliente ya no es error (`The_batch_client_is_the_default_and_a_row_with_its_own_client_wins`),
  catálogo de 18 campos y la ruta `inspect-file` en la prueba de seguridad del controlador.
- Sección SQL corrida **dos veces** a mano sobre `Teikem_fb2` (idempotente) y por `db-init` en bases desechables nuevas (`Teikem_fb2`, `_fb2s`, `_fb2t`, `_fb2u`, `_fb2v`).
- `db-update --dry-run` sobre una base desechable a la que se le quitaron las dos columnas: lista exactamente «Agregar columna dbo.ImportTemplate.DefaultClientId», «Agregar columna dbo.Location.ContactName» y la FK; `db-update` las aplicó y quedó «al día».
- `scripts/smoke.sh` con pasos nuevos (el paso «importador de consignatarios» pasó completo en dos bases nuevas; la corrida completa no se terminó por la regla nueva de Luis — corre la suite completa él): `inspect-file` de un CSV, plantilla con «ignore», partes y separador, cliente por defecto, «No usar» + campo → 400, validar con
  ZIP `901` → `00901` y persona de contacto, confirmar con `rememberClient`, consignatario creado con `contactName`; el paso del multipart con cliente de lote ahora
  espera que la fila con otro cliente quede con ese cliente; catálogo de 18 campos.

## Decisiones (por default; a revisar)

1. **Cliente por defecto en una columna nueva** (`DefaultClientId`) y no en `ImportTemplate.ClientId`: este último limita para qué cliente sirve la plantilla; el por
   defecto solo propone. Una plantilla de cliente solo admite ese cliente por defecto.
2. **«Recordar el cliente» es explícito** (`rememberClient` al confirmar, encendido por omisión en la pantalla cuando el cliente difiere del de la plantilla): no se
   cambia la plantilla sin que se vea.
3. **La fila con su propio cliente manda** (también sobre una plantilla de cliente): se quitó el error de cliente distinto (el FAQ lo marca como que ya no sale).
4. **Separador por parte** («lo que va antes de esta parte») en vez de uno por campo: permite «Calle, Urb.» con separadores distintos; la pantalla lo muestra por hueco.
5. **Teléfono a 10 dígitos** (lo mismo que guarda el alta manual) y el primero si hay varios, con aviso; no se crean dos ContactPoint.
6. **ZIP de 3–4 dígitos se rellena solo para PR/US** (incluye a EE. UU. por los estados del noreste con 0 inicial).
7. **Excel en el servidor** (no en el navegador): una sola lectura, mismos límites y mensajes que el CSV.
8. **El mensaje del duplicado no se cambió** («código» y no «número»): ya dice el número y la fila, y es un mensaje publicado.

## Defectos encontrados y arreglados (fuera del lote, en el smoke)

- El smoke esperaba 11 motivos de ajuste (ya son 12 con `PHYSICAL_COUNT` del ajuste múltiple) y los mensajes viejos de «muelle» (renombrado a «plataforma» el
  2026-10-12): `El código de la plataforma no se puede cambiar.`, `La plataforma ya tiene una cita que se solapa con ese horario.`, `…cruce de plataforma…`.
- **A revisar (no se tocó):** la sección del 2026-10-12 que renombró «Muelle» dejó la etiqueta del movimiento como «Cruce de **P**lataforma» (mayúscula a media
  frase, por el `REPLACE` de «Muelle»); el smoke ahora la espera así.

## Qué no se pudo verificar / fuera de alcance

- Catálogo de ZIP e inferencia de pueblo/estado (lote siguiente; el punto de enganche está en `ValidateRow`).
- El archivo con dos disposiciones de columnas mezcladas (decisión pendiente de Luis).
