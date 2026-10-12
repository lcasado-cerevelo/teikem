# Lote 40 — Catálogo de campos de las plantillas de órdenes (servidor de F-C3) (2026-10-12)

Servidor de las pantallas web F-C3 (entrada rápida e importación de órdenes). Solo cambia el API de plantillas de importación de órdenes; no hay cambios de base de datos.

## Qué se construyó

| Pieza | Resultado | Dónde |
|---|---|---|
| Endpoint nuevo | `GET /api/v1/import-templates/fields` → `[{ field, required, defaultAllowed }]` con los 21 campos del catálogo de órdenes (equivale a `GET /locations/import/fields`). Mismo módulo (`LTL_GROUND`) y permiso (`orders.edit`) que el resto de `/import-templates`. | `ImportTemplatesController.Fields`, `ImportTemplateService.GetOrderFields` |
| Banderas | `required` siempre `false` (las reglas son de grupo: consignatario y paquete). `defaultAllowed = false` para `orderNumber`, `clientInvoiceNumber` y `consigneeName` (`ImportFields.NoDefault`): identifican cada fila. Es una guía para el editor; `ValidateColumns` no rechaza esos defaults. | `ImportMapping.cs` |
| Defecto corregido | `ImportMapping.ValidateColumns` solo miraba las **columnas** para exigir consignatario, aunque el manual 03 §7.1 decía «columna o default». Ahora un default de `consigneeCode`/`consigneeName` también cuenta (una plantilla «todo va a este consignatario» se rechazaba con `La plantilla debe incluir consigneeName o consigneeCode.`). | `ImportMapping.ValidateColumns` |
| Contrato | `web-app/openapi.json` regenerado (solo la ruta nueva) y `schema.d.ts` de `web-app` y `app-almacen`. | — |

## Cómo se probó

- `ImportMappingTests`: catálogo (21 campos, en orden, `required` falso, los tres sin default) y el default de consignatario (+3 pruebas).
- `ConsigneeImportControllerSecurityTests`: la ruta `GET fields` existe en `ImportTemplatesController`, que ya estaba fijado a `LTL_GROUND` y `orders.edit` por acción.
- `dotnet build Teikem.sln` y `dotnet test`: todas verdes (3,562 pruebas).

## Decisiones a revisar

- El endpoint vive en `/import-templates/fields` (no en `/orders/import/fields`) porque el catálogo es de las plantillas y exige `orders.edit`, como ellas.
- Manual 03 §7.1 actualizado (ruta nueva y la aclaración del default de consignatario).
