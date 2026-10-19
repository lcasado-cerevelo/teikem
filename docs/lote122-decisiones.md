# Lote 122 — SLA en riesgo (P12)

## Qué se construyó
- **`SlaRules`** (Domain): el SLA de una orden es `MaxTransitHours` del nivel de servicio de su contrato por tipo de servicio. El reloj corre desde que se recoge (primer paso a «Recogida»; respaldo: la confirmación) hasta la entrega. Abierta y recogida: A tiempo, **En riesgo** (le queda ≤ max(1 h, 25 % del plazo)) o **Vencida**; entregada: **Cumplida** o **Fuera de SLA**; el resto, **Sin SLA**.
- **`SlaService`**: evalúa muchas órdenes con dos consultas (niveles de servicio activos y el historial de estatus de las que lo necesitan).
- **Fuente de datos de órdenes**: campos nuevos `SlaState`, `SlaMaxHours`, `SlaDueUtc` y `SlaHoursLeft` (dependencia opcional, para no romper quien la construye sin ella), usables en vistas, indicadores y gráficos.
- **Aviso `SLA_AT_RISK`** en «Necesita tu atención» (`SlaAtRiskAttentionProvider`): una fila agregada con «En riesgo», «Vencidas» y «La más apretada»; rojo si hay vencidas; `orders.view` + `LTL_GROUND`; «Revisar» abre Órdenes. Texto es/en en la web.

## Cómo se probó
- **xunit:** `SlaTests` (17: casos límite de las reglas, entregadas a tiempo y tarde, sin reloj, servicio con historial real, nivel sin horas máximas, el aviso con conteos, que desaparece al entregar y que sin riesgo no hay fila). Suite completa: 4.535 pasan, 0 fallan.
- **vitest:** 3 pruebas nuevas del aviso (rótulo, título con cifras, singular/inglés).

## Decisiones a revisar
1. **25 % del plazo (mínimo 1 h)** como umbral de «en riesgo»; es una constante (`SlaRules.RiskFraction`), fácil de volverla configurable por contrato.
2. **El reloj cuenta desde que se recoge**, no desde que se crea (la ventana de recogida es otro nivel de servicio, `PickupWindowMin`, que no se mide aquí).
3. **Tope de 2.000 órdenes abiertas por lectura** del aviso, para no recorrer todo el histórico.
4. **Sin indicadores sembrados todavía:** los campos están en la fuente; los indicadores de cumplimiento (% a tiempo) se agregan cuando Luis diga con qué meta. Sin recorrido Playwright de este lote: es un aviso de lectura cubierto con SQL real en xunit.
