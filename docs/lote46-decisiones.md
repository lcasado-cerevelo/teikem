# Lote 46 — Catálogo de códigos postales con tipo, inferencia de pueblo/estado/ZIP en la importación de consignatarios y variantes de mapeo (servidor de F-B3) (2026-10-14)

Pedido de Luis sobre la importación del Lote 45 (archivo real con dos disposiciones de columnas mezcladas y ZIP vacío en ~23 % de las filas). La pantalla es el lote
de frontend F-B3 (`docs/frontend/loteFB3-decisiones.md`). Manual: capítulo 02 §2.3 y `docs/manual/frontend/fb3-variantes-y-codigos-postales.md`; FAQ «Lote 46 / F-B3».

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| SQL | Sección «2026-10-14 — Catálogo de códigos postales con tipo, distrito y geolocalización; variantes de mapeo…» al final de `Diseño/logistica-db-update.sql`: `PostalLocality.ZipClass VARCHAR(10)`, `LocaleType CHAR(1)`, `DistrictName NVARCHAR(40)`, `DistrictNo VARCHAR(3)`, `Latitude`/`Longitude DECIMAL(9,6)`, `GeoAccuracy TINYINT` (todas NULL, con `COL_LENGTH`), `CK_PostalLocality_ZipClass` (NULL o los 4 tipos), `IX_PostalLocality_PostalCode` y `ImportTemplate.VariantsJson NVARCHAR(MAX) NULL`. Sin permisos nuevos. | — |
| Modelo EF | Las 7 columnas en `PostalLocality` (tipos 1:1, `IsUnicode(false)` / `IsFixedLength` / `HasPrecision(9,6)`) y el índice; `ImportTemplate.VariantsJson`. El CHECK se documenta en un comentario, como los demás del proyecto. | `CatalogConfigurations`, `OrderConfigurations` |
| Tipos de ZIP | `ZipClasses` (STANDARD, PO_BOX, UNIQUE, MILITARY; `IsDelivery`, `DeliveryRank`, filtro `ParseFilter`/`Passes` con el alias DELIVERY). | `Domain/Catalogs/PostalLocality.cs` |
| API de localidades | `PostalLocalityDto.ZipClass`; `GET /postal-localities?zipClass=` (lista o DELIVERY; desconocido → 400). `IndexAsync` da el índice en memoria (por ZIP de 5 dígitos, ciudad postal y municipio) del catálogo que ya se cacheaba (llave `v3`). | `PostalLocalityService`, `PostalLocalitiesController` |
| Inferencia | `PostalInference.Infer` (pura): ZIP → pueblo (municipio en PR) y estado; pueblo → primer ZIP de entrega (STANDARD, UNIQUE, sin clasificar; menor código); ZIP + pueblo → ambos, aviso si no coinciden; PO_BOX/MILITARY → ZIP de entrega de la misma ciudad (ciudad postal, luego municipio) o rechazo; ZIP o pueblo desconocidos → rechazo; pueblo homónimo en varios estados → rechazo salvo que el estado lo resuelva. Devuelve campos completados, ZIP original, avisos y error. | `Domain/Catalogs/PostalInference.cs` |
| Importación | `ConsigneeImportService.ValidateRow` llama a la inferencia donde el Lote 45 dejó el enganche (antes de los obligatorios; solo si el país es válido y el catálogo lo cubre). La fila guarda `Inferred`, `OriginalPostalCode` (RowsJson) y el DTO los expone (vista previa, GET del lote y resultado del confirm). La plantilla exige `city` **o** `postalCode`; `city` deja de ser `required` en el catálogo de campos. | `ConsigneeImportService`, `ConsigneeImportMapping`, `ConsigneeImportContracts` |
| Variantes | `ImportVariant` / `ImportVariantCondition` y `ConsigneeImportVariants` (validar, `Matches` sin acentos ni mayúsculas, `Select` = primera que se cumple, `Normalize`). `ImportTemplateService`: `variants` en alta/PATCH/DTO, `ColumnsJson` = la primera, `[]` vuelve a una sola, `columns` + `variants` → 400, ORDER → 400. Importación: `ValidateVariantRow` (variante y nombre en la fila; ninguna → error `variant` con `Cells`). | `Domain/Clients/ConsigneeImportVariants.cs`, `ImportTemplateService`, `ConsigneeImportService` |
| Contrato | `openapi.json` (fusión: solo los 12 nodos que cambiaron; el resto idéntico) y `schema.d.ts` de la web y de la app. | — |

## Cómo se probó

- `dotnet test` con `TEIKEM_TEST_SQLSERVER`, solo el área (regla de Luis del 2026-10-13): `--filter` ConsigneeImport, ConsigneePostal, ImportTemplate, PostalLocality,
  FrozenSql, TenantIsolation, SchemaDiffer, ImportMapping, ImportFileReader, OrderImport, Location, OrderCatalog → **354 en verde, 0 omitidas**. Nuevas (35, en
  `ConsigneeImportPostalVariantsTests.cs`): `ConsigneePostalInferenceTests` (ZIP → municipio y estado, pueblo → primer ZIP de entrega con STANDARD antes que UNIQUE y sin
  clasificar admitido, coincidencia y aviso, estado que no se reescribe, PO_BOX y militar sustituidos o rechazados, búsqueda por municipio, pueblo en varios estados,
  ZIP+4, país sin catálogo, filtro de tipos); `ConsigneeImportVariantRulesTests` (operadores, celda que falta, primera que se cumple, validación con llaves
  `variants[i]…`, normalización); `ConsigneeImportPostalVariantsTests` (servicio real con un CSV de dos formatos sintético: variante por fila, inferencia, ajuste del
  ZIP, filas rechazadas, lo guardado en RowsJson; alta/lista/PATCH/`[]` de variantes y sus 400; búsqueda con `zipClass` y 400; modelo EF; sección SQL y guarda del
  script de datos). El mundo de prueba de la importación (`ConsigneeImportWorld`) siembra un catálogo sintético de ZIP. Cambiadas: catálogo de campos (`city` ya no
  es obligatorio), mensaje de la plantilla sin pueblo, la fila de Peñuelas sin ZIP ahora recibe 00624.
- SQL en una base desechable `Teikem_fb3` (`db-init` con la sección nueva): `logistica-db-zip-catalog.sql` corrido **dos veces** con `sqlcmd` (18,6 s y 1,7 s; mismos
  totales: 42.736 filas, 29.808 STANDARD, 9.120 PO_BOX, 1.985 UNIQUE, 573 MILITARY, 1.250 sin clasificar); su **guarda** sobre una base sin las columnas: se detiene
  con el mensaje y salida 1, sin cambiar nada. `db-update --dry-run` después de quitar las 7 columnas, el CHECK, el índice y `VariantsJson`: lista exactamente esos 10
  cambios; `db-update` los aplicó y un segundo `--dry-run` dice 0 cambios.
- `scripts/smoke.sh`: pasos nuevos (zipClass 400 y DELIVERY, plantilla con variantes y sus 400, validar un CSV de dos formatos con pueblo/estado/ZIP deducidos y
  rechazos, confirmar, consignatario guardado, PATCH `variants: []`) y ajustados (mensaje de la plantilla sin pueblo, `city` no obligatorio, el aviso de la fila 8 ya no es
  el único porque se agrega el del estado). Se corrieron **solo los pasos nuevos** contra el API de la base desechable (en verde); la corrida completa queda para Luis.

## Decisiones (por default; a revisar)

1. **ZIP sin clasificar = de entrega.** Si el script de datos no se corrió (o el ZIP no está en la fuente), `ZipClass` es NULL: se acepta como de entrega (después de
   STANDARD y UNIQUE al escoger por pueblo) y el filtro `DELIVERY` lo incluye. Así nada se rompe antes de cargar el catálogo; un tipo explícito (`STANDARD,UNIQUE`) no
   trae los sin clasificar.
2. **Pueblo = municipio en Puerto Rico** (como el formulario de almacén): 00952 SABANA SECA → «Toa Baja». Fuera de PR, la ciudad postal del correo (en mayúsculas,
   como viene: no se inventa otra forma).
3. **Por pueblo, primero la ciudad postal y después el municipio** («Toa Baja» → 00949 por ciudad; «Guaynabo» → por municipio). Orden fijo STANDARD → UNIQUE → sin
   clasificar, menor código, y a igualdad el orden del catálogo.
4. **Pueblo homónimo en varios estados → rechazo**, salvo que el estado de la fila escoja uno (EE. UU.). Preferí no adivinar.
5. **«Misma ciudad» para sustituir un PO_BOX** = la ciudad postal del ZIP; si esa ciudad no tiene ZIP de entrega, su municipio (00611 ANGELES → 00641 Utuado). No se
   usa el pueblo del archivo (puede no coincidir).
6. **El estado del archivo nunca se cambia**; vacío → el del catálogo; distinto → aviso. «Puerto Rico» cuenta como «PR».
7. **Formato del ZIP**: 5 dígitos, ZIP+4 con guion o 9 dígitos; otro formato → rechazo propio («no es válido…»). El ZIP+4 de entrega se guarda como vino.
8. **La inferencia solo corre en países que el catálogo cubre (PR y US)** y con un país válido; los demás siguen como antes (pueblo obligatorio).
9. **Variantes: condiciones con Y** (todas), hasta 5 por variante y 10 variantes; los **valores por defecto son comunes** (no por variante). Una variante sin condición
   solo al final (si no, 400). Se guardan en una columna nueva (`VariantsJson`) y `ColumnsJson` queda con la primera, para que lo viejo siga leyendo algo coherente.
10. **Una sola variante con condición** se permite (filtra filas); el editor solo muestra condiciones con dos o más variantes.
11. **El catálogo en memoria dura una hora**: tras correr el script de datos hay que reiniciar el API (está en el manual y el FAQ).
12. **Índice solo por ZIP** (`IX_PostalLocality_PostalCode`): el script de datos cruza por ZIP; la importación usa el índice en memoria, así que el índice por
    (país, estado, ciudad, tipo) no ayudaba y no se creó.
13. **`city` deja de ser `required`** en `/locations/import/fields` (la regla real es «city o postalCode»): la etiqueta del editor ya no lleva «*» en Pueblo.

## Defectos encontrados

- Ninguno del código anterior. Al correr `sqlcmd` sin `-I` contra la base, un `UPDATE` sobre una tabla con índice filtrado falla por `QUOTED_IDENTIFIER`; el script de
  datos no toca tablas con índices filtrados (corre bien), pero el manual indica `-I` por si acaso.

## Qué no se pudo verificar / fuera de alcance

- La suite completa (`dotnet test` sin filtro, vitest completo, todos los recorridos Playwright y el smoke completo): no se corrieron por la regla de Luis.
- Corregir direcciones ya guardadas con ZIP de apartados (solo la importación ajusta; el alta manual ya no los ofrece).
