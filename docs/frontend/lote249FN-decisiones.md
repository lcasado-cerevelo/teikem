# Lote 249 FN — pantallas de modificar una orden de venta, adición, «Por aprobar», reabrir el empaque y unir al envío

## Qué se construyó
- `features/sales/changes/`: `ModifyOrderModal` (asistente de cuatro pasos: Líneas, Términos, Autorización, Vista previa), `ChangeLinesEditor`, `ChangeAuthorizationStep`, `ChangePreview`, `ChangeHistoryPanel`, `ChangeDetailModal`, `ChangeDecisionActions` (aprobar y aplicar, rechazar con nota, retirar), `AdditionModal`, `PendingApprovalBanner`, `PendingApprovalsTab`, `changesApi.ts`, `changeRules.ts` y `changes.css` (clases con prefijo `soc-`).
- Órdenes de venta: botones «Modificar orden» y «Orden interna de adición» en la ficha (`OrderModal`), etiquetas «Adición» y «Modificación pendiente» (`OrderBadges`), pestaña «Por aprobar» (`SalesOrdersScreen`, `?tab=approvals`, visible con `sales.modify.approve`); `salesApi.ts` ya no llama la ruta vieja de líneas.
- Pedido guiado: quien tiene `sales.price` escribe el precio de cualquier línea (`ProductsStep`, `wizardRules`).
- Almacén: `ReopenPackDialog` y `JoinOrderShipmentDialog` en la ficha de la recolección y de la maestra.
- Ajustes › Operación › «Modificaciones de órdenes» (`SettingsSalesSection`) y ficha del cliente › Perfil (`ClientSalesChangeSettings`).
- Contrato regenerado (`web-app/openapi.json`, `schema.d.ts` de `web-app` y `app-cliente`). `app-almacen` y `app-movil` no cambian.

## Decisiones
- El texto del servidor en español (bloqueos, motivos de recolecciones, regla de aprobación) se traduce en la web desde datos o por reconocimiento del mensaje conocido (`serverText` en `changeRules.ts`); un mensaje desconocido se muestra como lo manda el servidor. La razón del IVU (por ejemplo `TAXABLE`) y los números de factura salen como código en la diferencia.
- La fecha de autorización por defecto es «ahora + 59 s» redondeada al minuto, para no quedar antes de la orden recién creada (el servidor rechaza una fecha anterior a la orden).
- «Unir al envío» calcula la familia (orden, original y adiciones) con las recolecciones de `GET sales-orders/{id}`; no hay endpoint de candidatos.
- «Mínimo: 0» aparece en una línea solo recolectada porque la reversa mínima permite bajar (se deshace la recolección). Quitar la línea entera es la acción «Quitar línea».
- Los ajustes de la compañía usan un segundo panel dentro de `SettingsSalesSection`, sin tocar `OperationsTab`.
- La bandeja «Por aprobar» pagina con `skip`/`take` (tope 200) y lleva `exportRows`.
- El menú no cambia (`navigation.test.ts` sin cambios): la bandeja es una pestaña de Órdenes de venta.
- Se corrigieron en el recorrido: una fila del historial de 60 px, un interruptor que desbordaba a 390 px y la fecha de autorización por defecto anterior a la orden.

## Cómo se probó
- Pruebas de componente: `changeRules.test.ts`, `ModifyOrderModal.test.tsx`, `ChangeHistoryPanel.test.tsx`, `PendingApprovalsTab.test.tsx`, `ReopenPackDialog.test.tsx`, `wizardRules.test.ts` y las de lo tocado (`SalesOrdersScreen.test.tsx`).
- `npm run check` en `web-app`: verde (exit 0).
- `e2e/lote249-modificar-orden.spec.ts` contra un API de pruebas **sin correo**: pasó en 4 aspectos (1440 px es/claro y en/oscuro; 390 px es/oscuro y en/claro). Las capturas `docs/manual/frontend/img/l249-*.png` se regeneraron y se revisaron una por una: filas de la tabla ≤ 56 px, sin scroll horizontal, sin desbordes.
- No se capturó el botón «Aprobar y aplicar» (hace falta un segundo usuario con `sales.modify.approve`; se ve el aviso «otra persona») ni una orden facturada real (se capturó «salió completa», que usa el mismo aviso). No hay prueba de componente de `ProductsStep`, `PendingApprovalBanner` ni `JoinOrderShipmentDialog` (las cubre el recorrido).
