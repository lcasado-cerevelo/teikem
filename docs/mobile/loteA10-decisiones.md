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
