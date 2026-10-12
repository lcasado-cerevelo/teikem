# Lote F-F2 — Vistas e informes (2026-10-12)

Segundo y último sub-lote de F-F (F-F1 campos personalizados, **F-F2** este). Pantalla `/analytics/reports` (Análisis → Vistas e informes, `analytics.view`, módulo ANALYTICS; escribir `analytics.manage`). Manual: [ff2-vistas.md](../manual/frontend/ff2-vistas.md); preguntas: sección «Lote F-F2» de [faq.md](../manual/faq.md); API: [capítulo 01 §8](../manual/01-plataforma-y-seguridad.md). Referencia visual: `Diseño/teikem-mockups.html` (`views()`, `viewDatasetPanelHtml`, `viewResultsScreenHtml`, `renderViewBuilderHtml`).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pantalla | Sin `?view=` galería; con `?view=<id>` resultado | `features/analytics/ReportsScreen.tsx` |
| Galería | Un panel por fuente de `/data-sources` (acordeón abierto, 2 por fila), contador, «+» Nueva vista, filas con subtítulo, insignia Por defecto / Creada por X, Editar y Eliminar solo si `canEdit` y no es de sistema, vacío con el texto de la maqueta, buscador | `ReportGallery.tsx`, `ViewBadge.tsx` |
| Resultado | Título, fuente + combinadas, resumen de filtros/agrupación, Editar/Eliminar/Volver, rango de fechas (solo con campo de fecha), búsqueda libre, tabla ordenable con Exportar en el cliente, totales, aviso de truncado | `ReportResultScreen.tsx`, `ReportResultTable.tsx` |
| Constructor | Nombre, fuente principal fija, combinar, columnas (propias, `cf.`, `REL.`), filtros (16 operadores), agrupar por varios con agregados y totales, orden, visibilidad y compartir, vista previa real | `ViewBuilderModal.tsx`, `FilterRowEditor.tsx` (extraída de `DefinitionEditor`) |
| Reglas | columnas/groupJson/sortJson/filterJson ⇄ borrador, validaciones, galería, rotulado del resultado, rango | `viewRules.ts` |
| Hooks | lista, correr, vista previa, guardar, eliminar | `reportsApi.ts` |
| Menú | `/analytics/reports` pasó de `pending(...)` a pantalla (misma ruta, permiso, módulo y orden) | `app/routes.tsx` |
| Servidor | Tres defectos corregidos (abajo) | `AnalyticsService.cs` |

`openapi.json` y `schema.d.ts` no cambiaron (contrato igual). Rentas conserva su pantalla y sus hooks (`useRentalReports`, `useReportRun`).

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `viewRules.test.ts` (29) | campos y bloques por fuente, groupJson/sortJson (lectura y escritura), conteo de condiciones, operador en minúsculas, borrador ⇄ cuerpo (reenvío de programación/correos/tipo de gráfico), filtro con or/not conservado, vista previa, quitar fuente combinada o agrupación, función del agregado, validaciones, nombre repetido, compartir, galería, subtítulo, rotulado del resultado, rango, búsqueda |
| `ReportsScreen.test.tsx` (28) | galería (paneles, insignias, vacío, contraer, permisos con y sin `analytics.manage`, buscador, abrir, eliminar y 403 del dueño); resultado (rango Todo por omisión, cambio de rango, personalizado incompleto, sin fecha, búsqueda, truncado, error del servidor, solo lectura de sistema, vista inexistente, eliminar); constructor (validaciones, sin relaciones, crear con combinada + cf + filtro + orden, agrupar, 409 exacto, 400 por campo, compartir con y sin `admin.users`, editar con conservación y nombre repetido, 403 de sistema) |
| `definitions.test.ts`, `DefinitionEditor.test.tsx`, `IndicatorsPage`, `ChartsPage`, `navigation.test.ts`, `AppShell.test.tsx`, Rentas | siguen verdes (la fila de filtro pasó a `FilterRowEditor`; AppShell usa el Placeholder del ítem `reports` para probar la paleta) |
| `e2e/loteFF2-vistas.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio **1 de 1** y móvil (360 px, sin scroll horizontal en cada paso) **1 de 1**. Siembra por API 3 clientes, 6 órdenes y un campo personalizado; crea vista simple, agrupada con agregados y totales, y combinada con campo personalizado; ejecuta, busca, ordena, rango (7 días y personalizado vacío), exporta CSV, edita, 409, elimina y comprueba la vista de sistema (sin botones y 403 exacto por API). Capturas `ff2-*.png` (13) |
| `AnalyticsReportNameTests` (xunit, 3) | nombre de eliminada reutilizable al crear y al renombrar, 409 al renombrar a un nombre repetido, `chartType` guardado y rechazo del desconocido. Ejecutadas con `--artifacts-path` aparte (sin tocar el API en marcha) y comprobadas contra un segundo API (:5002, ya detenido) sobre `Teikem_pw` |

## Defectos encontrados y arreglados

1. **Servidor: crear una vista con el nombre de una eliminada daba 500** (`UQ_ReportDefinition` cuenta también las dadas de baja y la baja es lógica). Ahora la eliminada queda como `nombre (eliminada #id)` y el nombre se libera (también las eliminadas de antes: se renombran al chocar). Prueba xunit.
2. **Servidor: renombrar una vista a un nombre ya usado daba 500**; ahora 409 `Ya existe una vista '<name>' para <source>.`, igual que al crear. Prueba xunit; la pantalla además lo avisa antes de enviar.
3. **Servidor: `chartType` no se guardaba** (siempre null); ahora se guarda y uno desconocido da 400 `Tipo de gráfico: BAR, DONUT o LINE.` Prueba xunit.
4. **Pantalla (editor compartido): la fila de filtro mostraba el botón ✕ en una segunda fila vacía** (`.r3` tiene tres columnas y la fila lleva cuatro controles); ahora `fr-row` de cuatro columnas (también en Indicadores y Gráficos), con las etiquetas enlazadas a sus controles.
5. **Pantalla: un operador en minúsculas del servidor (`notnull` de las vistas de sistema) dejaba la fila sin operador en el editor**; `normalizeFilterOp` lo lee con la forma del editor. Prueba.
6. **Pantalla: el PUT de una vista borraría la programación y los correos guardados por API** (el servidor reemplaza ambos); el constructor los reenvía tal cual. Prueba.
7. **Pantalla: el conteo de un campo de dinero se mostraba como dinero y los agregados se rotulaban «Suma (CodAmount)»**; ahora «Suma · Monto COD» y un conteo nunca es dinero. Prueba.

**Deuda del servidor que NO se tocó:** `rows` de `/run` trae todas las columnas de la fuente además de las pedidas (carga útil mayor de la necesaria); el motor marca `isMoney` en falso para los campos de una fuente combinada y rotula los campos personalizados como texto con su clave (la pantalla lo corrige al pintar); un campo personalizado de una fuente combinada (`REL.cf.x`) no existe en el motor.

## Diferencias con la maqueta (todas con su razón)

1. **Fuentes: las que devuelve `/data-sources` (hoy 26), no las 8 de la maqueta**; ordenadas por nombre. No hay Facturación, Liquidación ni Compras: el API no las tiene.
2. **Título «Vistas e informes»** (el de la maqueta, «Vistas», es el del menú anterior) y sin el selector «Viendo como» (solo existe para probar la maqueta).
3. **Modal de 880 px (`lg` del kit)** en lugar de 780.
4. **Orden**: el constructor trae sección de orden (campo y dirección, varios); la maqueta no.
5. **Filtros con los 16 operadores del motor** (la maqueta tiene 5, con «neq»), unidos con Y; **valor como texto** en todos los tipos.
6. **Resultado**: rango de fechas, Exportar (Excel, CSV, PDF), aviso de truncado, totales en `SummaryBar` bajo la tabla (la maqueta pinta una fila de totales dentro de la tabla) y tabla con el `DataTable` del kit (orden, páginas de 25, tarjetas bajo 720 px).
7. **Editar desde el resultado y desde la galería** (la maqueta solo trae eliminar en la fila).
8. **Compartir**: personas (con `admin.users`) y roles, y «pueden editar» para las personas; la maqueta no comparte.
9. **Buscador de la galería** y vista previa de 10 filas (la maqueta, 6).
10. **El «+» y los íconos llevan nombre accesible** («Nueva vista de Clientes»).

## Decisiones tomadas por default para presentar al dueño al final de P1

- **Galería con todos los paneles abiertos** aunque haya 26 fuentes (la página es larga; el buscador y cerrar un panel lo compensan). Alternativa: abrir solo los paneles con informes.
- **La exportación es en el cliente con lo cargado, hasta 5000 filas** (el servidor no tiene endpoint de exportación): si la vista tiene más, la pantalla avisa y se exporta lo cargado. Con la búsqueda aplicada se exporta lo buscado.
- **El rango de fechas no se guarda en la vista** y siempre arranca en «Todo el tiempo»; no se ofrece programación ni correos (el servidor los guarda sin proceso que los use: queda para P2).
- **Con agrupación, las columnas marcadas se conservan en pantalla pero se guardan vacías** (`columns: []`), como las vistas de sistema agrupadas; al quitar la agrupación se pierden las ya guardadas.
- **«Pueden editar» es un solo interruptor para todas las personas elegidas** (el API permite uno por persona); los roles no editan (el servidor solo mira a las personas).
- **Un agregado distinto de Cantidad solo ofrece campos numéricos**; Cantidad puede contar «Todas las filas» o un campo.
- **Las vistas de otra persona (incluidas las compartidas con edición) se editan desde la pantalla** si el API devuelve `canEdit`; las de sistema nunca.
- **Insignia «Creada por X» solo cuando el dueño no es quien mira**; las de la compañía sin dueño no llevan insignia.
- **El nombre de una vista eliminada se libera renombrándola** («nombre (eliminada #id)») en lugar de cambiar la restricción SQL (el SQL está congelado y esto no exige cambio de estructura).
- **Vista previa de 10 filas** con retardo de 400 ms entre cambios y siempre con rango «Todo».
- **Sin columna por defecto al combinar**: marcar una fuente combinada no agrega columnas; el usuario las elige.

## Qué no se pudo verificar

- Compartir con edición entre dos usuarios reales en el navegador (se probó el cuerpo enviado en vitest y el 403 por API; no hay un segundo usuario sembrado en la base de pruebas).
- Fuentes con muchos miles de filas (el motor evalúa en memoria sobre un tope de 20 000 por fuente; se probó con decenas).
- El paso «volver a crear una vista con el nombre de una eliminada» en el navegador contra el API en marcha (:5000, anterior a la corrección): el recorrido lo ejecuta solo con `FF2_SERVER_FIXED=1`; la corrección se comprobó con xunit y contra un segundo API.
- `dotnet test` completo: solo se corrieron las pruebas nuevas.
