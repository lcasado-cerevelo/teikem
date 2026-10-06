# Lote F17 — Rentas F-R1 en la web: lista, ficha, alta con equipos por serie, extensión y "Convertir a serie" (2026-10-06)

Bloque **F-R1** de `docs/rentas-plan-de-ejecucion.md` (sección 5): las pantallas de la renta sobre el servidor de los Lotes 26–29 (R0–R3).
Solo se tocó `web-app/` y `docs/`: **el servidor no cambió** (`npm run api:types` deja `schema.d.ts` igual). Manual de pantallas:
[f17-rentas.md](../manual/frontend/f17-rentas.md); FAQ: sección "Lote F17" de [faq.md](../manual/faq.md); informe:
[rentas-informe.md](../rentas-informe.md) (bloque F-R1).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Ruta y menú | Almacén → **Rentas** (`/warehouse/rentals`, `rental.view` + `RENTAL_EQUIPMENT`; `RouteGate` = `ModuleGate`/`Can`) justo antes del Kárdex (order 110; Kárdex pasa a 120 y sigue siendo el último). Ficha `/warehouse/rentals/:publicId`. `?rental=<publicId>` ("Revisar" del aviso `RENTAL_DUE`) lleva a la ficha | `app/routes.tsx`, `nav.rentals.*` |
| Lista | Filtros al API: Estatus (`SearchSelect`), Cliente (`ClientPicker`), "Vencen en (días)" (`dueWithinDays`), "Solo vencidas" (`overdue`) y `QBox` (`search`, 300 ms); leídos una vez de la URL (`?dueWithinDays=7&overdue=true` de "Ver todos"); tabla paginada en el servidor, columnas ordenables, `StatusChip` (color y texto), **Vencimiento** calculado (`DueChip`), Exportar con todo lo filtrado (`exportRentals`), tarjetas bajo 720 px | `features/rentals/RentalListScreen.tsx`, `DueChip.tsx` |
| Ficha | Cabecera (número, cliente, estatus, vencimiento, recogido); Datos (cliente, localidad, contacto, almacén, inicio, recogido vigente y pactado, extensiones, contrato y firma, transporte, equipos, despacho, cierre, envío y factura vacíos, notas); Equipos (serie, SKU, producto, lote, posición de origen, tarifa vigente, despacho, estado; inactivos atenuados); Extensiones; Historial de estatus (`/status/history/RENTAL/{id}`). Acciones por capacidad del DTO y permiso | `RentalDetailScreen.tsx` |
| Diálogos | Programar / Despachar / Cancelar con texto de efectos y comentario opcional; Extender (nueva fecha, motivo, tarifa opcional para los equipos elegidos); Tarifa de un equipo; Agregar equipos; Quitar equipo (`ConfirmDialog`). El error del servidor sale tal cual y el diálogo no se cierra | `RentalDialogs.tsx` |
| Alta y edición | Encabezado con zod (mensajes del servidor), localidades propias del cliente (`/locations?clientId=&includeShared=false`), contactos, almacén preelegido si hay uno solo; alta con equipos en el mismo POST; edición con PATCH de solo lo cambiado (cliente fijo, almacén solo sin equipos, fechas solo sin extensiones) | `RentalFormModal.tsx` |
| Selector de equipos por serie | Producto propio con disponible en el almacén (si no es SERIAL: mensaje exacto y pista de Convertir a serie) → series AVAILABLE del almacén en zonas que se rentan, sin las ya elegidas; casillas + buscador, Enter con la serie exacta (lector); tarifa común opcional (`RateFields`) | `EquipmentPicker.tsx`, `RateFields.tsx`, `useFrequencyOptions.ts` |
| Lógica pura | Filtros/URL/consulta, vencimiento, validaciones espejo de `RentalRules` (códigos → `rentals.errors.*`), equipos, cuerpos de alta, PATCH y extensión | `rentalRules.ts` |
| API | Hooks de lectura y `useRentalAction` (unión por `action`) que dejan la ficha en caché e invalidan lista, extensiones, historial, "Necesita tu atención" y la existencia (las que reservan o mueven) | `features/rentals/api.ts`; `invalidateStock`, `useConvertToSerial` en `features/warehouse/api.ts` |
| Convertir a serie | Botón en `ProductDetailScreen` (`inventory.manage` + `inventory.adjust`, WMS_LOTSERIAL, producto NONE con existencia); modal con una caja por posición (pegar varias líneas, contador, repetidas, largas y conteo con el mensaje exacto; bloqueos de reserva/lote/sin posición/fracción), confirmación de **neto cero** y POST; los 400/409/422 vuelven a la captura | `ConvertToSerialModal.tsx`, `serialConversion.ts` |
| Aviso RENTAL_DUE | Texto en "Necesita tu atención": vencida / se recoge hoy / vence en N días, cliente · localidad · almacén, Recogido, Equipos y Días vencida; grupo "Rentas vencidas o por vencer" | `features/analytics/attention.ts`, `analytics.attention.items.RENTAL_DUE.*` |
| Paridad | `isPickableZone` excluye también la zona `RENTAL` (espejo de `PickBatchRules.IsPickableZone` desde el Lote 27) | `features/warehouse/collectForm.ts` |
| Textos | `rentals.*`, `warehouse.convertSerial.*`, `nav.rentals.*`, `analytics.attention.items.RENTAL_DUE.*` (es/en) | `kernel/i18n/{es,en}.json` |
| Contrato del kit | Secciones "Convertir a serie" y "Rentas" y el aviso `RENTAL_DUE` | `web-app/KIT.md` |
| Recorrido | `e2e/loteF17.spec.ts` + proyectos `escritorio-f17`/`movil-f17` (después de F16, antes de F9) | `playwright.config.ts` |

## Cómo se probó (resultados reales de esta sesión)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: `schema.d.ts` sin cambios, tsc, oxlint (9 avisos que ya existían; ninguno en archivos de este lote), vitest **129 archivos / 1297 pruebas** (antes 124 / 1252: +5 archivos y +45 pruebas: `rentalRules.test.ts` 15, `serialConversion.test.ts` 7, `RentalScreens.test.tsx` 13, `ConvertToSerial.test.tsx` 5, `attentionRental.test.ts` 3, +1 en `collectForm.test.ts` y +1 en `navigation.test.ts`), build |
| `dotnet test tests/Teikem.Tests -o .tmp-testout` (servidor sin cambios, para el API del recorrido) | **3172 pasan, 0 fallan** |
| `db-init` ×2 sobre la base nueva `TeikemF17` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces (la segunda, "ya existe") |
| `scripts/smoke.sh http://localhost:5000` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) a las 01:26 UTC | **FALLÓ en el paso 20** ("tarifas por servicio (Lote 2)", 21 `ok` antes): el smoke calcula "ayer" en UTC y entre las 00:00 y las 04:00 UTC ese día es "hoy" en Puerto Rico para el API (falla previa, independiente de este lote; ver decisión 72 del informe). No se repitió fuera de esa ventana: el orquestador pidió cerrar antes |
| `npx playwright test e2e/loteF17.spec.ts --project=escritorio-f17 --no-deps --workers=1` y lo mismo con `--project=movil-f17` (API real en :5000 sobre `TeikemF17`, Vite con `VITE_API_URL=http://localhost:5000`, Chromium de `/opt/pw-browsers`) | **escritorio 1 pasó, móvil 1 pasó** (25.6 s y 19.7 s; cada uno con 1 omitido por proyecto) |
| Suite completa `npx playwright test --workers=1` sobre esa misma base (smoke incompleto y con las rentas de F17 ya creadas) | **46 pasaron, 3 fallaron, 58 omitidos por proyecto, 64 no corrieron** (por las dependencias de los fallidos): `lote14` 7 (espera "Todo en orden" en "Necesita tu atención", pero había rentas por vencer: las de F17 de corridas anteriores; en el CI también las deja el paso R3 del smoke, ver "No verificado"), `lote15` 6 (gráfico "Valor de inventario por categoría" sin total: depende de los datos del smoke, ya documentado en F15) y `lote15` 9 móvil (franja fija; sin investigar). No es la corrida en el orden del CI |

Recorrido `e2e/loteF17.spec.ts` (escritorio y móvil, cada uno con sus datos): siembra por API un cliente con su localidad y un producto sin
seguimiento con 2 + 1 unidades en dos posiciones RSV de ALM-01; **Convertir a serie** (repetida sin distinguir mayúsculas y conteo con el
mensaje exacto, confirmación de neto cero, aviso final; por API queda SERIAL con 3 en mano y 3 disponibles); Almacén → **Rentas** → **Nueva
renta** (cliente, localidad, almacén, fechas, contrato, transporte; equipo por SKU, una serie por casilla y otra escaneada con Enter, tarifa
mensual; escanear una ya elegida avisa) → ficha REN-##### en Borrador → **Programar** con comentario (por API: en mano 3, disponible 1) →
**Despachar** (estatus En renta, sin "Cancelar renta"; por API las dos series ON_RENT en EN-RENTA) → **Extender** (el 400 de fecha
"…posterior a la actual (aaaa-mm-dd)." y el de motivo, luego bien con tarifa nueva; aparece en Extensiones y "Vence en 5 días") → lista
con `?dueWithinDays=7&overdue=true` (filtros leídos, la renta con "Vence en 5 días" y "En renta") → Pulso: "Necesita tu atención" con
"Renta REN-…: vence en 5 días" y "Revisar" abre la ficha. En móvil, sin scroll horizontal en cada paso. Capturas `f17-*` (las de otros
lotes que regeneró la corrida se descartaron con `git checkout --`).

## Decisiones para el dueño (valor más seguro)

1. **Ficha propia** (`/warehouse/rentals/:publicId`) en lugar de maestro-detalle: la ficha es larga (datos, equipos, extensiones,
   historial) y en el celular se lee mejor sola. `?rental=` (el enlace del aviso del servidor) redirige a ella.
2. **Rentas antes del Kárdex** en el menú de Almacén: el Kárdex sigue siendo el último (decisión de la Fase 8).
3. **Almacén de origen obligatorio en la web** (*"Elija el almacén de origen."*): el selector de series necesita el almacén. El servidor lo
   toma por defecto si hay uno solo; la web lo preelige en ese caso.
4. **Búsqueda de equipos por producto y luego por serie**: el API no tiene búsqueda de series entre productos; se elige el producto (SKU o
   nombre) y se buscan o escanean sus series. Las series en cuarentena o cruce de muelle no se ofrecen (se cuentan en una nota), igual que
   las rechazaría el servidor.
5. **Una tarifa común por cada tanda de equipos agregada**; cada equipo se corrige después con **Tarifa** antes del despacho.
6. **Extender con una sola tarifa nueva** aplicada a los equipos elegidos (todos por defecto); solo se mandan los que cambian (el servidor
   no versiona una tarifa igual). Alternativa: una tarifa distinta por equipo en el mismo diálogo.
7. **Confirmación con comentario opcional** (≤ 500, va al historial) en Programar, Despachar y Cancelar; Quitar equipo con `ConfirmDialog`.
8. **"Convertir a serie" solo para productos con existencia** (como pidió el encargo); un producto sin existencia se cambia a SERIAL con
   la edición normal si no tiene movimientos, o por el API. Captura de todas las posiciones a la vez (decisión 3 de R0) y dos pasos
   (captura → confirmación de neto cero).
9. **Validación previa con el mismo texto del servidor** (es) y su traducción (en); los mensajes solo de la web están en la FAQ.
10. **Hallazgo de permisos (servidor, no se cambió)**: crear una renta exige consultar clientes, sus localidades y contactos (`clients.read`,
    `locations.read`, módulo **Catálogo**), que la plantilla **Operador de almacén** no tiene aunque sí `rental.manage`. Con ese rol la
    lista y la ficha funcionan, pero el alta no puede elegir cliente ni localidad (la web lo avisa). Opciones: agregar `clients.read` y
    `locations.read` a la plantilla, o un endpoint de búsqueda acotado bajo `rental.manage`.
11. **Atribución del commit**: el encargo pedía `Co-Authored-By: Claude Sonnet 5.5`; se usó `Claude Opus 5.5`, el modelo que hizo el
    trabajo según la indicación de atribución del entorno (igual que F14 y F16).

## Qué no se pudo verificar

- Devoluciones de renta y proceso del equipo devuelto: son F-R2 (la ficha muestra "Devuelto" si el servidor lo trae, sin probar con datos
  reales).
- El recorrido con un usuario **Operador de almacén** (hallazgo 10): solo se probó con el administrador; los avisos de permiso se probaron
  en vitest.
- Inglés en Playwright (las pruebas de i18n son unitarias) y lectores de pantalla reales.
- Concurrencia (dos usuarios con la misma renta): el `rowVersion` va en cada escritura y el 409 se muestra, pero no se ejercitó con el API.
- **La suite completa de Playwright en el orden del CI** (base nueva → `db-init` → smoke completo → Playwright): el smoke no se pudo completar
  por la ventana horaria de 00:00–04:00 UTC (falla previa del Lote 2) y el orquestador pidió cerrar. Se corrió solo F17 (escritorio y móvil)
  y una suite completa sobre una base con el smoke incompleto (3 fallos de otros lotes, ver arriba).
- **Riesgo para el CI**: `lote14.spec.ts` 7 espera "Todo en orden" en "Necesita tu atención". Desde el Lote 29 el smoke deja dos rentas por
  vencer (y F17 crea las suyas, aunque corre después de `lote14`), así que ese paso probablemente ya falla en el CI desde R3. No se cambió
  la prueba de otro lote; hay que decidir si espera "sin descuadres" en lugar de "Todo en orden".
- El CI de GitHub Actions (no se hizo push).
