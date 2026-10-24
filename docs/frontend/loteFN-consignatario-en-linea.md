# Lote 213 (web del personal) — Consignatario nuevo en línea

Dictado de Luis: al crear un consignatario desde la captura de un envío no debe hacer falta el ratón, y menos para volver a la pantalla y seguir
entrando el envío; debe ser «como mágico».

## Qué se construyó
- **Kit:** `ComboSelect` (`src/kernel/ui/ComboSelect.tsx`) gana, de forma aditiva y apagada por omisión, `createLabel` / `onCreate` (opción «crear lo
  escrito» al final de la lista; con cero coincidencias queda resaltada) y `seedText` (reabrir con el texto que había). Anotado en `KIT.md`.
- **`features/consignees/ConsigneeInlineForm.tsx`:** formulario en línea (no es un `<form>`: en la detallada va dentro del formulario de la orden). Nombre*,
  Dirección*, Pueblo* (`PostalLocalityPicker` suelto, `zipClass="DELIVERY"`) y Teléfono (solo con `contacts.manage` + `locations.update`). Valida con el
  mismo `consigneeSchema` y los mensajes `consignees.errors.*`; crea con `useCreateConsignee` + `buildCreateRequest` (DELIVERY, país PR por omisión,
  dueño = cliente de la orden). Enter graba o lleva el foco al primer faltante; Esc cancela; error del servidor en el mismo formulario sin perder lo escrito.
- **`useInlineConsignee.ts`:** estado compartido por las pantallas (formulario abierto, texto de reapertura, a dónde va el foco al terminar).
- **`useConsigneePhoneSync.ts` y `consigneeMessages.ts`:** lógica sacada del `ConsigneeFormModal` para que el modal y el formulario en línea usen
  exactamente lo mismo (guardado del teléfono aparte como ContactPoint; mensajes del esquema). El modal quedó igual de comportamiento.
- **Pantallas:** `OrderQuickEntry` (fila extra `tr.qk-newcons` bajo la activa; foco final en `qk-pkg`; Tab desde el último campo sigue a Paquete),
  `OrderEntryForm` (panel en la sección 02 bajo el buscador; foco final en el siguiente control enfocable, el interruptor de entrega especial).
  `ConsigneePicker` recibe `inline`. `OrderDetailPanel` no cambia (sigue con «+» y modal).
- Estilos `.cs-inline` / `tr.qk-newcons` en `orders.css` (variables del tema, 360 px sin scroll horizontal).
- Textos `consignees.inline.*` y `ui.comboSelect.create` en es/en. Manual: capítulo 03 sección 1.0b; FAQ «Lote 213 (web del personal)».

## Teclado de punta a punta
Escribir nombre → Enter (opción «+ Nuevo consignatario») → foco en Dirección → escribir → Enter (si falta Pueblo, foco ahí) → escribir pueblo → Enter
(elige; foco al teléfono si se ve) → Enter (graba, toast, queda elegido, foco en Paquete) → Enter (guarda la orden).

## Cómo se probó
- Vitest: `ComboSelect.test.tsx` (6 casos de la opción crear), `OrderQuickEntry.test.tsx` (8 casos nuevos: abrir con foco en Dirección, faltantes,
  creación completa + guardar la orden, teléfono aparte, Tab de salida, sin teléfono sin permisos, Escape, error del servidor, sin permiso), `OrderEntryForm.test.tsx` (3 casos).
- Playwright `e2e/lote213-consignatario-en-linea.spec.ts` (rápida y detallada, solo escritorio, contra el API real). **Escrito pero no corrido** por quien lo
  construyó.

## Decisiones a revisar
1. Con coincidencias parciales, Enter elige la primera coincidencia (comportamiento de siempre); la opción de crear se alcanza con ↓. Solo con cero
   coincidencias queda resaltada.
2. Tras elegir el pueblo con Enter el foco salta al teléfono (si se ve) para no caer en la ✕ del selector; sin permiso de teléfono el foco se queda en el pueblo.
3. En el campo Pueblo el primer Esc cierra la lista de pueblos y el segundo cancela el formulario (la lista se abre sola al enfocar).
4. Cambiar de cliente cierra el formulario abierto (el dueño sería otro).
5. En la rápida, Tab desde el último campo pasa a Paquete sin grabar ni cerrar el formulario; si la orden se guarda sin haber grabado el consignatario,
   sale el error habitual de «consignatario obligatorio».
6. No se agregó «número» (código) ni «persona de contacto» al formulario en línea: para eso siguen el «+» y el modal.

## Tercera tanda (2026-10-23): PDF con la marca de la compañía y logo de Teikem
Los PDF de la web (tablas, tablas agrupadas y reportes) usan los colores de la marca de la compañía y las tablas llevan el logo de Teikem cuando no hay logo propio: ver `web-app/KIT.md` (§ «PDF con la marca de la compañía») y `docs/lote213-portal-decisiones.md` (tercera tanda). Probado con el API real en `e2e/lote213-pdf-marca-personal.spec.ts`.
