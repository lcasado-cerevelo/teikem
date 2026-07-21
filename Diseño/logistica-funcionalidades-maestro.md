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
11A. Liquidación a choferes (`DriverSettlementRun`)
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
- **Staff que trabaja para más de una compañía**: `UserTenant` (usuario↔tenant, muchos a muchos, con estatus de membresía) y `AspNetUsers.DefaultTenantId` ya existen para esto exacto — un mismo usuario puede pertenecer a varios tenants (ej. un gerente de cuenta que atiende a más de un cliente de la plataforma). **Regla de UX**: el selector de compañía en el encabezado solo aparece si el login trae más de un `UserTenant` activo — con uno solo, no se muestra (no hay nada que escoger). Al cambiar de compañía, la sesión completa recarga su contexto con el `TenantId` elegido: menú (`TenantModule`), datos, permisos (`RolePermission` es por tenant), todo — no es un filtro visual, es un cambio real de alcance de datos.

## 1. Clientes y contratos

- **Gestión de clientes** con código único por tenant, estatus (catálogo `ClientStatus`), límite de crédito, término de pago y moneda por catálogo. Teléfonos/correos vía `ContactPoint`.
- **Contactos del cliente** con principal y rol; sus medios de contacto también en `ContactPoint`.
- **Master de paradas (`Location`)**: define una vez cada punto recurrente (nombre, dirección geocodificada, zona tarifaria, ventana típica, tiempo de servicio, instrucciones de acceso/entrega). Reutilizable en órdenes para entrada en un clic. Puede pertenecer a un cliente o ser compartida del tenant. Absorbió el viejo `ClientAddress`.
- **Contratos marco** con vigencia, auto-renovación y estatus por catálogo.
- **Modelo de facturación (`ContractBillingModel`) — no es un solo valor, es un set de componentes independientes que se activan por checkbox, cada uno con su propia configuración:**
  1. **Por servicio (`porServicio`):** tarifa fija por combinación de tipo de servicio + tipo de paquete (`RateComponent` con clave servicio+paquete). Un cliente puede tener varias filas — una por cada combinación que factura distinto (ej. Next day/Box a $6.50, Same day/Box a $8.50).
  2. **Pieza extra con precio especial (`piezaExtra`):** cuando el cliente no cobra la pieza extra al precio genérico, sino con su propia tabla de rangos — cada fila dice, para un servicio+paquete dado, desde qué pieza hasta qué pieza aplica una tarifa (`RateTier` con `TierMode=GRADUATED` acotado por `FromUnit`/`ToUnit`). Puede tener **más de un rango** por servicio+paquete (ej. piezas 2–5 a $1.00, piezas 6+ a $0.75) — es la razón por la que este componente es una tabla y no un campo único.
  3. **Cargo por despacho (`despacho`):** un monto fijo adicional por despachar la orden, independiente de piezas o COD.
  4. **Cargo por COD (`cod`):** un cargo aparte por procesar órdenes con COD, que puede ser **fijo** (monto por orden) o **por ciento** (del monto COD o del total facturado, según se defina la regla) — es un campo de diseño abierto a propósito porque **Advance Logistics lo maneja fijo hoy**, pero el esquema no debe asumir que todo tenant lo hará igual. Es un concepto distinto de la comisión de remesa COD del módulo 11B (esa es lo que Advance retiene del dinero cobrado en la calle antes de devolverlo al cliente; este cargo por COD es lo que Advance le factura al cliente por el servicio de procesar COD, dos cosas de dinero distintas aunque ambas mencionen "COD").
  5. **Servicios especiales (`especiales`):** activa el catálogo `SpecialService` de ese cliente (ver más abajo) — si no está marcado, ese cliente no tiene servicios especiales y el catálogo no se muestra.

  Un cliente puede tener cualquier combinación de estos cinco marcados a la vez (ninguno es excluyente); cada uno que se marca despliega su sección de configuración en la misma pantalla, y cada uno que se desmarca oculta su sección (los datos no se pierden, solo dejan de aplicar). En el modelo de datos real esto vive como columnas booleanas en `Contract` más las tablas `RateComponent`/`RateTier` (componentes 1 y 2), `Contract.DispatchFee` (componente 3), `Contract.CodFeeType`/`Contract.CodFeeValue` (componente 4) y la relación a `SpecialService` (componente 5) — no como un string libre.
- **Niveles de servicio (SLA)** por contrato (`ContractServiceLevel`): tránsito máximo, ventana de recogida, meta de cumplimiento, penalidad.
- **Tarifas por zona**: zonas (`RateZone`) definidas por código postal, rango, municipio o polígono (`RateZoneMember`). El escalonamiento se modela con `RateComponent` + `RateTier` (mismo mecanismo que los componentes "por servicio" y "pieza extra" de arriba, ver módulo 11), no con matriz plana.
- **Motor de cotización**: resuelve zona origen/destino (de `Location.RateZoneId` o geocodificación), busca el componente de tarifa, suma recargos aplicables, aplica `MinCharge`. Reutiliza el patrón de evaluación de reglas del DSL de SEPHAS.
- **Adjuntos** de contrato en blob storage (metadata en BD), tipados por catálogo `DocType`.
- **Verificación de crédito** al confirmar órdenes contra `CreditLimit` y saldo pendiente.
- **Catálogo de servicios especiales por cliente (`SpecialService`, dentro de "Clientes y contratos", activo solo si el componente 5 del modelo de facturación está marcado):** aparte de sus paquetes normales, un cliente puede tener servicios que no son "un paquete" — el caso que lo disparó es traer un vagón/contenedor del muelle, pero el catálogo es abierto (nombre + tarifa, editable por cliente). Es la contraparte, del lado del cliente, de `DriverTripRate` (módulo 11A, el lado del chofer): el mismo concepto de negocio visto desde dos maestros distintos — cuánto cobra el chofer por hacer el viaje vs. qué servicio del cliente lo originó. Se usa en dos lugares: (1) **Entrada de órdenes**, al marcar una entrega como "especial" se escoge de este catálogo (filtrado por el cliente de la orden); (2) **Choferes y tarifas**, donde los "tipos de viaje" ya no se escriben libres — el selector sale de la unión de todos los `SpecialService` configurados, para que el mismo servicio se llame igual en ambos maestros.
- **El "tipo" de un `SpecialService` (el nombre — ej. "Vagón del muelle") no se escribe libre al asignárselo a un cliente:** es un `<select>` con los tipos ya usados por cualquier cliente del tenant (misma lista que alimenta el selector de tipo de viaje en Choferes y tarifas), tanto al crear una fila nueva como al editar una ya existente. La única forma de introducir un tipo que todavía no existe en ningún cliente es la opción "+ Nuevo tipo de servicio especial…" al final del selector, que revela un campo de texto para nombrarlo una sola vez — de ahí en adelante ese nombre queda disponible como opción del dropdown para cualquier otro cliente (y para Choferes y tarifas). Esto evita que el mismo servicio termine con variantes de nombre ligeramente distintas entre clientes (ej. "Vagón del muelle" vs. "Vagon Muelle").
- **Pantalla "Clientes y contratos":** lista de clientes a la izquierda (estado activo/inactivo, y un resumen de qué componentes de facturación tiene marcados) y detalle a la derecha. El panel **Contrato** trae los campos generales (estado, cliente desde, SLA de tránsito, límite de crédito) más los cinco checkboxes del modelo de facturación; debajo aparece, **solo si está marcado**, un panel por componente activo: Tarifas por servicio (tabla servicio+paquete+tarifa), Tarifas por pieza extra (tabla servicio+paquete+desde-pieza+hasta-pieza+tarifa, puede tener varias filas), Cargo por despacho (un campo), Cargo por COD (tipo fijo/por ciento + valor) y Servicios especiales (tabla del catálogo de ese cliente, con alta rápida). Mismo patrón visual que "Choferes y tarifas": maestro a la izquierda, tarifas editables en línea a la derecha, tablas que aparecen o desaparecen según lo que el contrato realmente necesita — no todos los campos para todos los clientes.

## 2. Órdenes de transporte

- **Creación mono o multi-parada**. Al escoger una `Location` recurrente, el `OrderStop` copia dirección, geo, ventana y tiempo de servicio (**snapshot**) — entrada en un clic, y la orden no se altera si después editas la parada master.
- **`OrderNumber` se autogenera si no se entra manualmente**: el operador puede escribir su propio número (ej. un consecutivo interno del cliente) o dejarlo en blanco — si lo deja en blanco, el sistema le asigna uno propio al guardar. Nunca bloquea la captura por falta de número.
- **Validación mínima para guardar**: consignatario, tipo de paquete y tipo de servicio son obligatorios — sin los tres, la orden no se guarda (ni en Entrada rápida ni en Detallada). El consignatario cuenta como "entrado" tanto si se escoge del directorio como si se escribe libre (y se resuelve a un `Location` nuevo o existente al guardar).
- **Tipo de paquete y tipo de servicio traen default por tenant** (`Tenant.DefaultServiceTypeLookupId`/`DefaultPackageTypeLookupId`) — en Entrada rápida, la fila nueva ya sale preseleccionada con el default configurado de la compañía (editable, no forzado); si el tenant no tiene default configurado todavía, el campo empieza en blanco y la validación anterior lo exige explícitamente. De paso, `PackageType` no existía como dominio propio en el esquema — vivía solo como texto libre en `CargoLine.Description`; se le agregó `CargoLine.PackageTypeLookupId` (Entity='PackageType': Caja/Sobre/Tarima) para que el tipo de paquete sea un valor real del catálogo, no texto suelto.
- **Líneas de carga** vinculadas a parada de pickup/delivery, opcionalmente enlazadas a `Product` (inventario) con lote/serie para trazabilidad. Agrega totales (peso/volumen/piezas) a la cabecera.
- **Estatus por etapas del tenant**: el pipeline (DRAFT→…→DELIVERED) y los laterales respetan la configuración del tenant; el cambio pasa por el servicio de transición (valida etapa activa + dependencia dura, registra bitácora, dispara efectos).
- **Concesiones/restricciones por estatus**: el UI habilita acciones (editar carga, asignar a trip, reprecio, cancelar) según `StatusCapability` del estatus actual.
- **Referencias externas** indexadas (`OrderReference`: PO de cliente, e-commerce, guía de transportista) — base para integraciones.
- **Adjuntos** tipados (BOL, factura, foto, firma) — donde escribe el módulo de POD.
- **Cotización al confirmar** + verificación de crédito.
- **COD en la orden**: tipo (efectivo/cheque/company check), monto y estatus COD propio (ver módulo 11B). El label de la orden imprime el COD destacado.
- **Eliminar una orden solo es posible en el estatus inicial** (recién entrada, antes de escanearse/asignarse a un trip). Pasado ese punto, la baja es una cancelación con bitácora (`StatusCapability`), no un delete — así no se pierde el rastro de auditoría de nada que ya haya tocado almacén o dinero.
- **El consignatario se elige de un directorio reutilizable, no se escribe cada vez**: `Location` (ya existe en el diseño) es ese directorio — nombre, dirección, pueblo, teléfono. Al elegir un consignatario en Entrada de órdenes, `OrderStop.LocationId` apunta a ese registro y los campos `Snap*` (dirección/pueblo/código postal) se copian automáticamente en ese momento — así si la dirección del `Location` cambia después, la orden vieja conserva la dirección con la que realmente se entregó. Se puede crear un `Location` nuevo al vuelo desde la misma pantalla de entrada, sin salir del flujo.
- **Comentario/nota por orden** usa el campo `TransportOrder.Notes` que ya existía — no hizo falta columna nueva, solo exponerlo en la entrada rápida.
- **Entrega especial (checkbox en Entrada detallada, después de Consignatario):** al marcarla, la sección 03 deja de pedir paquete/piezas/COD y en su lugar pide **servicio especial** (del catálogo `SpecialService` del cliente de la orden, módulo 1) y **chofer** (de los ya existentes en Flota, módulo 4) — el chofer se asigna de una vez, la orden no pasa por Sala de despacho/optimización de ruta. Al guardar se crean dos cosas: la orden (con el nombre del servicio en el campo de paquete, para que siga siendo visible en la lista de Órdenes) y un `DriverTrip` (módulo 11A) con el monto congelado a la tarifa vigente del chofer para ese tipo de viaje. Cliente y servicio (sección 01) y consignatario (sección 02) no cambian — solo se desactiva la sección de detalle del paquete.

## 3. Trips y rutas (planificación/optimización)

`Trip` es la capa de consolidación sobre `Route`. El Trip es dueño de vehículo/chofer/órdenes; la Route es el plan secuenciado, re-optimizable y versionado.

- **Planificación diaria**: agrupar órdenes confirmadas por fecha/zona en trips, asignando vehículo y chofer.
- **Consolidación** (`TripOrder`): varias órdenes de distintos clientes en un mismo viaje físico, según el modelo de operación.
- **Optimización** vía `IRouteOptimizer` (VROOM+OSRM self-hosted o OR-Tools, sin APIs pagas — consistente con colavora) respetando capacidad del vehículo, ventanas de tiempo y tiempo de servicio. Cada corrida genera una `Route` nueva (versionada) y archiva la anterior; queda registro en `OptimizationRun`.
- **Edición manual**: reordenar paradas (drag-and-drop) y recalcular ETAs sin re-optimizar todo.
- **Paradas no asignadas** visibles para reasignar a otro trip si exceden capacidad.
- **Despacho**: al pasar el trip a despachado, la ruta se congela y se vuelve visible para el chofer en la app móvil; el estado lo mueven eventos (salida, llegada, POD).
- **Mapa** con proveedor real (Google Maps o Leaflet + OSM), con alternador **2D / Satélite** como cualquier mapa moderno. Se marcan los **puntos de entrega** y la **posición en vivo del dispositivo del chofer** — no se dibuja una línea sintética conectando las paradas (eso solo tendría sentido si viene de la polyline real que devuelve el motor de ruteo/VROOM tras optimizar, no como decoración). Cuando una dirección no se geocodifica exacta, la parada se plotea al **centroide del código postal** como respaldo — `OrderStop.GeocodeAccuracyLookupId` (`EXACT`/`ZIP_CENTROID`/`CITY_CENTROID`/`MANUAL`) marca cuál fue el caso, y el mapa distingue visualmente el pin exacto del aproximado.
- **Zonas de despacho** (`DispatchZone`/`DispatchZoneMember`, por código postal o municipio): el territorio fijo que un chofer cubre habitualmente (ej. "R-01"). `DriverZone` guarda esa asignación estándar. Sirve para **reasignar en bloque** ("todo lo de R-01 pasa a Ana") sin tocar trip por trip, y para filtrar en Despacho/Escaneo. Es independiente de `RateZone` (esa es para tarifas, no para operación).
- **Alerta de máximo de paradas por ruta**: `Tenant.MaxStopsPerRouteDefault` es el default; `Driver.MaxStopsPerRoute` permite un override por chofer (NULL = usa el del tenant). Si una ruta sobrepasa el límite efectivo del chofer asignado, el dashboard de despacho lo marca con una alerta — no bloquea, avisa.
- **Despachar es una acción explícita y reversible de selección**: el botón "Despachar" abre un selector con **solo las rutas que aún no se han despachado** (`RouteStatus` distingue planificada/despachada/completada), con opción de marcar todas; las que ya salieron no vuelven a aparecer ahí.
- **Editar o eliminar una ruta libera sus órdenes**: quitar una orden de una ruta, o eliminar la ruta completa, la(s) devuelve a "sin asignar" (`TripOrder`/`RouteStop` se borra, la orden no) — no se pierde la orden, solo su asignación de ese día.
- **El escaneo alimenta la ruta, no al revés**: cuando se escanea una orden en modo Outbound, el sistema resuelve su `DispatchZone` (por pueblo/ZIP contra `DispatchZoneMember`) y la asigna automáticamente a la ruta abierta de ese día para esa zona (`TripOrder`), sin que nadie la arrastre a mano en Despacho. Si no hay ruta abierta para esa zona todavía, la orden queda en "sin asignar" como hoy. Esta asignación automática es una regla del backend; el mock la simula de forma liviana pero la app real la aplica siempre al confirmar el escaneo.
- **La estación de escaneo confirma por voz** (texto-a-voz, no solo visual): encontrado dice una palabra ("Sí"/"Yes"), ya escaneado dice otra ("Ya"/"Already"), no encontrado dice otra ("No") — en el idioma activo de la sesión, con opción de silenciar. Pensado para que el operador no tenga que mirar la pantalla en cada escaneo, solo escuchar la confirmación.

## 4. Flota, choferes y mantenimiento

- **Registro de flota** completo: tipo, propiedad (propio/arrendado/tercero), combustible, VIN, odómetro actual, almacén base, capacidad (`MaxWeightKg`/`MaxVolumeM3`).
- **Documentos vencibles** de vehículo (registro, seguro, inspección) y de chofer (licencia, certificaciones) con `ExpiryDate` indexado → **alertas de vencimiento** para el dashboard operacional.
- **Mantenimiento preventivo** por kilometraje o tiempo (`MaintenanceSchedule`): el sistema compara odómetro/fecha actual contra el intervalo y genera avisos/órdenes.
- **Órdenes de trabajo** preventivas y correctivas con costo de labor/partes (total computado), proveedor, odómetro y tareas; estatus por catálogo.
- **Bitácora de combustible** (`FuelLog`) con litros, costo y odómetro → cálculo de rendimiento (km/L) y costo por km.
- **Choferes** con código de empleado, fecha de contratación, licencias y certificaciones vencibles; teléfonos/correos en `ContactPoint`.
- **Disponibilidad para despacho**: el planificador de trips solo ofrece vehículos/choferes activos, con documentos vigentes y sin mantenimiento abierto que los inhabilite.
- **Lo que se le paga al chofer no vive aquí**: el maestro de tarifas (`DriverRateAgreement`, por entrega/intento/viaje) y las corridas de liquidación son el módulo 11A — este módulo es identidad y flota, no compensación.

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
- **Corrida por lote con aprobación** (`BillingRun`): generar → revisar → aprobar → exportar. **Es uno de los dos flujos con paso humano del producto** (el otro es `DriverSettlementRun`, ver abajo); ambos comparten la misma máquina de estados simple, no un grafo de workflow.
- **Exportación a contabilidad** (archivo/endpoint), sin emitir pagos directamente — la plataforma calcula y exporta, igual que el principio HUD-compliant de SEPHAS.
- **Mecanismo de intentos de entrega, pensado para cargo opcional al cliente (diseño, no activo aún en el mock):** cada entrega puede necesitar más de un intento; el ledger `DeliveryAttempt` (ver 11C) registra cada uno con su resultado. Eso deja lista la fuente de datos para que, el día que un contrato lo pida, `Facturación` pueda sumar una línea opcional "cargo por intento" (ej. solo a partir del 2do intento) igual que hoy suma Base/Extra/COD — falta el `RateComponent` de tipo intento en el contrato y la columna en la corrida; el dato ya existe.
- **Notas de crédito y reconciliación** de pagos contra facturas; estado de cuenta del cliente.

## 11A. Liquidación a choferes (`DriverSettlementRun`)

Mismo patrón que `BillingRun` (generar → revisar → aprobar → exportar, ajuste manual auditado por línea, "Teikem calcula y exporta a nómina/contabilidad; no emite el pago") pero para lo que se le paga a un chofer, no a lo que se le cobra a un cliente. Vive en el grupo de menú **Contabilidad**, junto a Facturación; su maestro de tarifas vive en la pantalla nueva **"Choferes y tarifas"**, dentro del grupo **Catálogo** (junto a Clientes y contratos, Flota y mantenimiento y Portal de clientes — el grupo se queda con ese nombre por ahora).

- **`DriverRateAgreement` (maestro, pantalla "Choferes y tarifas"):** por chofer, `PerDeliveryRate` (pago por entrega exitosa) + una tabla abierta de tarifas por intento (`DriverAttemptRate`: número de intento → monto). **No está limitado a dos intentos en el diseño** — el número de niveles es una lista abierta (`+ Agregar intento` suma un nivel más, en vivo, a todos los choferes); el negocio típicamente solo llena 1 y 2, pero el esquema no lo asume. Un intento con número mayor al máximo configurado usa la tarifa del nivel más alto definido (fallback), para que la corrida nunca se rompa aunque ocurra un 3er o 4to intento real.
- **`DeliveryAttempt` (ledger, nuevo — fuente de verdad de intentos):** una fila por intento de entrega: orden, número de intento, chofer, fecha, resultado (entregada/fallida) y razón de fallo si aplica. Nace del evento físico (chofer marca entrega o fallo en la app / Sala de despacho); en el mock se generó a partir de las órdenes ya sembradas para poder demostrar la pantalla. Es la misma idea que el ledger `InventoryTransaction` de Almacén: nada se paga ni se factura fuera de este registro — se reconstruye de él.
- **Fórmula configurable de cómo combina "pago por entrega" con "pago por intento":** en vez de asumir una sola regla, la pantalla de Choferes y tarifas trae un selector con tres modelos, con una vista previa en vivo del cálculo:
  1. **Entrega + cada intento** (default): se paga la tarifa de entrega una vez, al lograrla, más la tarifa de cada intento realizado (incluido el que tuvo éxito). El más simple: entrega e intentos son dos líneas independientes.
  2. **La entrega incluye el 1er intento**: el primer intento no se paga aparte; solo los intentos adicionales (2do en adelante) se pagan aparte, haya o no éxito al final.
  3. **Intento fallido reemplaza a entrega**: cada intento fallido cobra su propia tarifa; el intento que sí logra entregar cobra solo la tarifa de entrega (no lleva tarifa de intento aparte).
  La corrida de liquidación **congela la fórmula usada** en el momento de generarse (`DriverSettlementRun.PayoutFormula`), igual que Facturación congela `priceItem`/`priceOver` al generar — si el tenant cambia la fórmula después, las corridas viejas no se recalculan solas.
- **`DriverTrip` (pago por viaje, no por paquete):** un chofer también puede cobrar por viaje — ej. traer un vagón/contenedor del muelle — un cargo plano que no depende de piezas ni de intentos. `DriverTripRate` es una lista abierta por chofer (etiqueta + monto), editable/ampliable desde la misma pantalla de Choferes y tarifas. Cada viaje real (`DriverTrip`) referencia una de esas tarifas y entra a la liquidación del período igual que las entregas.
- **La etiqueta de `DriverTripRate` ya no es texto libre — sale del catálogo `SpecialService` del módulo 1 (Clientes y contratos):** el selector de "tipo de viaje" en Choferes y tarifas se llena con la unión de las etiquetas de servicios especiales de todos los clientes (`allServiceLabels()` en el mock). Esto conecta los dos lados del mismo viaje: el cliente lo pide como un servicio especial de su catálogo (con su propia tarifa, lo que Advance le cobraría si aplicara) y el chofer lo cobra con su propia tarifa por ese tipo de viaje (`DriverTripRate`) — dos maestros, un concepto. Un `DriverTrip` creado directamente desde Entrada de órdenes (ver módulo 2, "entrega especial") congela el monto del chofer al momento de crearse, tomando la tarifa vigente de `DriverTripRate` para ese chofer y esa etiqueta — mismo principio de "freeze at generation" que `BillingRun`/`DriverSettlementRun`.
- **La corrida (`DriverSettlementRun`) agrupa, por chofer y período,** las líneas de entrega (agrupando los `DeliveryAttempt` de cada orden y aplicando la fórmula vigente) más las líneas de viaje, con subtotal por día y resumen final (Entregas vs. Viajes). El ajuste manual por línea queda auditado igual que en Facturación.

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
5. **Dos flujos de aprobación humana** en todo el producto, ambos con la misma máquina de estados simple (generar → revisar → aprobar → exportar): `BillingRun` (cobro a clientes) y `DriverSettlementRun` (pago a choferes, módulo 11A). El resto lo mueven eventos físicos.
6. **Sin APIs pagas de mapas**: OSM + Nominatim + Leaflet/VROOM/OSRM self-hosted, consistente con colavora.
7. **Reutilización de SEPHAS/colavora**: `LookupCode`, capacidades por estatus, MFA/AAL2, DSL de reglas, middleware de idempotencia, offline-first.

## Convenciones de interfaz (aplican a toda pantalla nueva, no son reglas de negocio)

- **Toda tabla debe poder ordenarse por las columnas donde tenga sentido** (fecha, cantidad, monto, estado, código) — clic en el encabezado ordena, segundo clic invierte, con flecha indicando la dirección. No es opcional por pantalla; es el estándar del producto.
- **Todo filtro de tipo "Producto" busca por SKU y por nombre** a la vez (etiqueta `SKU · Nombre`), no solo por uno de los dos.
- **Todo dropdown de selección múltiple** trae buscador propio (tipo select2), no solo la lista cruda.
- **Eliminar/editar con guardas de estatus** siguen el mismo patrón en todo el producto: no se permite si el registro ya está en un estatus que implica que otro proceso lo está usando (recibiendo, despachado, etc.) — nunca un borrado silencioso de algo con actividad encima.
- **Todo modal usa el mismo padding y tipografía** del shell genérico (`.pal`/`.pi`/`.pb`/`.ft`), para que no se sienta como una pantalla aparte.
- **El botón "Limpiar" de los filtros usa el ancho de su contenido**, no se estira a toda la columna del grid.
- **Al cambiar de idioma la interfaz no se reinicia**: se preserva el estado del menú lateral (el grupo que el usuario tenga expandido se queda igual), la pantalla actual y sus filtros.
- **Ninguna pantalla ni tabla genera scroll horizontal.** El contenido usa todo el ancho disponible del stage (sin centrarse con un `max-width` angosto que deje un margen amplio a los lados) y las tablas terminan siempre en bloque, al borde derecho del panel — no se recorta ni queda un scrollbar aparte al fondo. Cuando una tabla tiene muchas columnas (como el detalle de Facturación o de Liquidación), las columnas de texto (nombre, ciudad) envuelven su contenido para ceder espacio y las columnas numéricas no lo hacen (para no partir montos a la mitad); si aun así no cabe, se reduce tipografía/padding de esa tabla puntual antes que dejarla escrolear.
- **Los `.chip` (píldoras de estatus/tipo) nunca envuelven su texto** aunque la columna quede angosta — son etiquetas cortas, partir "Entregada" o "Viaje" a la mitad se ve roto; si no caben, la columna cede espacio de otro lado, no el chip.
- **Toda lista/tabla que se genera desde un ledger de eventos (intentos de entrega, movimientos de inventario) se congela al momento de generarse un documento con aprobación** (`BillingRun`, `DriverSettlementRun`): la corrida guarda el cálculo hecho con las tarifas vigentes en ese momento, no una referencia viva al maestro — si el maestro cambia después, las corridas ya generadas no se recalculan solas. Mismo principio en Facturación (`priceItem`/`priceOver` congelados) y en Liquidación (`PayoutFormula` congelada por corrida).

---

## Bitácora de cambios del mock UI (sesión jul 2026)

Registro de las decisiones y cambios hechos sobre el mockup en esta sesión, para que el diseño y el prototipo no se desincronicen.

### Almacén — tres pantallas nuevas (el grupo queda completo)

- **Kárdex de movimientos** (`ledger`): vista de **solo lectura** del ledger `InventoryTransaction`. Columnas: fecha, hora, tipo (chip por Recepción/Despacho/Transferencia/Ajuste/Cruce de muelle), SKU, producto, cantidad con signo, posición, lote/serie, origen (`RefEntity·RefId`) y usuario. Es la fuente de verdad; recepción, conteo y cross-dock escriben aquí.
- **Conteo cíclico** (`conteo`): **modo informado** (decisión de Luis) — el operador ve la cantidad esperada mientras cuenta; la varianza se calcula al vuelo y, al confirmar, cada línea con diferencia genera un ajuste `InventoryTransaction` enlazado al conteo (`AdjustmentTxnId`). Nada se ajusta fuera del ledger.
- **Cruce de muelle** (`crossdock`): **decisión revisada** — la nota de alcance original dejaba Cross-dock sin pantalla para Advance; Luis pidió construir una **pantalla de demo funcional** de todos modos, marcada visualmente como "Módulo apagado para Advance · demo". Muestra muelles tipados con ocupación, agenda de citas de muelle (`DockAppointment`) y emparejamiento inbound↔outbound (`CrossDockAllocation`) que escribe un movimiento `CROSSDOCK` al ledger; lo no asignado cae a putaway normal.
- **Orden en el menú**: Conteo cíclico va **antes** de Cruce de muelle.

### Inventario — dueño del producto y filtros

- **Dueño del inventario** (`Product.ClientId`): el producto ahora puede tener dueño. En el modal de producto hay un selector "Dueño del inventario" (Propio = Advance, o un cliente 3PL) y en la tabla hay columna **Dueño** (los propios en gris tenue). Los productos que Advance revende quedan como Propio; el equipo médico administrado quedó atado a clientes de ejemplo.
- **Filtros de búsqueda**: Almacén, Ubicación, Producto y Categoría (multi-selección con buscador). El filtro de **Cliente se quitó**: el cliente es el **tenant** que ya se escoge en el encabezado, así que todo lo que se muestra es de ese cliente y filtrar por él era redundante.

### Filtros replicados en Conteo cíclico y Kárdex

- Mismos filtros de Inventario (Almacén, Ubicación, Producto, Categoría) replicados en **Conteo cíclico** y **Kárdex**. El Kárdex mantiene además su **Tipo** de movimiento y **rango de fechas** (Desde/Hasta van de primero).

### Procesar entregas (antes "Caja COD")

- **Renombre**: la pantalla se llama **"Procesar entregas"** (antes "Caja COD"), porque aquí se procesan **tanto órdenes COD como sin COD** (por petición del cliente), no solo cobros COD. El **grupo del menú** pasó de "Dinero" a **"Contabilidad"** (EN: "Accounting"), que abarca Procesar entregas, Facturación y Liquidación a choferes.
- **"Remesa" (definición)**: liquidación del COD al cliente. Advance cobra contra entrega en la calle; ese dinero es del cliente. La remesa es el pago de vuelta (Cobrado − Comisión = Neto a remitir). Teikem **calcula y exporta** la remesa; no emite el pago.
- **Escaneo/procesamiento inline**: se quitaron los dos botones de cabecera ("Escanear cobros" y "Crear remesa"). En su lugar hay un **campo de escaneo + botón Procesar**: se entra o se escanea un número de orden/factura y **Enter procesa automáticamente** (igual que en Entrada de órdenes rápida, y compatible con lectores de barras que envían Enter). El foco entra al campo al abrir la pantalla.
- **Generar remesa**: el botón "Generar remesa" del panel derecho abre un **documento de remesa** (modal tipo documento) con encabezado del cliente, período, detalle de los cobros cuadrados, subtotal, comisión y neto a remitir, con acción de Exportar/Imprimir.
- **Filtros**: Entrega (rango de fechas), Factura (búsqueda LIKE), Cliente, Ciudad y Chofer.
- **Columna Chofer**: muestra el **nombre completo** de la persona (antes solo el código); el código queda como chip pequeño de referencia. Se añadieron columnas Entrega y Ciudad.

### Facturación (pantalla nueva, `BillingRun`)

Diseñada a partir de un reporte real que Luis adjuntó (factura de Advance Logistics a TRUSS PR, órdenes agrupadas por día con desglose de precio). La pantalla replica esa estructura:

- **Nueva corrida**: se escoge Cliente + período (Desde/Hasta) y "Generar corrida" trae las órdenes **entregadas** (`st='deliv'`) de ese cliente en el rango, agrupadas por día — igual que el reporte de referencia.
- **Desglose por línea**: cada orden muestra Referencia, Consignatario, Ciudad, Paquete, Servicio, COD, Piezas, **Base** (cargo por servicio/paquete), **Extra** (piezas adicionales), **Cargo COD** (cuando la orden tiene COD) y **Ajuste** manual editable — mismos cuatro componentes que "Price Item / Price Over / Price COD / Price Adj" del reporte original. Cada día tiene su fila de subtotal.
- **Resumen de facturación**: al final, una tabla de reconciliación agrupada por línea de tarifa (servicio · paquete — pieza base / pieza extra / cargo COD) con Piezas, Tarifa y Monto, que cuadra contra el total de la corrida — igual que la sección "Invoice Summary" del reporte de referencia.
- **Flujo de aprobación** (uno de los dos con paso humano en todo el producto — el otro es Liquidación a choferes, ver más abajo): **Generada → Revisada → Aprobada → Exportada**. El ajuste manual solo es editable mientras la corrida está Generada o Revisada; se bloquea al aprobar.
- **Exportar factura**: abre un documento de factura (encabezado del cliente/período/corrida, tabla de resumen por línea de tarifa, piezas totales, ajustes y total) con acción Exportar/Imprimir — mismo patrón visual que el documento de remesa de Procesar entregas.
- **Corrida de ejemplo con los datos reales del PDF de Luis** (BR-2026-014, TRUSS PUERTO RICO, 2026-03-30 → 2026-04-01, estatus Exportada) para verificar que el diseño reproduce el reporte con fidelidad — reconcilia exacto: 19 órdenes, $353.00 total, 75 piezas.
- **Cantidades calculadas editables con recálculo en cadena**: Piezas, Base, Extra y Cargo COD dejaron de ser texto fijo — ahora son campos editables, igual que el Ajuste que ya existía. Cualquier edición de línea recalcula en vivo, sin necesidad de refrescar la pantalla: el total de esa línea, el subtotal del día correspondiente, el total de la corrida y la tabla de "Resumen de facturación" (que se deriva de esos mismos campos). Sigue la misma guarda de estatus que el Ajuste: solo editable mientras la corrida está Generada o Revisada; se bloquea al Aprobar.

### Entrada de órdenes

- El **buscador de consignatario** del filtro es un dropdown con filtrado tipo LIKE por nombre/número/pueblo; el combobox de consignatario de la fila de entrada se ancla con posición fija para que su lista no la recorte el contenedor con scroll de la tabla.

### Idioma

- **Cross-dock** se traduce como **"Cruce de muelle"** en español (menú, título de pantalla y tipo de movimiento en el Kárdex); en inglés se mantiene "Cross-dock".

### Scroll horizontal y margen — corrección de raíz (además del CSS global)

El fix de CSS global (`.wrap`, `.stage`, `.lst`) resolvió la mayoría de pantallas, pero al hacer editables las columnas de Facturación (ver arriba) la tabla de esa pantalla volvió a desbordar a 1440px y 1280px. La causa real, más profunda que el CSS puntual: `.stage{overflow-y:auto}` sin `overflow-x` explícito **calcula su `overflow-x` en `auto` automáticamente** (regla de la spec CSS: si un eje es `visible` y el otro no, el `visible` se fuerza a `auto`) — así que `.stage` siempre tuvo scroll horizontal latente, invisible mientras el contenido cupiera. Se corrigió declarando `overflow-x:hidden` explícito en `.stage` (para que nunca sea el propio `.stage` el que scrollee) más `min-width:0` en `.stage` y `.panel` (para que los tracks de grid puedan encoger de verdad en vez de forzar el contenido a desbordar). Con eso, una tabla que no cabe se ve forzada a encoger de verdad — de ahí las reglas de `.lst` de arriba (texto envuelve, números no) y una clase `.densetbl` (padding más compacto: `6px 3px`) para tablas con muchas columnas como Facturación y Liquidación.

### Liquidación a choferes + Choferes y tarifas (pantallas nuevas)

Pedido de Luis: diseñar y construir Liquidación a choferes asumiendo que existe un maestro de lo acordado por chofer, por entrega y por intentos (normalmente hasta 2, pero el sistema debe quedar abierto), con cargo opcional por intento también del lado de facturar al cliente, e inventar un mecanismo para saber cuántos intentos tuvo cada entrega. Ver diseño completo en la sección **11A** y la nota en **11** (Facturación). Decisiones tomadas en esta sesión:

- **Nombre**: el grupo de menú se queda como **"Catálogo"** (Luis todavía no decide a qué renombrarlo — sigue conteniendo Clientes y contratos, Flota y mantenimiento y Portal de clientes); la pantalla nueva dentro de ese grupo se llama **"Choferes y tarifas"** (corregido — la primera versión la había llamado "Tarifas de choferes" y el grupo "Choferes y tarifas", al revés de lo que pidió Luis).
- **Bug de alineación de columnas corregido**: tanto en Facturación como en Liquidación, cada día se armaba como un `<table>` HTML separado — el navegador calcula el ancho de columnas por tabla, así que si un día tenía filas con texto más largo (ej. la descripción de un viaje) esa tabla quedaba con columnas más anchas que la del día siguiente, y las columnas de Intentos/Detalle/Ajuste/Total se veían desalineadas verticalmente entre secciones de fecha. Se unificó a **una sola tabla por corrida** con un único `<thead>` y filas separadoras de fecha (`colspan`) dentro del mismo `<tbody>` — ahora el ancho de columna se calcula una sola vez para toda la corrida y todo alinea, sin importar cuánto varíe el contenido día a día.
- **Fórmula de pago configurable, no fija**: en vez de asumir cómo se combinan "pago por entrega" y "pago por intento", la pantalla de Choferes y tarifas trae un selector con los tres modelos razonables (ver 11A) y una vista previa en vivo del cálculo con un chofer de ejemplo, para que quien configure el tenant entienda el efecto antes de guardar.
- **Abierto de verdad, no solo de palabra**: el botón "+ Agregar intento" añade un nivel de tarifa más (intento 3, 4...) en vivo a todos los choferes; el motor de cálculo (`attemptRate`) usa el nivel más alto configurado como fallback si ocurre un intento con número mayor al configurado, así que la corrida nunca se rompe. El mock sembró a propósito una orden con 3 intentos reales para demostrarlo sin tocar nada.
- **Pago por viaje (no solo por entrega)**: Luis señaló que un chofer también cobra por viaje — ej. traer un vagón/contenedor del muelle — un cargo plano ajeno a piezas e intentos. Se agregó `DriverTrip`/`DriverTripRate`: una lista abierta de tipos de viaje con su tarifa, por chofer, editable/ampliable desde la misma pantalla (label + monto + agregar/quitar).
- **Bitácora de intentos de entrega** (`DeliveryAttempt`, nueva): registra cada intento con orden, número, chofer, fecha, resultado y razón de fallo. Es la fuente de verdad tanto para pagarle al chofer (Liquidación) como, a futuro, para cobrarle al cliente un cargo opcional por intento en Facturación — ese segundo uso queda documentado en 11 como mecanismo listo pero no activado todavía (falta el `RateComponent` de tipo intento en el contrato del cliente).
- **Liquidación a choferes** replica el patrón de Facturación al detalle: nueva corrida por chofer + período, líneas agrupadas por día (entregas con su detalle de pago desglosado por intento + viajes), ajuste manual auditado con recálculo en cadena (línea → subtotal del día → total de la corrida → resumen), mismo flujo Generada → Revisada → Aprobada → Exportada, y documento de liquidación exportable (mismo patrón que el documento de factura y la remesa).
- **Principio de diseño #5 actualizado**: pasó de "un solo flujo de aprobación humana" a "dos flujos, misma máquina de estados" — `BillingRun` y `DriverSettlementRun`.

### Servicios especiales por cliente + pantalla "Clientes y contratos" (nueva) + entrega especial en Entrada de órdenes

Pedido de Luis, a partir de la pantalla de Liquidación/Choferes y tarifas: los viajes al muelle (traer un vagón/contenedor) no deberían inventarse solo del lado del chofer — deberían nacer como un servicio especial configurado por cliente, escogible al capturar la orden, y ese mismo catálogo debería alimentar los "tipos de viaje" de Choferes y tarifas en vez de escribirse libre. Además, la pantalla "Clientes y contratos" (que ya existía como entrada de menú sin diseñar) quedó diseñada y construida en este cambio. Ver diseño completo en el módulo **1** (Clientes y contratos) y la actualización del `DriverTrip` en **11A**.

- **`SpecialService` (catálogo nuevo, por cliente):** id, cliente, etiqueta y tarifa — el lado del cliente del mismo concepto que `DriverTripRate` ya modelaba del lado del chofer. Es intencional que sean dos maestros separados y editables independientemente: lo que el cliente paga por el servicio especial no tiene por qué coincidir con lo que Advance le paga al chofer que lo hace (el mock los sembró iguales por conveniencia de la demo, pero `driverTripRateFor()` siempre resuelve del lado del chofer, nunca del catálogo del cliente).
- **Pantalla "Clientes y contratos" (nueva, diseñada en este cambio):** mismo patrón visual que Choferes y tarifas — lista de clientes a la izquierda (con chip de estado activo/inactivo) y detalle a la derecha en dos paneles: **Contrato** (estado, cliente desde, modelo de facturación, SLA de tránsito, límite de crédito, comisión COD, todos editables inline) y **Servicios especiales** (tabla del catálogo de ese cliente con alta rápida, igual que la tabla de tarifas por viaje de Choferes y tarifas).
- **Choferes y tarifas — el "tipo de viaje" dejó de ser texto libre:** tanto el selector para agregar una tarifa de viaje nueva como el de cada fila ya creada ahora son un `<select>` que sale de `allServiceLabels()` (la unión de las etiquetas de `SpecialService` de todos los clientes) — si un tenant no tiene todavía ningún servicio especial configurado, el selector lo dice explícitamente ("Sin servicios especiales — agrégalos en Clientes y contratos") en vez de dejar escribir cualquier cosa.
- **Entrada de órdenes — checkbox "Es una entrega especial" (después de Consignatario, antes de la sección de paquete):** al marcarla, la sección 03 cambia en vivo (sin perder lo ya escrito en las secciones 01/02 — se re-renderiza solo un contenedor interno, `#de_sec3`, no la pantalla completa) y pide **Servicio especial** (filtrado por el cliente escogido en la sección 01) y **Chofer** (de los ya existentes), en vez de tipo de paquete/piezas/COD. El ticket de la orden también cambia sus dos últimas filas para mostrar "Servicio especial" y "Chofer" en vez de "Paquete" y "COD".
- **Al guardar una entrega especial se crean dos registros:** la orden (estatus `route` de una vez, con el chofer ya asignado — **no pasa por Sala de despacho**, tal como pidió Luis) y un `DriverTrip` nuevo cuyo monto se congela en ese momento con `driverTripRateFor(chofer, servicio)` — la tarifa vigente del chofer para ese tipo de viaje, mismo principio de "freeze at generation" que ya aplicaba a `BillingRun`/`DriverSettlementRun`. Ese `DriverTrip` entra a la próxima Liquidación de ese chofer exactamente igual que uno creado a mano — se verificó generando una corrida después de guardar una entrega especial de prueba.
- **Cliente de la orden ahora es un selector real, no un texto fijo:** el campo "Cliente" de Entrada detallada estaba fijo en "Advance Solutions" (una simplificación de una sesión anterior); pasó a ser un `<select>` con los clientes reales del tenant, porque el catálogo de servicios especiales que se muestra en la sección 03 depende de cuál cliente es. Cambiar el cliente con la entrega especial activa refresca el catálogo mostrado.

### Modelo de facturación por cliente — de campo de texto a componentes independientes con checkbox

Corrección de Luis sobre la pantalla "Clientes y contratos": el campo "Modelo de facturación" no debe ser texto libre entrado por el usuario (así había quedado en el cambio anterior, con valores como "Por servicio + pieza extra + COD"). Debe ser un conjunto de **componentes independientes, cada uno con su propio checkbox**, y cada uno que se marca despliega su propia sección de configuración. Ver diseño completo (y la razón de cada componente) en el módulo **1**.

- **Cinco checkboxes, no mutuamente excluyentes:** Por servicio, Pieza extra con precio especial, Cargo por despacho, Cargo por COD, Servicios especiales. Un cliente puede tener cualquier combinación marcada — se sembraron los 5 clientes de ejemplo con combinaciones distintas a propósito (Advance: 4 de 5; AXISCARE: por servicio + pieza extra + despacho + COD, pero **sin** servicios especiales; TRUSS: por servicio + servicios especiales, nada más; Beauty Code e INTECHSOL: solo por servicio) para que la pantalla demuestre que de verdad son independientes.
- **"Por servicio" (`svcRates`):** tabla nueva — Servicio, Tipo de paquete, Tarifa. Puede tener varias filas (una tarifa por cada combinación servicio+paquete que el cliente factura distinto), con alta y edición inline (los selects de Servicio/Paquete son editables después de creada la fila, no solo al agregar).
- **"Pieza extra con precio especial" (`extraPieceRates`):** tabla nueva — Servicio, Tipo de paquete, Desde pieza, Hasta pieza, Tarifa. A propósito soporta **más de un rango** por servicio+paquete (ej. piezas 2–5 a una tarifa, piezas 6 en adelante a otra) — se sembró a Advance Solutions con ese caso exacto para demostrarlo.
- **"Cargo por despacho" (`dispatchFee`):** un solo campo, monto fijo.
- **"Cargo por COD" (`codFeeType`/`codFeeValue`):** selector Fijo ($) / Porciento (%) + el valor — el campo de tipo existe porque el diseño no debe asumir una sola forma de cobrarlo, aunque **Advance Logistics lo maneja fijo hoy** (así se sembró su cliente de ejemplo); se sembró a AXISCARE con "Porciento" para demostrar que el otro modo también renderiza bien (el label del campo de valor cambia entre "$" y "%" según el tipo escogido). Es un cargo de **facturación al cliente**, distinto de la comisión de remesa COD (8%, módulo 11B) que Advance retiene del dinero cobrado en la calle antes de devolverlo — dos "COD %" que no deben confundirse, aunque convivan en el mismo tenant.
- **"Servicios especiales" (`especiales`):** ahora la tabla de servicios especiales (ya existente, ver entrada anterior de esta bitácora) solo aparece si este checkbox está marcado — antes aparecía siempre para todo cliente. Se probó explícitamente que desmarcarlo la oculta y que los datos no se pierden (si se vuelve a marcar, el catálogo sigue ahí).
- **Lista de clientes (columna izquierda):** el resumen que aparecía bajo el nombre de cada cliente pasó de mostrar el viejo texto de `billingModel` a un resumen calculado (`billingSummary()`) que lista los componentes marcados en el idioma activo — se actualiza solo al marcar/desmarcar cualquier checkbox.
- **Campo "Comisión COD (%)" eliminado del panel Contrato:** existía desde el cambio anterior pero no estaba conectado a ningún cálculo real (la remesa COD de Procesar entregas sigue usando el 8% fijo del tenant); quedaba además muy cerca en nombre del nuevo "Cargo por COD", lo que hubiera confundido dos conceptos de dinero distintos. Se removió en vez de dejarlo como ruido.

### Tipo de servicio especial: de texto libre a dropdown

Corrección de Luis: al agregar (o editar) un servicio especial de un cliente, el nombre del servicio no debe teclearse libre — debe escogerse de un dropdown con los tipos de servicio especial que ya existen en el tenant.

- **Selector con los tipos ya usados** (`allServiceLabels()`, la misma fuente que ya alimentaba el selector de "tipo de viaje" en Choferes y tarifas) tanto en la fila nueva como en cada fila ya creada del catálogo de un cliente — antes la fila existente mostraba el nombre como texto plano, sin poder editarse; ahora es un `<select>` igual que el resto de la tabla.
- **"+ Nuevo tipo de servicio especial…"** como última opción del selector: al escogerla se revela un campo de texto para nombrar el tipo nuevo una sola vez. Sin esta opción no habría manera de que el catálogo de tipos creciera nunca — es la única puerta para dar de alta un nombre que ningún cliente tiene todavía. En un tenant sin ningún servicio especial creado (lista vacía), el selector arranca directamente en "+ Nuevo tipo" con el campo de texto ya visible, para no bloquear el primer alta.
- **Un tipo nuevo creado para un cliente queda disponible de inmediato para los demás** (y para Choferes y tarifas) — se probó creando "Entrega en frío" desde Advance Solutions y verificando que apareció en el dropdown de tipo de viaje de Choferes y tarifas sin recargar nada.

