# Lote 91 — Indicadores y gráficos de COD, facturación y ventas; Actividad de Operación y Contabilidad (P12, 4) (2026-10-17)

Cuarto lote de P12 (plan en [lote88-decisiones.md](lote88-decisiones.md)). Sigue el 92 (informes por correo).

## Qué se construyó
- **Fuentes de datos** `COD_COLLECTION` (`cod.view`, módulo COD) e `INVOICE` (`billing.view`, módulo Facturación), con saldo, días de vencida y pagos no anulados; permisos en `DataSourceExtraReadPermission`.
- **Contenido de fábrica** (`SystemAnalyticsSeeder`, `FinanceAnalyticsRules`): 7 indicadores (6 de COD/facturación + «Ventas (30 días)») y 5 gráficos (2 de COD, 2 de facturas y «Ventas por día»). Se siembran solos en toda compañía.
- **Actividad reciente**: pestañas Operación (`OperationsActivityProvider`, `orders.view`) y Contabilidad (`BillingActivityProvider` con `billing.view` y `CodActivityProvider` con `cod.view`); 18 eventos nuevos en el catálogo `ActivityEventType` (sección (ab) de `update.sql`); `IActivityEventProvider.TenantModule` para exigir el módulo del proveedor; enlaces y tonos en la web (`activity.ts`).
- Manual 07 §9, FAQ y este documento.

## Cómo se probó
- xunit `FinanceAnalyticsTests` (7): las dos fuentes (campos, estatus, saldo, días de vencida, rango, tenant, permiso), que filtros y campos de fábrica existan en las fuentes, los tres proveedores de Actividad y que cada evento esté en `update.sql`. Suite completa y `npm run check`.
- Contra SQL real (`db-init`): sembrados los indicadores y gráficos de COD (los de facturas y ventas quedan ocultos porque la compañía de prueba no tiene esos módulos), sus valores en cero (la base no tiene COD) y las pestañas Operación y Contabilidad en `/analytics/activity`.

## Decisiones a revisar con Luis
1. **Contabilidad = facturación + COD** en una sola pestaña, con dos permisos independientes.
2. **«Ventas»** usa el total de las órdenes de venta no borrador ni canceladas (Requested, Partial, Shipped), no lo facturado.
3. **Pulso:** van encendidos «COD cobrado sin cuadrar», «Facturas por cobrar», «Facturas vencidas» y los gráficos «COD cobrado por día» y «Facturas por estatus»; el resto apagado.
4. **Facturas por fecha de emisión:** los borradores quedan fuera de cualquier rango.
5. **Sin pantalla propia de factura:** el enlace de Actividad abre Facturación, no la factura.
