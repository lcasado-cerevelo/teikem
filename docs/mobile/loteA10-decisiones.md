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
