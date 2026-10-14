# Lote F-B5 — Direcciones, Perfil fiscal, labels automáticos por cliente y paneles con resumen (2026-10-14)

Pedido de Luis. Servidor: [lote48-decisiones.md](../lote48-decisiones.md). Manual de pantallas: [fb5-direcciones-y-perfil-fiscal.md](../manual/frontend/fb5-direcciones-y-perfil-fiscal.md); FAQ «Lote F-B5».

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/…`) |
|---|---|---|
| Panel «Direcciones» | Va después de la cabecera y antes del Perfil fiscal: dirección física (una, CORPORATE), postal (una, BILLING; «igual a la física» mientras no tenga la suya, y entonces admite P.O. Box), puntos de recogida (PICKUP, los que quiera) y «Se recoge en» (la física, la postal o cualquier punto; se guarda al escoger con `PATCH /profile`). Alta y edición en una ventana con el buscador de pueblo/código postal del directorio postal (física y puntos: solo códigos de entrega; postal: todos). Quitar = baja lógica. | `features/clients/ClientAddressesPanel.tsx`, `addressRules.ts`, hooks `useSaveClientAddress` / `useDeactivateClientAddress` en `clients/api.ts` |
| Perfil fiscal | «Perfil del cliente» se renombra; ya no lleva direcciones ni punto de recogida; la razón social explica que es el nombre legal; **Imprimir los labels automáticamente** va antes de **Imprimir la factura automáticamente**. | `ClientProfilePanel.tsx`, `clientRules.ts` |
| Labels automáticos por cliente | Al guardar una orden de un cliente con la bandera, salen sus labels aunque «Label al guardar» esté apagado (se lee la ficha del cliente, en caché 1 min; si no se puede leer, no imprime y no rompe el guardado). | `orders/OrderListScreen.tsx` (`printAfterSave`), `clients/api.ts` (`clientAutoPrintsLabels`) |
| Cabecera y resúmenes | La cabecera del cliente también es desplegable (`Estatus: Activo`, o `· Inactivo`); resúmenes más ricos: dirección y recogida, teléfono y correo principales (+N), personas (principal + 3 más), ejemplos de numeración (orden · factura · paquete), campos personalizados con valor (+N), contrato vigente. | `ClientDetailPanel.tsx`, `contacts/ContactPointsPanel.tsx`, `ClientPeoplePanel.tsx`, `ClientNumberingPanel.tsx`, `ClientCustomFieldsPanel.tsx`, `ClientContractsSection.tsx` |
| Textos | `clients.addresses.*`, `clients.summary.*` nuevos, `clients.profile.*` nuevo y `autoPrintLabels*` en es y en. | `kernel/i18n/{es,en}.json` |

## Cómo se probó

| Prueba | Resultado |
|---|---|
| vitest `features/clients`, `orders`, `contacts` (`addressRules.test.ts` nuevo con 10, `ClientListScreen.test.tsx` con 4 pruebas nuevas o reescritas: direcciones, «Sin dirección registrada», perfil fiscal y paneles cerrados) | 396 en verde |
| `tsc -b` | sin errores |
| Playwright `e2e/loteFB5-direcciones-cliente.spec.ts` (escritorio y móvil 360 px, API propio y base desechable) y regresión `loteFB4` | 4 en verde: agrega la física, la postal (P.O. Box) y un punto de recogida, «Se recoge en» en la postal y en el punto, quitar el punto vuelve a la física, labels automáticos antes que la factura y guardados contra el API, todos los paneles cerrados con su resumen, sin scroll horizontal |

## Decisiones tomadas por omisión (a revisar con Luis)

1. **«Tipo de cliente» de la cabecera = el estatus** (Luis lo confirmó con dudas): `Estatus: Activo` / `Estatus: Suspendido`. El «desvío» es el propio estatus cuando está en un desvío; no hay un campo de tipo de cliente.
2. **La postal es opcional**: sin dirección postal propia es la física. «Usar la física» da de baja la postal propia.
3. **El nombre de la física y de la postal es interno** («Dirección física — {cliente}»): el usuario no lo escribe; el punto de recogida sí lleva nombre.
4. **«Se recoge en» se guarda al escogerlo** (no hay botón Guardar en esa parte) y usa el mismo `PATCH /profile` con `rowVersion`.
5. **Permisos**: ver con `clients.read`; agregar con `locations.create`; editar y quitar con `locations.update`; «Se recoge en» con `clients.update`.
6. **Los labels automáticos del cliente no pisan la configuración de la estación**: si no hay impresora de labels, la orden se guarda y se avisa. El interruptor sigue las reglas de impresión directa de la compañía (F-G).

## Fuera de este lote

- Escribir la dirección física al **crear** el cliente (el modal de alta no la pide todavía).
- Varias direcciones físicas o postales por cliente (el servidor impone una de cada una).
- Imprimir la factura (sigue siendo solo la bandera).
