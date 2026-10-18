# Lote 113 — «Devolver tras N intentos» (2026-10-18)

Luis dictó: «devolver tras N intentos» (por omisión 2) es solo una **sugerencia/marca por compañía**, nunca bloquea.

## Qué se construyó
- **SQL (update.sql, sección (au))**: `Tenant.ReturnSuggestAfterAttempts` (INT NOT NULL, por omisión 2, de 0 a 20; 0 = sin sugerencia).
- **`ReturnSuggestionRules`** (dominio): `Suggest(umbral, intentos, estatus)` (no sugiere en Sometida ni en estatus finales) y `Message`. **`ReturnSuggestionService`**: ajuste por compañía (`GET/PUT /api/v1/tenant/return-settings`, cambiar con `admin.tenant`) y `EvaluateAsync` (intentos fallidos vigentes: `DeliveryAttempt` activos y no entregados).
- **Escaneo**: el resultado de la acción trae `failedAttempts` y `suggestReturn`, y su mensaje termina con la sugerencia (el escaneo se hace igual). **Ficha de la orden** (solo interna): `failedAttempts`, `suggestReturn` y, en una orden de vuelta, `returnOfOrderNumber`.
- **Web**: Ajustes → General → **Devolución** (campo numérico, solo lectura sin `admin.tenant`) y, en la ficha, el aviso «Se sugiere devolverla: N intento(s) fallido(s)» y «Vuelta de {orden}».

## Cómo se probó (solo el área)
- xunit `ReturnSuggestionTests` (14: reglas, escaneo con 2 y con 1 intento, ajuste en 0, intento anulado, ajuste leído/escrito/validado, seguridad del controlador). vitest: `ReturnSuggestPanel` (3) y `OrderDetailScreen` (+2).
- Contra SQL real: `db-update` dos veces (la segunda sin cambios); `GET` devuelve 2, `PUT 3` y `PUT 2` guardan, `PUT 99` da 400 con el mensaje, y la ficha trae `failedAttempts`, `suggestReturn` y `returnOfOrderNumber`.

## Decisiones a revisar con Luis
1. La sugerencia **no cambia ningún estatus ni bloquea el reintento**; solo se ve en la ficha y en el mensaje del escaneo. Si también la quieres como columna o filtro de la lista de órdenes («por devolver»), es un lote chico.
2. Cuentan los intentos **no entregados y vigentes** del registro de intentos (Lote 93); un intento anulado no cuenta.
