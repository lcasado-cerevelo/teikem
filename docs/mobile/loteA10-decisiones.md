# Lote A10 — modo de recepción por recibo en la app

## Qué se construyó
- Pantalla Recibir (`receive.tsx`): selector "Modo de este recibo" (Con acomodo / Directo a posición), prellenado con el modo del almacén del aparato.
- El modo elegido se guarda en el recibo local y viaja en `receivingMode` al crear el recibo (el servidor ya lo soportaba).
- Sin elección y sin modo conocido del almacén se manda null, igual que antes.

## Cómo se probó
- `receiveModeChoiceScreen.test.tsx`: viene marcado el del almacén; cambiar vale solo para ese recibo; almacén con acomodo abre recibo directo.
- jest completo (80 suites, 454 pruebas), typecheck y oxlint limpios. No se probó en el Zebra.

## Decisiones a revisar
1. El modo se elige solo antes de abrir el recibo; no hay cambio con el recibo abierto en la app (sí en la web).
2. No hay permiso aparte para cambiarlo: cualquiera que reciba puede elegir.

## Acomodo repartido (tarea 24a/24b)
- **Servidor:** `POST /api/v1/warehouse-tasks/{id}/distribute` (`quantityPerBin`, `toBinIds`): un TRANSFER por posición en una sola transacción; lo que no cupo queda como tarea nueva (igual que un completado parcial). Reglas puras en `WarehouseTaskRules.DistributionPlan`. Pruebas: 3 de reglas + 3 de servicio (3201 en verde).
- **App:** pantalla Acomodar con "Cantidad por posición (opcional)"; cada escaneo suma una posición y "Confirmar reparto" las manda juntas. Pruebas: `putawaySplitScreen` y lógica pura (460 en verde).
- **Decisiones a revisar:** (1) solo caben posiciones llenas: la décima de 185 de 20 se rechaza y los 5 sueltos son otra tarea; (2) cada posición una vez; (3) series fuera de esta versión; (4) el permiso es el del acomodo (`warehouse.receive`); (5) el cupo de la posición no bloquea el reparto (D4: solo avisa, y aquí ni avisa todavía).
- No probado en el Zebra.

## Recibo directo con reparto (tarea 24c)
- App: en el paso de posición destino, "Cantidad por posición (opcional)"; cada escaneo suma una posición y "Confirmar reparto" agrega una línea por posición; los sueltos quedan en la captura para una posición aparte. Sin cambios de servidor (cada línea ya lleva su posición).
- Pruebas: `receiveSplitScreen` (461 jest en verde). No probado en el Zebra.
- Decisiones a revisar: (1) en recibos con aviso/OC el reparto choca con la regla de "una posición por línea" (H11) y se avisa; (2) productos con serie no se reparten.

## Despacho con plan de salida (tarea 24d)
- App: `planExit` reparte la cantidad en el orden de salida del servidor (20 de P-01 + 30 de R-02); "Cambiar" reemplaza una posición escaneando otra con existencia; "Usar este plan" agrega las líneas. Sin cambios de servidor.
- Pruebas: `dispatchLogic` (5 nuevas) y `dispatchPlanScreen` (2). No probado en el Zebra.
- Decisiones a revisar: (1) con producto por lote no hay plan (manda el FEFO, como pidió el dueño el 2026-10-05); (2) sin existencia suficiente no se deja usar el plan (hay que bajar la cantidad); (3) el plan se calcula con la foto de existencias del momento (en línea o la copia del aparato).

## Buscar en la lista (tarea 26)
- App: `ScanField` acepta `pick` (`product` | `bin` | `any`) y muestra "☰ Buscar en la lista" (debajo del campo, para no apretar la fila de 360 dp); abre `PickerModal` con buscador sobre la base local (`pickerSearch.ts`: LIKE sin distinguir mayúsculas, tope de 50). Aplicado a 12 campos: Recibir (producto, posición destino), Acomodar (destino), Despacho (producto x2, posición de salida), Conteo (posición, producto x2, otra posición) y Consultar (`any`). Los campos de documento y de series no lo llevan.
- Pruebas: `pickerSearch` (4) y `pickerScreen` (2); 474 jest en verde. No probado en el Zebra.
- Decisiones a revisar: (1) busca solo en lo sincronizado (sin llamada al servidor); (2) la lista de posiciones no filtra por existencia (en Despacho sería útil mostrar solo las que tienen el producto: queda como mejora); (3) límite de 50 filas.

## Regla del reparto cambiada por el dueño (2026-10-06)
- Antes: solo cabían posiciones llenas (la décima de 185 de 20 se rechazaba). **Ahora:** caben las llenas **y una más que recibe el resto** (9 de 20 y una décima con 5); una undécima se rechaza. Servidor (`WarehouseTaskRules.DistributionPlan`, 400 `Con {per} por posición caben {max} posición(es) para {pending}; no hay más unidades por repartir.`), Acomodar y recibo directo.
- **Alerta fija:** `StickyAlert` (`KeyboardScreen`, propiedad `banner`): se dibuja fuera del área desplazable, así que no se va al desplazarse, y se cierra con la ✕. Sale cuando entra la posición que recibe menos que la cantidad por posición (`{bin} recibe solo {qty} (lo que quedaba), no {per}.`).
- Pruebas: servidor 3201 en verde (reglas y servicio actualizados); app 475 (lógica, pantallas de Acomodar y recibo, StickyAlert).
- A revisar: la alerta también sale cuando la tarea entera es menor que la cantidad por posición (5 de 20 en una posición); al quitar la última posición la alerta desaparece sola.
