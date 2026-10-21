# Lote 169 — Quién ve cada indicador, gráfico y vista, también los «Por defecto» (F; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Servidor (F1):** `AnalyticsService.IsVisible` ya no deja ver siempre lo de sistema. Los «Por defecto» nacen TENANT (todos, como hoy) y respetan privado / compartido (personas y roles). Quien tiene `analytics.manage` siempre ve lo de sistema y lo de la compañía sin dueño (para poder devolverlo); el dueño ve lo suyo; el administrador de plataforma ve todo.
- **Servidor (F2):** `PUT /analytics/{indicators|charts|reports}/{id}/visibility` (`analytics.manage`, cuerpo `VisibilityRequest`): cambia SOLO quién lo ve. Un «Por defecto» sigue sin poder editarse (nombre, fuente, campo, filtros) ni eliminarse (403). En los demás, vale el dueño o, si es de la compañía, quien administra. La auditoría sale sola (`[AuditEntity]` en definición y shares).
- **Web (F3):** botón «Quién lo ve» en la tarjeta de un «Por defecto» de Indicadores y Gráficos (`analytics.manage`), con el mismo selector de personas y roles del editor.
- **F4 (permisos):** crear/editar/compartir ya exigían `analytics.manage` y ver `analytics.view`; no se tocó (el administrador de la compañía los trae de la plantilla).

## Cómo se probó
- xunit `AnalyticsVisibilityTests` (9): nace para toda la compañía; privado oculta al lector (lista, lectura y evaluación → 404) pero el administrador lo sigue viendo; compartido por persona y por rol; volver a «Todo el tenant» quita las comparticiones; no se edita ni elimina; 400 de visibilidad desconocida; gráfico privado fuera de la lista; administrador de plataforma; permiso de los tres endpoints. Corrieron además las clases de Análisis, Pulso de almacén, entrega de informes y Rentas que usan el servicio.
- vitest `IndicatorsPage.test.tsx` (botón solo con `analytics.manage`; el PUT manda solo la visibilidad) y `ChartsPage.test.tsx`; tipos y lint de `features/analytics`.

## Decisiones a revisar
1. **Quien administra Análisis ve todo lo del sistema y de la compañía** aunque esté privado: si no, un «Por defecto» privado quedaba huérfano sin quién lo devolviera.
2. **Solo la visibilidad** se cambia en un «Por defecto» (nombre, fuente, campo y filtros no; para eso se crea uno propio). «Mostrar en Pulso» y el rango ya son preferencia de cada usuario, así que no hizo falta tocarlos.
3. Sin botón de «Duplicar» todavía: hoy se crea uno nuevo a mano.

## Qué quedó fuera
- Botón «Quién la ve» en la pantalla de Vistas (el servidor y el endpoint de vistas ya están).
- Duplicar un «Por defecto» con un clic.
- Pruebas de pantalla para la tarjeta de Gráficos (usa la misma condición que Indicadores).
