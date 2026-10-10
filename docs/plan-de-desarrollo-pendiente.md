# Plan de desarrollo de lo que falta (2026-10-10, actualizado tras las decisiones de Luis)

Documento para llevar a otra sesión. Resume **qué está construido, qué falta, en qué orden hacerlo, qué decidir y qué verificar**.
Fuentes: `Diseño/logistica-funcionalidades-maestro.md` (diseño), `Diseño/teikem-mockups.html` (maqueta), código en `src/`, `web-app/`,
`app-almacen/` y los `docs/lote*-decisiones.md`. Estado del repo al escribirlo: commit `b4aa14e` (master y
`claude/company-settings-screen-plan-uajc2i`).

> **Producción desde 2026-10-09.** Todo cambio de base va como **sección nueva al final de `Diseño/logistica-db-update.sql`**
> (idempotente; estructura y datos). `logistica-db-estructura.sql` y `logistica-db-seed.sql` están **congelados**
> (`FrozenSqlTests`). `db-reset` y `scripts/recrear-base*.ps1` **solo en desarrollo**. Ver `CLAUDE.md`.

## 0. Límites de este análisis (leer primero)

- La auditoría se hizo **leyendo código y documentos**, no ejecutando. No se corrió nada contra SQL Server ni en un Zebra.
- Backend: se comparó el DDL (150 tablas) con los `DbSet` de `TeikemDbContext`, controladores, servicios, permisos y docs.
  Web: rutas de `web-app/src/routes.tsx`, `features/*` y docs. Maqueta: menú `NAV`, tabla `SCREENS` y la mayoría de los cuerpos.
  La comparación columna por columna solo se hizo a fondo en Pulso, Indicadores, Gráficos, Órdenes y Ajustes.
- Antes de construir cada pieza hay que **re-verificar** en el código lo que aquí se afirma (marcado «verificar»).
- Hay contradicciones documentales: `docs/lote18` y `lote19` listan servicios aún en UTC, `lote20` dice «Hecho». Verificar en código.
  `docs/frontend/diseno-frontend.md` sigue diciendo «nada construido» (está obsoleto; lo real está en `loteF*-decisiones.md`).
- La maqueta buena es `teikem-mockups.html` (guion). `teikem_mockups.html` (guion bajo, 2461 líneas, 2026-07-21) es una versión vieja que nadie usa.

## 1. Qué está construido (resumen)

| Área | Estado |
|---|---|
| Capas A catálogos, B estatus, C contactos, F campos personalizados, H indicadores, I gráficos | Construido |
| D seguridad | Parcial: TOTP, recuperación, reauth AAL2, sesiones, PIN de aparato, contraseña temporal y «olvidé mi contraseña». Falta SMS, biometría, filtro real de `UserDataScope` (se guarda, no filtra), verificar HIBP/Argon2id |
| E auditoría | Parcial: falta agrupar por `CorrelationId` y orden del servidor en actividad |
| G informes | Parcial: `ScheduleCron`/`DeliveryEmails` se guardan, no hay job que los envíe |
| J reglas de aviso | **Solo diseño** (solo existe el correo transaccional de seguridad) |
| 0B módulos | Parcial: faltan las claves `SALES_ORDERS`, `TAX`, `BILLING`, `NOTIFICATIONS` |
| 1 Clientes y contratos | Backend casi completo; **web: sin pantalla** |
| 2 Órdenes de transporte | Backend parcial; web: solo lectura (sin alta/edición/importación) |
| 3 Trips y rutas | Backend parcial (falta cerrar trip, eventos por parada, pings, motores reales); **web sin pantallas** (Despacho, Monitoreo, Escaneo) |
| 4 Flota y choferes | Backend construido; **web sin pantalla** |
| 5 WMS | Muy completo (web y app). Falta: despacho manual, FIFO/FEFO configurable, olas/cartones, despacho contra orden de venta |
| 6 Cross-docking | Demo, módulo apagado en el tenant demo |
| 7 Lote/serie | Construido |
| 8A App de almacén (Zebra) | Construida: recibir, acomodar, despacho, conteo, consultar, transferir, ajustar, daño |
| 8B App de choferes | **No existe** |
| 9 POD | **Solo diseño** (tablas en el SQL sin entidad) |
| 10 Portal de clientes | Solo la administración de usuarios del portal. Login del portal rechazado a propósito (`AuthService.cs:108`) |
| 11 Facturación | **Solo diseño** (hay permisos `billing.*` sin uso y tablas sin entidad) |
| 11A Liquidación a choferes | Parcial: tarifas, política y viajes; falta la corrida, `DeliveryAttempt` (ni está en el SQL) |
| 11B COD / remesa | **Solo diseño** (campos COD en la orden; permisos `cod.*` sin uso) |
| 12 Dashboards | Parcial: Pulso, indicadores y gráficos de almacén/rentas. Faltan ríos, fotos diarias, indicadores de COD/facturación/ventas |
| 13 API de integraciones | Parcial: idempotencia y versionado. Faltan `ApiCredential`, webhooks, conectores |
| 13B Compras | Construido (falta la contabilización de compras) |
| 13C Órdenes de venta | **Solo diseño** |
| 13D Devoluciones y notas de crédito | **Solo diseño** |
| 16C Rentas | Construido (reescrito en lote 27, diverge del diseño). Falta `RentalCharge` y facturación |

### Pantallas de la maqueta (38 ítems de menú)
- **Con pantalla web real (14):** Almacenes, Ubicaciones, Productos e inventario, Compras, Recibo, Recolección y empaque, Conteo cíclico,
  Cruce de muelle, Kárdex, Indicadores, Gráficos, Roles y usuarios, Seguridad y auditoría, Ajustes de la compañía.
- **Parciales (3):** Pulso (faltan los dos «ríos», Radar de órdenes abiertas y avisos sin chofer/COD/SLA), Órdenes (solo lectura), Ajustes de inventario (reubicado).
- **Placeholder, sin construir (21):** Escaneo, Sala de despacho, Monitoreo; COD, Facturación, Liquidación, Contabilización de compras y de despachos;
  Clientes y contratos, Consignatarios, Choferes, Flota; Vistas, Campos personalizados; Impresoras, Integraciones; las 5 del Portal.
- **En la web pero no en la maqueta:** login/MFA/onboarding, Proveedores, Rentas (4 pantallas), Daños, Reabasto, Conciliación, Saldos,
  Aparatos móviles, Catálogos de valores, Actividad reciente, Etiquetas y reportes de códigos de barras.

## 2. Orden recomendado y por qué

Principio: primero lo que protege la operación que ya está en producción, luego lo que no necesita código nuevo de backend, luego los
módulos nuevos en su orden de dependencia. **Luis decide el orden final**; este es el que recomiendo.

```
P0 Producción y deuda ──► P1 Web de lo que ya tiene backend ──► P2 Cimientos (J, jobs, módulos) ──► P3 Impuestos ──► P4 Ventas (S1–S4)
                                                                                        │
                      P5 Facturación ──► P6 Devoluciones y notas de crédito (S6/S7)
                                              ▲
        P7 App de choferes + POD + intentos ──► P8 COD ──► P9 Liquidación a choferes
                                                                                        │
                                   P10 Portal de clientes ──► P11 API de integraciones ──► P12 Dashboards/Pulso completos
```

**Decidido (D1): impuestos antes de órdenes de venta**, para que cada línea nazca con su copia de impuesto. El diseño ya lo dice (13C: S0).

---

## P0 — Producción y deuda inmediata

### P0.1 Señal débil: el aparato trabaja con sus datos locales (decisión de Luis, 2026-10-10; **EN CURSO: S1 servidor, S2 base local y S3 Consultar/Transferir/Ajustar/Conteo/Daño y S4 (cola de Transferir/Ajustar/Daño) hechos; faltan abrir conteo sin señal y capacidad en Acomodar/Recibir — ver `docs/mobile/senal-debil-decisiones.md`**)
**Problema:** el cliente del API (`app-almacen/src/kernel/api/client.ts`) no tiene tiempo máximo y las pantallas esperan al servidor antes de
mostrar (Consultar, Acomodar, Transferir, Ajustar, Daño, conteo, plan de salida del Despacho, `findBinByCode`). Con señal floja la petición
queda colgada. Los saldos no residen en el aparato (solo `balance_cache` de lo último consultado).
**Principio (en el diseño, 8A):** *todo se trabaja desde el aparato*. Cada pantalla muestra **primero lo local, al instante**, sincroniza en paralelo,
muestra un **indicador de estado** (sincronizando / al día + hora / sin conexión / señal débil) y **actualiza la pantalla** cuando llega el dato. Al cambiar de pantalla,
la nueva muestra lo que hay y su indicador trabaja de fondo. Ya **no** se usa el tiempo máximo de 3–4 s para bloquear: nada bloquea la pantalla.
**Alcance (cada punto con su prueba):**
1. **Servidor:** sincronización por diferencia de **saldos por posición** del almacén (`sync/balances?since=`), con marca de cambio por saldo. Verificar si `StockBalance` tiene una marca fiable de actualización; si no, agregarla con una sección de `update.sql` (no tocar los SQL congelados) o derivarla del ledger (`InventoryTransaction`). Incluir lotes/series, reservado y disponible recolectable.
2. **App, base local:** tabla local de saldos (migración `SCHEMA_VERSION` 10), descarga inicial por almacén activo y por diferencia; limpiar al cambiar de almacén; cada fila con su hora de sincronización.
3. **App, pantallas:** Consultar, Conteo, Acomodar, Transferir, Ajustar, Daño y Despacho leen de lo local; reemplazar `searchBalances`/`fetchBinContents`/`findBinByCode`/`fetchProductBins`/exit-options por lecturas locales + refresco en segundo plano.
4. **Indicador único** (componente del núcleo, `kernel/ui`) alimentado por un estado global de sincronización (`syncStatus`): tamaño de cola de salida, última sincronización, error, señal débil. Visible en todas las pantallas de trabajo.
5. **Sincronización con prioridad:** primero la cola de salida, luego diferencias; reintento con espera creciente; una petición de fondo con vencimiento largo (15–20 s) solo para marcar «señal débil», sin bloquear.
6. **Operaciones que cambian inventario (Transferir, Ajustar, Daño):** hoy son solo en línea. Con «todo desde el aparato» hay que decidir **D2b**: ¿pasan a la cola de salida con efecto optimista en el saldo local (se reconcilia al sincronizar, y si el servidor la rechaza —saldo insuficiente, zona no permitida— queda «requiere revisión»)? Recomendado: sí para Ajustar (es una cantidad con signo) y Daño; Transferir también, con la regla de que un rechazo no detiene la cola.
7. **Riesgos a verificar:** datos viejos (mostrar hora y avisar si pasan de N minutos), saldo local contra ledger tras una operación propia (recalcular al sincronizar), tamaño de la descarga inicial en Depot (contar saldos), batería, conflictos entre dos aparatos sobre la misma posición, y que la cola de salida mantenga el orden.
**Verificar:** pruebas unitarias con SQL real (`better-sqlite3`, como ya se hace); `fetch` que nunca responde, que responde lento y sin red; cola sigue enviando en orden; APK con `versionCode` mayor (`scripts/construir-apk.ps1`, `actualizar-aparatos.ps1`); **probar en el Zebra** al fondo del almacén; manual 09 y FAQ actualizados.

### P0.2 Cosas de producción que dependen de Luis (no son código)
- Redeploy de API + web + APK con lo último (Transferir, Ajustar, orden del menú, `update.sql`).
- Crear el rol con `warehouse.adjust` (el permiso no tiene plantilla de rol a propósito).
- Probar `scripts/recrear-base-depot.ps1` **solo en desarrollo**.
- **Decisión D3:** ¿llevar a `Depot-Implementation` lo de Transferir/Ajustar/`update.sql`? Hoy esa rama no los tiene ni tiene el congelamiento.
- Decisión vieja sin responder (D4): permiso para cancelar un conteo (opciones 1 vs 2).

### P0.3 Deuda técnica del backend (verificar cada punto antes)
- Filtro real de `UserDataScope` (almacén/cliente): hoy se guarda y no filtra (`lote1`, `lote6` D42).
- Cerrar un trip: `TripStatuses.Completed` existe pero ningún endpoint lo mueve (solo `StartAsync`).
- `Contract.CodCommissionPct` en el modelo pero no expuesto; decisiones 39 y 44 del lote 2/3 (baja de cliente, reinvitar).
- Propagación de `orders.credit_override` a roles clonados (sin prueba).
- Validar la ventana AAL2 a 15/30/60 (loteF10). Atributo `[SkipIdempotency]` declarativo (hoy lista fija).
- Tablas obsoletas de Rentas (`RentalAsset`, `RentalContract`…) siguen en la base; confirmar vacías y decidir (nunca borrar sin mirar).
- Resolver contradicción de zona horaria (lote18/19 vs 20) con una búsqueda de `DateTime.UtcNow.Date` en servicios.
- Actualizar `docs/frontend/diseno-frontend.md` (obsoleto).

### P0.4 Pendientes de almacén ya decididos
- **Despacho manual** como operación propia (salida sin documento; Luis rechazó usar «Ajustar»): `docs/frontend/loteF8a-decisiones.md` §3. Servidor + web + app.
- Rotación FIFO/FEFO configurable (hoy FEFO fijo): `docs/lote15`, `lote16`.
- Sin probar en Zebra físico: hojas/etiquetas de posición y producto (lotes 23/24, F14–F16).
- `sync/clients` y `sync/consignee-locations` (`lote8A-decisiones`); resumen de recibos por almacén en una llamada (`lote19`).

---

## P1 — Pantallas web de lo que ya tiene backend

Bajo riesgo (no hay lógica nueva) y habilitan a Advance Logistics a operar desde la web. Cada pantalla: lista con `DataTable` y
`exportRows`, ficha/modal, `<Can perm>`/`<ModuleGate>`, i18n es/en, 360 px, Playwright, capítulo del manual con capturas
(`docs/manual/frontend/`) y `docs/frontend/loteFN-decisiones.md`.

| Lote | Pantalla (maqueta) | Backend existente | Notas |
|---|---|---|---|
| F-A | Clientes y contratos (expediente, contratos, tarifas, servicios especiales, usuarios de portal, cotización) | `ClientsController`, `ContractsController`, `ContractRatesController`, `SpecialServicesController`, `PortalUsersController`, `BillingQuoteController` | Es la más grande; separarla en F-A1 expediente y perfil, F-A2 contratos y tarifas, F-A3 portal |
| F-B | Consignatarios y localizaciones (incl. importación CSV por plantilla) | `LocationsController`, `ImportTemplatesController` | |
| F-C | Órdenes: **alta** (entrada rápida, detallada, entrega especial), edición, cotizar/confirmar/cancelar, importación, Expediente | `OrdersController`, `OrderStatusController`, `OrderImportController`, `SpecialDeliveryController` | Hoy solo lectura |
| F-D | Choferes y Flota (vehículos, documentos, mantenimiento, combustible, tarifas del chofer y política de pago) | `DriversController`, `FleetController`, `VehiclesController`, `Maintenance*`, `FuelLogs`, `DriverRates`, `DriverPayPolicy`, `DriverTrips` | |
| F-E | Despacho (planificar el día, rutas, optimizar, asignar), Monitoreo, Escaneo (salida) | `Trip*Controller`, `ScanController` | Falta backend de cierre de trip y eventos por parada (ver P7) |
| F-F | Vistas y Campos personalizados | `/analytics/*` (vistas, informes), `ExtensibilityControllers` | Confirmar qué falta en backend de vistas |
| F-G | Impresoras y labels | **No hay backend**: decidir si se hace aquí o se difiere | |

**Verificar por lote:** `npm run check` (tipos generados, tsc, oxlint, vitest, build); `npx playwright test` contra el API real; el cliente se
genera con `npm run api:types` desde `web-app/openapi.json` (regenerar tras cambios de API); permisos y módulos con los códigos exactos del API.

---

## P2 — Cimientos transversales (antes de cualquier módulo nuevo)

### P2.1 Capa J — Reglas de aviso (S3), con canales configurables
- **Reglas con interruptores:** cada `NotificationRule` se prende o apaga y elige **uno o varios canales** (`EMAIL`, `SMS`, `APP`, `PUSH`), cada uno con su propio interruptor y su plantilla. Un canal que el tenant no tenga configurado aparece apagado.
- **Entidades nuevas** (secciones nuevas en `update.sql`): `NotificationEvent` (catálogo sembrado desde código), `NotificationRule`, `NotificationRuleChannel` (o columnas por canal; decidir al diseñar), `NotificationOutbox`, `NotificationLog` (por canal; idempotente por evento + regla + destinatario + canal), `UserNotification` (bandeja de avisos del usuario) y `UserNotificationPref` (silenciar eventos opcionales).
- **Servicios:** registro de eventos por módulo (como `IDataSource`), `INotificationPublisher` que escribe en la outbox **dentro de la misma transacción**, `NotificationWorker` (BackgroundService) con reintentos acotados y un `INotificationChannelSender` por canal: correo (existente, Brevo), SMS (**D16: proveedor** —p. ej. Twilio— con límite mensual por tenant), app/push.
- **Etapas:** (a) **ahora**: eventos + reglas + outbox + correo + bandeja `UserNotification` en servidor y pantalla de reglas; (b) **después**: pantalla *Avisos* y contador en la app del almacén (sincroniza la bandeja, funciona sin señal); (c) **después**: SMS y push (Expo/FCM) y la app de choferes (P7).
- **Permiso** `notifications.manage` (actualizar `PermissionCatalog`, `update.sql`, pruebas de conteo de permisos y `WmsControllerSecurityTests`). Módulo `NOTIFICATIONS` (verificar si es necesario o va siempre encendido).
- **Pantalla** Sistema → Reglas de aviso: eventos por módulo, interruptor de regla y de cada canal, destinatarios (roles, usuarios, correos/teléfonos), plantillas por canal, envío de prueba por canal y consulta del log.
- Eventos primeros: los de ventas (13C). Después: recibo con diferencia, daño reportado, conteo por revisar, faltante de compra, devolución con daño.
**Verificar:** un aviso que falla nunca deshace la operación; reintentos; idempotencia; un canal apagado no envía; teléfonos y correos validados; sin fuga entre tenants; el SMS respeta el límite; el aviso en la app aparece sin señal una vez descargado; prueba con servidor de correo caído.

### P2.2 Motor de trabajos programados
Hoy solo existe `InventoryReconciliationWorker`. Falta una base común para: barrido SCHEDULED de conciliación, foto diaria (`KpiDailySnapshot`),
envío programado de informes (`ScheduleCron`/`DeliveryEmails`), outbox de notificaciones. **Decidir D5:** BackgroundService propio vs. Hangfire/Quartz
(recomendado: BackgroundService simple con tabla de bloqueo para no duplicar si hay varias instancias; verificar cómo se despliega el API).

### P2.3 Módulos
Agregar claves `SALES_ORDERS`, `TAX`, `BILLING` (y `NOTIFICATIONS` si aplica) en `ModuleKeys` + `ModuleDefinition` (con dependencias) vía `update.sql`.
Encender por tenant: Advance Logistics e Island Wide (ventas), según `TenantModule`.

---

## P3 — Impuestos (IVU) (S0: va primero, decisión de Luis 2026-10-10)

- Tablas nuevas: `TaxRate`, `TaxRateComponent`, `TenantTaxSetting`, `ClientTaxExemption`; `Product.TaxCategoryLookupId` + catálogo `TaxCategory`; columnas de copia en líneas (`TaxRateId`, `TaxRatePct`, `TaxAmount`, `IsTaxExempt`) en orden de venta e `InvoiceLine`.
- Reglas: una tasa **no se edita** (se cierra la vigencia y se crea otra); redondeo por línea o documento; precios con impuesto incluido o no; flete/manejo gravables por `ChargeType`; exención vigente → sin impuesto con certificado anotado; vencida → avisa y cobra.
- Permiso `tax.manage`; pantallas (tasas, categorías, exenciones) + informe de impuestos por período.
- **D6:** las tasas, los componentes (estatal/municipal), qué es gravable (flete, manejo) y el redondeo los entrega el contador de Luis; **Teikem programa el mecanismo completo** para que todo sea configuración. Se siembra con valores de ejemplo claramente marcados, no con cifras legales.
**Verificar:** cálculo con casos reales de la compañía; nota de crédito acredita con la copia de la línea original; pruebas con vigencias que cambian a mitad de mes.

---

## P4 — Órdenes de venta (13C), partes S1–S4 (después de impuestos)

Entidades nuevas (no existen en el DDL congelado; verificar): `SalesOrder`, `SalesOrderLine`, `SalesOrderReservation`; columnas nuevas nullable
`PickBatch.SalesOrderId`, `PickBatchLine.SalesOrderLineId`, `TransportOrder.SalesOrderId`, `InvoiceLine.SalesOrderLineId`. Estatus `SalesOrderStatus` en
`StatusCode`; catálogos y permisos en `update.sql` (`sales.view/create/cancel/price/backorder`), plantilla de rol **Ventas** y permisos al Operador de almacén,
propagados a los roles de los tenants existentes (revisar cómo lo hizo `PermissionSeeder` con `warehouse.transfer`).

| Parte | Contenido | Verificar |
|---|---|---|
| S1 servidor+web | Crear/editar/solicitar, validar y **reservar en la misma transacción** con el candado del ledger, diálogo de faltante (pedido/disponible/faltante), `allowBackorder`, vista Backorders, Surtir, cerrar con faltante, cancelar libera reservas, crédito del cliente (`orders.credit_override`), modificar línea reajusta reserva | Dos vendedores a la vez no sobrevenden; «disponible para vender» excluye cuarentena/cross-dock/renta; reserva se libera exacta (`SalesOrderReservation`); transiciones solo por `StatusService`; auditoría; tenant |
| S2 despacho (web+app) | «Despachar contra orden de venta» (`PickBatch.SalesOrderId`), por partes, consume la reserva, `QtyShipped`, retiro o entrega (crea `TransportOrder`), deshacer recolección vuelve a reservar | Siempre baja inventario; no despacha más de lo reservado; `SHIPPED` cuando todo despachado/cerrado; app: lista de órdenes por despachar; mismo ledger |
| S3 avisos | Eventos `SalesOrderRequested`, `SalesBackorderAvailable`, `SalesOrderShipped`, `SalesOrderCancelled`, `SalesCreditOverrideNeeded` sobre la capa J | Detección de backorder surtible al entrar inventario (recibo, ajuste, transferencia, liberar cuarentena, cancelación) |
| S4 análisis | Fuentes `SALES_ORDER` y `SALES_ORDER_LINE`, indicadores (órdenes abiertas, unidades en backorder, fill rate, tiempo solicitud→despacho), avisos del Pulso | |

`SalesBackorderMode` MANUAL (AUTO después) y `SalesBillingTrigger` ON_DISPATCH/ON_DELIVERY son ajustes del tenant (decidir dónde viven: Ajustes de la compañía).
**Cierre:** `docs/loteN-decisiones.md`, capítulo nuevo `docs/manual/12-ordenes-de-venta.md` (o el número que toque), FAQ con cada mensaje de error, README del manual.

---

## P5 — Facturación (módulo 11)

Las tablas `BillingRun`, `Invoice`, `InvoiceLine`, `Payment` ya existen en el DDL congelado (**verificar columnas contra el diseño**: `RateComponentId`, `ChargeType`,
`SalesOrderLineId`, impuestos; lo que falte va por `ALTER` idempotente en `update.sql`). Hoy no tienen entidad.

1. Entidades + configuraciones + servicios: generar cargos desde órdenes entregadas (según `BillingModel` y `RateComponent`/`RateTier`, guardando `RateComponentId`), líneas `PRODUCT_SALE` desde ventas según `SalesBillingTrigger`.
2. Corrida: generar → revisar → aprobar (**`[RequireAal2]`**) → exportar. Ajuste manual por línea auditado. Congelar el cálculo al generar (principio 8 del diseño).
3. Exportación a contabilidad con plantillas `ACCT_TEMPLATES` (CSV/TXT, delimitador, columnas) — QuickBooks. Esta infraestructura de plantillas **no existe** aún en el backend; construirla aquí y reutilizarla en liquidación, compras y despachos.
4. Pagos y reconciliación, estado de cuenta del cliente. Permisos `billing.generate/approve/export` (ya sembrados), `billing.view` (nuevo).
5. Cargo opcional por intento de entrega (depende de `DeliveryAttempt`, P7).
6. Contabilización de compras (13B) y de despachos: corridas con plantilla (nunca se construyó; los docs la llaman «lote 10» pero ese lote fue la migración).
7. Pantallas: Facturación, Contabilización de compras y de despachos.
**Decisiones:** D7 resuelta (precios en Teikem). D8 resuelta: exportación **genérica** por plantillas, ajustable luego al formato de QuickBooks. D9 numeración de facturas (hay `Client.InvoiceNumberBy` y `NumberFormat`).
**Verificar:** nunca se factura dos veces (`QtyInvoiced`, `SalesOrderLineId`); `Facturable = QtyShipped − QtyReturned − QtyInvoiced`; aprobar exige AAL2 reciente; exportación reproducible; el flete sigue el `BillingModel` del contrato.

## P6 — Devoluciones de venta y notas de crédito (13D, S6/S7)

- Entidades: `SalesReturn`, `SalesReturnLine`, `CreditNote`, `CreditNoteLine`, `CreditApplication`; catálogos `ReturnReason`, `ReturnDisposition`, `CreditReason`, causa de daño «Devolución de cliente»; ajustes `SalesReturnWindowDays`.
- Recepción: `ReceiptHeader` tipo RETURN enlazado a la devolución; por línea **buenas / dañadas**; lo dañado genera el reporte `DAN-#####` (origen Recibo) reutilizando el flujo de daños; cuarentena primero; botón *Reportar daño* en la ficha.
- Notas de crédito: DRAFT → APPROVED (**AAL2**) → APPLIED/exportada; acredita con el impuesto de la línea original; no mueve inventario.
- Permisos `sales.return`, `sales.return.authorize`, `billing.credit`; eventos `SalesReturnRequested/Authorized/Received/Damaged`, `CreditNoteApproved`.
- Web (Devoluciones de venta, Notas de crédito) y app (Recibir → devolución con buenas/dañadas).
**Verificar:** no se devuelve más de `QtyShipped − QtyReturned`; ventana de días; series por serie; lo dañado no entra como bueno; ledger íntegro; nota por devolución no duplica inventario.

---

## P7 — App de choferes, POD e intentos de entrega (módulos 8B y 9)

**Es el bloque más grande y hoy no existe nada.** Prerrequisito de COD y de liquidación.
- Backend: registro de aparato del chofer (generalizar `UserDevice`/`DriverDevice`), sincronización de ruta/paradas/contactos, eventos por parada (en camino, llegada, completar, fallo), cierre de trip, `DriverLocationPing` y pings GPS, push (`Driver.PushToken`).
- POD: entidades `ProofOfDelivery`, `ProofOfDeliveryPhoto`, `ProofOfDeliveryItem` (tablas ya en el DDL), almacenamiento de firma/foto (**decidir D10 el proveedor de archivos/blob**), resultado tipado, entrega parcial con devolución al inventario, geo-validación.
- `DeliveryAttempt` (nueva tabla, ni siquiera está en el DDL): una fila por intento; fuente de verdad de liquidación y de cargo por intento.
- App Expo nueva (`app-chofer/`) reutilizando el núcleo de `app-almacen` (autenticación por aparato, motor de sync con outbox e idempotencia, SQLite, escáner): ruta, mapa OSM, flujo por parada, POD con firma y fotos offline, fallo con razón, COD.
- **Decisiones:** D11 ¿repo/proyecto separado o monorepo? D12 mapas (Leaflet/MapLibre + OSM; motor de ruta real VROOM/OSRM self-hosted o seguir con la heurística). D13 push (Expo push o FCM).
**Verificar:** todo offline con idempotencia; reintentos no duplican; POD visible en el portal; probar en un teléfono real con señal débil (aplica el mismo criterio de P0.1).

## P8 — COD y remesa (11B)
Entidades `CodCollection`, `CodRemittanceBatch`, `CodRemittanceLine` (tablas en el DDL, sin entidad); cobro en la entrega (parciales, offline, `cod.collect`); reconciliación por escaneo (`cod.reconcile`, Estación de escaneo modo COD); remesa con comisión (`Contract.CodCommissionPct` → exponer) y neto; `RemittanceStatus`; exportación con plantillas; KPIs; búsqueda por número de orden en «Procesar entregas». Pantalla COD. Los permisos `cod.*` ya están sembrados.
**Verificar:** el dinero cuadra a centavo; cobros parciales; una venta COD devuelta se concilia con su remesa (13D); trazabilidad de orden → cobro → remesa.

## P9 — Liquidación a choferes (11A)
Corrida `DriverSettlementRun`/`DriverSettlementLine` (el DDL trae `CarrierSettlement`/`SettlementLine`: resolver el choque de nombres con una tabla nueva en `update.sql`), `PayoutFormula` congelada por corrida, ajuste manual auditado, exportación con plantillas. Insumos ya existen (`DriverRatesController`, `DriverPayPolicyController`, `DriverTrips`). Pantalla Liquidación.
**Verificar:** las tres fórmulas (entrega + cada intento; la entrega incluye el 1er intento; intento fallido reemplaza a entrega) con la vista previa existente; sin tarifa = línea en $0 con nota; corridas viejas no se recalculan.

---

## P10 — Portal de clientes (módulo 10)
- Hoy `AuthService.cs:108` rechaza el login de usuarios de portal. Construir: login separado (`UserKind = portal`), claim `cid` (ClientId) en el principal, `ITenantContext` con cliente, **segundo filtro global por `ClientId`** (BOLA), invitación de un solo uso, política de contraseña NIST 800-63B (12 caracteres, sin rotación, HIBP, Argon2id), MFA por política, lockout, sin enumeración de usuarios, CAPTCHA tras N intentos, cabeceras (CSP, HSTS).
- Endpoints/pantallas: inicio, seguimiento en vivo (necesita pings, P7), documentos y POD, entrada de órdenes (mismo componente con el cliente fijo), consignatarios, configuración, perfil, indicadores del cliente, `PortalNotificationPref` (tabla sin entidad).
- Es un sitio web aparte (otro dominio/login). **Decidir D14:** mismo proyecto `web-app` con otra entrada, o proyecto separado.
**Verificar:** OWASP ASVS nivel 2 como lista de aceptación; pruebas de acceso cruzado entre clientes del mismo tenant (cambiar ids en cada endpoint); un usuario de portal jamás entra a la app interna ni al revés; eventos a `SecurityEvent`; acciones de administradores de plataforma atribuidas a ellos.

## P11 — API de integraciones (13)
`ApiCredential` (client_id/secret → token con scopes y rate limit por credencial), `WebhookSubscription` y `WebhookDelivery` (HMAC, reintentos con backoff), conectores ERP/e-commerce/transportistas, pantalla Integraciones (registro de plantillas + crear API key; hoy sin backend). Reutiliza el outbox de P2.1.
**Verificar:** el secreto se muestra una sola vez y se guarda hasheado; scopes; idempotencia; entregas reintentadas; rate limit.

## P12 — Dashboards y Pulso completos (12)
`KpiDailySnapshot` + job nocturno (P2.2); ríos de «paquetes en la calle» y «dinero COD»; Radar de órdenes abiertas; avisos sin chofer/COD/SLA; indicadores de COD, facturación, ventas y SLA (necesita POD); Actividad para Operación y Contabilidad (hoy solo Almacén); alertas operacionales fuera de almacén; programación de informes por correo. Kemi AI: **no verificado**, decidir si sigue en alcance.

## Otros pendientes menores
Sin SMS ni biometría en MFA (decisión 11 del lote 1); `RentalCharge` y facturación de rentas (módulo `RENTAL_BILLING`, apagado); rutas reales de motor (VROOM/OSRM) y geocodificador; adjuntos (`OrderDocument`, `ContractDocument`, documentos de flota) sin proveedor de archivos; `RateZone`/`RateRule`/`MinCharge`/DSL de tarifas; multi-parada real; tarifa genérica del tenant; `PickWave`/`PickTask`/`Carton`; `ProductUom`; costo promedio; retomar conteo ya enviado (el dueño decidió no crearlo).

---

## 3. Checklist de cierre de cualquier lote (obligatorio)

**Base de datos**
- [ ] Todo cambio de estructura/datos es una sección nueva al final de `Diseño/logistica-db-update.sql`, con fecha, idempotente (`COL_LENGTH`/`OBJECT_ID`, `MERGE`/`IF NOT EXISTS`).
- [ ] No se tocó `logistica-db-estructura.sql` ni `logistica-db-seed.sql` (`FrozenSqlTests` verde).
- [ ] `db-init` dos veces seguidas en base limpia sin error; `db-update` con simulación y respaldo antes de producción.
- [ ] Ninguna migración EF; entidades mapeadas 1:1 en `Persistence/Configurations`.
- [ ] Nunca se corrió `db-reset`/`recrear-base*` fuera de desarrollo.

**Backend**
- [ ] `TenantId` sale del principal; entidades `ITenantScoped`; pertenencia validada en asociaciones polimórficas.
- [ ] `[RequirePermission]` en cada acción, `[RequireModule]` por módulo; `[RequireAal2]` en aprobaciones sensibles.
- [ ] Permisos nuevos en `PermissionCatalog` **y** en `update.sql` **y** propagados a roles/tenants existentes; pruebas de conteo de permisos (hoy 71) y `WmsControllerSecurityTests.Expected.Count` (hoy 155) actualizadas.
- [ ] Estatus solo por `StatusService.TransitionAsync`; acciones por estatus con `EnsureAllowedAsync`.
- [ ] Catálogos por `LookupCode`/`StatusCode` (nada de strings sueltos); etiquetas es/en.
- [ ] Soft delete; `[AuditEntity]` en entidades nuevas; `[SensitiveData]`/`[NotAudited]` donde toque.
- [ ] Excepciones de dominio con mensaje en español exacto y código HTTP.
- [ ] Fuentes de datos (`IDataSource`) registradas en `DependencyInjection`.
- [ ] `dotnet build Teikem.sln && dotnet test Teikem.sln` (hoy 3306 pruebas) y `scripts/smoke.sh`.

**Web**
- [ ] `npm run api:types` tras cambiar el API; sin DTOs a mano.
- [ ] `t('clave')` en `es.json` y `en.json`; `<Can>`/`<ModuleGate>` con códigos exactos.
- [ ] Tablas paginadas por servidor con `exportRows` (ordenar actúa sobre todo lo filtrado), ordenar columnas, buscador `QBox`, chips sin envolver, sin scroll horizontal, 360 px, idioma sin reiniciar, ningún modal se cierra con clic fuera.
- [ ] `npm run check` y `npx playwright test` (API en :5000).

**App (Zebra / choferes)**
- [ ] Migración SQLite con `SCHEMA_VERSION` incrementada (hoy 9); `jest` (hoy 573).
- [ ] Operaciones que cambian inventario en línea con fallo rápido; lecturas con tiempo máximo (P0.1).
- [ ] APK con `versionCode` mayor, misma firma y paquete; probar actualización encima sin perder configuración.

**Documentación**
- [ ] `docs/loteN-decisiones.md` (qué se construyó, cómo se probó, decisiones a revisar, qué quedó fuera).
- [ ] Capítulo `docs/manual/NN-*.md` con cada mensaje de error exacto y código HTTP, estatus y transiciones; `docs/manual/faq.md`; `docs/manual/README.md`.
- [ ] Frontend: `docs/frontend/loteFN-decisiones.md` + capítulo en `docs/manual/frontend/` con capturas.
- [ ] Diseño actualizado si cambió una decisión (`Diseño/logistica-funcionalidades-maestro.md`, bitácora).

**Cierre**
- [ ] CI de GitHub Actions verde (es el árbitro si el entorno no puede compilar).
- [ ] Commit en español, push a `master` y a la rama de trabajo; cherry-pick a `Depot-Implementation` solo si Luis lo pide.
- [ ] Lista a Luis de lo que no se pudo probar (SQL Server real, Zebra, PowerShell) y de lo que debe hacer él (deploy, roles, pruebas físicas).

## 4. Decisiones que necesita de Luis

| # | Decisión | Recomendación |
|---|---|---|
| D1 | ¿Impuestos antes de órdenes de venta? | **Decidido: sí** |
| D2 | ¿Sincronizar saldos por posición al aparato? | **Decidido: sí, todo desde el aparato** (ver P0.1) |
| D2b | ¿Transferir/Ajustar/Daño pasan a la cola de salida con efecto optimista? | **Decidido: sí, las tres (hecho en el bloque S4)** |
| D3 | ¿Llevar Transferir/Ajustar/`update.sql` a `Depot-Implementation`? | **Sin responder** — solo si Dani lo necesita para soporte |
| D4 | Permiso para cancelar un conteo (opciones 1 vs 2 pendientes) | Revisar `docs/decisiones-del-dueno-2026-10-03.md` |
| D5 | Motor de trabajos: BackgroundService propio vs. librería | Propio, con bloqueo en tabla |
| D6 | Reglas del IVU | **Las da el contador de Luis; Teikem programa todo el mecanismo configurable** (sin tasas fijas en código) |
| D7 | ¿Los precios viven en Teikem? | **Decidido: sí** |
| D8 | Formato exacto de exportación a QuickBooks | **Decidido: formato genérico configurable ahora; se ajusta después con una muestra real** |
| D16 | Proveedor de SMS y push para los avisos | Decidir antes de la etapa (c) de P2.1 |
| D9 | Numeración de facturas | Usar `NumberFormat` del cliente |
| D10 | Almacenamiento de firmas y fotos (POD) | Blob/archivo del proveedor que ya use el despliegue |
| D11 | App de choferes: proyecto separado o monorepo | Carpeta hermana `app-chofer/` reutilizando el núcleo |
| D12 | Motor de rutas real (VROOM/OSRM) | Después; mantener heurística |
| D13 | Push (Expo o FCM) | Expo push |
| D14 | Portal: mismo `web-app` con otra entrada o proyecto aparte | Entrada aparte del mismo repo, dominio aparte |
| D15 | Orden global de las fases | El de la sección 2 (impuestos antes de ventas ya decidido) |

## 5. Para arrancar en otra sesión
1. Leer `CLAUDE.md`, este plan y la sección del diseño de la fase (`Diseño/logistica-funcionalidades-maestro.md`).
2. Confirmar con Luis la decisión D-correspondiente y el orden.
3. Trabajo **secuencial, un bloque probado a la vez**; nunca editar los SQL congelados; nunca recrear la base; no tocar `.env.development`.
4. Commits en español con los trailers de coautoría, push a `master` y a `claude/company-settings-screen-plan-uajc2i`, sin force-push.

## Decisiones del dueño antes de F-A2 (2026-10-10)
- **Despacho manual**: documento propio con numeración y ficha consultable, además del Kárdex.
- **Permiso**: nuevo `warehouse.issue` (propagarlo a los roles ya clonados en `Diseño/logistica-db-update.sql`).
- **SLA de contratos**: se captura en horas por tipo de servicio (igual que el backend).
- **Contratos del cliente en la web**: pestañas dentro de la ficha del cliente (contrato, tarifas, SLA, servicios especiales).
