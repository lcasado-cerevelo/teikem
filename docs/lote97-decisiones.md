# Lote 97 — Número de conduce (P10, 1) (2026-10-17)

Primer lote del portal de clientes (diseño y orden en [p10-portal-diseno.md](p10-portal-diseno.md)). Luis pidió un número **único por orden de entrega** (waybill number en Advance; «número de conduce» para nosotros) que sea el identificador de la orden para el cliente y para el consignatario: el número de orden se repite entre clientes y el de empaque es del almacén.

## Qué se construyó
- `TransportOrder.WaybillNumber` (`CND-#######`), contador `WAYBILL` por compañía (`WaybillCodes`), índice único entre las órdenes activas, generado por `OrderService` al crear (en el mismo orden fijo de los contadores, al final, para no crear interbloqueos).
- `update.sql` (af): columna, CHECK del contador, **relleno de las órdenes existentes en orden de creación** (por compañía), contador al siguiente libre, columna NOT NULL e índice.
- API: `waybillNumber` en la lista y la ficha de la orden, filtro `waybill`, búsqueda libre y **buscar o escanear** con el conduce como primera coincidencia (`WAYBILL`).
- Web: columna Conduce (lista), campo en la ficha y el «Waybill» del label ahora es el conduce (con respaldo al número de orden); textos es/en; openapi y cliente generado.
- Manual 03 (sección nueva), FAQ y este documento.

## Cómo se probó
- xunit `WaybillTests` (3): tipo de contador y patrón, lista/ficha/búsqueda/filtro/lookup con el conduce y que otra compañía no lo encuentra. Suite completa.
- vitest: lista (columna y orden de columnas), label (conduce y respaldo). `npm run check`.
- Contra SQL real (`db-init` aplicó (af)): las 357 órdenes existentes con su conduce, dos órdenes nuevas con CND consecutivos (414 y 415), búsqueda libre y lookup (`matchedBy = WAYBILL`).

## Decisiones a revisar con Luis
1. **El label cambia:** el campo «Waybill» ahora muestra el conduce (antes el número de orden). Si algún cliente quiere seguir viendo su número de orden, es un elemento aparte del label.
2. **Patrón fijo** `CND-#######` (siete dígitos; pasa de nueve millones y se alarga sin romper). Si Advance quiere el formato de su waybill actual, se cambia la constante.
3. El conduce es **secuencial**: la página pública del portal solo dirá estatus y última actualización (nunca nombres ni direcciones) y tendrá tope de consultas.
4. La importación desde el sistema viejo (MSWM) no trae su waybill: las órdenes migradas reciben conduce nuevo.
