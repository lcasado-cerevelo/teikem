# Lote F-C3 — Órdenes: entrada rápida e importación CSV (2026-10-12)

Último sub-lote de F-C (F-C1 lista y alta detallada, F-C2 expediente). Pantalla **Operación → Órdenes** (`/orders`). Servidor: [lote40-decisiones.md](../lote40-decisiones.md) (`GET /import-templates/fields` y un defecto de `ValidateColumns`). Manual de pantallas: [fc3-ordenes-rapida-importacion.md](../manual/frontend/fc3-ordenes-rapida-importacion.md); preguntas: sección «Lote F-C3» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`quickEditor`, `initQuick`, `ordenes`) y la importación de consignatarios de F-B ([loteFB-decisiones.md](loteFB-decisiones.md)).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/`) |
|---|---|---|
| Segmentado | **Rápida / Detallada** en la barra del formulario desplegable; por omisión Rápida; recuerda la última elección en `localStorage` (`teikem.orders.entryMode`, con try/catch). | `features/orders/OrderListScreen.tsx`, `orderQuickRules.ts` |
| Entrada rápida | Rejilla `.qg` con barra (Cliente, Líneas, COD acumulado, leyenda Tab/Enter). Enter o **Guardar fila** → `POST /orders` (1 paquete, 1 pieza, `confirmNow=false`, `Idempotency-Key`). Fila guardada de solo lectura con número y enlace; fila nueva con cliente y servicio conservados. Errores del servidor por campo o bajo la fila sin perder lo capturado. | `OrderQuickEntry.tsx`, `orderQuickRules.ts`, `orders.css` |
| Importación | Modal **Importar** (plantilla → cliente → archivo → vista previa del servidor → casillas de confirmación → resultado). | `OrderImportModal.tsx`, `importApi.ts`, `orderImportRules.ts` |
| Editor de plantillas ORDER | El mismo `ImportTemplateEditorModal` de consignatarios con `kind="ORDER"`: reglas de consignatario y paquete, textos `orders.import.*`, endpoint `/import-templates`. | `features/consignees/ImportTemplateEditorModal.tsx` |
| Kit | `IconBolt`, `IconForm` (`screenIcons.tsx`, con prueba); `ConsigneePicker` acepta `aria-label` y `clearable`. | `kernel/ui`, `features/consignees` |
| Textos | `orders.quick.*`, `orders.import.*`, `orders.entry.{mode,quick,detailed}`, `orders.list.import` en es/en. | `kernel/i18n/` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `orderQuickRules.test.ts`, `orderImportRules.test.ts` | validación de la fila, request, errores del servidor por campo, preferencia en localStorage (incluido bloqueado), reglas de plantilla, cuerpo del confirm, estado de fila, paridad es/en |
| `OrderQuickEntry.test.tsx` (16) | Enter y «Guardar fila», fila nueva, contadores, Enter con la lista abierta, validaciones, 422/400/409, reintento con la misma clave tras un corte de red, cliente que asigna el número, «+» |
| `OrderImport.test.tsx` (26) | plantilla estándar, cliente obligatorio, vista previa con errores/avisos/consignatario, límite 2 MB, 400/422, confirmar con opciones e `Idempotency-Key`, descartar, cancelar, 409, permisos, editor ORDER (validaciones, defaults, PATCH, desactivar/reactivar) |
| `OrderListScreen.test.tsx` (+4) | Rápida por omisión, recuerda la elección, almacenamiento bloqueado, botón Importar según permiso |
| `e2e/loteFC3-ordenes-rapida-importacion.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal) verdes. Capturas `docs/manual/frontend/img/fc3-*.png` |
| `e2e/loteFC1-ordenes.spec.ts` | ajustado para elegir **Detallada** (la predeterminada ahora es Rápida) |

## Defectos encontrados y arreglados

1. **Servidor**: `ValidateColumns` ignoraba los defaults al exigir consignatario (el manual decía otra cosa). Arreglado con prueba (Lote 40).
2. **Resultado de la importación** mostraba el código crudo `DRAFT` del estatus: ahora es el chip de estatus del tenant.
3. **Tab en la rejilla** pasaba por la ✕ de quitar consignatario antes de llegar a «+»: `ConsigneePicker` ofrece `clearable={false}` y la rejilla lo usa.
4. **Editor de plantillas** (también el de consignatarios): la tabla de columnas tenía scroll propio y escondía las primeras filas con 7 columnas; ahora crece con el modal.
5. Si falla un envío sin campo señalado, el foco iba al consignatario y su lista se abría tapando el valor: ahora va al botón «Guardar fila».

## Diferencias con la maqueta (todas con su razón)

1. **Sin «Label al guardar»** (los labels son de F-G).
2. **Cliente como selector** en la barra (la maqueta lo fija a un texto) y **Guardar fila** como botón (para táctil; la maqueta solo tenía Enter).
3. **Columna N.º de orden** «(automático)» deshabilitada salvo que el cliente asigne sus números (la maqueta siempre la dejaba escribir).
4. **Consignatario por selector** del directorio del cliente + compartidos (no texto libre con modal al teclear un nombre nuevo); «+» abre el alta de F-B.
5. **Fila guardada: la más reciente primero**, debajo de la fila activa (la maqueta las acumula al final).
6. **Bajo 1,200 px cada fila pasa a tarjeta** con etiquetas; la maqueta usaba scroll horizontal.
7. **Importación**: la vista previa la hace el servidor, hay opciones de confirmación y «Descartar archivo»; el modal es ancho (`lg`).
8. Plantillas: se desactivan/reactivan (no hay DELETE), igual que en F-B.

## Decisiones tomadas por default para presentar al dueño al final de P1

- La rápida solo guarda **borrador** (no confirma); confirmar se hace desde la lista o el expediente.
- Cada fila lleva `requestedDate` y `promisedDate` = hoy de la compañía, como el alta detallada.
- La fila nueva conserva cliente y servicio; el paquete vuelve al predeterminado de la compañía o, si no hay, se conserva el anterior.
- Reintento tras corte de red/5xx con la **misma** `Idempotency-Key`; tras un 4xx, clave nueva (el servidor guarda los 4xx).
- Rápida es la predeterminada; la elección se guarda por navegador (no por usuario del servidor).
- Importar: casilla **Confirmar las órdenes al importar** visible con `orders.create` (el permiso del endpoint; el servidor no pide `orders.edit` para `confirmNow`); **Autorizar crédito excedido** solo con `orders.credit_override` y solo junto con la anterior; **Aceptar facturas repetidas** siempre.
- Idempotency-Key del confirm estable por lote **y por combinación de opciones**.
- Se importan todas las filas válidas (sin elegir subconjunto `rows`).
- Plantilla estándar: consigneeName, line1, city, serviceType, packageType, pieces, codAmount; se ofrece crearla si no hay ninguna.
- Cliente de la importación: selector nativo con los clientes activos (`clients.read`); sin ese permiso no se puede elegir.
- Sin `orders.edit` el modal avisa y no ofrece plantillas (el endpoint las exige).
- Catálogo de campos: `GET /import-templates/fields` (Lote 40).

## Qué no se pudo verificar

- Sin `orders.edit`/`orders.credit_override` con roles reales en el navegador: cubierto por pruebas de componente.
- Crédito excedido real al importar confirmando: solo con respuestas simuladas.
- Archivos de 5,000 filas / cercanos a 2 MB contra el servidor real.
