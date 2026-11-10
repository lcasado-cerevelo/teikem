# Lote 237 — Exención del cliente por componentes y campo «Impuesto» en el editor de producto (2026-11-10)

Dos pedidos aprobados por Luis. Backend, web, manual y FAQ (es y en). **Las pruebas quedaron escritas y NO se corrieron** (orden del dueño): las corre el coordinador.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base (`logistica-db-update.sql`, sección «2026-11-10 — Lote 237») | `ClientTaxExemption.Scope` (`NVARCHAR(20)`, `'ALL'` por omisión: así quedan las exenciones que ya existían) y `ClientTaxExemption.ComponentCodes` (`NVARCHAR(310)`, `NULL`), más `CK_ClientTaxExemption_Scope`. Solo agrega; idempotente. |
| Alcance de la exención | `ALL` (todo el impuesto) o `COMPONENTS` (solo los componentes nombrados). `POST` / `PATCH /tax/exemptions` aceptan `scope` y `componentCodes`; `TaxExemptionDto` los devuelve. Los códigos se guardan en mayúsculas, sin repetir y ordenados (`CITY,STATE`). |
| Cálculo (`TaxRules`, puro) | `Calculate(..., exemptComponents)`: a la línea gravable se le aplica la tasa **sin** los componentes exentos; motivo `EXEMPT_CLIENT_PARTIAL`; `TaxRatePct` = lo realmente cobrado (10.5, no 11.5); el componente exento va en 0 (`IsExempt`) y el impuesto se reparte solo entre los que se cobran. `ResolveExemptions`: qué aplica en la fecha del documento (una total gana; las parciales se unen; de las vencidas se avisa). `ValidateExemptionScope`: normaliza y valida. |
| Servicio (`TaxService.CalculateAsync`) | Usa lo anterior; anota el certificado en la línea y en el componente exento (`TaxCalcComponentDto.exemptReason` / `exemptionCertificate`); avisa del componente que la tasa vigente no tiene. |
| Consumidores | Orden de venta, IVU del cargo de envío (Lote 222), factura (producto, flete, cargos de devolución y de cheque), nota de crédito y exportaciones: **no hubo que cambiarlos**. Todos llaman a `CalculateAsync` con el cliente y copian `TaxRatePct` / `TaxAmount` / `IsTaxExempt` de la línea, o recalculan con esa copia. QuickBooks IIF (Lote 225): la línea de IVU lleva `Invoice.TaxAmount` y la línea de producto sigue «gravable» (tiene impuesto). Portal y apps solo muestran montos. |
| Pantalla de exenciones | Formulario con «Aplica a: Todo el impuesto / Solo estos componentes» y casillas de los componentes de la tasa por omisión vigente; columna «Aplica a» en la lista. Simulador: motivo «Cliente exento en parte», «Se cobró el 10.5 %» y «Municipal: exento (certificado …)». |
| Campo «Impuesto» del producto | `ProductEditorModal`, bloque Precios: select Gravable / Exento / Tasa cero. Solo con el módulo `TAX`; editable con `tax.manage` (el permiso que exige `TaxController`), si no, solo lectura con nota. Guarda con el endpoint que ya existía (`PUT /tax/products/{id}/category`). `ProductDetailDto.taxCategory` (nuevo, solo lectura) para poder mostrarlo a quien no tiene `tax.manage`. |
| Contrato | `web-app/openapi.json`: 5 esquemas (`TaxExemptionDto`, `TaxExemptionRequest`, `TaxExemptionPatchRequest`, `TaxCalcComponentDto`, `ProductDetailDto`), 0 rutas, fusionados del swagger del API en Development sin llave de Brevo; `schema.d.ts` regenerado en web-app y copiado igual a app-cliente y app-almacen. |

## Decisiones (para ratificar)
1. **Los componentes van en una columna** (`ComponentCodes`, códigos separados por coma, normalizados por el API), no en una tabla hija: son pocos (tope 10), se leen siempre junto a la exención y así la bitácora de `TAX_EXEMPTION` registra el cambio de alcance sin tocar el interceptor.
2. **La línea con exención parcial NO queda marcada como exenta** (`IsTaxExempt = false`): paga impuesto. En las pantallas de orden y factura muestra su monto, no «Exento».
3. **Si los componentes exentos son toda la tasa**, la línea queda como `EXEMPT_CLIENT` (exención completa), con los certificados que aplicaron.
4. **Al guardar se exige que el código sea un componente de alguna tasa de la compañía** (de cualquier vigencia): 400 «Ninguna tasa de la compañía tiene el componente «X».». En el cálculo, un componente que la tasa **vigente** no tiene no afecta y avisa.
5. **Aviso de exención vencida**: es el de siempre. Con varias exenciones se avisa de la vencida más reciente, **salvo** que lo que cubría siga cubierto por una vigente (certificado renovado) o que haya una total vigente.
6. **Precios con impuesto incluido + exención parcial**: del precio se extrae solo la parte que se cobra (el precio de la línea no cambia), igual que hoy con la exención total (no se extrae nada).
7. **La fecha es la del documento**, como ya era. El IVU del cargo de envío se calcula con la fecha de hoy (así estaba desde el Lote 222; no se tocó).
8. **PATCH**: lo que no viene no cambia; con `scope: "ALL"` los componentes se quitan; pasar a `COMPONENTS` sin componentes → 400.
9. **Casillas del formulario**: componentes de la tasa por omisión vigente; si la compañía no escogió tasa por omisión, los de todas las tasas vigentes. Un código que la exención ya tenía y la tasa vigente no trae sigue saliendo (marcado, «no está en la tasa vigente») para no perderlo al editar.
10. **Alta de producto**: el impuesto se guarda **después** de crear (no se amplió `POST /products`, para no meter el permiso `tax.manage` en el alta). Si ese segundo paso falla, el producto queda creado como Gravable y se avisa. En edición, cambiar solo el impuesto no manda el `PATCH` del producto.
11. Las exenciones se administran solo en Contabilidad → Impuestos → Exenciones (la ficha del cliente no las tiene hoy): no se agregó otra pantalla.

## Pruebas ESCRITAS, sin correr
- xunit `tests/Teikem.Tests/TaxPartialExemptionTests.cs`:
  - `TaxPartialExemptionTests` (16, puras): municipal exento cobra solo el estatal; componente exento primero / tres componentes; componente que la tasa no tiene; todos exentos = exención completa; solo cambian las líneas gravables; redondeo por línea y por documento; precios con impuesto incluido; sin componentes exentos todo igual; parcial vigente; total gana; combinación; vencida con aviso (y el día límite); renovada no avisa; validación del alcance con mensajes exactos; línea de IVU del IIF de QuickBooks; sección del SQL.
  - `TaxPartialExemptionSqlTests` (4, SQL Server): de punta a punta con producto gravable, producto exento y la línea del envío con IVU (`PRODUCT_SALE`); combinación, total gana y componente inexistente con aviso; validación, normalización y PATCH del alcance; redondeo por documento y precios con impuesto incluido.
- vitest: `features/tax/taxRules.test.ts` (alcance: validación, casillas, nombres); `features/tax/TaxScreen.test.tsx` (formulario de exención con alcance y columna de la lista); `features/warehouse/ProductEditorModal.test.tsx` (4 casos del select Impuesto: sin módulo, edición, solo lectura, alta).

## Lo que sí se comprobó
- `dotnet build Teikem.sln`: 0 errores (API y pruebas). `tsc -b` en web-app: limpio. `oxlint` de los archivos tocados: sin avisos. `npm run help:index`.
- Base local nueva `TeikemL237` con `db-init` y la sección 237 corrida dos veces más (EXIT 0 las dos); columnas y restricción en su sitio. Base borrada al final.
- Recorrido por curl con el API local en Development y **sin** llave de Brevo: tasa IVU 10.5 + 1; las cuatro validaciones del alcance → 400 con su mensaje; exención municipal vigente → cálculo y **orden de venta** con 10.50 de impuesto y `taxRatePct` 10.5 (producto exento en 0); la misma exención vencida → 11.50 con el aviso del certificado, en el cálculo y en una orden nueva; cambiada a «todo el impuesto» → 0 con el certificado; `GET /products/{id}` con `taxCategory`.

## Pendiente
- Correr las pruebas de arriba, `npm run check` y el CI.
- No se vio en un navegador: falta revisar el formulario de exención y el campo Impuesto en claro / oscuro y a 360 px, y tomar las capturas del manual (`l56-nueva-exencion.png`, `l56-exenciones.png` y el editor de producto).
- No se probó por el API una factura ni una nota de crédito de un cliente con exención parcial, ni el archivo IIF real (solo lectura del código y las pruebas escritas).
