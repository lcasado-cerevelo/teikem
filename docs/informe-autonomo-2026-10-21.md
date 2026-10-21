# Informe del trabajo autónomo (2026-10-21)

Pedido de Luis: hacer sin supervisión las partes que faltan —C4, H1, G2 y los menores—; al terminar cada parte: pull, commit y push; anotar el resultado aquí y reportar al final.

## Supuestos que tomé (a revisar)
- **C4 (recibo con varios documentos):** misma regla que el despacho: un solo dueño/cliente, se arma al empezar el recibo, no se suman documentos después.
- **H1 (contabilización):** la regla del plan (por dueño del inventario, costo congelado por línea); pendiente de visto bueno de Luis.
- **G2 (avisos de flota):** umbrales ajustables por compañía con valores por omisión razonables.
- **Menores:** donde había que elegir, elegí lo más simple y lo anoto en la parte.

## Resultados por parte

### G2 — Avisos de flota, parte 2 (Lote 179) ✔
- Cuatro avisos nuevos en el trabajo diario existente: órdenes de trabajo abiertas > 14 días, combustible fuera de rango (> 30 % del promedio de las 3 a 5 cargas anteriores), vehículos y choferes sin viaje > 14 días.
- Pruebas: `FleetAlertTests` 24 verdes (+10) y las de avisos/trabajos (203). Sin cambios de estructura.
- **Para revisar:** umbrales fijos (no ajustables por compañía); lo dejé así a propósito para no abrir cuatro ajustes sin datos reales. Detalle: `docs/lote179-decisiones.md`.
