# Avance autónomo — 2026-10-21

Luis pidió (2026-10-21): hacer un plan para lo que falta, ir por partes **sin esperar su confirmación** entre una y otra, escribir aquí cada parte al terminarla y, al final del día, contarle todo; las decisiones que le toquen se le dejan en la sección «Decisiones para Luis».

Reglas: trabajar en `master`, `git pull --rebase` antes de cada commit, un commit por parte, `npm run check` (las 4 pruebas de login intermitentes se ignoran si pasan solas), manual y FAQ por parte, nada de ramas.

## Plan

| # | Parte | Estado |
|---|---|---|
| A | Crear la plantilla desde la pantalla de la integración (sin salir) | ✅ hecha |
| B | Recibo web: pedir lote y vencimiento en el mismo formulario de la línea | ✅ ya estaba hecha (Lote 140) |
| C | Atar la orden de venta a su despacho del almacén (ver y abrir el despacho desde la venta) | ✅ hecha |
| D | Plantillas de importación para más entidades | 🔧 Luis decidió: motor genérico, Productos primero, solo crea. **Productos hecho**; faltan las demás entidades |
| E | Restos de la marca (botón secundario azul marino, apps móviles) | ✅ ya estaba hecha (Lote 138b) |

## Registro (una entrada por parte terminada)

### A — Plantilla desde la integración (hecha)
En el formulario de integración (entrada) aparece «Nueva plantilla» (si el usuario tiene `orders.edit`); abre el editor de plantillas de envíos y, al guardar, la deja elegida. Archivos: `IntegrationFormModal.tsx`, textos es/en, manual 39 §14. Pruebas de Integraciones pasan (59); sin prueba nueva de pantalla ni verificación en navegador.

### B — Lote y vencimiento en la línea del recibo (ya estaba hecha)
Al revisar, el recibo web ya los pide en la misma línea (Lote 140, `InlineCapture` en `ReceiptLinesEditor.tsx`: lote con fabricación y vencimiento, o series, guardando al salir del grupo). El plan viejo estaba desactualizado; no hubo cambios.

### C — Venta ↔ despacho (hecha)
El vínculo ya existía en los datos (`PickBatch.SalesOrderId`) y la venta ya listaba sus despachos; faltaba poder saltar de uno al otro. Ahora el despacho trae `salesOrderPublicId`/`salesOrderNumber` (`PickBatchService`), la ficha del despacho muestra «Orden de venta» con enlace, y en la venta el número del despacho es enlace a su ficha. Manual 18, FAQ, `openapi.json` y tipos. Pruebas: 57 de backend (pick batch/ventas) y 716 de web (ventas/almacén) pasan. No se probó en navegador. *Lo que sigue sin existir:* amarrar ventas con un aviso de salida (otro vínculo); no se pidió.

### D — Plantillas de más entidades (diseño, sin construir)
Hoy solo envíos y consignatarios tienen importación, cada una con ~700-900 líneas propias. Escribí `docs/plantillas-mas-entidades-diseno.md`: motor genérico (`IImportEntity` por entidad), plantilla única para importar y exportar, orden sugerido (productos, proveedores/clientes, almacenes/posiciones, choferes, órdenes de venta/recibos/despachos/ajustes) y estimado. No lo construí porque cambia el modelo de importación entero y tiene decisiones que son tuyas (abajo).

### E — Restos de la marca (ya estaba hecha)
El Lote 138b ya dejó botón primario naranja, secundario azul marino y la paleta de `app-cliente`. Queda como decisión tuya solo el texto sobre naranja: azul marino oscuro (cumple contraste AA) o blanco como su página (no cumple). `app-almacen` se queda solo en tema claro por tu decisión del 2026-10-17.

### Sin verificar en navegador (todo lo de hoy)
El API y la web de tu máquina están corriendo, pero con la base de producción restaurada y sin la sección SQL nueva (columna `Kind` de los labels de factura); no me conecté (no uso tus credenciales). Antes de probar el label de factura: aplicar `db-update` y reiniciar el API.

_(fin del registro del día)_

## Decisiones para Luis

_(se llena cuando aparece una que solo él puede tomar; mientras tanto se toma el valor por omisión que se anota aquí)_

- Label de factura: uno por envío, sin importar los paquetes (**ya decidido por Luis**).
- «Completada» en la última tarjeta de «Paquetes en la calle» = **envíos entregados hoy** (decidido por Luis; ya aplicado).
- Respuestas de Luis a la parte D: motor genérico; Productos primero; importar solo crea y rechaza repetidos; la exportación de las listas ya existe y lo que falta es la salida por Integración. Texto de botones naranja: azul marino oscuro (como está).
- Tercer valor por defecto del cliente («tipo»): solo se implementó servicio y paquete (sin confirmar).
- Facturación por división de Colavora (sin confirmar).
- **Plantillas de más entidades** (parte D): ¿motor genérico o entidad por entidad? ¿orden de entidades? ¿importar actualiza o solo crea? ¿botón Exportar en cada lista? Ver `docs/plantillas-mas-entidades-diseno.md`.
