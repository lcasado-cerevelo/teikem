# Plataforma de Logística — Documento Maestro de Funcionalidades

**Versión:** Unificada (consolida v3 + Módulos 4–6 + Módulos Restantes + Seguridad/Auditoría)
**Stack:** .NET 8 / ASP.NET Core / EF Core / SQL Server · React Native + Expo (móvil) · Leaflet + OSM (mapas)
**Alcance:** 13 módulos de negocio + 5 capas transversales

Este documento es la referencia única de **qué hace** la plataforma. El detalle de **cómo está modelado** (DDL, FKs, índices) vive en los cuatro documentos de diseño y en los dos scripts SQL (`logistica-db-estructura.sql`, `logistica-db-seed.sql`). Aquí se consolida, al máximo nivel de detalle, la funcionalidad acordada en todas las sesiones.

---

## Índice

**Capas transversales**
- A. Catálogos (`CatalogDomain` / `LookupCode`)
- B. Estatus y transiciones (`StatusCode`, sin grafo)
- C. Contactos múltiples (`ContactPoint`)
- D. Seguridad (identidad, RBAC, MFA/AAL2, aislamiento multi-tenant)
- E. Auditoría (tres planos)
- F. Campos personalizados por entidad (`CustomFieldDefinition`)
- G. Informes personalizados por entidad (`ReportDefinition`)

**Módulos de negocio**
1. Clientes y contratos
2. Órdenes de transporte
3. Trips y rutas (planificación/optimización)
4. Flota, choferes y mantenimiento
5. Almacenes (WMS)
6. Cross-docking
7. Inventario y trazabilidad por lote/serie
8. App móvil para conductores
9. Prueba de entrega (POD)
10. Portal de clientes
11. Facturación y liquidación
0B. Plataforma multi-tenant y catálogo de módulos
11B. Ciclo COD (cobro contra entrega y remesa)
12. Dashboards e indicadores
13. API para integraciones
13B. Compras (módulo PURCHASING)
16C. Equipos en alquiler (módulo RENTAL_EQUIPMENT / RENTAL_BILLING)

---

# Capas transversales

## A. Catálogos — `CatalogDomain` / `LookupCode`

El concepto de catálogo es riguroso: **todo valor de clasificación que se repite** (tipos, razones, categorías, estados de cosas que no son máquinas de estado) vive en `LookupCode` con FK, nunca como string suelto ni como tabla dedicada por cada lista.

- **Dominio registrado**: `CatalogDomain` es el registro de qué dominios de catálogo existen (`Entity` + `Scope` Lookup/Status). `LookupCode.Entity` y `StatusCode.Entity` le hacen FK. `Scope` es constante de sistema (Lookup/Status) — ahí se corta la recursión infinita de "catálogo de catálogos".
- **Multilingüe**: nombre/descripción en JSON por idioma (`{"en": "...", "es": "..."}`), resuelto en runtime según el idioma del usuario. El FE nunca hardcodea etiquetas de catálogo.
- **Override por tenant**: `LookupCodeOverride` permite que un tenant renombre, reordene o deshabilite un valor del catálogo base sin tocar la semilla global. El valor base se siembra una vez; cada operadora lo ajusta.
- **Distinción maestra vs. catálogo**: una entidad maestra con hijos o atributos propios (`RateZone`, `Warehouse`, `Product`) **no** es catálogo aunque tenga Code+Name — tiene su propia tabla. Catálogo = lista de clasificación plana.
- **Patrón heredado de SEPHAS**: misma convención de `LookupCode` con discriminador `Entity`, JSON multilingüe y override por tenant que ya corre en producción.

## B. Estatus y transiciones — `StatusCode` (sin grafo de workflow)

El core lo mueven **eventos físicos** (salida, llegada, POD), no aprobaciones. Se eliminaron las tablas Workflow/WorkflowStep/WorkflowTransition. El pipeline de cada tenant es data, no código.

- **Pipeline por tenant configurable sin deploy**: las etapas son `StatusCode` activas (`StatusCodeOverride.IsEnabled`) ordenadas por `SortOrder`. Caguas opera solo delivery; otro tenant corre la cadena completa — la misma tabla, distinto subconjunto encendido.
- **Clasificación por `StageKind`**: cada estatus es PIPELINE (avance principal), LATERAL (desvío: cancelado, en espera, devuelto) o TERMINAL (cierre). El FE dibuja el flujo a partir de esto.
- **Estados laterales con punto de entrada por tenant** (`StatusLateralEntry`): desde qué etapa del pipeline se puede saltar a un lateral, ligado al modelo de contratación. Caso Caguas: cancela desde `PICKUP` porque cobra por recogido — su punto de entrada al lateral "cancelado" es distinto al de un tenant que cobra por entrega.
- **Concesiones/restricciones por estatus** (`StatusCapability`): define qué acciones permite cada estatus (editar carga, asignar a trip, reprecio, cancelar). El FE habilita/inhabilita botones consultándolo; el backend lo reaplica como guard. Es el patrón SEPHAS de capacidades por estado.
- **Dependencias duras + efectos en código**: el validador corre **al guardar la configuración** del pipeline (no en cada transición), así que un pipeline mal armado se rechaza antes de operar. Los efectos de cada transición (disparar cotización, congelar ruta, descontar inventario) viven en código.
- **Transiciones seguras**: el servicio valida etapa-activa + dependencia-dura antes de mover, registra en `EntityStatusHistory` y dispara los efectos. Sin saltos ilegales ni retrocesos que rompan trazabilidad.
- **Etiqueta y color** de cada estatus resueltos por idioma desde `StatusCode` (+override); el FE nunca hardcodea el config de estatus.
- **Dos compuertas que no se confunden**: `StatusCapability` = *regla de negocio* ("¿el estado permite la acción?"); `Permission` RBAC = *seguridad* ("¿el usuario puede?"). Ambas tienen que pasar.

## C. Contactos múltiples — `ContactPoint`

Asociación polimórfica que reemplazó todos los campos `Email`/`Phone` sueltos del modelo.

- **N contactos por entidad**, cada uno tipado (tel/móvil/email/fax/WhatsApp) con etiqueta libre ('Oficina', 'Facturación') y bandera de principal por tipo.
- **Dueño por catálogo, no por string**: se identifica vía `OwnerEntityLookupId` (catálogo `EntityType`) + `OwnerId`. Un solo punto para teléfonos/correos en toda la app: cliente, contacto, chofer, parada, almacén.
- **Validación en servicio**: `IContactPointService` valida unicidad de principal, formato (email/teléfono) y que `OwnerEntity`+`OwnerId` apunten a una entidad real del tenant activo — la integridad de la asociación polimórfica vive en servicio, no en FK declarativa.

## D. Seguridad

### Identidad y tenancy
- **ASP.NET Core Identity con llaves `int`** (`IdentityUser<int>`/`IdentityRole<int>`): no se reinventa hashing, lockout ni security stamp. Se extiende con tenancy/RBAC/MFA/auditoría. Todos los `CreatedBy`/`UpdatedBy` referencian `AspNetUsers.Id`.
- **Una identidad, múltiples tenants**: soporte/admin pueden operar varias operadoras; un usuario normal pertenece a una. El tenant activo se fija al login y queda en el JWT.
- **Tres tipos de usuario** (`UserKind`): interno (operación), portal (cliente, módulo 10), servicio (integraciones, `ApiCredential` del módulo 13). El tipo decide superficie de autenticación y scoping.
- **Estado de membresía** separado del estado del usuario: activo globalmente pero suspendido en un tenant específico.

### RBAC — roles y permisos
- **Permisos granulares** por acción (`recurso.acción`, ej. `trips.dispatch`), agrupados por categoría. **Sembrados desde código; el tenant no los edita.**
- **Roles componibles por tenant**: el admin del tenant arma roles marcando permisos. Seis plantillas de sistema (`TenantId NULL`) clonables al provisionar: Admin de tenant, Despachador, Facturación, Operador de almacén, Chofer, Solo lectura.
- **Autorización por policy**: `[RequirePermission("trips.dispatch")]` → un policy handler verifica que el `UserRole` del tenant activo incluya el permiso (cacheado por usuario+tenant).
- **Alcance de datos opcional** (`UserDataScope`): un operador limitado a su almacén, un account manager a sus clientes — filtro adicional sobre el filtro de tenant.

### MFA / AAL2 y sesiones (patrón SEPHAS)
- **Enrolamiento MFA**: TOTP con QR, SMS de respaldo, gate biométrico en la app (`expo-local-authentication`). Códigos de recuperación de un solo uso (hasheados).
- **AAL2 step-up**: acciones sensibles (aprobar facturación, cambiar permisos, exportar a contabilidad) exigen reauth reciente. El handler verifica `RefreshToken.Aal2VerifiedAtUtc` dentro de la ventana; si caducó, fuerza re-verificación. Reusa los reauth events de SEPHAS.
- **Sesiones revocables**: refresh tokens hasheados con rotación en cada uso (`ReplacedByTokenHash`), revocación por dispositivo o global. El `SecurityStamp` de Identity invalida access tokens vivos.
- **Política por tenant**: MFA obligatorio o no, ventana de reauth, expiración de sesión — configurable.

### Aislamiento multi-tenant (el espinazo)
- **`TenantId` del principal, NUNCA del request**: el tenant activo va en el JWT; un `ITenantContext` lo expone por request. Si el caller manda un `TenantId`, se ignora. Cierra el vector de "pedir datos de otro tenant cambiando un parámetro".
- **Filtro global en EF Core**: `HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)` en toda entidad con `TenantId`. Olvidarse de filtrar deja de ser posible.
- **Segundo nivel para portal**: los `PortalUser` se acotan además por `ClientId`.
- **Asociaciones polimórficas validadas**: el servicio valida que el `EntityId` pertenezca al tenant activo antes de resolver.
- **Defensa en profundidad**: filtro global (data) + chequeo de permiso (acción) + validación de pertenencia (recurso). Los tres, no uno.

## E. Auditoría — tres planos

Sobre una misma entidad, tres bitácoras que juntas dan la historia completa para disputas, cumplimiento y forense.

- **`AuditLog` — qué cambió**: un `SaveChangesInterceptor` de EF Core detecta entidades modificadas, calcula el diff campo-a-campo y escribe con el `CorrelationId` de la operación, sin que cada controlador se acuerde. Reemplaza el `WriteAsync` manual disperso de SEPHAS por algo central. Campos sensibles (secretos, hashes) excluidos por convención de mapeo.
- **`EntityStatusHistory` — cómo se movió de estado**: quién/cuándo/de qué a qué/comentario. Base para SLA, tiempo de ciclo y dashboards.
- **`SecurityEvent` — quién intentó qué**: login éxito/fallo, logout, lockout, enrolamiento/uso de MFA, reauth AAL2, cambio de contraseña, otorgar/revocar rol, **permiso denegado**, revocación de token, creación/uso de credencial de API.
- **Correlación**: el `CorrelationId` (del request/trace) une todos los cambios de una operación multi-tabla — útil cuando crear una orden toca 6 tablas.
- **Retención y exportación**: políticas por tipo (`DriverLocationPing` y `AuditLog` crecen rápido → particionado/archivo); exportación para auditorías externas.

## F. Campos personalizados por entidad — `CustomFieldDefinition`

Cada tenant define campos propios sobre las **entidades principales** sin tocar el esquema ni hacer deploy. Mismo patrón que el `FieldDefinition` de SEPHAS, generalizado por `EntityType`.

- **Definición por tenant + entidad** (`CustomFieldDefinition`): el admin del tenant agrega campos a Cliente, Orden, Trip, Vehículo, Chofer, Almacén, Producto, Contrato, Factura, Ubicación, etc. Cada campo tiene `FieldKey` estable, etiqueta multilingüe, tipo de dato, requerido/único, valor por defecto, orden y bandera `ShowInList` (candidato a columna en listados/informes).
- **Tipos de dato** (`CustomFieldDataType`): texto, número, fecha, fecha-hora, booleano, selección, multi-selección y **referencia a catálogo** (`LOOKUP_REF` apunta a un dominio de `LookupCode`, así un campo personalizado puede reusar un catálogo existente).
- **Opciones para selección** (`CustomFieldOption`): valores tipados con etiqueta multilingüe y orden, para campos SELECT/MULTISELECT.
- **Validación por DSL** (`ValidationJson`): regex, min/max, longitud, etc. — **reusa el evaluador de reglas del DSL de SEPHAS** (paridad TS/C#), así la validación corre igual en frontend y backend.
- **Almacenamiento EAV tipado** (`CustomFieldValue`): el valor se guarda en la columna por tipo (`ValueText`/`ValueNumber`/`ValueDate`/`ValueBool`), no como string genérico — así se puede **filtrar y ordenar** por campo personalizado de forma eficiente (clave para los informes). Un valor por (definición, registro).
- **Integridad en servicio**: la pertenencia del `EntityId` al tenant y el cumplimiento de requerido/único se validan en servicio (igual que las demás asociaciones polimórficas). El `EntityType` lo implica la definición, no se repite en cada valor.

## G. Informes personalizados por entidad — `ReportDefinition`

Informes guardados y reutilizables, definidos por el usuario sobre una entidad base, que combinan **campos nativos + campos personalizados** de esa entidad.

- **Definición por tenant + entidad base** (`ReportDefinition`): nombre, descripción, entidad base (`EntityType`) y la configuración del informe en JSON.
- **Columnas** (`ColumnsJson`): selección de campos a mostrar — tanto nativos de la entidad como `CustomFieldDefinition.FieldKey`. Un informe de Órdenes puede mezclar `OrderNumber`, estatus y el campo personalizado `cost_center`.
- **Filtros por DSL** (`FilterJson`): condiciones compuestas (AND/OR, operadores por tipo) **reusando el mismo evaluador del DSL de SEPHAS** que valida los campos personalizados — un solo motor para validar y para filtrar.
- **Agrupación y orden** (`GroupJson`/`SortJson`): agregados (conteo, suma, promedio) y ordenamiento, sobre campos nativos o personalizados.
- **Visualización** (`ReportChartType`): tabla, barras, líneas o pastel — el mismo dataset alimenta el dashboard.
- **Visibilidad** (`ReportVisibility`): privado (solo el dueño), de todo el tenant, o compartido con roles/usuarios específicos (`ReportShare`, con bandera `CanEdit`). Respeta el filtro multi-tenant y el alcance de datos (`UserDataScope`) del que corre el informe.
- **Programación opcional** (`ScheduleCron`/`DeliveryEmails`): corridas recurrentes con entrega por correo, para reportes operativos/gerenciales periódicos.
- **Exportable**: el resultado se baja a Excel/CSV/PDF, consistente con el principio de "la plataforma calcula y exporta".

---

# Módulos de negocio

## 0B. Plataforma multi-tenant y catálogo de módulos

**Este no es el sistema de Advance Logistics — es una plataforma para toda la industria de logística, sin importar el modo de transporte.** Advance es el primer cliente (tenant) que se beneficia de ella, no el límite del diseño.

- **Todas las capacidades viven siempre en el esquema** (`ModuleDefinition`): última milla terrestre, COD, WMS con lote/serie, cross-docking, equipos en alquiler (tracking y facturación por separado), marítimo, portal de clientes, campos personalizados. Nada se construye "solo para Advance" y luego hay que rehacer para el próximo cliente.
- **Cada tenant enciende lo que usa** (`TenantModule`): un courier de última milla puro prende Última milla + COD; un 3PL con bodega prende WMS; una empresa de renta de equipo médico prende Equipos + Facturación de alquiler; un consolidador de carga prende Marítimo + Cross-dock. La ausencia de fila en `TenantModule` = módulo apagado por default (seguro).
- **Dependencias entre módulos** (`ModuleDefinition.DependsOnModuleKey`): p. ej. Facturación de alquiler depende de tener Equipos en alquiler encendido primero.
- **Para el tenant demo de Advance** quedan encendidos: Última milla, COD, WMS (lote/serie), Equipos en alquiler (solo tracking), Portal, Campos personalizados. Quedan definidos pero **apagados**: Cross-dock formal, Marítimo, Facturación de alquiler — listos para otro tenant o para cuando Advance los necesite, sin rediseñar nada.
- El menú/sidebar de la app se arma leyendo `TenantModule` en sesión — mostrar/ocultar una capacidad es una fila en una tabla, no un deploy.
- **Campos personalizados por tenant ya cubren las entidades nuevas sin trabajo extra**: `CustomFieldValue` es genérico (`EntityId` + la definición dice de qué entidad es). Al añadir `RENTAL_ASSET` y `RENTAL_CONTRACT` al catálogo `EntityType`, cualquier tenant puede definirle campos propios a sus equipos en alquiler (ej. "paciente asignado", "cobertura de plan médico") desde el mismo mecanismo que ya usan Órdenes o Clientes — cero tablas nuevas.

## 1. Clientes y contratos

- **Gestión de clientes** con código único por tenant, estatus (catálogo `ClientStatus`), límite de crédito, término de pago y moneda por catálogo. Teléfonos/correos vía `ContactPoint`.
- **Contactos del cliente** con principal y rol; sus medios de contacto también en `ContactPoint`.
- **Master de paradas (`Location`)**: define una vez cada punto recurrente (nombre, dirección geocodificada, zona tarifaria, ventana típica, tiempo de servicio, instrucciones de acceso/entrega). Reutilizable en órdenes para entrada en un clic. Puede pertenecer a un cliente o ser compartida del tenant. Absorbió el viejo `ClientAddress`.
- **Contratos marco** con vigencia, auto-renovación, estatus por catálogo y **modelo de cobro** (`BillingModel`: por recogido / por entrega / mixto) — el dato que alimenta las reglas de estados laterales y la liquidación.
- **Niveles de servicio (SLA)** por contrato (`ContractServiceLevel`): tránsito máximo, ventana de recogida, meta de cumplimiento, penalidad.
- **Tarifas por zona**: zonas (`RateZone`) definidas por código postal, rango, municipio o polígono (`RateZoneMember`). El escalonamiento se modela con `RateComponent` + `RateTier` (ver módulo 11), no con matriz plana.
- **Motor de cotización**: resuelve zona origen/destino (de `Location.RateZoneId` o geocodificación), busca el componente de tarifa, suma recargos aplicables, aplica `MinCharge`. Reutiliza el patrón de evaluación de reglas del DSL de SEPHAS.
- **Adjuntos** de contrato en blob storage (metadata en BD), tipados por catálogo `DocType`.
- **Verificación de crédito** al confirmar órdenes contra `CreditLimit` y saldo pendiente.

## 2. Órdenes de transporte

- **Creación mono o multi-parada**. Al escoger una `Location` recurrente, el `OrderStop` copia dirección, geo, ventana y tiempo de servicio (**snapshot**) — entrada en un clic, y la orden no se altera si después editas la parada master.
- **Líneas de carga** vinculadas a parada de pickup/delivery, opcionalmente enlazadas a `Product` (inventario) con lote/serie para trazabilidad. Agrega totales (peso/volumen/piezas) a la cabecera.
- **Estatus por etapas del tenant**: el pipeline (DRAFT→…→DELIVERED) y los laterales respetan la configuración del tenant; el cambio pasa por el servicio de transición (valida etapa activa + dependencia dura, registra bitácora, dispara efectos).
- **Concesiones/restricciones por estatus**: el UI habilita acciones (editar carga, asignar a trip, reprecio, cancelar) según `StatusCapability` del estatus actual.
- **Referencias externas** indexadas (`OrderReference`: PO de cliente, e-commerce, guía de transportista) — base para integraciones.
- **Adjuntos** tipados (BOL, factura, foto, firma) — donde escribe el módulo de POD.
- **Cotización al confirmar** + verificación de crédito.
- **COD en la orden**: tipo (efectivo/cheque/company check), monto y estatus COD propio (ver módulo 11B). El label de la orden imprime el COD destacado.
- **Eliminar una orden solo es posible en el estatus inicial** (recién entrada, antes de escanearse/asignarse a un trip). Pasado ese punto, la baja es una cancelación con bitácora (`StatusCapability`), no un delete — así no se pierde el rastro de auditoría de nada que ya haya tocado almacén o dinero.

## 3. Trips y rutas (planificación/optimización)

`Trip` es la capa de consolidación sobre `Route`. El Trip es dueño de vehículo/chofer/órdenes; la Route es el plan secuenciado, re-optimizable y versionado.

- **Planificación diaria**: agrupar órdenes confirmadas por fecha/zona en trips, asignando vehículo y chofer.
- **Consolidación** (`TripOrder`): varias órdenes de distintos clientes en un mismo viaje físico, según el modelo de operación.
- **Optimización** vía `IRouteOptimizer` (VROOM+OSRM self-hosted o OR-Tools, sin APIs pagas — consistente con colavora) respetando capacidad del vehículo, ventanas de tiempo y tiempo de servicio. Cada corrida genera una `Route` nueva (versionada) y archiva la anterior; queda registro en `OptimizationRun`.
- **Edición manual**: reordenar paradas (drag-and-drop) y recalcular ETAs sin re-optimizar todo.
- **Paradas no asignadas** visibles para reasignar a otro trip si exceden capacidad.
- **Despacho**: al pasar el trip a despachado, la ruta se congela y se vuelve visible para el chofer en la app móvil; el estado lo mueven eventos (salida, llegada, POD).
- **Mapa** Leaflet + OSM (polyline + marcadores por parada). Cuando una dirección no se geocodifica exacta, la parada se plotea al **centroide del código postal** como respaldo — `OrderStop.GeocodeAccuracyLookupId` (`EXACT`/`ZIP_CENTROID`/`CITY_CENTROID`/`MANUAL`) marca cuál fue el caso, y el mapa distingue visualmente el pin exacto del aproximado.
- **Zonas de despacho** (`DispatchZone`/`DispatchZoneMember`, por código postal o municipio): el territorio fijo que un chofer cubre habitualmente (ej. "R-01"). `DriverZone` guarda esa asignación estándar. Sirve para **reasignar en bloque** ("todo lo de R-01 pasa a Ana") sin tocar trip por trip, y para filtrar en Despacho/Escaneo. Es independiente de `RateZone` (esa es para tarifas, no para operación).
- **Alerta de máximo de paradas por ruta**: `Tenant.MaxStopsPerRouteDefault` es el default; `Driver.MaxStopsPerRoute` permite un override por chofer (NULL = usa el del tenant). Si una ruta sobrepasa el límite efectivo del chofer asignado, el dashboard de despacho lo marca con una alerta — no bloquea, avisa.
- **Despachar es una acción explícita y reversible de selección**: el botón "Despachar" abre un selector con **solo las rutas que aún no se han despachado** (`RouteStatus` distingue planificada/despachada/completada), con opción de marcar todas; las que ya salieron no vuelven a aparecer ahí.
- **Editar o eliminar una ruta libera sus órdenes**: quitar una orden de una ruta, o eliminar la ruta completa, la(s) devuelve a "sin asignar" (`TripOrder`/`RouteStop` se borra, la orden no) — no se pierde la orden, solo su asignación de ese día.
- **El escaneo alimenta la ruta, no al revés**: cuando se escanea una orden en modo Outbound, el sistema resuelve su `DispatchZone` (por pueblo/ZIP contra `DispatchZoneMember`) y la asigna automáticamente a la ruta abierta de ese día para esa zona (`TripOrder`), sin que nadie la arrastre a mano en Despacho. Si no hay ruta abierta para esa zona todavía, la orden queda en "sin asignar" como hoy. Esta asignación automática es una regla del backend; el mock la simula de forma liviana pero la app real la aplica siempre al confirmar el escaneo.

## 4. Flota, choferes y mantenimiento

- **Registro de flota** completo: tipo, propiedad (propio/arrendado/tercero), combustible, VIN, odómetro actual, almacén base, capacidad (`MaxWeightKg`/`MaxVolumeM3`).
- **Documentos vencibles** de vehículo (registro, seguro, inspección) y de chofer (licencia, certificaciones) con `ExpiryDate` indexado → **alertas de vencimiento** para el dashboard operacional.
- **Mantenimiento preventivo** por kilometraje o tiempo (`MaintenanceSchedule`): el sistema compara odómetro/fecha actual contra el intervalo y genera avisos/órdenes.
- **Órdenes de trabajo** preventivas y correctivas con costo de labor/partes (total computado), proveedor, odómetro y tareas; estatus por catálogo.
- **Bitácora de combustible** (`FuelLog`) con litros, costo y odómetro → cálculo de rendimiento (km/L) y costo por km.
- **Choferes** con código de empleado, fecha de contratación, licencias y certificaciones vencibles; teléfonos/correos en `ContactPoint`.
- **Disponibilidad para despacho**: el planificador de trips solo ofrece vehículos/choferes activos, con documentos vigentes y sin mantenimiento abierto que los inhabilite.

## 5. Almacenes (WMS)

Tarea genérica `WarehouseTask` (putaway/pick/pack/replenish/count/load/crossdock) + cabeceras específicas. **Todo movimiento escribe en el ledger `InventoryTransaction`** (fuente de verdad).

- **Jerarquía física** almacén → zona → bin, con códigos de ubicación (pasillo-rack-nivel-posición) para picking dirigido. Zonas tipadas (picking, reserva, refrigerado, cuarentena) por catálogo `ZoneType`.
- **Multi-almacén desde la base**: `Warehouse` es una entidad propia del tenant desde el día uno; zonas, bins, `StockBalance`, órdenes de compra y equipos en alquiler ya referencian `WarehouseId`. Hoy Advance opera uno solo, pero un tenant con varios no requiere ningún cambio de esquema — solo se le habilita el selector de almacén en las pantallas que hoy lo asumen implícito.
- **Jerarquía física** almacén → zona → bin (`WarehouseBin`), y **una posición puede tener varios productos distintos** — `StockBalance` es la tabla puente (`ProductId`+`WarehouseId`+`WarehouseBinId`+`LotId`, cantidad única) que permite exactamente eso; no hay "un producto por bin" en el diseño, esa fue solo una simplificación temprana del mock ya corregida.
- **La posición en `StockBalance` es la ubicación real y actual**, no una sugerencia — refleja dónde está físicamente el inventario ahora mismo. La sugerencia de dónde debería ir un producto al hacer putaway es un concepto aparte y opcional (`Product.PreferredWarehouseId` + `Product.PreferredBinId`, almacén y posición por default para cuando el producto entra), que no reemplaza ni se confunde con el balance real.
- **Mantenimiento de almacenes**: `Warehouse` ya trae todo lo necesario para una pantalla propia de alta/baja (código, nombre, dirección, estatus) — vive en el grupo Almacén, por encima de Ubicaciones en la jerarquía (Almacén → Zona → Posición).
- **Recepción** (`ReceiptLine`) contra ASN (validando esperado vs recibido) o ciega; cada línea confirmada escribe un `InventoryTransaction` positivo y deja la mercancía en bin de staging.
- **Al abrir un recibo nuevo, la cantidad recibida arranca igual a la esperada** (se asume que llega completo) — el receptor la corrige hacia abajo o hacia arriba según lo que de verdad llegue; solo entonces se genera el ajuste si hubo diferencia.
- **Recibir distinto a lo esperado es el caso normal, no la excepción**: la cantidad recibida por línea es editable hasta confirmar. Si difiere de lo esperado (faltante o excedente del proveedor/cliente), la diferencia se registra **como ajuste de inventario** al confirmar — mismo mecanismo y mismo ledger que usa el conteo cíclico (`InventoryTransaction`/`AdjustmentTxnId`), no un concepto aparte. El stock siempre refleja lo que físicamente entró; el ajuste es el rastro de auditoría de *por qué* no cuadró con lo esperado.
- **Putaway dirigido**: el sistema sugiere bin destino (por tipo de zona, rotación, capacidad) y genera `WarehouseTask` PUTAWAY; al completarse, movimiento staging→bin.
- **Picking por olas**: agrupa órdenes (atadas a un trip de salida) en una `PickWave`, genera `PickTask` con secuencia de recorrido optimizada y reserva stock (`QtyReserved`). Lo que dispara un `PickTask` es que la orden de salida tenga `CargoLine.ProductId` (hay que sacarlo de un bin), no que sea un paquete que el cliente ya trajo armado. Dos ejemplos reales de Advance con la misma mecánica y distinto dueño del producto (`Product.ClientId`): (a) Advance administra el almacén de una marca; un vendedor de la marca informa una venta, Advance la factura en QuickBooks (fuera del sistema) y genera la orden de envío — `Product.ClientId` = esa marca; (b) Advance compra medidores de glucosa para revenderlos (módulo 13B) y una farmacia le hace una orden — `Product.ClientId` = NULL (propio de Advance). Mismo Picking, misma pantalla, solo cambia de quién es el inventario.
- **Packing**: consolida lo pickeado en cartones con su contenido (`CartonLine`), peso y código de barras.
- **Replenishment**: tareas para reabastecer zonas de picking desde reserva cuando el disponible baja del mínimo.
- **Conteo cíclico**: cuenta por bin/producto, calcula varianza y genera el ajuste como `InventoryTransaction` (enlazado en `AdjustmentTxnId`) — nada se ajusta fuera del ledger.
- **Cola de tareas unificada** (`WarehouseTask`) para el operador móvil, con tipo, prioridad y asignación.
- **Trazabilidad**: todo (recepción, putaway, pick, ajuste) referencia su origen vía `RefEntity`+`RefId` y queda en el ledger.

## 6. Cross-docking

> **Nota de alcance (mock/UI):** el módulo `CROSSDOCK` existe completo en el esquema (tablas de abajo) pero **no tiene pantalla en el mock actual** — quedó apagado para el tenant de Advance porque lo que describen (vagones recurrentes de lo mismo, con tiempo de estadía variable) suena a **almacenaje real** (cubierto por Recibo + Ubicaciones + Inventario), no a cross-dock puro. Se mantiene diseñado porque el mismo concepto de citas de muelle (`DockAppointment`) es candidato fuerte para el futuro módulo **Marítimo**: coordinar la llegada de un contenedor/barco con la salida de las órdenes que consolida es, estructuralmente, el mismo problema que cross-dock terrestre. No se pierde el trabajo — se reutiliza cuando llegue el experto marítimo.



- **Muelles** tipados (inbound/outbound/ambos) con estatus de ocupación y **agenda de citas** (`DockAppointment`) para coordinar llegadas de ASN y salidas de trips.
- **Emparejamiento inbound↔outbound** (`CrossDockAllocation`): asigna cantidades de líneas de recepción directamente a órdenes/trips de salida, sin pasar por reserva.
- **Flujo directo**: la mercancía va del muelle inbound a la zona de staging cross-dock y de ahí al muelle outbound, vía `WarehouseTask` CROSSDOCK; el ledger registra el movimiento con `InventoryTxnType='CROSSDOCK'`.
- **Manejo de excedente/faltante**: lo no asignado en una recepción cae a putaway normal (a reserva); el faltante outbound queda visible como pendiente.
- **Trazabilidad** completa del flujo aunque no haya almacenaje, manteniendo lote/serie de inbound a outbound.

## 7. Inventario y trazabilidad por lote/serie

- **Maestro de productos (SKU)** propio o por cliente (3PL), con UoM base, tipo de seguimiento (ninguno/lote/serie), peso/volumen y código de barras. Trae **costo de compra** (`PurchaseCost`, default para la línea de orden de compra) y **precio de venta** (`SalePrice`, alimenta la línea `PRODUCT_SALE` de la factura) — ambos editables por producto, no fijos.
- **Desactivar un producto** (`Product.IsActive=0`) solo se permite si no tiene inventario disponible — no se puede apagar algo que todavía hay que despachar o contar. Un producto inactivo desaparece de los selectores de creación (nueva orden, nueva posición, línea de compra) pero **sigue apareciendo en Inventario e informes**, para no perder el histórico.
- **Ajuste manual de inventario**: además del que generan recepción y conteo cíclico, cualquier producto admite un ajuste directo (cantidad +/- con motivo) — mismo ledger, mismo reporte de ajustes.
- **Lotes y series** con fechas de fabricación/expiración; series con estatus (disponible/reservado/despachado).
- **Balance por ubicación** (`StockBalance`): en mano, reservado y disponible (columna computada) por almacén/bin/lote.
- **Ledger de movimientos** (`InventoryTransaction`) como fuente de verdad: recepción, despacho, transferencia, ajuste, cross-dock. El balance siempre se reconstruye del ledger.
- **Trazabilidad punta a punta**: desde una `CargoLine` (con lote/serie) sigues el rastro vía `InventoryTransaction.RefEntity='TransportOrder'`. Cada despacho descuenta stock y escribe movimiento negativo; cada recepción, positivo. Vista `vw_LotGenealogy` reconstruye la genealogía de un lote.

## 8. App móvil para conductores

Offline-first, patrón colavora.

- **Offline-first**: al despachar un trip, el dispositivo descarga ruta, paradas, contactos y datos de cliente a SQLite local. El chofer opera sin señal; los cambios (llegada, POD, fallo) se encolan en un outbox local con idempotency-key y se sincronizan al recuperar conexión. La API aplica idempotencia (middleware) para no duplicar en reintentos.
- **Navegación** con OpenStreetMap + Leaflet/MapLibre (sin APIs pagas), polyline de la ruta y marcadores por parada.
- **Flujo por parada**: en camino → llegada (estampa GPS) → completar con POD (módulo 9) o registrar fallo con razón. El estatus de parada/orden lo mueven estos eventos vía el servicio de transición.
- **Pings GPS** periódicos durante el trip → tracking en vivo para dispatcher y portal. Frecuencia adaptativa (más densa en movimiento).
- **Notificaciones push** (token en `DriverDevice`): nuevo trip asignado, cambio de ruta, mensaje del dispatcher.
- **Biometría/sesión** reutilizando el patrón de SEPHAS (`expo-local-authentication`, refresh JWT con reauth).

## 9. Prueba de entrega (POD)

- **Captura en el dispositivo**: firma (canvas), una o varias fotos, GPS automático y nombre/relación del receptor, todo offline y sincronizado con idempotencia.
- **Resultado tipado** (entregado/parcial/rechazado/fallido) con razón de fallo por catálogo → alimenta KPIs de cumplimiento y el reintento/reprogramación.
- **Entrega parcial** a nivel de línea (`ProofOfDeliveryItem`): qué se entregó y qué se rechazó, con el delta opcionalmente devuelto al inventario vía un `InventoryTransaction`.
- **POD visible** en el portal del cliente y adjunto a la factura como evidencia.
- **Cobro de COD en la entrega**: si la orden trae COD, el chofer registra el monto y método cobrados (`CodCollection`), incluso parcial — entrada al ciclo COD/PWBack del módulo 11B.
- **Geo-validación**: comparar `CapturedGeoPoint` contra la ubicación de la parada para detectar entregas fuera de sitio.

## 10. Portal de clientes

- **Alcance estricto por cliente**: todo se filtra por `ClientId` (segundo nivel sobre el filtro de tenant). Un usuario de portal nunca ve datos de otro cliente del mismo tenant.
- **Booking**: crear órdenes (escogiendo de su master de paradas `Location`), ver cotización en línea con el motor de tarifas.
- **Tracking en vivo**: estado de orden/parada y posición del vehículo (de `DriverLocationPing`) en mapa OSM.
- **Documentos y POD**: descargar BOL, POD (firma/fotos) y facturas.
- **Indicadores del cliente**: cumplimiento, volumen, gasto — subconjunto del módulo de dashboards filtrado a su `ClientId`.
- **Notificaciones** configurables por evento (`PortalNotificationPref`).

## 11. Facturación y liquidación

**Tarifas escalonadas (modelo final):** `RateComponent` + `RateTier` con `TierMode` (GRADUATED = marginal por tramo / VOLUME = un solo tramo según volumen total) cubre cualquier escalonamiento. El `BillingModel` del contrato (por recogido / por entrega / mixto) es el disparador; es **ortogonal** al escalonamiento. Caguas usa GRADUATED.

- **Generación de cargos** desde órdenes/trips completados, aplicando el `RateComponent`/`RateTier` correcto según `BillingModel` y guardando el `RateComponentId` en la línea para auditar el cálculo.
- **Corrida por lote con aprobación** (`BillingRun`): generar → revisar → aprobar → exportar. **Es el único flujo con paso humano del producto**; se modela con estatus simple, no con grafo de workflow.
- **Exportación a contabilidad** (archivo/endpoint), sin emitir pagos directamente — la plataforma calcula y exporta, igual que el principio HUD-compliant de SEPHAS.
- **Liquidación a choferes/transportistas** (settlement) por viaje/orden, con pagos, deducciones y bonos; neto computado.
- **Notas de crédito y reconciliación** de pagos contra facturas; estado de cuenta del cliente.

## 11B. Ciclo COD (cobro contra entrega y remesa al cliente)

El corazón del negocio de Advance Logistics: el courier cobra el COD al consignatario en la entrega, lo retiene, lo cuadra y lo **devuelve al cliente (sender)** menos comisión. Es el flujo que el sistema actual llama *PWBack*.

- **COD en la orden**: `CodTypeLookupId` (sin COD / efectivo / cheque / company check), `CodAmount`, moneda y `CodStatusCodeId` (por cobrar → parcial → cobrado → cuadrado → remitido). Las órdenes sin COD dejan el estatus en NULL. El motor de captura (ráfaga y guiada) y el label imprimen el COD destacado.
- **Cobro en la entrega** (`CodCollection`): al hacer el POD, el chofer registra el monto cobrado, el método (`CodPaymentMethod`: efectivo / cheque / company check / ATH Móvil / tarjeta), número de cheque o referencia, y queda atado a la orden, al POD y al chofer. Soporta cobros **parciales** (varios `CodCollection` por orden). Permiso `cod.collect` (rol Chofer); se captura offline y sincroniza con idempotencia.
- **Reconciliación (PWBack)**: los cobros pasan de `COLLECTED` a `RECONCILED` cuadrando lo cobrado en calle contra lo registrado — típicamente **escaneando** los paquetes/cheques de regreso (la "Estación de escaneo" en modo COD usa `OrderReference`/barcode). Reportes de faltantes y cuadre por chofer/cliente. Permiso `cod.reconcile`.
- **Remesa al cliente** (`CodRemittanceBatch` + `CodRemittanceLine`): se agrupan los cobros cuadrados por cliente y período; el lote calcula bruto cobrado, **comisión** (`Contract.CodCommissionPct` por defecto) y **neto a remitir** (columna computada). Estatus `RemittanceStatus` (abierta → cuadrada → aprobada → remitida). Igual que la facturación, **la plataforma calcula y exporta** la remesa a contabilidad; no emite el pago (principio HUD-compliant de SEPHAS). Permiso `cod.remit`.
- **Trazabilidad del dinero**: desde una orden ves el/los `CodCollection` y, si ya se devolvió, en qué `CodRemittanceBatch` salió, con su comisión y neto. Cada transición queda en `EntityStatusHistory` y `AuditLog`.
- **KPIs COD** (módulo 12): COD pendiente, cobrado y remitido — alimentan el dashboard y el portal del cliente.

## 13B. Compras (módulo PURCHASING)

Confirmado con Advance: además de mover cargo de terceros, **compran inventario propio para revenderlo** (ej. medidores de glucosa que le compran a la marca y venden a las farmacias). Eso sí es una compra real — Advance como comprador, no como transportista — y por eso, a diferencia de Cross-dock/Marítimo, este módulo queda **encendido** para su tenant desde ya.

- **`Supplier`**: catálogo liviano de proveedores (nombre, contacto, término de pago).
- **`PurchaseOrder` + `PurchaseOrderLine`**: qué se le compra a quién, cantidad y costo unitario por línea, con estatus (borrador → enviada → recibida parcial/completa → cancelada).
- **Eliminar una orden de compra solo es posible mientras nada se está recibiendo o se ha recibido contra ella** (`PurchaseOrderStatus` en `DRAFT`/`SENT`/`CANCELLED`, no en `PARTIAL`/`RECEIVED`); una vez el recibo arrancó, la baja es cancelación con bitácora, igual que con las órdenes de transporte.
- **El recibo reutiliza todo lo que ya existe**: `Asn` ahora puede originarse de un `ClientId` (cargo de un cliente de paso) **o** de un `PurchaseOrderId` (compra propia) — misma tabla, mismo `ReceiptHeader`/`ReceiptLine`, mismo flujo de Recibo. No se duplicó nada.
- **La distinción de propiedad ya existía**: `Product.ClientId` NULL = producto propio de Advance (como los medidores); no nulo = producto que administran para un cliente específico. El módulo de Compras alimenta el primero.
- **La venta a la farmacia** es una `TransportOrder` normal con `CargoLine.ProductId` apuntando al producto — la única pieza que faltaba era poder facturar el **costo del producto**, no solo el flete: se añadió `InvoiceLine.ProductId` y el tipo de cargo `ChargeType='PRODUCT_SALE'`, así una factura puede traer ambas líneas (flete + producto) sin inventar un módulo de ventas aparte.

## 16C. Equipos en alquiler (módulo RENTAL_EQUIPMENT / RENTAL_BILLING)

Activos serializados que Advance (y potencialmente otros tenants: renta de equipo médico, herramientas, etc.) prestan al cliente por semanas o meses — no son paquetes que se entregan y terminan, son activos que hay que rastrear en el tiempo.

- **Registro del activo** (`RentalAsset`): tipo, serie, modelo, marca, estatus (disponible → en alquiler → mantenimiento/perdido/retirado), y su **ubicación actual**: en un almacén propio o en sitio del cliente.
- **Contrato de alquiler** (`RentalContract`): cliente, fechas de inicio/fin (o plazo abierto), fecha esperada de devolución, depósito. El estatus vencido se detecta comparando `ExpectedReturnDate` contra hoy.
- **El movimiento es una entrega real**: cuando el equipo sale o regresa, se ata a una `TransportOrder` (`DeliveryOrderId`/`ReturnOrderId`) — usa el mismo Despacho, sin pantalla de rutas duplicada.
- **Mantenimiento** (`RentalAssetMaintenance`): mismo patrón que `MaintenanceWorkOrder` de Flota, aplicado al equipo en vez del vehículo.
- **Facturación recurrente** (módulo separado `RENTAL_BILLING`, apagado en Advance por ahora): `RentalBillingRule` define frecuencia (semanal/mensual/único), tarifa, prorrateo del primer período y cargo por atraso; `RentalCharge` genera los cargos por período, enlazables a `Invoice` cuando el tenant decide activar el cobro automatizado.

## 12. Dashboards e indicadores

Tres audiencias: operacional (dispatcher), gerencial (tendencias/costos), cliente (subconjunto por `ClientId` en el portal).

- **KPIs operacionales**, todos derivables de tablas existentes:
  - *On-time delivery %*: `ProofOfDelivery.CapturedAtUtc` vs `OrderStop.WindowEndUtc`/`PromisedDate`.
  - *Cumplimiento de SLA*: contra `ContractServiceLevel`.
  - *Costo por km / por entrega*: `FuelLog` + `MaintenanceWorkOrder` + distancia de `Route`.
  - *Utilización de flota*: trips/vehículo, capacidad usada vs `Vehicle.MaxWeightKg/MaxVolumeM3`.
  - *Entregas fallidas* por razón (`PodOutcome`/`DeliveryFailureReason`).
  - *Tiempo de ciclo*: de `EntityStatusHistory` (DRAFT→DELIVERED).
  - *Estado de inventario*: niveles, próximos a vencer (`InventoryLot.ExpiryDate`), rotación.
  - Las tendencias de tarjetas/sparklines usan **los últimos N días hábiles** según el calendario laboral del tenant (`Tenant.WorkDaysMask` + `TenantHoliday`) — sin barras muertas de fines de semana o feriados. El mismo calendario alimenta SLA y fechas límite en días hábiles.
  - *COD*: pendiente, cobrado y remitido (`CodStatus`/`CodCollection`/`CodRemittanceBatch`), por cliente y por chofer.
- **Alertas operacionales** (patrón panel SEPHAS): documentos por vencer (vehículo/chofer), mantenimiento pendiente, paradas en riesgo de SLA, crédito de cliente excedido.
- **Snapshots opcionales** (`KpiDailySnapshot`) recalculados por job nocturno cuando los KPIs sobre el ledger crudo se vuelvan lentos; mientras tanto, vistas/consultas en vivo.

## 13. API para integraciones

- **REST versionada** (`/api/v1/...`) con autenticación por `ApiCredential` (client_id/secret → token), scopes por integración y rate limiting por credencial.
- **Idempotencia** en escrituras (header `Idempotency-Key`, registrado en `IntegrationMessageLog`) — reaprovecha el middleware de idempotencia de colavora; reintentos no duplican órdenes.
- **Webhooks salientes** con firma HMAC, reintentos con backoff y bitácora (`WebhookDelivery`) — el ERP/e-commerce del cliente se entera de eventos (orden creada, en tránsito, entregada, factura emitida) sin polling.
- **Conectores por tipo**:
  - *ERP*: sincronizar clientes/productos, exportar facturas/liquidación.
  - *E-commerce* (Shopify/Woo/etc.): ingestar pedidos como órdenes de transporte, mapear el ID externo en `OrderReference` (RefType=ECOMMERCE), devolver tracking.
  - *Transportistas*: tendering, intercambio de estatus y POD cuando se subcontrata un trip.
- **Mapeo de IDs externos** vía `OrderReference` (sin tabla nueva): correlaciona PO de cliente, pedido de e-commerce y guía de transportista con la orden interna.
- **Trazabilidad** completa de cada mensaje (inbound/outbound) para depurar integraciones.

---

## Principios de diseño que atraviesan todos los módulos

1. **Multi-tenant** con `TenantId` + filtro global EF Core; el tenant sale del principal, nunca del request.
2. **PK `INT IDENTITY` interno + `PublicId UNIQUEIDENTIFIER`** para exposición externa.
3. **Auditoría inherente**: `CreatedAtUtc`/`By`, soft-delete `IsActive`, `RowVersion`. Geo en `GEOGRAPHY`, dinero en `DECIMAL(18,4)`.
4. **El ledger manda**: ningún balance se edita directo; se reconstruye de `InventoryTransaction`.
5. **Un solo flujo de aprobación humana** en todo el producto: `BillingRun`. El resto lo mueven eventos físicos.
6. **Sin APIs pagas de mapas**: OSM + Nominatim + Leaflet/VROOM/OSRM self-hosted, consistente con colavora.
7. **Reutilización de SEPHAS/colavora**: `LookupCode`, capacidades por estatus, MFA/AAL2, DSL de reglas, middleware de idempotencia, offline-first.
