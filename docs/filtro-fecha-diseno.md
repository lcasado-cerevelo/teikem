# Filtro de fecha compacto con rangos predefinidos (diseño, 2026-10-12)

> **Estado: CONSTRUIDO (2026-10-12) en `Depot-Implementation`**, con las decisiones de Luis de la sección 9. Pedido de Luis (2026-10-12). Commits `[filtro-fecha]`: cálculo de rangos, control compacto y juegos «futuro»/«ambos». Resumen de lo construido y de lo que cambió respecto al diseño en la sección 10.

## 1. Qué se quiere
Hoy los filtros de fecha de las listas del almacén son **dos campos de fecha grandes** (Desde / Hasta) que ocupan el doble de un filtro normal. Se quiere:
1. Que ocupen **el tamaño que corresponde** a una fecha (no el doble de ancho).
2. Un **desplegable** con rangos predefinidos (Todas las fechas, Hoy, Ayer, Últimos 7 días, Esta semana, Mes pasado, Este mes… y **Personalizado**) en lugar de los dos campos.
3. Solo al escoger **Personalizado** aparecen los dos campos de fecha, también compactos.
4. Que aplique a **todas** las pantallas con filtro de fecha, como **un solo control** compuesto.

## 2. Cómo es hoy (lo que se reutiliza)
- Ya existe **un solo componente compartido**: `DateRangeFilter` ([web-app/src/kernel/ui/Filters.tsx](../web-app/src/kernel/ui/Filters.tsx)). Recibe `label`, `value: { from, to }` (textos `YYYY-MM-DD` o vacío = abierto) y `onChange`, y ocupa `.span2` (el doble de ancho; todo el renglón a 480 px). `DateRange` y `inDateRange` viven en `kernel/ui/dateRange.ts`.
- **Once pantallas** lo usan, y por eso **todas mejoran con cambiar solo ese componente**:

| Pantalla | Etiqueta del filtro hoy | Tipo de fechas |
|---|---|---|
| Compras (órdenes de compra) | rango | pasado |
| Recibos (`ReceiptFilterBar`) | creado | pasado |
| Recolección y empaque (`PickBatchesPanel`) | recolectado | pasado |
| Transferencias y ajustes | rango | pasado |
| Conteo cíclico (`CycleCountFilterBar`) | creado | pasado |
| Devoluciones de rentas | devuelto el | pasado |
| Kárdex y saldos (`InventoryFilterBar`) | rango | pasado |
| Órdenes | rango | pasado |
| Actividad del sistema (auditoría) | fecha | pasado |
| ASN / recibos esperados (`AsnsTab`) | esperado | **futuro** |
| Citas de muelle (`DockAppointmentsTab`) | rango | **pasado y futuro** |

- El sistema **ya conoce** lo que hace falta para calcular «hoy», «esta semana», etc. bien: la **zona horaria de la compañía** (`kernel/api/tenantZone.ts`, `Tenant.TimeZoneId`; sin ajuste, Puerto Rico), `todayIso()` en esa zona (`kernel/format`) y el **primer día de la semana** configurado (`weekStartDay` en los ajustes de formato, también usado por Ajustes → Calendario).
- Cada control de filtro anota su valor en el «ámbito de filtros» (`useRegisterFilter` → `dateRangeFilterText`), que alimenta los avisos en pantalla y el bloque «Filtros aplicados» de los reportes y exportaciones.
- Ya hay un precedente de filtros que crecen hacia abajo (el filtro de productos muestra «chips» debajo): un filtro de altura variable es aceptable en esta barra.

## 3. El control nuevo
**Se cambia por dentro `DateRangeFilter`, con las mismas propiedades** (`label`, `value`, `onChange`; el valor sigue siendo `{ from, to }`). Ninguna pantalla cambia su lógica ni su consulta al API, y no hay cambios en el backend. Se agrega una propiedad opcional `mode` (`'past'` por omisión, `'future'` o `'both'`) para escoger el juego de rangos.

**Forma (una sola celda de tamaño estándar, 140 px):**
```
Fecha                 ← la etiqueta de cada pantalla (Fecha, Creado, Recolectado, Esperado…)
[ Este mes        ▾ ] ← desplegable (tamaño de un filtro normal)
```
Al escoger **Personalizado**, bajo el desplegable, dentro de la misma celda, aparecen dos campos compactos (no se ensancha la barra):
```
Fecha
[ Personalizado   ▾ ]
 Desde [ 01/10/2026 ]
 Hasta [ 15/10/2026 ]
```
- Cada campo mide lo que una fecha necesita (≈ 118–140 px, tipografía del filtro), con su rótulo corto «Desde» / «Hasta» visible.
- `Desde` no puede pasar de `Hasta` y viceversa (como hoy: `max`/`min`).
- A 480 px o menos la celda ocupa el renglón y los dos campos quedan uno junto al otro, a mitad de ancho cada uno.

## 4. Rangos predefinidos
Todos se calculan con **«hoy» en la zona horaria de la compañía** y con el **primer día de semana configurado** de la compañía. Los de calendario (semana, mes, año) cubren el **período completo** (inicio a fin); «Últimos N días» termina hoy e incluye hoy.

**Juego «pasado»** (movimientos, documentos creados; el que usan casi todas):

| Opción | Desde → Hasta |
|---|---|
| **Todas las fechas** (por omisión) | vacío → vacío |
| Hoy | hoy → hoy |
| Ayer | ayer → ayer |
| Últimos 7 días | hoy−6 → hoy |
| Esta semana | primer día de la semana → último día de la semana |
| Semana pasada | la semana anterior completa |
| Últimos 30 días | hoy−29 → hoy |
| Este mes | día 1 → último día del mes |
| Mes pasado | el mes anterior completo |
| Este año | 1 de enero → 31 de diciembre |
| **Personalizado** | los dos campos, escritos a mano |

**Juego «futuro»** (fechas esperadas: ASN): Todas las fechas, Hoy, Mañana, Próximos 7 días (hoy → hoy+6), Esta semana, Próxima semana, Este mes, Próximo mes, Personalizado.

**Juego «ambos»** (citas de muelle: se mira el pasado reciente y lo que viene): Todas las fechas, Ayer, Hoy, Mañana, Últimos 7 días, Próximos 7 días, Esta semana, Semana pasada, Próxima semana, Este mes, Personalizado.

**«Esta semana» es la semana completa** (del primer día de la semana configurado al último), no «hasta hoy»: si hoy es lunes y la semana termina el sábado, «Esta semana» llega hasta ese sábado. «Últimos 7 días», en cambio, **termina hoy**.

## 5. Comportamiento
- **Escoger un rango** llama `onChange` con las fechas concretas de ese rango. El valor que ven las pantallas sigue siendo `{ from, to }`; **no se guarda «el nombre» del rango**, solo las fechas.
- **Qué muestra el desplegable:** se **deduce de las fechas** que tiene el valor: vacías → «Todas las fechas»; iguales a las de una opción (con la fecha de hoy) → esa opción; si no coinciden con ninguna → «Personalizado». Así funciona igual cuando la pantalla llega con fechas ya puestas (por ejemplo desde un enlace). Si una pantalla queda abierta de un día para otro, la etiqueta puede pasar de «Hoy» a «Ayer»: siempre describe las fechas reales.
- **Personalizado** sin fechas escritas todavía no filtra (queda igual que «Todas»); el control recuerda que se escogió Personalizado para seguir mostrando los campos.
- **«Limpiar»** de la barra vacía el valor y el desplegable vuelve a «Todas las fechas».
- **Escaneo y ámbito de filtros:** en pantalla el aviso dice el nombre y las fechas («Este mes · del 01/10/2026 al 31/10/2026»); en **reportes y exportaciones** («Filtros aplicados») siguen saliendo **las fechas concretas**, nunca solo «Este mes» (un reporte guardado debe decir de qué días es).
- **Accesibilidad:** el desplegable es un `<select>` nativo con su etiqueta; los dos campos tienen su rótulo y se pueden usar con teclado. La opción activa se anuncia con el texto de la etiqueta.
- **Formato de fecha de los campos:** igual que hoy (campo de fecha del navegador). Escribirlas con el formato de la compañía **se deja para después** (ver 9).

## 6. Textos
Claves nuevas es/en en el bloque de filtros del kit (`ui.filters.range.*`): `all`, `today`, `yesterday`, `last7`, `thisWeek`, `lastWeek`, `last30`, `thisMonth`, `lastMonth`, `thisYear`, `custom`, y las del juego futuro `tomorrow`, `next7`, `nextWeek`, `nextMonth`. Se conservan `ui.filters.from` / `ui.filters.to`.

## 7. Cómo se divide para llevarlo a master (evitar choques)
Todas las pantallas consumen el mismo componente, así que **no hace falta tocar ninguna pantalla** para el cambio principal. Archivos nuevos en lo posible, y lo compartido con cambios mínimos y al final. Estado de master (consultado en solo lectura): **no tocó** `Filters.tsx`, `dateRange.ts` ni `filterRegistry.ts`; sí agregó líneas en `ui.css` (+43), `kernel/ui/index.ts` (+16) y los textos es/en (muchísimas líneas nuevas), todos cambios de «agregar», por lo que se esperan pocos o ningún choque.

Commits (cada uno con la etiqueta `[filtro-fecha]` al inicio del mensaje):
1. `[filtro-fecha] Cálculo de rangos predefinidos` — archivo **nuevo** `kernel/ui/datePresets.ts` (funciones puras: zona horaria, primer día de semana, bordes de mes y año, años bisiestos) y sus pruebas. Choques: ninguno.
2. `[filtro-fecha] Control DateRangeFilter compacto con rangos predefinidos` — componente nuevo `kernel/ui/DateRangeFilter.tsx` + su estilo en archivo propio + su prueba; en `Filters.tsx` se reemplaza el cuerpo viejo por un `export` del nuevo (cambio de pocas líneas, en un archivo que master no tocó); claves es/en agregadas **juntas, en un solo bloque**. Es el único commit con puntos de contacto compartidos.
3. `[filtro-fecha] Juegos «futuro» (ASN) y «ambos» (citas de muelle)` — una línea en `AsnsTab` (`mode="future"`) y otra en `DockAppointmentsTab` (`mode="both"`). Se puede **omitir** al pasar a master si esas pantallas cambiaron allá.
4. `[filtro-fecha] Manual y decisiones` — texto en el manual de uso de filtros y este documento pasa a «decisiones».

Se hace en `Depot-Implementation`, se prueba en staging y se pasa a `master` con `cherry-pick` (o con la fusión de Depot hacia master), igual que los otros dos diseños.

## 8. Pruebas
- **Cálculo de rangos** (puras): hoy/ayer/últimos N, semana con primer día lunes y domingo, semana pasada y próxima, mes y mes pasado en enero y diciembre, año, bisiestos, y que «hoy» respete la zona de la compañía (un instante cerca de la medianoche UTC no cambia el día de Puerto Rico).
- **Control:** escoger un rango llama `onChange` con las fechas esperadas; el desplegable se deduce de las fechas (incluido «Personalizado»); Personalizado muestra los dos campos y respeta mínimo/máximo; «Todas las fechas» vacía el valor; Limpiar; el juego futuro; el texto del ámbito de filtros con nombre y fechas.
- **Pantallas:** las pruebas existentes casi no dependen de los dos campos viejos (solo `FilterScope.test.tsx` usa `DateRangeFilter` directamente); se ajustan las que cambien.
- Revisión visual a 1280, 768 y 375 px de al menos Compras, Recibos, Recolección, Transferencias, Conteo cíclico, Devoluciones y Kárdex.

## 9. Decisiones
**Tomadas (Luis, 2026-10-12):**
1. La lista de rangos del juego «pasado» está bien tal cual.
2. **Citas de muelle:** juego «ambos» (incluye el pasado reciente y lo que viene).
3. **«Esta semana» = la semana completa**; «Últimos 7 días» termina hoy.
4. **Campos de fecha con el formato de la compañía: se deja para después.** Es bastante más trabajo que este cambio: hay que construir un campo de texto con máscara, validación y calendario propios (el campo nativo del navegador no permite escoger el formato), y conviene hacerlo una sola vez para todos los formularios (`DateInput`). Se diseña aparte, cuando se decida. Mientras tanto los campos usan el formato del navegador, como hoy.
5. **No** se recuerda el último rango usado entre visitas.

**Abiertas:** ninguna por ahora.

## 10. Lo construido y cambios respecto al diseño
- **Archivos:** `kernel/ui/datePresets.ts` (+ pruebas), `kernel/ui/DateRangeFilter.tsx` y `dateRangeFilter.css` (+ pruebas); `Filters.tsx` solo lo re-exporta; textos `ui.filters.range.*` en es/en; `mode="future"` en `AsnsTab` y `mode="both"` en `DockAppointmentsTab`. El backend no cambió. Las once pantallas lo usan sin cambiar su lógica.
- **Cambio de diseño al construir:** en «Personalizado» los dos campos **no** van apilados debajo del desplegable sino **en la misma línea** (la celda se ensancha a ≈ 400 px con los rótulos Desde / Hasta arriba, como «Fecha»): así el desplegable queda alineado con los demás filtros de la barra (la barra alinea por abajo y una celda más alta lo desalineaba). A 480 px o menos, el desplegable arriba a todo el ancho y los dos campos debajo, uno junto al otro.
- **Texto en reportes:** la línea de filtros de las exportaciones lleva solo las **fechas concretas** («del 01/10/2026 al 31/10/2026»), nunca el nombre del rango (decidido al construir: así un reporte dice de qué días es y no depende de cuándo se genera).
- **Pruebas:** 13 del cálculo de rangos y 10 del control (incluida la zona horaria de la compañía y el primer día de semana domingo/lunes); se ajustó la prueba de `ReceiptScreen` que escribía en el campo viejo. Revisión visual hecha en escritorio y en celular (375 px).
