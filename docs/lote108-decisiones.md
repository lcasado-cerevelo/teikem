# Lote 108 — Barra de progreso de cuatro etapas (2026-10-18)

Luis dictó que la barra que le sirve al cliente y al consignatario tenga **solo cuatro cosas** y que todos los demás estatus se encajonen en una: **Sometida, Recibida, En ruta y Completada**. En la tercera, un intento fallido se ve **Intentada**; si vuelve al almacén tras el intento, **Reprogramada**; y cuando el chofer la vuelve a coger, **En ruta**. La cuarta es cualquier estatus final y el texto es el del estatus que sea (cancelada, devuelta, lo que sea). Esto reemplaza el criterio del Lote 103 («tantas etapas como estatus tiene la compañía»).

## Qué se construyó
- **`OrderProgress`** (dominio) reescrito: cuatro etapas fijas (`SUBMITTED`, `RECEIVED`, `ON_ROUTE`, `DONE`; texto es/en) y `Compute(código, etiqueta, tipo, tuvoIntentoFallido, idioma)` que ubica cada estatus: Sometida→1; Recogida/Sin asignar/Asignada→2; En tránsito→3 «En ruta»; Intento fallido→3 en alerta «Intentada»; Sin asignar o Asignada con un intento fallido en el historial→3 «Reprogramada»; cualquier estatus final→4 con su propio nombre (Cancelada pinta la barra apagada); un estatus propio de la compañía queda en 2 con aviso.
- **`OrderProgressService`:** las etapas ya no salen de los estatus de la compañía; para las órdenes Sin asignar o Asignadas consulta en una sola lectura si alguna vez tuvieron un Intento fallido.
- **Página pública y portal:** misma respuesta de siempre con las cuatro etapas; una cancelada no marca ninguna etapa como hecha.
- La app del cliente no cambió de código (ya pintaba cualquier cantidad de etapas y los estados IN_PROGRESS, COMPLETED, ALERT y CANCELLED).

## Cómo se probó
- xunit `TrackingTests` reescrito (13 casos por la tabla de estatus, idiomas, estatus propio, servicio con las cuatro etapas, intento fallido y cancelada) y `PortalAccessTests`; suite completa; jest de la app del cliente; Playwright 102 y 103 (la página pública muestra 4 etapas y «Intentada · va por el paso 3 de 4»).
- Para correr esos recorridos hubo que darles contrato y tarifa a sus clientes: ver la nota de abajo.

## Nota importante (cernidor al someter, Lote 107)
Como contrato y tarifa se exigen **al crear** la orden, **ocho recorridos Playwright** que creaban órdenes por API para clientes sin contrato fallaban. Se les agregó el ayudante `web-app/e2e/rates.ts` (`contractData()` y `withRates()`: contrato vigente con tarifa STANDARD/BOX y ENVELOPE) y el de expediente (FC2) ya no espera el aviso de crédito al «Marcar como recogido» (ahora el crédito se mide al crear).

## Decisiones a revisar con Luis
1. **Cancelada** muestra la barra apagada con la etapa «Completada» como actual en el servidor (paso 4); en la app se ve toda apagada y dice «Cancelada». Si prefieres que una cancelada antes de recogerse no llegue a la etapa 4, se cambia en una línea.
2. **Un estatus propio de la compañía** (que no sea de los de fábrica) queda en «Recibida» con aviso.
3. «Devuelta» (estatus final futuro) caerá en «Completada» con ese nombre y en verde; si la quieres de otro color, se decide cuando exista.
