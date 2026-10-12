# Lote F-D3 — Mantenimiento y combustible (2026-10-12)

Tercer y último sub-lote de F-D (F-D1 choferes y tarifas, F-D2 flota y zonas, **F-D3** este). Se agrega como **pestañas y tarjetas de la pantalla Catálogo → Flota y mantenimiento** (`/catalog/fleet`, `fleet.view` lee, `fleet.maintenance` escribe, módulo `CATALOG`). Manual de pantallas: [fd3-mantenimiento-combustible.md](../manual/frontend/fd3-mantenimiento-combustible.md); preguntas: sección «Lote F-D3» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`flota()`, textos `flota.*`). Lote hermano: [loteFD2-decisiones.md](loteFD2-decisiones.md). Siguen F-E, F-F, F-A3 y F-G.

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/fleet/`) |
|---|---|---|
| Pantalla | Cuatro tarjetas (las dos nuevas: «Mantenimiento preventivo (vencidos)» y «Órdenes de trabajo (abiertas)», cada una lleva a su pestaña) y siete pestañas: Vehículos, **Mantenimiento**, **Órdenes de trabajo**, **Combustible**, Documentos por vencer, Zonas, Disponibilidad. | `FleetScreen.tsx`, `fleet.css` |
| Mantenimiento | Panel de vencimiento (Vehículo con placa y código, Programa, Base, Próximo con lo que falta, Estado) desde `/due`, filtros Estado y Vehículo al servidor, buscador, y el renglón crea la orden del programa. Debajo, **programas**: alta y edición en modal (por vehículo O por tipo), desactivar con confirmación y reactivar. | `MaintenanceTab.tsx`, `ScheduleModal.tsx` |
| Órdenes de trabajo | Lista con las columnas de la maqueta + fecha, filtros Estado (varios), Tipo, Vehículo y Fechas, buscador, «Nueva orden de trabajo». Ficha arriba de la lista (`?workOrder=`): encabezado editable solo si `canEdit`, tareas (alta, edición, completar, quitar) con totales, historial, Iniciar / Cerrar orden / Cancelar orden. | `WorkOrdersTab.tsx`, `WorkOrderCreateModal.tsx`, `WorkOrderDetailPanel.tsx`, `WorkOrderDialogs.tsx` |
| Combustible | `DataTable` paginado por el servidor con total y `exportRows`; filtros Vehículo, Chofer y Fechas; resumen por vehículo (`summary` del API); registrar, editar (sin cambiar el vehículo) y quitar. | `FuelTab.tsx`, `FuelLogModal.tsx` |
| Reglas y datos | Esquemas zod con los mensajes del servidor, armado de requests mínimos, hooks. | `maintenanceRules.ts`, `maintenanceApi.ts`, `vehicleChoices.ts`, `maintenanceShared.tsx` |
| Servidor | `WorkOrderListItemDto` trae `laborCost` y `partsCost`; una orden sin vehículo es 400 y no 404. Contrato regenerado (`openapi.json`, dos `schema.d.ts`). | `MaintenanceContracts.cs`, `MaintenanceWorkOrderService.cs` |
| Textos y kit | `fleet.maintenance.*`, `fleet.workOrders.*`, `fleet.fuel.*` en `es.json`/`en.json`; sección nueva en `KIT.md` («Flota»). No se agregó ningún componente al kit. | `kernel/i18n`, `KIT.md` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `maintenanceRules.test.ts` (60) | mensajes exactos del manual 04 §5–§7, PATCH mínimo (`clear*`), estados de vencimiento, acciones por estatus, cierre, tareas, combustible (fecha y hora de la compañía, tolerancia de 5 min, «hasta» exclusivo), paridad es/en |
| `FleetMaintenance.test.tsx` (15), `FleetWorkOrders.test.tsx` (31), `FleetFuel.test.tsx` (16) | cada pestaña y acción con errores del servidor y con/sin `fleet.maintenance`; lectura completa de las órdenes (de a 200) y orden sobre todo lo filtrado; paginación del servidor y `exportRows` de combustible; transiciones y tareas; odómetro rechazado |
| `WorkOrderListCostsTests.cs` (xunit, 2) | la lista trae labor y partes; sin vehículo es 400 en el campo |
| `dotnet test Teikem.sln` | 3565 pruebas, todas pasan |
| `npx vitest run` | 183 archivos / 2183 pruebas, todas pasan (el worker de vitest cae a veces con 3221225477 en este equipo: se repite) |
| `e2e/loteFD3-mantenimiento-combustible.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal). Capturas `docs/manual/frontend/img/fd3-*.png` |

## Defectos encontrados y arreglados

1. **Servidor: la lista de órdenes no traía labor ni partes** (solo el total), pero la maqueta pinta las tres columnas; llenar la tabla pedía la ficha de cada orden. Ahora `WorkOrderListItemDto` los trae (prueba xunit; contrato regenerado).
2. **Servidor: crear una orden sin vehículo respondía 404 «Vehículo no encontrado.»** Ahora 400 «El vehículo de la orden de trabajo es obligatorio.» en el campo (prueba xunit; igual que combustible).
3. **Pantalla: una casilla «Completada» parecía ignorar el clic** hasta que respondía el servidor. Ahora muestra lo pedido de inmediato y vuelve atrás si falla (lo detectó el recorrido de Playwright).
4. **Pantalla: el tipo, el programa y el vehículo de partida de «Nueva orden» podían quedar sin elegir** cuando sus opciones llegaban después de abrir el modal. Los formularios con selects de red no pintan sus campos hasta tener las opciones (prueba en `FleetMaintenance.test.tsx`).
5. **Manual 04**: decía «máximo 1 decimal» y el servidor escribe «1 decimales»; faltaba el 400 de la orden sin vehículo y que la lista devuelve un arreglo sin total. Corregido.
6. **Combustible: al quitar la última carga de la última página la tabla quedaba vacía.** Ahora vuelve a la página anterior.

Del servidor, lo demás se recorrió por API y por pantalla (alta de programas por vehículo y por tipo, vencimientos, órdenes con efecto en el vehículo, cierre con odómetro, 422 de tareas sin completar, EDIT_WORK_ORDER, combustible con odómetro monótono y cálculos) y no mostró más defectos.

## Diferencias con la maqueta (todas con su razón)

1. **Pestañas** (Mantenimiento, Órdenes de trabajo, Combustible) en lugar de paneles apilados, y cuatro tarjetas: las dos nuevas con «(vencidos)» y «(abiertas)» para decir qué cuentan.
2. **Mantenimiento**: columna **Programa** (la maqueta tenía un programa por vehículo; el servidor permite varios) y debajo de «Próximo» lo que falta. Estado con el estatus real del servidor: **Sin historial** además de Al día / Por vencer / Vencido; el umbral de «Por vencer» es el del servidor (10 % del intervalo), no 500 km / 14 días.
3. **Base** muestra Kilometraje, Tiempo o **Ambos**.
4. **Gestión de programas** (alta, edición, desactivar, reactivar): la maqueta solo los mostraba.
5. **Órdenes**: número `OT-#####` (la maqueta `WO-501`), **cuatro estatus reales** (Abierta, En proceso, Cerrada, Cancelada) en lugar de Abierta/Cerrada, columna **Fecha** y ficha con tareas (la maqueta tenía un texto libre «tareas»).
6. **Combustible**: columnas **Chofer** y **Estación** además de las de la maqueta; **hora** de la carga; ids `FL-###` no existen (la carga se identifica por fecha y vehículo); resumen por vehículo.
7. **Km/L con dos decimales** (la maqueta, uno): con uno, 8.00 y 8.04 se ven igual.
8. **Todas las acciones son reales** (la maqueta no editaba ni quitaba cargas ni órdenes).

## Decisiones tomadas por default para presentar al dueño al final de P1

- «Abiertas» de la tarjeta = Abierta **y** En proceso (las que no son terminales).
- «Sin historial» (como el manual 04 y el servidor) y no «Sin base».
- Las órdenes se leen **completas** de a 200 (el API no devuelve total ni ordena) y la tabla ordena y pagina en pantalla; así «ordenar» vale para todo lo filtrado. Con miles de órdenes conviene filtrar por fechas; si el dueño prefiere, se agrega `total` al API.
- La ficha de la orden va arriba de la lista (no en un modal) para no apilar diálogos (Cerrar y Cancelar abren los suyos).
- Iniciar con `ConfirmDialog` (explica el efecto en el vehículo); Cancelar con `CommentDialog` (confirmación con comentario opcional); Cerrar con su propia ventana (fecha de cierre por omisión hoy, odómetro precargado con el de la orden, comentario).
- Cerrar no pre-valida las tareas sin completar: muestra el 422 exacto del servidor dentro de la ventana.
- Costos: con tareas, Labor y Partes del encabezado quedan bloqueados; al agregar la primera tarea el servidor reemplaza los costos escritos a mano (se explica en el manual y la FAQ).
- Un valor guardado (fecha programada, odómetro, costos, último servicio) no se puede vaciar: el PATCH lee `null` como «no cambiar».
- Programas: siempre se ven los dos intervalos y el último servicio; solo se piden los que exige el disparador (los demás pueden quedar vacíos o quitarse con `clear*`). Cambiar de «un vehículo» a «un tipo» manda solo el tipo (el servidor limpia el último servicio).
- Combustible: **sin buscador libre** (el API no tiene `search`; un buscador sobre la página engañaría); filtros Vehículo, Chofer y Fechas al servidor. Fecha y hora son dos campos (hora de la compañía; por omisión, ahora). El **costo total es obligatorio** en pantalla (el servidor lo toma como 0 si falta; un olvido no debe guardarse como gratis). Cargas quitadas no se ven (no hay «Mostrar inactivas»).
- «Hasta» de las fechas incluye el día completo (se manda la medianoche local del día siguiente, exclusiva).
- Campos personalizados y contactos de `MAINTENANCE_SCHEDULE`, `WORK_ORDER` y `FUEL_LOG` existen en el servidor pero **no se exponen** en esta pantalla (no los pide la maqueta); se agregan si el dueño los pide.
- Sin subida de archivos ni DELETE: todo es baja lógica.

## Qué no se pudo verificar

- **Roles reales sin permisos** (solo `fleet.view`): cubierto con pruebas de componente, no con Playwright.
- **Un tenant con la capacidad `EDIT_WORK_ORDER` relajada en Cerrada/Cancelada** (la ficha se vería editable porque `canEdit` lo diría): probado solo con respuestas simuladas.
- **Miles de órdenes**: la lectura completa de a 200 se probó con 250 simuladas, no con un volumen real.
- **Subida del odómetro por una orden cerrada con un odómetro menor al del vehículo**: el servidor lo acepta sin subirlo (no es monótono como combustible); no se cambió.
