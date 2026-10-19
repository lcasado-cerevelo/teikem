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
| 1 Clientes y contratos | Backend casi completo; web: **F-A completo (F-A1, F-A2 y F-A3: clientes, contratos, usuarios de portal y cotizador)** |
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

> **Recordatorio (Luis, 2026-10-16): hay reglas comerciales por aplicar** (aviso al 80 % del límite, tolerancia, sin excedentes sorpresivos el primer mes, llamadas fallidas por la plataforma, límite de frecuencia, usuarios de consulta gratis, choferes en paquete). Están en [reglas-comerciales-por-aplicar.md](reglas-comerciales-por-aplicar.md); se aplican cuando se diseñen los planes y el cobro por uso.

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

**Decisión del dueño (2026-10-15): la app de choferes (P7) y el portal de clientes (P10) se dejan para lo ÚLTIMO de todo**, aunque aquí aparezcan antes. Lo que dependía de la app de choferes (POD, COD, liquidación, seguimiento en vivo) se reordena al construirlo.
Después de P12 va **P13 (app móvil para staff)**, que se diseña primero (ver abajo).

**Decidido (D1): impuestos antes de órdenes de venta**, para que cada línea nazca con su copia de impuesto. El diseño ya lo dice (13C: S0).

---

## P0 — Producción y deuda inmediata

### P0.1 Señal débil: el aparato trabaja con sus datos locales (decisión de Luis, 2026-10-10; **HECHO: S1 servidor, S2 base local, S3 pantallas, S4 (cola de Transferir/Ajustar/Daño) y S5 (conteo por posición sin señal y espacio libre sin servidor, Lote 35, 2026-10-11) — ver `docs/mobile/senal-debil-decisiones.md` y `docs/lote35-decisiones.md`; falta probar en el Zebra físico**)
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
- ~~**Decisión D3**~~ **Resuelta (2026-10-11):** `Depot-Implementation` ya tenía Transferir, Ajustar y `update.sql`; se le pasó además lo de almacén que le faltaba (corrección de `db-update`, rotación FIFO/FEFO, conteo sin señal, PIN sin señal).
- ~~Decisión D4~~ **Resuelta** (2026-10-08): cancelar un conteo pide el permiso de capturar (`docs/manual/06-inventario-y-almacen.md` §6.v).

### P0.3 Deuda técnica del backend (verificar cada punto antes)
- ~~Filtro real de `UserDataScope` (almacén/cliente)~~ **Hecho (Lote 32, 2026-10-11: `docs/lote32-decisiones.md`)**: filtro global en el servidor; los bordes (filas hijas, transferencias) quedan anotados allí.
- ~~Cerrar un trip~~ **Hecho (Lote 33, 2026-10-11: `docs/lote33-decisiones.md`)**: `POST /trips/{id}/complete`, permiso `trips.close`; falta el botón en la web y el cierre automático desde la app del chofer (P7).
- ~~`Contract.CodCommissionPct` sin exponer; decisiones 39 y 44 del lote 2/3~~ **Cerrado (Lote 36)**: D39 = un cliente de baja tampoco admite cotizar (409); D44 = se acepta el 200 multi-cliente (decisión de Luis, 2026-10-12: una persona con varios clientes entra con un solo enlace); `CodCommissionPct` se expone junto con la remesa COD en **P8** (ya está en su alcance).
- ~~Propagación de `orders.credit_override` a roles clonados (sin prueba)~~ **Cerrado (Lote 36)**: prueba con el `PermissionSeeder` real (`CreditOverridePropagationTests`).
- ~~Validar la ventana AAL2 a 15/30/60; atributo `[SkipIdempotency]` declarativo~~ **Cerrado (Lote 36)**: el servidor solo admite 15, 30 o 60 (un valor viejo ya guardado se conserva) y la exclusión de idempotencia es un atributo con prueba del conjunto exacto.
- ~~Tablas obsoletas de Rentas~~ **Resuelto**: el retiro (solo si están vacías; con filas avisa por PRINT) ya está en el bloque del Lote 27 de `Diseño/logistica-db-estructura.sql` y `db-update` lo aplica solo; no hay script aparte.
- ~~Contradicción de zona horaria (lote18/19 vs 20)~~ **Cerrado (Lote 36)**: el Lote 20 ya la resolvió; se verificó que no queda `DateTime.UtcNow.Date` ni `DateOnly.FromDateTime(DateTime.UtcNow)` en `src/` (solo un comentario y la hora de un informe de migración).
- ~~Actualizar `docs/frontend/diseno-frontend.md`~~ **Hecho (Lote 36)**: sección «Estado real» al principio.

### P0.4 Pendientes de almacén ya decididos
- ~~**Despacho manual** como operación propia~~ **Hecho** (servidor Lote 31; app y web: `docs/manual/09-app-almacen.md` §6.y, `docs/manual/frontend/f19-despacho-manual.md`).
- ~~Rotación FIFO/FEFO configurable~~ **Hecho (Lotes 34 y 37, 2026-10-11)**: servidor (por compañía + excepción por producto, fecha de primera entrada por saldo) y selector en la web (Ajustes de la compañía → Operación y ficha del producto); la app del almacén no tiene pantallas de ajustes ni de edición de productos y ya respeta el método.
- **Sin probar en Zebra físico** (queda para Luis): hojas/etiquetas de posición y producto (lotes 23/24, F14–F16) y todo lo de señal débil (lector, batería, señal real).
- ~~`sync/clients` y `sync/consignee-locations`~~ **Hecho (Lote 37)**: el aparato baja clientes y consignatarios y despacha/empaca sin señal. ~~Resumen de recibos por almacén en una llamada~~ **Hecho (Lote 37)**: `GET /receipts/summary-by-warehouse`.

---

## P1 — Pantallas web de lo que ya tiene backend

Bajo riesgo (no hay lógica nueva) y habilitan a Advance Logistics a operar desde la web. Cada pantalla: lista con `DataTable` y
`exportRows`, ficha/modal, `<Can perm>`/`<ModuleGate>`, i18n es/en, 360 px, Playwright, capítulo del manual con capturas
(`docs/manual/frontend/`) y `docs/frontend/loteFN-decisiones.md`.

| Lote | Pantalla (maqueta)                                                                                                            | Backend existente                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Notas                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| ---- | ----------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| F-A  | Clientes y contratos (expediente, contratos, tarifas, servicios especiales, usuarios de portal, cotización)                   | `ClientsController`, `ContractsController`, `ContractRatesController`, `SpecialServicesController`, `PortalUsersController`, `BillingQuoteController`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | **F-A completo**: F-A1 y F-A2 (`docs/frontend/loteFA1-decisiones.md`, `loteFA2-decisiones.md`) y **F-A3 hecho (2026-10-12)**: «Usuarios de portal» de la ficha del cliente (invitar, reenviar, suspender, reactivar, dar de baja, volver a invitar; `portalusers.manage` + módulo `CLIENT_PORTAL`) y pestaña «Cotizador» de Contratos (`POST /billing/contract-rate-quote`, `contracts.read`) — decisiones en `docs/frontend/loteFA3-decisiones.md`, manual `docs/manual/frontend/fa3-portal-cotizador.md`. Las rutas `/portal/*` (portal del cliente) siguen en P10                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| F-B  | Consignatarios y localizaciones (incl. importación CSV por plantilla)                                                         | `LocationsController`, `ConsigneeImport*Controller`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     | **Hecho (Lotes 38 y F-B, 2026-10-11)**: importación CSV de consignatarios con backend propio (`docs/lote38-decisiones.md`, `docs/frontend/loteFB-decisiones.md`)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| F-B2 | Importación de consignatarios mejorada (pedido de Luis) | `ConsigneeImportController` (`inspect-file`), `ConsigneeImportTemplatesController`, `LocationsController` | **Hecho (Lote 45 y F-B2, 2026-10-13)**: Excel .xlsx con hoja elegible, CSV UTF-8 o Windows-1252, columnas «(No usar)» guardadas, una columna a varios campos y campos con varias columnas unidas (orden y separador), cliente por defecto guardado en la plantilla (la fila con su cliente manda), persona de contacto del consignatario, teléfonos mezclados y ZIP con ceros (`docs/lote45-decisiones.md`, `docs/frontend/loteFB2-decisiones.md`). Sigue: catálogo de ZIP e inferencia pueblo/estado; las dos disposiciones de columnas en un mismo archivo (pendiente de Luis) |
| F-C  | Órdenes: **alta** (entrada rápida, detallada, entrega especial), edición, cotizar/confirmar/cancelar, importación, Expediente | `OrdersController`, `OrderStatusController`, `OrderImportController`, `SpecialDeliveryController`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | **F-C1 hecho (Lotes 39 y F-C1, 2026-10-12)**: lista con filtros y filas por paquete, alta detallada (borrador o confirmar), entrega especial sin chofer, eliminación y permiso `orders.view_price`. **F-C2 hecho (2026-10-12)**: expediente de la orden (edición en el Detalle, cotizar, confirmar con crédito, re-cotizar, cancelar, eliminar, laterales y regreso, asignar chofer, COD, campos personalizados, contactos). **F-C3 hecho (Lote 40 y F-C3, 2026-10-12)**: entrada rápida (rejilla, una orden por fila, recuerda Rápida/Detallada) e importación CSV de órdenes (plantillas, vista previa, opciones de confirmación, descartar, resultado) → **F-C completo**. Siguen F-D… y los de etiquetas, POD y documentos (F-G)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| F-D  | Choferes y Flota (vehículos, documentos, mantenimiento, combustible, tarifas del chofer y política de pago)                   | `DriversController`, `FleetController`, `VehiclesController`, `Maintenance*`, `FuelLogs`, `DriverRates`, `DriverPayPolicy`, `DriverTrips` **F-D1 hecho (2026-10-12)**: Choferes y tarifas (lista y ficha, alta, edición, Activo/Disponibilidad/Eliminar, licencias y certificaciones con chip de vencimiento, tarifas por entrega, intento y viaje con vigencia e historial, fórmula de pago con vista previa, viajes pagados, dispositivos, contactos, campos personalizados). **F-D2 hecho (2026-10-12)**: Flota y mantenimiento (vehículos con lista y ficha, alta, edición con `rowVersion`, Activo / Disponibilidad / baja definitiva, documentos con chip de vencimiento y reemplazados, Documentos por vencer, Zonas de despacho y Disponibilidad con sus motivos; el servidor ya no deja editar un vehículo dado de baja) — decisiones en `docs/frontend/loteFD2-decisiones.md`. **F-D3 hecho (2026-10-12)**: Mantenimiento (estado de vencimiento por vehículo, programas con alta, edición y desactivar), Órdenes de trabajo (lista completa con orden sobre todo lo filtrado, alta, ficha con tareas y costos calculados, Iniciar / Cerrar con odómetro / Cancelar con efecto en el vehículo) y Combustible (bitácora paginada por el servidor con Km/L y Costo/km, resumen por vehículo, odómetro monótono); el servidor ahora devuelve labor y partes en la lista de órdenes y rechaza con 400 una orden sin vehículo — decisiones en `docs/frontend/loteFD3-decisiones.md`. **F-D completo**; siguen F-E, F-F, F-A3 y F-G |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| F-E  | Despacho (planificar el día, rutas, optimizar, asignar), Monitoreo, Escaneo (salida)                                          | `Trip*Controller`, `ScanController`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     | **F-E2a hecho (2026-10-12)**: Sala de despacho sin mapa (`/ops/dispatch`: planificar el día, nueva ruta, reasignar por zona, órdenes sin asignar y añadir, ruta activa con chofer/vehículo, ▲ ▼, optimizar, despachar con bloqueantes/avisos/lote, iniciar, cerrar, eliminar) — decisiones en `docs/frontend/loteFE2a-decisiones.md`, manual `docs/manual/frontend/fe2a-despacho.md`. **F-E2b hecho (2026-10-12)**: plano de ruta esquemático en SVG propio (`RouteMap`, `kernel/ui`: pines exacto/aproximado/manual, estados, sin ubicación, punto vivo opcional) integrado en la ruta activa con selección pin↔fila, reordenar arrastrando el asa (Pointer Events, con vuelta atrás) y pin manual (`PUT …/stops/{id}/location`) — decisiones en `docs/frontend/loteFE2b-decisiones.md`; el manual está en la misma `fe2a-despacho.md`. **F-E3 hecho (2026-10-12)**: Monitoreo de ruta (`/ops/monitor`, solo lectura: insignia EN VIVO / Sin señal de choferes, río de totales reales, lista con búsqueda, zona e Incluir completadas, ruta elegida con `RouteMap`, velocidad, rumbo, último ping y paradas, refresco cada 30 s con Actualizar y Siguiendo en vivo, enlace a la Sala de despacho) — decisiones en `docs/frontend/loteFE3-decisiones.md`, manual `docs/manual/frontend/fe3-monitoreo.md`. **F-E completo**; siguen F-F, F-A3 y F-G. **F-E1 hecho (2026-10-12)**: Estación de escaneo de salida (`/ops/scan`: lector o teclado, resultado con mensaje del servidor y qué hacer, voz, medidor, lista de la sesión, pendientes con filtros, ventana de la orden) — decisiones en `docs/frontend/loteFE1-decisiones.md`. **Falta F-E3 (Monitoreo)**. Falta backend de cierre de trip y eventos por parada (ver P7) |
| F-F  | Vistas y Campos personalizados                                                                                                | `/analytics/*` (vistas, informes), `ExtensibilityControllers`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           | **F-F1 hecho (2026-10-12)**: Campos personalizados (`/analytics/custom-fields`: entidades con contador, tabla con Incluir inactivos, alta y edición de los 8 tipos con clave, etiquetas es/en, validación amigable, valor por defecto, lista de valores con Nueva lista…, Desactivar y Reactivar); el servidor ahora rechaza valores y definiciones de entidades sin resolver y listas que son dominios de estatus — decisiones en `docs/frontend/loteFF1-decisiones.md`, manual `docs/manual/frontend/ff1-campos-personalizados.md`. **F-F2 hecho (2026-10-12)**: Vistas e informes (`/analytics/reports`: galería por fuente, resultado con rango de fechas, búsqueda, orden, totales y Exportar, constructor con fuentes combinadas, campos personalizados, 16 operadores, agrupar con agregados y totales, orden y compartir; servidor: el nombre de una vista eliminada se puede reutilizar, renombrar a un nombre repetido da 409 y el tipo de gráfico se guarda) — decisiones en `docs/frontend/loteFF2-decisiones.md`, manual `docs/manual/frontend/ff2-vistas.md`. **F-F completo**; la entrega programada de informes por correo sigue en P2                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| F-G  | Impresoras y labels                                                                                                           | `PrintersController`, `LabelTemplatesController`, `PrintingController` (Lote 43)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        | **F-G1 hecho (Lote 43 y F-G1, 2026-10-12)**: impresoras de la compañía (uso, tamaño, conexión directa con **QZ Tray** o PDF del navegador, nombre en el sistema, girar, predeterminada), plantillas de label (9 elementos de la maqueta; código de barras = **número de cada paquete**), opciones por estación en el navegador, motor del label (PDF del tamaño exacto, vista previa igual), firma propia para QZ Tray (`/printing/qz/*`), ajuste de compañía «Imprimir labels directo a la impresora», bandera del cliente «Imprimir la factura automáticamente» (solo la bandera) y permiso `printers.manage` — decisiones en `docs/lote43-decisiones.md` y `docs/frontend/loteFG1-decisiones.md`, manual `docs/manual/12-impresoras-y-labels.md` y `docs/manual/frontend/fg1-impresoras.md`. **F-G2 hecho (Lote 44 y F-G2, 2026-10-12)**: labels desde Órdenes (uno por pieza con el número de su paquete; marcadas, todo lo filtrado, ficha y «Label al guardar» en la entrada rápida y detallada; un fallo nunca deshace la orden), waybill summary y packing list en carta, informe de faltantes en Escaneo, y el escaneo y `/orders/lookup` encuentran la orden por número de paquete (índice `IX_CargoLine_PackageNumber`) — decisiones en `docs/lote44-decisiones.md` y `docs/frontend/loteFG2-decisiones.md`, manual `docs/manual/frontend/fg2-labels-y-documentos.md` y capítulo 12 §10. **F-G completo** (falta probar en una Zebra física con QZ Tray, a cargo de Luis)                                                                                                                                                                                                                                           |

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

### P4-T — Caso real que arma P4: órdenes de Truss por correo (diseño aprobado por el dueño, 2026-10-12)

> **Corrección de rumbo (Luis, 2026-10-12): la entrada de Truss es una INTEGRACIÓN, no una función de órdenes de venta.** La lectura del correo, los alias, el acuse y la
> exportación a QuickBooks que describe esta sección **se definen en el módulo de Integraciones** (P11), con Truss como la **primera integración** que lo usa. Cuando lleguemos a ese
> punto hay que **definir primero una estructura general para integraciones por documento**, con un **tipo de entrada** por integración: **subida manual** (archivo .eml/PDF/CSV),
> **lectura de un buzón de correo** (buzón compartido), **Server Post / webhook** directo, y los que se agreguen después; cada tipo con su configuración, su bandeja de
> «entrantes» (Lista / Por revisar), su regla de idempotencia (p. ej. `Reference #`) y su acuse. P4 aporta solo lo suyo (kits, orden de venta S1, despacho contra orden y
> exportación); la capa de integración alimenta a la orden de venta, no al revés. El resto de esta sección sigue siendo el diseño del caso, a reubicar al definir esa estructura.


**Por qué aquí.** Advance Logistics maneja el almacén de Truss (cliente 3PL). Hoy: el representante de Truss llena un formulario
(Formsite) → llega un correo a `drivera@`, `yquintana@` y `almacen@advancelogisticspr.com` (reenviado por el representante) → el personal
lo entra al QuickBooks **Desktop** de Truss y luego crea la orden en Teikem a mano. Meta: Teikem lee ese correo, propone la orden, el usuario
la confirma, se despacha (entrega o recogida) y al final se exporta la factura a QuickBooks sin duplicar. Es el primer caso real de P4: se
construye P4 alrededor de él. Correo de muestra analizado: notificación de Formsite con bloque fijo «Order Summary» (descripción, cantidad,
monto), `Reference #` único, nombre, teléfono, correo, salón, dirección, pueblo, ZIP, «Entrega o Recogida», opción COD (ATH), IVU 11.5 %,
representante, más una nota libre del representante en el reenvío.

**Decisiones del dueño (2026-10-12)**
- **Entrada:** NO leer buzones personales ni grupos (un grupo de distribución no es buzón). Se usa un **buzón compartido** de Microsoft 365
  (p. ej. `ordenes@advancelogisticspr.com`, sin licencia extra) que Teikem lee con permiso limitado a ese buzón; lo llenan Truss (agregándolo
  al «EMAIL TO» del formulario) o una regla de Outlook de Dalyshamel («asunto contiene “PR Orden” → reenviar»). El filtro corre **en el servidor
  de correo** (asunto / bloque «Reference #»), nunca se bajan todos los correos. Ojo: lo que llega viene reenviado por el representante, no «de
  Formsite»: filtrar por asunto/contenido, no por remitente. Respaldo desde el día uno: subir el .eml/PDF a mano desde Integraciones. Mejor
  opción si Truss lo permite: Formsite «Server Post» directo a una URL de Teikem (confirmar plan de Formsite).
- **Lectura determinista** del bloque de Formsite (no hace falta IA para la tabla). IA solo para: la nota libre (urgente / recoge hoy / paga
  allí), sugerir candidatos para líneas no encontradas, y resistir un cambio de formato (con aviso para revisar). El contenido del correo es
  entrada NO confiable: nunca se ejecuta lo que diga.
- **Cruce exacto** contra una tabla de **alias por cliente** (texto de Truss → producto de Truss en Teikem). Todo encontrado → «Lista»;
  algo sin encontrar → «Por revisar» + aviso al usuario de Teikem (capa J, canal APP/EMAIL); en la pantalla la IA sugiere candidatos y, al
  escoger, el alias se aprende. `Reference #` = clave de idempotencia (repetido → se ignora y se avisa). Los datos del salón crean o reusan el
  consignatario de Truss («Nuevo cliente?»).
- **Confirmación:** al confirmar el usuario de Teikem nace la **orden de venta** (S1: pedido/disponible/faltante). Desde ella: «Recolectar»
  abre Recolección con el detalle puesto; **entrega o recogida se decide en el proceso** (el formulario trae lo que pidió el cliente, el usuario
  lo puede cambiar): entrega → empacar y a las rutas; recogida → despacho manual «Retiro del cliente» (`CUSTOMER_PICKUP`) **enlazado a la
  orden**. Aviso en la app/web «orden lista para despachar».
- **Acuse al representante solo cuando todo cuadró** (al confirmar), correo «Orden #<Reference> confirmada» a quien envió (Reply-To), con
  copia a `admin@trusspuertorico.com` si ese buzón sirve. Mientras está «por revisar» no sale nada.
- **Montos e IVU tal cual vienen** del formulario: Teikem no pone precios al catálogo de Truss; las «ofertas» (p. ej. `Y-OFFER01`: 6
  decolorantes a $148.50 repartidos por línea) son precio, no composición. El IVU va como una línea más del archivo; se da por hecho que
  QuickBooks Desktop no lo recalcula al importar (confirmar en la prueba).
- **Exportación a QuickBooks Desktop (IIF), sin duplicar:** Teikem asigna el número de factura según la numeración del cliente (ficha F-A1:
  «¿Quién asigna el número de factura?»): recomendado = el `Reference #` de Formsite. «Exportar a QuickBooks» (en Recolección y empaque /
  órdenes) toma solo las **despachadas y no exportadas**, genera un **lote** con su archivo y las pasa a «Exportada, por confirmar»; la misma
  pantalla lista las pendientes; el usuario marca «Importadas» (o «Fallidas» → vuelven a exportable); un lote se puede volver a bajar sin
  cambiar estado. Es un estado + una lista, no un job; opcional: recordatorio diario (capa J) «N facturas exportadas sin confirmar». El
  archivo puede crear el cliente (salón) y los ítems si no existen. **Obligatorio probar contra una copia del QuickBooks de Truss** antes
  de usarlo.
- **Kits (los arman al pedir, nunca de antemano):** tipo de producto «Kit» (catálogo, no string) + tabla `ProductKitComponent`
  (kit → componente → cantidad). El kit **no tiene existencia ni movimientos propios**: todo pasa en los componentes. Al recolectar la línea
  «kit × n» se **explota** en una línea por componente (cantidad × n) con su posición por FEFO; Kárdex con una salida por componente y nota
  «<kit> × n · EMP-…»; costo del kit = suma de los costos congelados de los componentes; disponibilidad del kit = mín(disponible del
  componente ÷ cantidad) («se pueden armar N»). Ficha del producto: al marcar Kit se ocultan posiciones/lote/serie/mínimo/existencia y aparece
  el panel «Componentes» (misma tabla que tarifas); lista con chip «Kit». Orden de venta: la línea es el kit con su precio y debajo, desplegable,
  los componentes con pedido/disponible/faltante. Factura: una sola línea (QuickBooks Desktop «Group item»). Conteo: los kits no se cuentan.
  Definición del catálogo = default; al confirmar la orden se guarda una **foto** de los componentes de esa línea; kit «ajustable al pedir» permite
  cambiar piezas en esa orden sin tocar el catálogo. La app puede ofrecer kits después (se escogen de la lista y se explotan igual).

**Orden de construcción propuesto:** (1) kits en el catálogo (entidad, tabla, ficha, disponibilidad, explosión al recolectar) → (2) S1
orden de venta → (3) bandeja de órdenes entrantes + alias + lectura del correo (buzón compartido / subida manual) + acuse → (4) despacho
contra orden (S2) con entrega o recogida → (5) exportación IIF por lotes con confirmación → (6) S3/S4. Pantallas en Integraciones
(conector de correo) y en Clientes (alias de productos del cliente).

**Pendiente de averiguar (dueño):** si ese QuickBooks Desktop acepta el número de factura que mande Teikem o pone el suyo (si pone el
suyo: exportar sin número y capturar el de QuickBooks al confirmar el lote); si Truss agrega el buzón compartido o se usa la regla de
Outlook; si el plan de Formsite permite «Server Post»; obtener una copia del QuickBooks de Truss para la prueba del IIF.

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

> **Se deja para lo ÚLTIMO de todo (decisión del dueño, 2026-10-15).** Va después de P13.

### Decisiones de Luis (2026-10-18) que cambian el alcance de P7
- **HECHO (Lote 118, 2026-10-18): registrar la entrega desde la web** con el permiso `orders.deliver`, la firma automática del usuario, el comprobante y la ubicación con Google (asíncrona, un intento, 30 s). Ver `docs/lote118-decisiones.md` y el capítulo 36 del manual. Falta: fotos (D10), el comprobante en el portal del cliente (P10) y lo de la app del chofer.
- **Registrar la entrega desde la web (personal), sin depender de la app del chofer.** Pantalla web con **permiso nuevo** («registrar entrega»). Si la orden exige firma, la firma va **automática a nombre del usuario que registra** (no del cliente) y el comprobante dice «registrada por [usuario], sin firma del consignatario», visible para el cliente. Con esto el POD y los documentos de entrega **ya no dependen de la app del chofer** (la app los alimentará después con el mismo modelo). *(Decidido por Luis, 2026-10-18: el permiso lo tiene **quien Luis designe**, sin rol por omisión. El comprobante trae fecha y hora, quién recibió (nombre escrito), comentario opcional, fotos opcionales, la firma automática del usuario y el aviso visible «registrada por [usuario]».)*
- **Geolocalización de esa entrega:** si no trae ubicación, se geocodifica la dirección con **Google (Geocoding API)**. **Un solo intento, asíncrono** (el usuario no espera ni se bloquea), **tiempo máximo 30 s**; si no se logra, la entrega queda **sin geocodificar** y se registra igual. Referencia de cómo hacerlo: `F:\Visual Studio 2022\Projects\RentComparable` (`api/Services/GoogleGeocoder.cs`: `GET …/geocode/json?address=…&components=country:PR&key=…`, confianza por `location_type` ROOFTOP/RANGE_INTERPOLATED/GEOMETRIC_CENTER/APPROXIMATE con mínimo configurable, rechazo de coordenadas fuera de Puerto Rico —lat 17.8–18.7, lng −68.2 a −65.1—, caché en tabla `GeocodeCache`). La llave va en variable de entorno (`GoogleMaps__ApiKey`), nunca en el repositorio. Prioridad de coordenadas: las de la entrega → las del consignatario → geocodificar.
- **App de choferes:** debe **verse exactamente igual** a la app que Luis usa hoy: `F:\Visual Studio 2022\Projects\Colavora_Courier\Colavora New Project\colavora` (Expo ~56 / React Native 0.85 / expo-router, nativa y web). Solo cambia el API al que llama. Hallazgos: pantallas Login → Home (4 tarjetas: entregas de hoy, manifiestos de entrega, recogidas de hoy, manifiestos de recogida) → Manifiestos → Lista de órdenes (buscador, escáner de código de barras, filtros) → Detalle con pestañas Detalles/Piezas/Comentarios/Acciones → **Completar** (quién recibe, comentario, COD, foto y firma cuando se exigen, GPS) o **Excepción** (motivo del catálogo, comentario, foto) → Perfil (sincronizar, autosync, biometría). Trabaja sin señal con una cola SQLite en orden y `Idempotency-Key` (igual que `app-almacen`). Para apuntarla a Teikem: la URL base es una sola variable (`EXPO_PUBLIC_BASE_URL`) y el transporte está poco acoplado, pero **sí está acoplada a la forma de los datos** (`src/types/models.ts`, listas de IDs + un GET por orden, estatus «Created» + `assignPK`) y a **Auth0**: plan = capa adaptadora que convierta las respuestas de Teikem a esos tipos, reescribir `services/custom-api/custom-api.ts`, `offline/mutations.ts` y `services/auth/*`.
- **Diferencias a construir:** la app **no tiene «Recoger» ni «En tránsito»** (el flujo de Luis: el chofer escoge); hay que agregarlos. Hoy usa mapa OpenStreetMap/Leaflet y Nominatim (no Google) y no tiene notificaciones push.
- **Rastreo de posición:** la app de choferes **debe mandar su posición periódicamente** (hoy solo toma una posición puntual al completar la entrega y no hay rastreo de ruta). **Ese rastreo y el seguimiento en vivo se dejan para lo ÚLTIMO de todo** (Luis, 2026-10-18); no es prioridad que el cliente lo vea.

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

> **Regla del dueño (2026-10-18): TODOS los usuarios —de portal, de consignatario y de staff— los controlan el administrador de la plataforma (todas las compañías) y el administrador de la compañía (los de su compañía).** Controlar = verlos, invitarlos o crearlos, suspenderlos, reactivarlos, darlos de baja y restablecer su acceso. Cada pantalla o endpoint nuevo de usuarios debe cumplirlo y toda acción queda en la bitácora de seguridad con quién la hizo. **Cumplida (Lote 121):** el personal en Roles y usuarios → Usuarios; las personas de los clientes y los accesos de consignatarios en las pestañas Clientes y Consignatarios (lista de toda la compañía, con tipo, cliente y acciones); la sesión de quien se suspende o se da de baja se corta al instante. El administrador de la plataforma trabaja dentro de la compañía que tenga activa.

> **Se deja para lo ÚLTIMO de todo (decisión del dueño, 2026-10-15).** Va después de P13. Recordatorio: `messages.use` solo se asigna a los gerenciales del cliente y un usuario de cliente solo escribe a quien se designe (ver `docs/lote53-decisiones.md`).
- Hoy `AuthService.cs:108` rechaza el login de usuarios de portal. Construir: login separado (`UserKind = portal`), claim `cid` (ClientId) en el principal, `ITenantContext` con cliente, **segundo filtro global por `ClientId`** (BOLA), invitación de un solo uso, política de contraseña NIST 800-63B (12 caracteres, sin rotación, HIBP, Argon2id), MFA por política, lockout, sin enumeración de usuarios, CAPTCHA tras N intentos, cabeceras (CSP, HSTS).
- **HECHO (Lote 120, 2026-10-18):** mi perfil (nombre editable; correo, rol y empresa en solo lectura), cambio de contraseña desde la app, indicadores del cliente (7/30/90 días) y los mensajes de contraseña en el idioma de la persona. Ver `docs/lote120-decisiones.md`. **Con esto P10 queda completo salvo los documentos adjuntos y el seguimiento en vivo.**
- **HECHO (Lote 119, 2026-10-18):** comprobante de entrega en el portal, facturas de solo lectura, «Mis avisos» (preferencias por persona) y la lista de consignatarios rotulada. Ver `docs/lote119-decisiones.md` y el capítulo 37 del manual. **Falta de P10:** configuración, perfil e indicadores del cliente; documentos adjuntos (archivos); seguimiento en vivo (lo último).
- **Pendiente de P10 tras el Lote 117 (aclaración de Luis, 2026-10-18):** no todo depende de P7. Se hacen ya: **lista de consignatarios del cliente**; **preferencias de aviso del portal** (Luis espera que existan, aunque vacías, con una pantalla para configurarlas: verificar qué hay de `PortalNotificationPref`); **facturas en la app del cliente** (se muestran, **no se marcan como pagadas**); configuración, perfil e indicadores del cliente que no necesiten POD. El «ajustes» del Lote 102 en la app del cliente es idioma, tema y huella, no las preferencias de aviso (verificar). **Documentos y comprobante de entrega:** llegan con «registrar la entrega desde la web» (ver P7). **Solo el seguimiento en vivo espera al rastreo (lo último de todo).**
- Endpoints/pantallas: inicio, seguimiento en vivo (necesita pings, P7), documentos y POD, entrada de órdenes (mismo componente con el cliente fijo), consignatarios, configuración, perfil, indicadores del cliente, `PortalNotificationPref` (tabla sin entidad).
- Es un sitio web aparte (otro dominio/login). **Decidir D14:** mismo proyecto `web-app` con otra entrada, o proyecto separado.
**Verificar:** OWASP ASVS nivel 2 como lista de aceptación; pruebas de acceso cruzado entre clientes del mismo tenant (cambiar ids en cada endpoint); un usuario de portal jamás entra a la app interna ni al revés; eventos a `SecurityEvent`; acciones de administradores de plataforma atribuidas a ellos.

## P11 — API de integraciones (13)

### Decisiones de Luis (2026-10-18)
- **API mínima y controlada, no un libro abierto.** Más lecturas que escrituras; la escritura se limita a **crear órdenes** (lo mismo que hace la pantalla) y poco más. Luis analiza qué se expone y cómo. Propuesta de partida: lecturas de órdenes, estatus y seguimiento, y facturas; escritura solo crear órdenes; llave por cliente que solo ve lo suyo, con límite de uso; y la lista de procesos que se pueden disparar. *(Pendiente: lista final de endpoints.)*
- **Integraciones al estilo de Ícaro** (`F:\Visual Studio 2022\Projects\Icaro_Main`), con la diferencia de que **una de las dos fuentes es Teikem**: tipos de comunicación (archivo, SFTP/FTP, carpeta, API, correo, webhook a futuro) y poder definir qué se hace **sin programar cada vez**. Se toman los **conceptos**, no el código (Ícaro tiene el movimiento de datos a medias: operador de archivo esqueleto, sin mapeo, sin correo ni SFTP, credenciales en claro). Modelo propuesto: integración = **conector** (credenciales cifradas y separadas) + **disparador** (horario con zona horaria, vigilar carpeta/SFTP, manual, webhook) + **pasos ordenados** (leer/escribir); formato de archivo declarativo (delimitador, codificación, encabezado, hoja de Excel); **mapeo columna → campo de Teikem** con tipo, requerido, clave de cruce, valor por defecto y transformaciones simples; bitácora por corrida y por paso (leídas, creadas, actualizadas, rechazadas, error por fila); reintentos configurables, una sola corrida a la vez, aviso por correo al fallar; registro de archivos ya procesados (nombre, fecha, hash); activo/inactivo y bloqueo de edición.
- **Requisito (Luis, 2026-10-18): la integración de Teikem debe poder integrar CUALQUIER cosa con Teikem, de entrada o de salida** (archivos, SFTP/FTP, API, correo, base de datos, lo que se agregue). AxisCare (carpeta `AxisCareDelivery`) era solo un **ejemplo** para ilustrar el concepto: lo que su integración hace hoy (CSV de órdenes por SFTP más un Excel de respuestas) sirve de ejemplo de configuración, no de alcance. Cada integración se define por configuración, no por programación.
`ApiCredential` (client_id/secret → token con scopes y rate limit por credencial), `WebhookSubscription` y `WebhookDelivery` (HMAC, reintentos con backoff), conectores ERP/e-commerce/transportistas, pantalla Integraciones (registro de plantillas + crear API key; hoy sin backend). Reutiliza el outbox de P2.1.
**Verificar:** el secreto se muestra una sola vez y se guarda hasheado; scopes; idempotencia; entregas reintentadas; rate limit.

**Integraciones por documento (primer caso: Truss, ver P4-T).** Estructura general: una integración = cliente/tenant + **tipo de entrada** (subida manual, buzón de correo, Server Post/webhook, …) +
reglas de lectura + alias/mapeos + bandeja de entrantes + acuse; las órdenes de venta (P4) son quien recibe lo que la integración confirma. Definir en el diseño de P11 antes de construir P4-T.

## P12 — Dashboards y Pulso completos (12)
`KpiDailySnapshot` + job nocturno (P2.2); ríos de «paquetes en la calle» y «dinero COD»; Radar de órdenes abiertas; avisos sin chofer/COD/SLA (**SLA en riesgo hecho en el Lote 122**); indicadores de COD, facturación, ventas y SLA (el SLA ya se puede medir: campos `SlaState`… en la fuente de órdenes); Actividad para Operación y Contabilidad (hoy solo Almacén); alertas operacionales fuera de almacén; programación de informes por correo. Kemi AI: **no verificado**, decidir si sigue en alcance.

## P13 — App móvil para staff (idea del dueño, 2026-10-15)

> ## ⛔ ANTES DE EMPEZAR NADA: SENTARNOS A DISEÑARLO CON LUIS
> Esta fase **no se construye** hasta que Luis y yo nos sentemos a diseñarla juntos (pantallas, si es una app nueva o parte de `app-almacen`, proveedor de push, entrada a la web, qué estadísticas, qué enlaces). Primero el diseño presentado y aprobado; después el código.

**Qué quiere el dueño:** una app para el **personal (staff)**, liviana, con **notificaciones push** que alimenten lo ya construido:
- **Mensajería** (hilos entre dos y grupos) y **avisos** (reglas y manuales) con notificación push cuando llegan y la persona no tiene la app abierta (hoy llegan en vivo por SignalR a la web).
- Ver **resultados y estadísticas** (indicadores y gráficos de lo que la persona tenga permitido; el Pulso).
- **Enlaces que lleven a la aplicación web** (órdenes, rutas, etc.) y, al revés, enlaces de la web que abran la app en la pantalla correcta (p. ej. un hilo).
- **Entrada a la web sin volver a iniciar sesión** estando conectado en la app (ver el patrón de seguridad abajo).

**Lo que ya existe y se reutiliza:** el API de mensajería y avisos (Lotes 51–54), la conexión en vivo (`/hubs/messages`), el núcleo de `app-almacen` (aparato registrado, sesión por aparato, PIN, motor de sync), el permiso por rol (`messages.use`, `notifications.send`…) y la bitácora de seguridad.

**Decisiones por tomar al diseñarla:**
- **Push:** hoy no hay proveedor (D16/D13 abiertas). Para una app Expo lo más simple es el **servicio de push de Expo** (reparte a Android e iPhone); requiere cuenta y credenciales que configura el dueño (nunca en el repositorio). Alternativa: FCM/APNs directo. Contenido del push: **sin datos sensibles** (solo «tienes un mensaje de Ana»; el detalle se pide al abrir).
- **App nueva o parte de `app-almacen`:** otro paquete con el mismo núcleo, o un modo «staff» dentro de la misma app según el permiso de la persona.
- **Qué estadísticas** y con qué permisos (mismas fuentes de datos que el Pulso y los indicadores).
- **Enlaces profundos** (universal links / app links) y qué pasa si la app no está instalada.

**Entrada a la web sin volver a iniciar sesión — es posible sin abrir una brecha de seguridad, con este patrón (código de traspaso):**
1. La app, autenticada, pide al servidor `POST /auth/web-handoff`: devuelve un **código de un solo uso**, aleatorio, de vida corta (~60 s), **atado a la persona, la compañía y el aparato registrado**; el servidor guarda solo su **huella (hash)**.
2. La app abre el **navegador del sistema** (no una vista embebida) en la web con ese código.
3. La web lo **canjea por una sesión normal** con una llamada segura (POST) y **borra el código de la dirección** al instante (`history.replaceState`, sin `Referer`). Nunca viajan tokens de acceso ni de renovación por la URL.
- Reglas: un solo uso; vence solo; si el aparato se desactiva o la persona cierra sesión, el código muere; **mantiene el nivel de verificación (MFA/AAL)** de la sesión de la app y las acciones sensibles siguen pidiendo el paso extra; límite de intentos; **cada traspaso queda en la bitácora de seguridad**; solo para personal (nunca portal de clientes); el administrador puede apagarlo por compañía.
**Verificar:** código usado dos veces falla; código vencido falla; código de otro aparato/persona/compañía falla; sin tokens en la URL ni en los registros; revocar el aparato lo invalida; push sin datos sensibles.

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

## Decisiones de F-A2 (2026-10-10)
- Contratos múltiples: la pestaña Contrato edita el vigente, con selector de contrato y botón «Nuevo contrato».
- Contrato: se muestran todos los campos (título, número, fechas, renovación automática, moneda, disparador de cobro, notas).
- SLA: un renglón por tipo de servicio con horas máximas, ventana de recogido, meta de puntualidad y penalidad; empieza vacío.
- **Cotización (`POST /api/v1/billing/contract-rate-quote`): fuera de F-A2. PENDIENTE para cuando se construyan las órdenes de venta (P4): ahí se hace el panel «Simular cotización» / uso en la orden.** *(2026-10-12: el «Cotizador» del cliente —pestaña de Contratos— se hizo en F-A3; el uso dentro de la orden sigue en P4.)*
- Por defecto (sin objeción del dueño): el SLA se captura solo en la pestaña SLA (no en el alta del cliente); tarifas nuevas con modal «Vigente desde» (hoy por omisión); tipos de servicio especial: alta con «+ Nuevo tipo…», sin gestión de baja/reactivación; el recorrido Playwright pendiente de F-A1 se cierra en F-A2.

## IA en Teikem (análisis 2026-10-12; para trabajar más adelante)

**Kemi, decisión de Luis (2026-10-18):** a futuro trabajará con IA; **por ahora sin IA**: Kemi **busca en el manual y la FAQ y trae las opciones**, sin que el usuario vea de dónde salen; si la persona no queda satisfecha, **pregunta si le contestó** y busca por otro lado. Otra opción de Kemi: **«Pregúntame cómo usar el sistema»**; eventualmente ayudará a **hacer operaciones** en la aplicación (por el API normal, con permiso y confirmación). **Para la demostración:** la pregunta aparece en la interfaz de Kemi y, aunque no haga nada todavía, al hacer clic responde «en construcción». *(Pendiente: dónde vive el buscador, qué pasa si no hay resultado, idioma es/en; proveedor de IA y quién paga, para la etapa con IA.)*
La maqueta llama **Kemi** al asistente (hoy solo demo). Dónde aporta una IA real, de mayor a menor valor/riesgo:
1. **Kemi de solo lectura**: la pregunta se traduce a las consultas que ya existen (vistas, indicadores, `IDataSource`) con los permisos del usuario; nunca cifras de memoria.
2. **Soporte sobre el manual y la FAQ** (mensajes de error exactos ya documentados).
3. **Captura desde documentos** (correo, factura de proveedor, ASN, foto de etiqueta): la IA propone, el usuario confirma.
4. **Excepciones explicadas**: resumen del día, varianzas de conteo, cuadre COD, ajustes raros.
5. Exportación contable (QuickBooks) y avisos redactados (capa J). 6. Voz en la app.
No usar un LLM para rutas, posiciones ni reabasto (algoritmos clásicos).
**Reglas**: la IA no escribe sola a Kárdex ni saldos; propone → el usuario confirma → acción por el API normal (permiso + `TenantId` del principal) con auditoría; un asistente por compañía; los datos de correos/documentos son entrada NO confiable (inyección de instrucciones).

### Procesos manuales de Advance a automatizar (pedido del dueño, 2026-10-12)
- **A. COD por lotes**: hoy escanean el código de barras (= número de orden) de cada factura/hoja y la app marca la orden como procesada. Meta: subir de una vez todos los documentos escaneados (PDF/imágenes); el sistema lee los códigos (técnica determinista; IA solo para lo ilegible/manuscrito), busca cada orden y la marca; al final del día un **reporte**: procesadas, no encontradas, duplicadas, ya procesadas, ilegibles, diferencias de monto, y por qué.
- **B. Órdenes de Trust por correo**: el vendedor de Trust manda la orden por email; el personal la entra al QuickBooks de Trust y luego crea la orden en Teikem. Meta: Teikem tiene el inventario de Trust; el sistema lee el correo/documento, deduce los productos y cantidades (con alias del catálogo), **sugiere** la orden al usuario (con confianza y lo que no encontró) y, al final del día, **genera un archivo para crear las facturas en QuickBooks** (el QuickBooks de Trust no es controlable: se sube a mano).
- **C. Entregas marcadas por documento** (Yesenia): le mandan documentos de órdenes y ella marca las órdenes como entregadas. Meta: mismo motor que A (leer documento → ubicar la orden → proponer o marcar entregada vía `StatusService.TransitionAsync`) con reporte.
- **D. Integraciones configurables desde la sección Integraciones (sin programar cada vez)**: un «conector de entrada» = fuente (buzón de correo, carpeta, subida manual, API) + tipo de documento + plantilla de extracción (el usuario sube un ejemplo, la IA propone qué campo es cuál y dice qué no encontró; el usuario corrige y se guarda) + reglas de cruce (orden, producto/alias, cliente) + acción (marcar COD, crear orden, marcar entregada) + modo (sugerir o automático) + reporte diario. Viable: las FUENTES y las ACCIONES son un catálogo que se programa una vez; los formatos y campos los configura el usuario con ayuda de la IA.
- **Orden sugerido**: A (casi sin IA, mucho valor) → C (mismo motor) → B (necesita catálogo con alias del inventario de Trust y la exportación genérica de facturas, P5) → D (generalizar). Modo «sugerir» por defecto; «automático» solo cuando la exactitud esté medida.
- **Decisiones del dueño pendientes**: proveedor de IA y dónde viven los datos; quién paga el consumo; cómo llegan los correos (buzón dedicado/reenvío vs conexión a Microsoft 365/Google); formato de importación de facturas que acepte ese QuickBooks (Desktop IIF/CSV u Online CSV).

## Pendientes de calidad (a hacer cuando Luis lo diga; anotados 2026-10-18)
1. **Fallas intermitentes de Playwright** (en la última corrida limpia, 6 distintas que cambian en cada corrida; cada una pasa sola). Investigarlas una por una: ¿API o recorridos que comparten datos de la misma compañía (rutas por zona, Pulso, empaque, avisos)? Aislar el recorrido o corregir el código.
2. **`scripts/smoke.sh`** (5.449 líneas, flujo de estatus anterior al Lote 106). Su paso del CI saldría en rojo. Arreglarlo o reemplazarlo en el CI por uno corto y vigente (recomendación: reemplazarlo).
3. **Recorridos en móvil:** repasarlos uno por uno.
4. **Cierre:** corrida completa final sobre base limpia; dejar apagados el API y la base de pruebas.
5. Otros: la ficha de la orden no muestra el cargo por devolución.

Cómo se corre local: API con `--no-launch-profile`, `Legal__RequireAcceptance=false`, `Auth__Sessions__SingleSessionPerBrowser=false`, base limpia con `db-init` + `Diseño/logistica-db-zip-catalog.sql`, módulos encendidos con reautenticación (TAX apagado), `defaultServiceType=STANDARD` y `defaultPackageType=BOX` en los ajustes de la compañía.
