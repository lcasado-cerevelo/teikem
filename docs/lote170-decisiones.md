# Lote 170 — Número del aviso de llegada: LLE-##### (A5; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Servidor:** `Asn.Number` (único por compañía, `UX_Asn_Number`), contador `ASN` con patrón `LLE-#####` (`AsnCodes`), asignado en las dos formas de crear un aviso (cliente/personal/portal y el que nace de una orden de compra). `AsnDto.Number`, `PortalArrivalDto.Number`, `SyncAsnDto.Number`; el aviso de notificación usa el número en `{number}`; la búsqueda y el filtro «Referencia» de la lista encuentran también por número; Actividad usa el número cuando no hay referencia.
- **Base (`Diseño/logistica-db-update.sql`, sección 2026-10-21 Lote 170):** columna, ampliación de `CK_NumberSequence_Kind`, **numeración de los avisos existentes en orden de creación por compañía**, fila del contador lista para seguir, `NOT NULL` y el índice único. Idempotente.
- **Web:** el número en la pestaña Avisos de llegada, el selector de aviso del recibo y la cita de muelle; el filtro pasa a «Referencia o número».
- **App de almacén:** migración local v18 (columna `number`, y se reinicia la marca de agua de avisos para bajarlos completos una vez); el número en el listado, en la búsqueda parcial y al escanear/teclear `LLE-#####`.
- **App del cliente (portal):** el número en «Mis avisos de llegada».

## Cómo se probó
- xunit `AsnNumberTests` (contador conocido, números consecutivos, el aviso de notificación lleva el número y no el id, búsqueda por número, espejo del SQL) + `WmsContractsTests` (contrato posicional) y `ActivityRulesTests`; las clases de ASN, recibo, portal y sincronización que usan el servicio.
- **Contra SQL Server real:** una prueba desechable (no se deja en el repo) llevó una base al estado «sin la columna», insertó avisos de dos compañías (uno inactivo) y corrió la sección dos veces: numeró `LLE-00001/00002` y `LLE-00001`, dejó el contador en 3 y 2, la columna `NOT NULL`, el índice y el `CHECK` con `ASN`.
- vitest `ReceiptScreen.test.tsx` (número en la lista, filtro «Referencia o número»); jest de la app (`localLookup`, `download`, `database`); tipos de web, app de almacén y app del cliente.

## Decisiones a revisar
1. **El número reemplaza al `#id`** en pantallas y avisos de notificación; la referencia del cliente sigue al lado.
2. En la app de almacén, escanear acepta el número **o** la referencia (si coincidieran, gana el número).
3. Los avisos viejos se numeran por orden de creación (no por fecha esperada).
4. `schema.d.ts` de la app de almacén y de la del cliente se parcharon a mano (una línea), porque regenerarlos desde `openapi.json` los desfasa del resto.

## Qué quedó fuera
- Imprimir/etiquetar el número en el recibo impreso.
- Mostrar el número en la ficha del recibo creado desde el aviso (el recibo ya tiene el suyo, REC-#####).
