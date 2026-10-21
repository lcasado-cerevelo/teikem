# Lote 131 — P11: archivos con varias líneas por orden, aplicable a cualquier cliente (dictado de Luis, 2026-10-19)

## Qué se construyó
- **La plantilla de órdenes dice qué NO es pieza** (sección (bi) de `update.sql`: `ImportTemplate.MultiLine` y `LineRulesJson`). Reglas de línea `{kind: DESCRIPTION|INVOICE, position, operator: EMPTY|NOT_EMPTY|EQUALS|CONTAINS|IN, value}`: la primera que se cumple manda; sin regla que aplique, la línea es **pieza**. Es configuración por plantilla (que puede ser general o de un cliente), no código de AxisCare: sirve a cualquier cliente.
- **Importador (`OrderImportService`)**: con `MultiLine` agrupa las filas por número de envío y arma **un registro por orden** (los datos de la orden salen de la primera celda con contenido de cada columna, piezas primero); cada línea se valida aparte (tipo de paquete, piezas, peso, volumen, número de pieza). Pieza → línea de carga con su `PackageNumber` (campo nuevo `pieceNumber`); descripción → descripción de las líneas de carga (si la pieza no trae la suya); factura → `ClientInvoiceNumber`. Orden sin pieza, pieza repetida en el archivo o ya existente en la compañía, factura sin número → la orden queda con error y no se crea; las demás sí.
- **Web:** el editor de plantillas de órdenes gana «El archivo trae varias líneas por envío» con la tabla de reglas; la vista previa muestra el resumen de líneas y los errores de cada línea; `pieceNumber` entra al catálogo de campos (sin valor por defecto, como orden y factura).

## Cómo se probó
- xunit `ImportLineRulesTests` (clasificación por regla, lista `IN`, primera regla gana, sin reglas todo es pieza, validación con mensajes exactos, normalización) y ajustes del catálogo de campos (22 campos). Suite completa verde.
- vitest: reglas de plantilla (orderNumber obligatorio con varias líneas, reglas mal escritas), resumen y problemas de líneas, constructores del formulario; `npm run check`.
- Playwright `lote131-lineas-por-orden.spec.ts` (escritorio y móvil) contra API y SQL reales: 400 de plantilla, vista previa con líneas (2 piezas + descripción + factura), orden sin pieza, pieza repetida en el archivo, confirmar (la orden nace con 2 piezas, descripción y factura) y volver a subir (orden y piezas ya existen).

## Decisiones a revisar
1. **Una línea de carga por pieza** (con su label); no se agrupan piezas del mismo tipo en una línea (el API necesita el número por línea). Las piezas siguen siendo piezas individuales en escaneo y rutas.
2. **La descripción va a las líneas de carga**, no a una tabla nueva; con varias descripciones se unen con «; » sin repetir.
3. **La factura**: su número es el de la columna de factura mapeada o, si no, el label de esa línea (así llega en AxisCare); si trae varias distintas usa la primera y avisa.
4. **«Bandeja de la integración»** del plan (filas que el mapeo no clasifica): hoy una fila sin número de envío o una orden con errores queda en la vista previa con su motivo; la bandeja persistente llega con las integraciones automáticas (SFTP/correo) del resto de P11.
5. **Las plantillas de consignatarios** no cambian.

## Qué queda de P11 (propuesta, por lotes y con Luis)
Conectores y disparadores (SFTP/carpeta/correo/horario) que corren estas mismas plantillas sin subir el archivo a mano; llaves de API por cliente (secreto una sola vez, hasheado, scopes, límite de uso) con lecturas de órdenes/estatus/facturas y crear órdenes; webhooks de salida con HMAC y reintentos. La lista final de endpoints la decide Luis.
