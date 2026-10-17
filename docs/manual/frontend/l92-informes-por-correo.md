# L92 — Enviar una vista por correo (pantalla)

Dónde: **Análisis → Vistas e informes → Nueva vista / Editar vista**, bloque **«Enviar por correo»** (entre «Visibilidad» y «Vista previa»). Requiere `analytics.manage`. El funcionamiento del envío (trabajo, validaciones, resultado) está en [el capítulo 01, §8.2.1](../01-plataforma-y-seguridad.md).

## Qué se ve
1. Casilla **«Enviar este informe por correo automáticamente»**. Apagada, el bloque solo muestra una explicación. Al activarla se arma una programación diaria a las 07:00 con el período «Últimos 7 días».
2. **Frecuencia:** Todos los días / Una vez por semana (aparece **Día de la semana**) / Una vez al mes (aparece **Día del mes**, 1 a 28) y **Hora (de la compañía)**.
3. **Período del informe enviado:** Últimos 7 días, Últimos 30 días, Este mes, Todo el historial.
4. **Destinatarios:** caja de texto con correos separados por coma o punto y coma. Debajo, un aviso en vivo: cuántos destinatarios hay, o el primer correo mal escrito, o que faltan (máximo 10).
5. En una vista **ya guardada**: línea con el **último envío** (Enviado / Omitido / Con error, fecha y mensaje) y el botón **«Enviar ahora»**. Con cambios sin guardar en el bloque el botón se apaga y avisa «Guarde los cambios para enviar lo nuevo.».
6. Una programación que la pantalla no sabe armar (puesta por la API) se conserva y se avisa; desmarcar la casilla la quita.

Los errores del servidor (programación, correos o período inválidos) salen debajo del bloque con el mensaje exacto. Al desmarcar la casilla y guardar, se borran la programación, los destinatarios y el período.

## Archivos
`ReportScheduleParts.tsx` (bloque), `reportSchedule.ts` (cron ⇄ formulario, correos), `reportsApi.ts` (`useDeliverReport`), cambios en `ViewBuilderModal.tsx` y `viewRules.ts`; pruebas `ReportScheduleParts.test.tsx` y `reportSchedule.test.ts`. Textos en `analytics.reports.schedule.*` (es/en).

Sin capturas en este lote: el envío real depende del proveedor de correo, que no está en desarrollo.
