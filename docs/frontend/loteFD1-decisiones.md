# Lote F-D1 — Choferes y tarifas (2026-10-12)

Pantalla **Catálogo → Choferes y tarifas** (`/catalog/drivers`, `fleet.view`, módulo `CATALOG`). Primer sub-lote de F-D (F-D1 choferes y tarifas; **F-D2** flota y zonas de despacho, **F-D3** mantenimiento y combustible: pendientes). Solo se tocó `web-app/` y `docs/`: el servidor y el contrato no cambiaron (el API ya traía todo; no se regeneró `openapi.json` ni `schema.d.ts`). Manual de pantallas: [fd1-choferes.md](../manual/frontend/fd1-choferes.md); preguntas: sección «Lote F-D1» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`choferes()`, textos `drv.*`). Lote hermano: [loteFC2-decisiones.md](loteFC2-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/`) |
|---|---|---|
| Pantalla | Cabecera, nota de la maqueta, panel de la **fórmula de pago** y maestro-detalle (lista de 260 px + ficha). Lista: contador, buscador (`search` al servidor), filtros de estatus y zona, «Mostrar inactivos», filas con código, nombre, chip y «zona · área». Selección en `?driver=`. | `features/drivers/DriverListScreen.tsx`, `drivers.css` |
| Fórmula de pago | Selector de las 3 fórmulas (editable con `driverpay.manage`) y vista previa «ejemplo con 2 intentos» **calculada por el servidor** para el chofer elegido. | `PayPolicyPanel.tsx` |
| Ficha | Campos editables con un solo «Guardar cambios» (PATCH mínimo + `rowVersion`), usuario vinculado (`admin.users`), y el estado en **tres cosas distintas**: casilla Activo, Disponibilidad (Disponible / No disponible) y «Eliminar chofer». | `DriverProfilePanel.tsx`, `DriverCreateModal.tsx`, `CommentDialog.tsx` |
| Licencias y certificaciones | Alta, edición y baja con chip de vencimiento compartido. | `DriverDocumentsPanel.tsx`, `kernel/ui/ExpiryChip.tsx` |
| Tarifas | Por entrega (filtros Servicio/Paquete), por intento (celdas; «sin tarifa» ≠ $0; «+ Agregar intento») y por viaje; editar con vigencia, «Cerrar tarifa», «Ver historial». | `DriverRatesPanels.tsx`, `rateRules.ts` |
| Viajes pagados | Lista con filtros de fecha y estatus, «Registrar viaje», «Cancelar viaje». | `DriverTripsPanel.tsx` |
| Otros paneles | Dispositivos (sin token), Campos personalizados (`DRIVER`), Contactos (`DRIVER`), Historial de estatus. | `DriverDevicesPanel.tsx`, `DriverDetailPanel.tsx` |
| Reglas y datos | Esquemas zod con los mensajes del manual 04, armado de requests, hooks. | `driverRules.ts`, `rateRules.ts`, `api.ts` |
| Kit | `ExpiryChip` / `expiryChipInfo` (con pruebas), `ContactPointsPanel.readOnly`; `useDrivers` pasó de `features/orders/api.ts` a `features/drivers/api.ts` (Órdenes lo importa, mismo comportamiento). Anotado en `KIT.md`. | `kernel/ui`, `features/contacts`, `features/orders` |
| Ruta y textos | `/catalog/drivers` pasó de `pending(...)` a pantalla (mismo `perm`, módulo y orden 30); `drivers.*` y `ui.expiry.*` en `es.json`/`en.json`. `navigation.test.ts` y `Placeholder.test.tsx` usan `/catalog/fleet` como ruta pendiente. | `app/routes.tsx`, `kernel/i18n` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `npm run check` | tipos generados sin cambios, `tsc -b`, oxlint (solo avisos; los de `features/drivers` son del mismo tipo que los ya existentes), vitest **175 archivos / 1969 pruebas** y build: **pasan** |
| `driverRules.test.ts`, `rateRules.test.ts` (45) | mensajes exactos del manual, PATCH mínimo (`clearZone`/`clearMaxStopsPerRoute`/`clearUser`), estados, usuarios, licencias, vigencias, vista previa, viajes, paridad es/en |
| `DriverListScreen.test.tsx` (26) | lista, filtros al servidor, inactivos, URL, alta (409, 400), ficha, edición, 409 de `rowVersion`, Activo, Disponibilidad, eliminar, solo lectura, usuario, permisos |
| `DriverPanels.test.tsx` (17) | licencias/certificaciones, dispositivos, contactos, historial, campos personalizados |
| `DriverRates.test.tsx` (29) | tarifas, vigencias, cierre, historial, filtros, intentos, viaje, fórmula y vista previa, permisos |
| `DriverTrips.test.tsx` (12) | lista, filtros, registrar, cancelar (409 de entrega especial), permisos |
| `ExpiryChip.test.tsx` (4) | tonos, singular/plural, reemplazada, inglés |
| `e2e/loteFD1-choferes.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal). Capturas `docs/manual/frontend/img/fd1-*.png` |

## Defectos encontrados y arreglados

1. **La ficha saltaba a otro chofer** al desactivar o eliminar el elegido por omisión (la lista deja de traerlo y «el primero» pasaba a ser otro). Ahora la elección por omisión se queda fija (prueba en `DriverListScreen.test.tsx`).
2. **El panel de tarifas parpadeaba** (y perdía los filtros) al activar «Ver historial»: se mostraba un indicador de carga en vez de las tablas. Se mantienen las filas anteriores mientras llega el historial (`keepPreviousData`).
3. **El botón «Guardar cambios» seguía encendido** tras guardar un nombre con espacios (el servidor lo recorta): al guardar, el formulario toma lo que devolvió el servidor.
4. **La casilla «Activo» parecía ignorar el clic** hasta que respondía el servidor: ahora muestra lo pedido de inmediato y vuelve atrás si falla.
5. **La vista previa pedía un cálculo sin chofer** (dos peticiones y un parpadeo del mensaje de error): solo calcula con un chofer elegido y sus tarifas leídas.
6. **El texto «Choferes y tarifas» de `maxStopsHint`** (Ajustes → Operación) no estaba mal: ya apunta a una pantalla real y el tope por chofer se captura aquí (Tope de paradas). No se cambió.
7. **Manual 04**: no tenía nada incorrecto sobre choferes y tarifas (se contrastó con el servidor); solo se agregó la referencia al capítulo de pantalla.

Del servidor no se encontró ningún defecto: se recorrieron por API el alta, PATCH con `clear*`, estatus, licencias reemplazadas, tarifas efectivo-fechadas (versión del mismo día, cierre, historial), vista previa, viajes y baja con todos sus efectos.

## Diferencias con la maqueta (todas con su razón)

1. **Edición con «Guardar cambios»**, no un guardado por campo (la maqueta guardaba al cambiar cada input): el servidor valida y usa `rowVersion`; un guardado por campo daría errores a medias.
2. **«Área» es de solo lectura** (nombre de la zona elegida): el servidor no tiene un área libre.
3. **Zona = select de zonas de despacho** (la maqueta tenía zonas fijas); crearlas es de F-D2.
4. **Estado en tres controles** (Activo, Disponibilidad, Eliminar chofer) en lugar del select «Activo/Inactivo»: son tres cosas distintas en el servidor.
5. **Chip «No disponible» / «Eliminado»** además de Activo/Inactivo en la lista.
6. **Tarifas editables por diálogo** con fecha de vigencia y «Cerrar tarifa» en vez de borrar; la maqueta editaba en la celda y quitaba filas. Historial solo con «Ver historial».
7. **Tarifas por intento en celdas** (una por nivel) en lugar de una tabla horizontal: envuelven en móvil. «Sin tarifa» se ve distinto de $0.
8. **Vista previa del servidor**, con las tarifas vigentes del chofer (la maqueta la calculaba en el navegador con la primera tarifa).
9. **Textos de las fórmulas**: los de la maqueta (más largos que las etiquetas del catálogo).
10. **Paneles que la maqueta no tiene**: Licencias y certificaciones, Viajes pagados, Dispositivos, Contactos, Historial.
11. **Buscador y filtros de estatus/zona** de la lista (la maqueta no los trae; los pidió el encargo).
12. **Usuario vinculado** (solo con `admin.users`).
13. **Fechas** en el formato de la compañía; dinero con la moneda de la compañía.

## Decisiones tomadas por default para presentar al dueño al final de P1

- Lista sin `DataTable` (el API no pagina), con `search` y filtros que van al servidor.
- La ficha se edita con un solo «Guardar cambios»; la fecha de alta de un chofer que ya la tiene no se puede quitar (el servidor lee `null` como «no cambiar»).
- Estado en tres controles; «Eliminar chofer» con efectos explicados y comentario opcional; un chofer eliminado solo se ve con «Mostrar inactivos» y en solo lectura (también contactos).
- Casilla Activo sin confirmación (es reversible); Disponibilidad sin confirmación (reversible); eliminar sí confirma.
- Tarifas: «Vigente desde» por omisión hoy (día de la compañía), nunca anterior; editar o cerrar solo filas abiertas (una fila ya cerrada a futuro no se toca); cerrar usa la fecha de hoy.
- «+ Agregar intento» sube un nivel de **toda la compañía** (tope 20; no se pueden quitar niveles); el botón se apaga en el tope.
- Vista previa con el ejemplo fijo de 2 intentos (1 fallido + 1 entregado), la primera tarifa de entrega vigente del chofer y sus tarifas de intento.
- Viajes: por omisión solo los vigentes; «Cancelar viaje» se ofrece en todo viaje abierto (el de una entrega especial muestra el 409 del servidor, no se esconde el botón).
- Usuarios vinculables: internos, activos y con membresía ACTIVE; el 409 de «ya vinculado» lo da el servidor.
- `ExpiryChip` vive en `kernel/ui` (lo reutiliza F-D2); `CommentDialog` queda en `features/drivers`.
- Dispositivos: solo lista y desactivar; nunca se pide ni se muestra el token.

## Qué no se pudo verificar

- **Dispositivos con datos reales** (los registra la app del chofer; el recorrido solo ve el estado vacío; la lista con datos y «Desactivar» se probaron con respuestas simuladas).
- **Usuario vinculado** contra el API real (el recorrido corre con el administrador, sin otros usuarios internos listos para vincular): probado con respuestas simuladas y por API.
- **Roles reales sin permisos** (Despachador solo con `fleet.view`): cubierto por pruebas de componente, no por Playwright.
- Viaje que nace de una entrega especial (cancelar directo = 409): probado con respuesta simulada; el servidor lo cubre con sus pruebas.
