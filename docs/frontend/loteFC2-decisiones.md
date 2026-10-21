# Lote F-C2 — Órdenes: expediente de la orden (2026-10-12)

Pantalla **Operación → Órdenes → (abrir una orden)** (`/orders/:publicId`). Segundo sub-lote de F-C (F-C1 lista y alta; **F-C3** entrada rápida e importación CSV, pendiente). Solo se tocó `web-app/` y `docs/`: el servidor y el contrato no cambiaron (se revisó `canReprice`, ver «Defectos»). Manual de pantallas: [fc2-ordenes-expediente.md](../manual/frontend/fc2-ordenes-expediente.md); preguntas: sección «Lote F-C2» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`record()`, `recordDetailPanelHtml`). Lote hermano: [loteFC1-decisiones.md](loteFC1-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/`) |
|---|---|---|
| Expediente | Cabecera + `StatusPipeline` a ancho completo; columna principal (Detalle, Historial real) y columna de 320 px (COD, Cotización, Chofer asignado, Campos personalizados, Contactos); bajo 720 px la derecha cae debajo. Sin pestañas. | `features/orders/OrderDetailScreen.tsx`, `orders.css` |
| Detalle | Lectura o edición según `capabilities.canEditCargo` + `orders.edit`; un solo «Guardar cambios» (PATCH con lo cambiado + `rowVersion`); 409 con aviso, recarga y lo escrito conservado; factura repetida confirmable; paradas de solo lectura. | `OrderDetailPanel.tsx` |
| Acciones | Confirmar (con crédito excedido → «Autorizar y confirmar»), Re-cotizar, Cancelar el envío (comentario ≤ 500), Eliminar el envío, Asignar/Reasignar chofer. Cada una con permiso Y capacidad. | `OrderDetailScreen.tsx`, `OrderActionDialogs.tsx`, `CreditOverrideDialog.tsx` |
| Paneles | COD (solo lectura), Cotización (solo `orders.view_price`; «Cotizar» = `GET /quote`), Chofer asignado, Campos personalizados (`TRANSPORT_ORDER`), Contactos. | `OrderSidePanels.tsx` |
| Reglas | `orderActionVisibility`, `isAssignableOrder`, `orderEditValues`/`orderEditSchema`/`orderPatchChanges`/`buildOrderPatch`, `isConcurrencyConflict`. | `orderRules.ts` |
| Hooks | `useUpdateOrder`, `useOrderQuote`, `useConfirmOrder`, `useRepriceOrder`, `useCancelOrder`, `useOrderStatusChange`, `useAssignOrderDriver`, `useDrivers`; los POST llevan `Idempotency-Key` propia por intento; cada mutación invalida ficha, listas, búsqueda, cotización e historial. | `features/orders/api.ts` |
| Kit | `StatusPipeline.lateralOnly` + `lateralMoves`/`lastPipelineStage` (solo desvíos y regreso exacto) con pruebas; `ContactPointsPanel` compartido (clientes y órdenes) con `features/contacts/api.ts`. Anotado en `KIT.md`. | `kernel/catalogs`, `features/contacts` |
| Textos | `orders.detail.*` (rehecho), `orders.actions.*`, `status.returnTo`. | `kernel/i18n/{es,en}.json` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `npm run check` | ver el reporte de cierre (tipos, tsc, oxlint, vitest, build) |
| `orderExpediente.test.ts` (27) | visibilidad por acción (permiso Y capacidad), derivación de «asignable», valores/esquema/PATCH solo con lo cambiado, `clearCod`, paquetes como reemplazo, 409 de edición, paridad es/en |
| `OrderDetailScreen.test.tsx` (53) | cada acción y sus errores del servidor, crédito con y sin override, Facturación (solo override), re-cotizar, cancelar (500), eliminar, laterales y regreso, edición y 409, factura repetida, asignar chofer (409/422/404, crédito, sin `fleet.view`), Cotización con y sin `orders.view_price` (sin llamar a `/quote`), COD, contactos, campos personalizados, capacidades |
| `StatusPipeline.test.tsx`, `transitions.test.ts` | modo `lateralOnly`: solo desvíos y el regreso exacto |
| `e2e/loteFC2-expediente.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal) verdes. Capturas `docs/manual/frontend/img/fc2-*.png` |

## Defectos encontrados y arreglados

1. **Asignar chofer en IN_TRANSIT.** La derivación inicial del botón (pedida: «aún no IN_TRANSIT») ocultaba **Reasignar chofer** justo cuando más se usa. El servidor (`SpecialDeliveryDispatchService`) solo rechaza *después* de IN_TRANSIT. Se corrigió a «hasta IN_TRANSIT inclusive» (con prueba) y el texto del manual 04 §11 («IN_TRANSIT o más adelante») que decía otra cosa.
2. **Botón «Guardar cambios» apagado sin avisar** al vaciar una fecha capturada (el servidor lee `null` como «no cambiar»): se habilita con cualquier cambio del formulario y la validación dice «La fecha no se puede quitar; elija otra.».
3. **Textos del panel de contactos** nombraban al cliente («Este cliente no tiene teléfonos…»): el panel compartido recibe las claves de texto del dueño.
4. **Estilos del panel de contactos** no cargaban fuera de Clientes (`clients.css`): el componente compartido los importa.
5. `canReprice` **no** era defecto: se calcula con el monto real antes de ocultar el precio (`OrderPriceRules.Apply` no toca `Capabilities`; ya lo prueba `OrderViewPriceTests`). No se tocó el servidor.

## Diferencias con la maqueta (todas con su razón)

Decididas por el dueño:

1. **Sin «Label» ni «POD»** (no hay endpoint): no hay botones falsos. Se difiere a F-G. *Actualización F-G2 (2026-10-12): la ficha ya tiene
   «Imprimir label» (un label por pieza); POD sigue pendiente (P7).*
2. **Línea de tiempo = Historial real** (`StatusHistory`), no los eventos ilustrativos.
3. **Consignatario por selector** (directorio del cliente + compartidos), no texto libre; pueblo y ZIP salen de la dirección elegida (en la maqueta eran campos editables).
4. **Textos de ayuda**: «Esta orden está en {estatus}: puede corregir sus datos y sus paquetes.» y «Esta orden ya avanzó de {estatus inicial}: sus datos se muestran como referencia, ya no se editan aquí.» La maqueta decía «Entrada»; aquí el nombre sale del catálogo de estatus (hoy «Borrador», como se ve en la lista). Tratamiento de «usted» como el resto de la web.
5. **Edición por `capabilities.canEditCargo`** y no por «estar en Entrada» literal.

Del implementador:

6. Además de la maqueta se pintan **Prioridad**, **Lista**, **Límite**, **Recogido**, N.º de envío/factura/empaque, referencias y las **Paradas** (solo lectura), porque el pedido incluía «paradas» y fechas en el Detalle.
7. **Acciones del servidor** que la maqueta no tiene: Confirmar, Re-cotizar, Cancelar, Eliminar, Asignar chofer, laterales, Cotizar; los botones «Asignar» de la maqueta (`r.assign`) sí está, pero ahora es real y solo para entregas especiales.
8. **Barra de estatus** del kit (etapas, desvíos y cierres) en lugar de nada en la maqueta, con una nota de que avanzar/confirmar/cancelar son botones propios.
9. **Cotización** como panel propio (la maqueta no mostraba precio en el expediente).
10. **Fechas** como día sin zona (`YYYY-MM-DDT00:00:00`), igual que el alta.

## Decisiones tomadas por default para presentar al dueño al final de P1

- Edición dentro del Detalle con un solo «Guardar cambios», habilitado con cualquier cambio; fechas capturadas no se pueden vaciar.
- «Confirmar» también lo ve quien solo tiene `orders.credit_override`, y para él autoriza directamente (`overrideCredit=true`) — así Facturación (sin `orders.edit`) puede autorizar el exceso desde el expediente.
- Asignar chofer: visible hasta **IN_TRANSIT inclusive**, con `trips.dispatch` + módulo CATALOG; lista de choferes activos (no filtra por disponibilidad: el servidor responde 409 con los motivos).
- COD: solo lectura; el tipo se muestra solo si existe y siempre con la nota «se registra al entregar». Si no hay COD solo se ve «Monto —».
- Contactos: panel compartido con Clientes; texto «Etiqueta» como rótulo; sin lectura de contactos el panel se oculta.
- «Viaje» del panel Chofer solo si existe código de ruta (una entrega especial no tiene `assignedTripCode`).
- Regreso desde un lateral: solo a la etapa exacta (el kit antes ofrecía también la siguiente).
- Cada POST lleva `Idempotency-Key` nueva por intento (el reintento con `overrideCredit` es otra operación).
- Textos de los diálogos de cancelar usan «Volver» como botón secundario para no confundirlo con «Cancelar el envío».

## Qué no se pudo verificar

- **Sin `orders.view_price`, con roles reales en el navegador**: cubierto por pruebas de componente (no se pide `/quote` ni se pinta el panel), no por Playwright (el recorrido corre con el administrador).
- Crédito excedido al **asignar chofer** y `fleet.view` ausente: solo con respuestas simuladas.
- Aspecto en móvil de los diálogos de asignar/cancelar: se comprobó que no hay scroll horizontal; las capturas del manual son de escritorio.
