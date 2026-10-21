# Lote 179 — Avisos de flota, parte 2 (G2; autónomo, 2026-10-21)

## Qué se construyó
- Cuatro avisos nuevos en el mismo trabajo diario `FLEET_ALERT_SCAN` (mismo mecanismo del Lote 168: un aviso por evento con el conjunto, no se repite en la semana, opcionales, sin reglas sembradas): **órdenes de trabajo abiertas demasiado tiempo**, **combustible fuera de rango**, **vehículos sin viaje** y **choferes sin viaje**.
- Reglas puras en `FleetAlertRules` (umbrales como constantes: 14 días de orden abierta, 14 sin viaje, 30 % de desviación de km/L, historia de 3 a 5 cargas, ventana de 30 días); el servicio lee solo lo necesario y arma «orden · vehículo · abierta hace N días», «vehículo · km/L · promedio · diferencia», «vehículo · sin viaje desde … · N días».

## Cómo se probó
- xunit `FleetAlertTests` (24; +10): reglas puras (borde de 14 días, inactividad desde alta o último viaje, desviación y tope de historia) y la revisión sobre datos (órdenes, combustible con historia suficiente, vehículos y choferes sin viaje con un viaje cancelado que no cuenta); más las pruebas de avisos y trabajos (203). Sin SQL nuevo ni cambios de estructura.

## Decisiones a revisar (Luis)
1. **Umbrales fijos**, no ajustables por compañía (asumí «ajustables» al inicio, pero cada ajuste suma columna, DTO, pantalla y pruebas; conviene decidirlo con datos reales). Pasarlos a ajuste de la compañía es mecánico (mismo camino que `fleetDocWarnDays`).
2. «Sin viaje» mira el último viaje **no cancelado** por vehículo y por chofer, con la fecha del plan; un alta reciente no avisa en sus primeros 14 días.
3. Combustible: evalúa solo la última carga y compara contra el promedio de las anteriores (no contra un rango por tipo de vehículo).

## No se probó
- Contra SQL Server real (las consultas son sencillas: unión con vehículos, agrupar viajes por vehículo/chofer, ventana de cargas); el trabajo diario ya corría el Lote 168.
