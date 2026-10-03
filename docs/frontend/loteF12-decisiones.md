# Lote F12 — Conteo cíclico por producto en la web: revisión rápida y corrección del supervisor (2026-10-03)

Parte 5b (web) del diseño aprobado en [conteo-por-producto-diseno.md](../conteo-por-producto-diseno.md), sección "Web — revisión
rápida y corrección del supervisor", sobre el contrato del servidor del [Lote 21](../lote21-decisiones.md). Número de lote: en
`docs/frontend/` había F1, F6, F7A, F8, F8a, F9 y F10 (F11 se documentó en el Lote 19); se tomó el siguiente libre, **F12**. Solo se
tocó `web-app/` y `docs/`: nada de `src/`, `app-almacen/` ni la maqueta. Manual de pantallas:
[f12-conteo-por-producto.md](../manual/frontend/f12-conteo-por-producto.md); FAQ: sección "Lote F12" de
[faq.md](../manual/faq.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pestañas | `Conteo cíclico` gana **Conteos** / **Por revisar** (`?tab=review`, solo con `warehouse.count`; sin él no se pinta y `?tab=review` abre la lista de siempre) | `CycleCountListScreen.tsx` |
| Por revisar | `GET /cycle-counts/review` paginado en el servidor: Conteo, Contó ("y N más"), Producto ("y N más"), Posiciones, Líneas, Con diferencia, Correcciones y el estado calculado (Cuadra, Con diferencia, Con errores, Faltan líneas); filtros Almacén, Contó y `QBox` (→ `search`); orden por columna (página local); Exportar todo lo filtrado; clic = abrir a la derecha (`?count=`) | `CountReviewTab.tsx` |
| Cerrar los que cuadran | casillas solo en los que cuadran; sin elegir = todos los que cuadran de la página. Confirmación con cuántos y cuáles + comentario (≤ 500) → `POST /cycle-counts/reconcile-matching { ids, comment }`; modal de resultado con cerrados y omitidos con su motivo en español (`WouldPost`, `Errors`, `Pending`, `Stale`, `NotCounted`, `AlreadyReconciled`, `NotFound`, `NoLines`, `Failed`; desconocido → el texto del servidor) y enlace a cada omitido; refresca la lista | `CountReviewTab.tsx`, `countReview.ts` |
| Detalle para revisar | con `warehouse.count` y el conteo abierto: vista previa en segundo plano; columnas **Ajuste** (contra la existencia actual, con el error de la línea) y **Evidencia** (*Contó X (quién, cuándo) · Corrección · Corregido a Y (quién, cuándo)*); un conteo **Contado** abre con **solo las líneas que fallan** e interruptor "Ver todas"; la fila corregida en la sesión no desaparece; "N de M líneas" | `CountDetailPanel.tsx` |
| Corrección | la misma captura en la fila de siempre (`PUT /cycle-counts/{id}/lines`); en un Contado el servidor la registra como corrección; la interfaz la nombra "Corrección" y avisa que no es un ajuste | `CountDetailPanel.tsx`, `useCountDrafts.ts` (sin cambios) |
| Vista previa | "Confirmar conteo y ajustar" abre `ReconcilePreviewModal` (`GET /reconcile-preview`): totales (`SummaryBar`), avisos (faltan líneas, Concordancia, Diferencia con N movimientos, saldo movido, `blockingError`), tabla por posición (existencia actual, reservado, contada, ajuste ±, saldo resultante, error); Confirmar deshabilitado con el motivo si algo bloquea; confirmar = `reconcile` con el `rowVersion` de la vista previa | `ReconcilePreviewModal.tsx` |
| Posición provisional | chip "Posición pendiente de revisión" en el detalle y la vista previa; "Confirmar posición" (`warehouse.manage`, `POST /warehouses/{id}/bins/{binId}/confirm-provisional`); en la ficha del almacén → Posiciones: chip, filtro "Solo pendientes de revisión" (`isProvisional=true`) y acción de fila; chip en Ubicaciones | `CountDetailPanel.tsx`, `WarehouseDetailScreen.tsx`, `LocationsScreen.tsx`, `warehouseFilters.ts` |
| Hooks | `useCycleCountReview`, `exportCycleCountReview`, `useReconcilePreview`, `useReconcileMatching`, `useConfirmProvisionalBin`; toda escritura del conteo invalida también "Por revisar" y la vista previa | `api.ts` |
| Lógica pura | estado y tono, producto, consulta, opciones de "Contó", qué cierra el cierre en bloque, motivos, líneas que fallan, evidencia, bloqueos de la vista previa, signo y color del ajuste, pestaña | `countReview.ts` |
| Textos | `warehouse.cycleCounts.{tabs,review,evidence,provisional,preview}`, `…detail.*` nuevos y `warehouse.bins.*provisional*` en es/en; se quitó `…detail.confirmBody` (ya no se usa) | `kernel/i18n/{es,en}.json` |
| Estilos | `.cc-review*`, `.cc-failbar`, `.cc-bincell`, `.cc-adjcell`, `.cc-evidence*`, `.cc-row-error`, `.cc-preview-*`, `.cc-result` | `warehouse.css` |
| Contrato del kit | sección "Conteo cíclico" ampliada (piezas, props, hooks y lógica pura) | `web-app/KIT.md` |

No hubo que regenerar el cliente: `web-app/openapi.json` ya traía el contrato del Lote 21 (`npm run api:types` no cambió
`schema.d.ts`).

## Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `cd web-app && npm run check` (api:types, tsc, oxlint, vitest, build) | **Pasó**: 113 archivos de prueba, **1131 pruebas** pasan; oxlint 0 errores y 9 avisos, todos previos (ninguno de este lote); build de Vite correcto |
| Vitest nuevas | `countReview.test.ts` (14: estado, producto, consulta, "Contó", pestaña, qué se cierra, selección, motivos de omisión, resumen, líneas que fallan, evidencia, bloqueos, signo/color) y `CycleCountReview.test.tsx` (12: lista con columnas y estados, filtros/orden/clic, cierre sin elegir con confirmación y resumen, cierre con casillas y comentario largo, solo las que fallan + Ver todas + evidencia, corrección en la fila con recálculo, confirmar posición, vista previa con error de reservado y totales, con `blockingError` y línea sin contar, confirmar con el rowVersion de la vista previa, Concordancia, a ciegas) |
| Vitest ajustadas | `CycleCountScreen.test.tsx` (confirmar pasa por la vista previa; la varianza y el ajuste se ven tras guardar), `warehouseFilters.test.ts` (`isProvisional`), `warehouseAdmin.test.tsx` (chip, filtro y "Confirmar posición" en la ficha) |
| `scripts/dev-sqlserver.sh` | no hizo falta: SQL Server 2022 ya corría en el entorno |
| `dotnet build src/Teikem.Api -c Release` | 0 errores |
| `dotnet run … -- db-reset --yes` + `db-init` (Development) | "Base de datos recreada en blanco e inicializada." / "Inicialización de BD completada." (dos veces: la segunda para partir de una base limpia tras varias corridas) |
| API (`-c Release`, Development, `Auth__Onboarding__Enabled=false`, :5000) + Vite (`VITE_API_URL=http://localhost:5000`, :5173) | arriba (`/health` 200) |
| `npx playwright test e2e/loteF12-conteo.spec.ts` (Chromium de `/opt/pw-browsers/chromium`) | **4 pasaron** (escritorio 3 + móvil 1) |
| `npx playwright test` (suite completa) | 1.ª corrida: 1 falló (`lote14` 6) por el `role="status"` permanente del contador de líneas de este lote → corregido (`aria-live`). Siguientes corridas en paralelo: `lote14` 6 (limpieza de "lo cambiado" con la página llena, causada por las posiciones que siembra F12, y el clic en el Kárdex antes de terminar de cargar) y `lote15` 6 (ver abajo) → corregidos/diagnosticados. **Corrida final en serie (`--workers=1`) sobre la base: 76 pasaron, 0 fallaron, 69 omitidos (6,8 min)**, incluidos `escritorio-f12` y `movil-f12` |

Recorrido `e2e/loteF12-conteo.spec.ts` (siembra por API, posiciones propias `F12-…` en la zona RSV de ALM-01): un producto con
existencia en tres posiciones y un conteo por producto donde el operario cuenta 9 (hay 8), 3 (hay 5) y 6 (hay 6) y halla 2 en una
posición provisional creada desde el conteo; otro conteo que cuadra. Escritorio: "Por revisar" con su estado, detalle con 3 de 4
líneas que fallan y la evidencia, corrección 3 → 5 en la fila ("Corregido a 5", chip "Corrección"), vista previa (2 movimientos,
+2 en la provisional), cierre en bloque del que cuadra (Concordancia), "Confirmar posición" y reconciliación del otro (Diferencia, 2
ajustes); el API confirma estatus, evidencia (`capturedQty` 3, `countedQty` 5) y que la posición ya no es provisional. Móvil (360
px): pestaña en tarjetas, detalle y vista previa sin desplazamiento horizontal de página. Capturas `f12-*` en
`docs/manual/frontend/img/` (las regeneradas de otros lotes se descartaron).

`playwright.config.ts`: F12 corre en proyectos propios (`escritorio-f12`, `movil-f12`) **después** de `escritorio` y `movil`, porque
sus movimientos entrarían en el "Conteo de lo cambiado" del Lote 14 (un conteo por posición cambiada); `escritorio-f9` espera también
a F12 (cambia región y formatos). `lote14.spec.ts`: la limpieza espera a que desaparezca el conteo borrado (con la página llena el
número de filas no cambia) y el clic en el Kárdex espera a que la red quede quieta.

## Decisiones para el dueño

1. **"Cerrar los que cuadran" cierra lo que se ve**: los que cuadran de la página visible (o los marcados), mandados por `ids`, para
   que el aviso diga exactamente cuántos y cuáles. El servidor permite cerrar "todos los del almacén" (hasta 200) sin ids; no se
   usa porque cerraría conteos que no se ven y no respetaría el filtro "Contó". Cambiarlo es usar `warehousePublicId` sin `ids`.
2. **Corregir en la web exige `warehouse.count`** (la web cuenta en modo informado), aunque el servidor acepta la corrección con
   `warehouse.count.capture` (decisión 4 del Lote 21). Quien solo captura no ve "Por revisar".
3. **Solo Contados en "Por revisar"** (la opción `includeOpen` del servidor no se ofrece), como la decisión 2 del Lote 21.
4. **"Solo las que fallan" por defecto solo en conteos Contados**; un Pendiente (contado en la web) se abre con todas las líneas.
   "Falla" = ajuste ≠ 0 o error **contra la existencia actual** (la vista previa); mientras la vista previa no llega se usa la
   varianza contra la foto.
5. **Confirmar usa el `rowVersion` de la vista previa**: si el conteo cambió después de verla, el servidor responde 409 y la vista
   previa se recalcula; así no se confirma algo distinto de lo que se vio.
6. **Estado del chip**: Con errores > Faltan líneas > Con diferencia > Cuadra; un conteo sin líneas sale "Faltan líneas" (nunca
   "Cuadra" si el servidor no dijo `matches`).
7. **Filtro "Contó"**: el API no tiene un catálogo de quién contó; se ofrecen los que aparecen en las páginas vistas y, si el
   usuario tiene `admin.users`, todos los usuarios activos de la compañía.
8. **Columnas Ajuste y Evidencia siempre presentes al capturar en la web** (con "—" si no hay dato): si aparecieran cuando llega la
   vista previa o la primera captura, la tabla volvería a montar las celdas y el campo que se está tecleando perdería el foco.
9. **Comentario del cierre en bloque opcional** (queda en el historial de cada conteo cerrado).
10. **Lista "Por revisar" a la izquierda (50 %) y el conteo a la derecha**, como la pestaña Conteos; con el panel por debajo de
    720 px la lista va en tarjetas (en una pantalla de 1280 px, por defecto). La barra se arrastra para darle más ancho.
11. **Crear conteos por producto desde la web: no se incluyó** (decisión pendiente del dueño). Hoy se crean desde la app.

## Pendientes

- **Backend**: un catálogo de "quién contó" (o los contadores en la respuesta de "Por revisar") para el filtro; orden y filtro
  `onlyMatching` en el servidor para "Por revisar" (hoy el orden es de la página y el cierre en bloque mira solo la página).
- **Playwright en una base nueva**: `lote15.spec.ts` 6 ("Tus gráficos") necesita algún producto con costo y existencia (el gráfico
  "Valor de inventario por categoría" no tiene cifras con valor 0); en una base recién creada falla hasta que algún recorrido o la
  demo deje uno. Se comprobó sembrando por API un producto con costo 3 y existencia 2: el Lote 15 pasó completo (8/8). No es de
  este lote; queda para quien mantenga ese recorrido (sembrar su propio producto con costo).
- **Intermitencia previa** del paso 6 de `lote14.spec.ts` en paralelo (documentada en F10): se endureció el clic en el Kárdex; en
  serie pasa.
- Sin prueba automática del motivo `Stale` en pantalla (el servidor tampoco puede forzar esa carrera; la pantalla muestra el texto
  del motivo como cualquier otro).
