# Lote F-D2 — Flota: vehículos, documentos y disponibilidad (2026-10-12)

Pantalla **Catálogo → Flota y mantenimiento** (`/catalog/fleet`, `fleet.view`, módulo `CATALOG`). Segundo sub-lote de F-D (F-D1 choferes y tarifas, hecho; **F-D3** mantenimiento y combustible: pendiente y se agregará como pestañas y tarjetas de esta misma pantalla, sin placeholders). Manual de pantallas: [fd2-flota.md](../manual/frontend/fd2-flota.md); preguntas: sección «Lote F-D2» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`flota()`, textos `flota.*`). Lote hermano: [loteFD1-decisiones.md](loteFD1-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pantalla | Cabecera con «Nuevo vehículo», nota de la maqueta, tarjetas «Vehículos activos» y «Documentos por vencer (≤ 30 d)» (llevan a su pestaña) y `Tabs` en `?tab=`. | `web-app/src/features/fleet/FleetScreen.tsx`, `fleet.css` |
| Vehículos | Lista maestro-detalle con `SplitPane`: buscador, filtros Tipo / Propiedad / Combustible / Estatus al servidor, «Mostrar inactivos», selección en `?vehicle=`. Alta en modal. | `VehiclesTab.tsx`, `VehicleCreateModal.tsx`, `VehicleFields.tsx` |
| Ficha | Un solo «Guardar cambios» (PATCH mínimo + `rowVersion`), casilla Activo, Disponibilidad (Disponible / En mantenimiento), «Dar de baja definitiva», documentos, campos personalizados, contactos e historial `VehicleStatus`. | `VehicleProfilePanel.tsx`, `VehicleDocumentsPanel.tsx`, `VehicleDetailPanel.tsx` |
| Documentos por vencer | Filtros multi Documento y Entidad, «Dentro de (días)», «Incluir vencidos», buscador, Limpiar; columnas ordenables con chip de días; clic abre al vehículo o al chofer. | `ExpiringDocumentsTab.tsx` |
| Zonas de despacho | Lista (código, nombre, choferes, estado), alta, edición del nombre, inactivar con el 409 visible y reactivar, buscador. | `DispatchZonesTab.tsx` |
| Disponibilidad | Fecha, «Solo disponibles», buscador; tablas Choferes y Vehículos con chip y motivos en texto claro (Bloquea / Aviso). | `AvailabilityTab.tsx` |
| Reglas y datos | Esquemas zod con los mensajes del manual 04, armado de requests, hooks. | `fleetRules.ts`, `api.ts` |
| Kit | `IconTruck`; `Form` quita el aviso superior del servidor al editar un campo. Anotado en `KIT.md` («Flota»). | `kernel/ui` |
| Servidor | Un vehículo dado de baja definitiva ya no se edita (409), ni sus documentos. | `VehicleService.cs`, `VehicleDocumentService.cs` |
| Ruta y textos | `/catalog/fleet` pasó de `pending(...)` a pantalla (mismo `perm`, módulo y orden 40); `fleet.*` en `es.json`/`en.json`. `navigation.test.ts` afirma que ya no es pendiente; `Placeholder.test.tsx` usa `/ops/dispatch`. | `app/routes.tsx` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `fleetRules.test.ts` | mensajes exactos del manual (código, placa, VIN, capacidades, año, precisión, documentos, zonas), PATCH mínimo, estados, filtros, chip de días, motivos de disponibilidad, paridad es/en |
| `FleetVehicles.test.tsx`, `FleetDocuments.test.tsx`, `FleetZonesAvailability.test.tsx` | pantalla, lista y filtros, alta, ficha (PATCH, 409 de `rowVersion`, Activo, Disponibilidad, baja definitiva, solo lectura), documentos, vencimientos, zonas, disponibilidad y permisos con/sin `fleet.manage` |
| `Form.test.tsx`, `icons.test.tsx` | aviso superior que se quita al editar; `IconTruck` |
| `VehicleRetiredReadOnlyTests.cs` (xunit, 4) | 409 al editar un vehículo dado de baja y sus documentos; el vigente sigue editable; 404 de otro tenant |
| `e2e/loteFD2-flota.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal). Capturas `docs/manual/frontend/img/fd2-*.png` |

Los conteos de la corrida final van en el reporte de entrega.

## Defectos encontrados y arreglados

1. **Servidor: un vehículo dado de baja definitiva seguía editable** por la API (PATCH y alta, edición y baja de documentos), a diferencia del chofer eliminado. Ahora responde 409 «El vehículo está dado de baja definitiva; solo se consulta su historial.» (prueba xunit y recorrido). Se anotó en el capítulo 04 y en el FAQ.
2. **El aviso de error de arriba de un formulario no se quitaba nunca** (p. ej. «Ya existe un vehículo activo con ese VIN.» seguía a la vista después de corregir el campo). `Form` ahora lo quita cuando el usuario edita un campo; recargar la ficha tras un 409 no lo quita (prueba en `Form.test.tsx` y `FleetVehicles.test.tsx`).
3. **Año del modelo con separador de miles** en el mensaje («entre 1,900 y 2,027»): los años se pasan al texto como cadena (prueba en `fleetRules.test.ts`).
4. **Documentos por vencer, Zonas y Disponibilidad no tenían buscador** (la base de pruebas y los clientes reales acumulan decenas de choferes y zonas): se agregó `QBox` a esas tres pestañas.

El resto del servidor (alta, PATCH con `rowVersion`, estatus, documentos reemplazados, documentos por vencer, disponibilidad y zonas) se recorrió y no mostró más defectos.

## Diferencias con la maqueta (todas con su razón)

1. **Pestañas** (Vehículos, Documentos por vencer, Zonas de despacho, Disponibilidad) en lugar de todos los paneles apilados: F-D3 suma más y la página se haría interminable.
2. **Dos tarjetas resumen**, no cuatro: las de mantenimiento vencido y órdenes abiertas son de F-D3. Las tarjetas son botones que llevan a su pestaña.
3. **Subtítulo** sin «mantenimiento preventivo, órdenes de trabajo y combustible»: aún no existen.
4. **Vehículos en maestro-detalle** (lista a la izquierda, ficha a la derecha) en lugar de una tabla con lápiz y papelera: la ficha lleva documentos, contactos, campos personalizados e historial.
5. **Sin «Almacén base»** (el servidor no lo guarda) ni columna «Peso máx» en la lista (no viene en el DTO de lista; va en la ficha).
6. **Tipo, Propiedad y Combustible** son selects de catálogo, no texto libre; se agregan Marca, Modelo, Año y Tope de paradas.
7. **La papelera** pasó a «Dar de baja definitiva» (confirmar, comentario opcional): el servidor no borra. La casilla Activo y la Disponibilidad son controles aparte.
8. **Documentos por vencer**: además de Documento y Entidad, «Dentro de (días)», «Incluir vencidos» y buscador; chip de días como la maqueta (rojo vencido, hasta 14, hasta 30, verde). Un clic abre al dueño.
9. **Disponibilidad y Zonas** no están en la maqueta (son de este lote por el contrato).
10. **Edición con «Guardar cambios»**, no por campo (igual que F-D1).

## Decisiones tomadas por default para presentar al dueño al final de P1

- Pantalla única con pestañas y tarjetas resumen solo de este lote; F-D3 agrega las suyas.
- Vehículos: lista con `SplitPane` y filtros al servidor; el chip de la fila es Disponible / En mantenimiento / Inactivo / Baja definitiva (no el nombre del estatus del catálogo, que llama «Activo» a ACTIVE y se confundiría con la casilla).
- Un vehículo dado de baja es de solo lectura en pantalla **y ahora también en el servidor** (409). Decisión de servidor a confirmar.
- Un número ya guardado (año, capacidades, tope de paradas, odómetro) no se puede dejar vacío: el PATCH lee `null` como «no cambiar». Los textos sí se pueden borrar.
- El odómetro puede corregirse hacia abajo, pero no por debajo de la última lectura registrada (regla del servidor; el mensaje sale bajo el campo).
- Casilla Activo y Disponibilidad sin confirmación (reversibles); «Dar de baja definitiva» y «Quitar documento» sí confirman; inactivar zona confirma.
- Documentos por vencer: ventana 30 días, vencidos incluidos; tope 0–365 con aviso propio («Escriba un número entero entre 0 y 365.»); mientras el valor es inválido se queda la última ventana válida.
- Disponibilidad: fecha de hoy de la compañía; los motivos se traducen leyendo el mensaje del servidor (que va en español) y, si no calza, se muestra el mensaje tal cual; los que bloquean salen primero y con la etiqueta «Bloquea», los avisos con «Aviso». Un clic en la fila abre la ficha del chofer o del vehículo.
- Zonas: solo código, nombre y activo; el nombre es opcional; los miembros (municipios y códigos postales) quedan para Despacho. Contactos y campos personalizados de zona no se exponen aquí.
- El chip de vencimiento compartido dice «Reemplazada» / «Vencida» (femenino, de las licencias) también en documentos de vehículo.
- Sin subida de archivos ni DELETE de vehículos.

## Qué no se pudo verificar

- **Roles reales sin permisos** (Despachador solo con `fleet.view`): cubierto con pruebas de componente, no con Playwright.
- **Odómetro menor que la última lectura** contra el API real (no hay cargas de combustible ni órdenes de trabajo hasta F-D3): probado con respuesta simulada; el servidor lo cubre con sus pruebas.
- **Motivo «Orden de trabajo en proceso»** con datos reales (las órdenes de trabajo son de F-D3): probado con respuesta simulada.
- **Campos personalizados y contactos de vehículo** con datos reales: se prueban con respuestas simuladas.
