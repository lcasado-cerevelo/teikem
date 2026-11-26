# Lote 248 FN — pantallas de documentos con vigencia, exención de IVU y avisos

## Qué se construyó
Ficha del cliente: Documentos (columnas de vigencia, edición con rowVersion, pregunta de exención) y panel Impuestos nuevo; Listas › Documentos por vencer (ruta `/catalog/expiring-documents`, `clients.read`, CATALOG); Impuestos: captura única (`ExemptionCaptureModal`), «Vincular documento», pestaña Exención por temporada; Ajustes › Operación › Avisos de vencimiento; marcas del tipo de documento en Catálogos; «Documentos por vencer» en Avisos al cliente; `TaxNoticeBanner` en el pedido guiado y la orden.

## Decisiones
- El panel de documentos de la ficha es angosto: tabla de 4 columnas con dos líneas por fila (la fila mide ≤ 56 px); el archivo se abre con la acción de la fila.
- En listas de exenciones se muestra la etiqueta localizada del tipo de documento (no el texto guardado al crear), para que salga en el idioma de la interfaz.
- El aviso al vender se arma en la web con `t()` a partir de nivel, fecha y días.
- «Documentos por vencer» en Avisos al cliente no cuenta en el resumen de avisos encendidos (viene encendido por omisión).
- La navegación del grupo Catálogo pasa a 6 ítems (orden 30 para el nuevo).

## Cómo se probó
Pruebas de componente de cada pieza, `e2e/lote248-documentos-ivu.spec.ts` (4 aspectos, capturas en `docs/manual/frontend/img/l248-*.png`) contra un API sin correo. El `npm run check` completo lo corre el orquestador.
