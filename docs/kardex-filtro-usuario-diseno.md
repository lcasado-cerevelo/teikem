# Kárdex: filtro por usuario (diseño, 2026-10-12)

> **Estado: SOLO DISEÑO. No hay código.** Pedido de Luis (2026-10-12). Decisiones de Luis incorporadas (2026-10-12); ver sección 8.

## 1. Qué se quiere
En el Kárdex de movimientos, poder **saber qué usuario movió o ajustó** algo: un filtro **Usuario**, como la mayoría de los filtros de la pantalla: un desplegable **con búsqueda y selección múltiple**.

## 2. Cómo es hoy (lo que se reutiliza)
- Cada fila del Kárdex **ya trae** el usuario que hizo el movimiento (`UserId`, `UserName` en `KardexRowDto`) y la tabla ya lo muestra. **Lo que falta es poder filtrar por él**: `KardexQuery` no tiene filtro de usuario.
- La misma consulta (`KardexQuery`) alimenta la lista (`GET /inventory/transactions`), su **resumen** (`GET /inventory/transactions/summary`, «con LOS MISMOS filtros de la lista»), la exportación de la tabla y el Reporte de ajustes. Un filtro nuevo debe entrar en todos.
- Hay un **molde exacto** de cómo se agregó un filtro así: el filtro **Dueño** (Lote 14) — campos al final del contrato con valor por defecto, parámetros en los dos endpoints, filtro en `InventoryReadService`, estado y constructor de la consulta en `kardexView.ts` / `movementFilters.ts`, y el control en `InventoryFilterBar`. El filtro **Motivo** se hizo igual. Este filtro **repite ese camino**.
- Ya existe el patrón para las **opciones** de un filtro que salen de los datos: `GET /inventory/owners` (cliente dueño de algún producto), con caché de 1 minuto en la web.

## 3. Diseño
**Qué filtra:** los movimientos hechos por **cualquiera** de los usuarios elegidos (varios se combinan con O; con los demás filtros, Y). Vacío = todos.

**Opción «Sistema (sin usuario)»:** algunos movimientos no los hace una persona (cierres automáticos, procesos del sistema, cargas de datos heredados); tienen `UserId` nulo. Se ofrece como una opción más al principio de la lista, y solo si existen movimientos así.

**Opciones de la lista (nuevo `GET /inventory/transactions/users`, permiso `inventory.view`):** los usuarios de la compañía que **tienen al menos un movimiento** en el Kárdex, con nombre, ordenados por nombre; los usuarios dados de baja que tienen historial salen marcados «(inactivo)» (su historial sigue siendo consultable). No se expone nada nuevo: los nombres ya se ven en cada fila del Kárdex. Caché de 1 minuto, como Dueño.

**API:** parámetros `userIds` (varios) e `includeSystem` (bool) en `GET /inventory/transactions` y en `/transactions/summary`; en `KardexQuery` se agregan **al final con valor por defecto** (como se hizo con Dueño). Sin ninguno de los dos no se filtra por usuario.

**Web:**
- Control `SearchSelect` (el de Motivo, Marca, Dueño: buscador y casillas), etiqueta «Usuario», en la barra de filtros del Kárdex (`InventoryFilterBar`, pestañas de saldos y movimientos donde aplique) y anotado en el ámbito de filtros (aparece en «Filtros aplicados» de lo que se exporte).
- Estado: lista de textos (ids y la marca fija «SYSTEM» para «Sistema», igual que «Propio» en Dueño); constructor de la consulta y limpieza con el botón «Limpiar» como los demás.
- **Transferencias y ajustes** usa la misma consulta (`movementFilters.ts`) y es donde más sirve para saber «quién ajustó»: **el filtro se incluye también ahí** (decidido), con el mismo control.
- La columna **Usuario** de la tabla se queda como está.

**Rendimiento:** la lista de opciones sale de un `DISTINCT` de usuarios sobre el libro de movimientos y el filtro es una condición más sobre esa tabla; hay que **verificar que exista un índice** que ayude (compañía + usuario) y, si no existe, agregarlo en una sección **nueva e idempotente** al final de `logistica-db-update.sql` (el mismo sitio donde master agregó sus índices de rendimiento, así que cada sección va separada por su fecha).

## 4. Cómo se divide para llevarlo a master
Todo es aditivo y sigue el molde de Dueño. Estado de master (consultado en solo lectura): **no tocó** `InventoryContracts.cs`, `InventoryController.cs`, `InventoryReadService.cs`, `InventoryFilterBar.tsx`, `kardexView.ts`, `movementFilters.ts` ni `TransfersAdjustmentsScreen.tsx`. Los puntos de contacto reales son solo dos: la prueba de firma `WmsContractsTests.cs` (hay que agregar los campos nuevos a la línea de `KardexQuery`) y `openapi.json` / `schema.d.ts` (ahí **se regenera** con el comando del proyecto en lugar de resolver choques a mano).

Commits, con la etiqueta `[kardex-usuario]` al inicio del mensaje:
1. `[kardex-usuario] API: filtro por usuario y lista de usuarios con movimientos` — `KardexQuery`, controlador (lista y resumen), servicio, endpoint de opciones, índice si hace falta, pruebas, línea de `WmsContractsTests`, y `openapi.json` + `schema.d.ts` regenerados.
2. `[kardex-usuario] Web: filtro Usuario en Kárdex y en Transferencias y ajustes` — estado, constructor de la consulta, control, textos es/en y pruebas.
3. `[kardex-usuario] Manual y decisiones` — manual y este documento pasa a «decisiones».

Se hace en `Depot-Implementation`, se prueba en staging y se pasa a `master` con `cherry-pick` (o con la fusión), como los otros diseños.

## 5. Pruebas
- **Servicio:** un usuario, varios usuarios, solo «Sistema», usuarios + «Sistema», combinación con otros filtros (fechas, motivo, almacén), que el **resumen** respete el mismo filtro, usuario inexistente o de otra compañía (no devuelve datos ajenos), y la lista de opciones (solo usuarios con movimientos, inactivos marcados, «Sistema» solo si hay).
- **Contrato:** la firma de `KardexQuery` en `WmsContractsTests`.
- **Web:** el control arma `userIds`/`includeSystem` correctamente, «Limpiar» lo vacía, el texto del ámbito de filtros, y que Transferencias y ajustes envíe lo mismo.

## 6. Seguridad
Mismo permiso del Kárdex (`inventory.view`); el filtro no muestra nada que la tabla no muestre ya; y la consulta sigue acotada a la compañía activa.

## 7. Relación con el ajuste múltiple
Un ajuste múltiple (`AJM-######`) deja a cada movimiento con el usuario que lo aplicó: este filtro permite ver, por ejemplo, todo lo que ajustó una persona en la semana. No depende de ese diseño.

## 8. Decisiones
**Tomadas (Luis, 2026-10-12):**
1. El filtro **también va en Transferencias y ajustes**, además del Kárdex.
2. La lista **muestra a los usuarios inactivos que tienen historial** (marcados «(inactivo)»).
3. Se ofrece la opción **«Sistema (sin usuario)»**, solo si existen movimientos sin usuario.

**Abiertas:** ninguna por ahora.
