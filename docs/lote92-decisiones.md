# Lote 92 — Informes programados por correo (P12, 5) (2026-10-17)

Quinto y último lote de P12 (plan en [lote88-decisiones.md](lote88-decisiones.md)): la programación opcional de las Vistas (`ScheduleCron` / `DeliveryEmails`, que existían en el modelo pero no hacían nada) ahora funciona.

## Qué se construyó
- **Reglas** (`ReportScheduleRules`, dominio): cron restringido de 5 campos (diario, semanal, mensual día 1 a 28), correos (hasta 10, sin repetir, formato), período (`LAST7`, `LAST30`, `THIS_MONTH`, `ALL`), cuándo toca enviar (una vez por ocurrencia, hora local de la compañía).
- **Datos:** columnas nuevas en `ReportDefinition` (`DeliveryRangeMode`, `LastDeliveryUtc`, `LastDeliveryStatus`, `LastDeliveryMessage`; `update.sql` (ac)). `ScheduleCron` y `DeliveryEmails` se validan y normalizan al guardar; guardar una programación nueva fija el «último envío» a ese momento.
- **Servicio** `ReportDeliveryService`: `RunDueAsync` (lo que ya toca) y `SendNowAsync` («Enviar ahora»); corre la vista **como su dueño** (permisos, visibilidad y alcance de datos del dueño), arma el correo (`ReportEmailBuilder`: tabla de hasta 200 filas, totales, HTML escapado) y lo manda por destinatario con el envío transaccional que ya existía.
- **Trabajo** `REPORT_DELIVERY` (cada 5 min, activo) y endpoint `POST /api/v1/analytics/reports/{id}/deliver` (`analytics.manage`).
- **Web:** bloque «Enviar por correo» en el constructor de vistas (frecuencia, día, hora, período, destinatarios, último envío y «Enviar ahora»); openapi y cliente generado; textos es/en.
- Manual 01 §8.2.1, cap. 13, FAQ y este documento.

## Cómo se probó
- xunit `ReportDeliveryTests` (13): cron, correos, validaciones con mensajes exactos, cuándo toca (diario/semanal/mensual, zona de Puerto Rico, ocurrencias perdidas), el HTML (escape, formatos), el envío como dueño (permisos y alcance por cliente), omitidos, destinatario que falla, «Enviar ahora», guardar y el trabajo. Suite completa verde.
- vitest: reglas (`reportSchedule.test.ts`) y el bloque con fetch simulado (`ReportScheduleParts.test.tsx`); `npm run check`.
- Contra SQL real: `db-init` aplicó (ac); crear una vista con programación inválida (400 con el campo), válida (200 con el contador fijado), «Enviar ahora» (SKIPPED porque el proveedor de correo no está configurado en desarrollo) y el trabajo (`Ningún informe por enviar.`). **No se probó un envío real de correo** (no hay Brevo en desarrollo).

## Decisiones a revisar con Luis
1. **Cron restringido** en lugar de un cron libre: solo diario, semanal y mensual (día 1 a 28) para que la pantalla lo pueda armar y leer.
2. **Sin adjunto:** el correo lleva la tabla (hasta 200 filas) porque `ITransactionalEmailSender` no soporta adjuntos. Un CSV adjunto exige ampliar el envío (Brevo lo admite).
3. **Se corre como el dueño:** si el dueño pierde un permiso o se desactiva, el envío falla/omite en vez de usar otros permisos.
4. **Una vez por ocurrencia**, aunque se hayan perdido varias; un fallo no se reintenta hasta la siguiente ocurrencia («Enviar ahora» sirve para reintentar).
5. **Solo vistas propias** (las de sistema no se programan); no hay programación de indicadores ni gráficos.
6. **Kemi AI** queda fuera de P12 (sin verificar); decidir si sigue en alcance.

## Cierre de P12
Lotes 88 (ríos), 89 (Radar y avisos), 90 (fotos diarias), 91 (indicadores/gráficos de COD, facturación y ventas + Actividad) y 92 (informes por correo). Pendiente de P12: SLA en riesgo (espera al POD, P7) y Kemi AI.
