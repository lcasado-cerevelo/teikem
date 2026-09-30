# Manual funcional de Teikem

## Qué es Teikem

Teikem es una plataforma de logística para varias compañías (multi-tenant): cada empresa que la usa (cada
"tenant") ve solo sus propios datos, con su propia configuración de catálogos, estatus, usuarios y roles. Un
mismo usuario puede pertenecer a más de una compañía y elegir con cuál trabajar al iniciar sesión.

La plataforma se construye por lotes. Cada lote agrega un módulo de negocio (órdenes, almacén, flota, alquiler
de equipos, compras, portal de clientes, etc.) sobre una base común de seguridad, catálogos, estatus,
auditoría, campos personalizados y análisis (vistas, indicadores, gráficos). Este manual documenta, capítulo
por capítulo, lo que cada lote deja disponible para el usuario final y para soporte.

## Capítulos

1. [01 — Plataforma y seguridad](01-plataforma-y-seguridad.md): acceso y sesión, MFA, mi perfil y permisos,
   usuarios y roles del tenant, catálogos, estatus, puntos de contacto, campos personalizados, vistas,
   indicadores, gráficos y Pulso del día, auditoría y seguridad, módulos por tenant, configuración de la
   compañía, administración de plataforma.
2. [02 — Clientes y contratos](02-clientes-y-contratos.md): expediente del cliente (alta compuesta, perfil, numeración,
   contactos, estatus, baja), consignatarios y localizaciones (direcciones física/postal, almacenes, compartidas), contratos
   (contrato vigente, modelo de facturación con 5 componentes, despacho, COD, SLA, estatus y efectos), tarifas por servicio y
   pieza extra con historial efectivo-fechado, cotización, servicios especiales y tipos compartidos, usuarios de portal del
   cliente (incluye el portal multi-cliente del Lote 3), permisos y módulos, fuentes de análisis y auditoría.
3. [03 — Órdenes de transporte](03-ordenes-de-transporte.md): captura de órdenes (entrada rápida, detallada y de entrega
   especial), numeración de los cuatro identificadores, cotización y chequeo de crédito al confirmar (con autorización por
   permiso cuando se excede), estatus y transiciones, importador de órdenes por plantilla (validar → confirmar), el
   bloqueo de lo nuevo para un cliente dado de baja, permisos y módulos, fuentes de análisis y auditoría.
4. [04 — Flota, choferes y mantenimiento](04-flota-choferes-mantenimiento.md): vehículos y sus documentos, choferes
   (identidad, licencias, certificaciones, dispositivos, zonas de despacho), documentos por vencer y disponibilidad
   para despacho, mantenimiento preventivo y órdenes de trabajo, bitácora de combustible, tarifas del chofer (por
   entrega, por intento, por viaje) y política de pago con vista previa, viajes pagados al chofer, la entrega
   especial con chofer (extiende el Lote 3), estatus y transiciones, permisos y módulos, fuentes de análisis y
   auditoría.
5. [05 — Trips y rutas](05-trips-y-rutas.md): planificación diaria (alta con numeración y chofer/salida por
   defecto, ficha, edición de cabecera, eliminar ruta, reasignación en bloque), consolidación de órdenes en la
   ruta y lista "Sin asignar", optimización en tres fases (motor HEURISTIC), reordenamiento manual y pin por
   parada, despacho y salida (extiende la costura del futuro Lote 7), zonas de despacho por código postal/rango/
   municipio (extiende el Lote 4), estación de escaneo Outbound, "Planificar el día", monitor de rutas, estatus y
   transiciones, permisos y módulos, fuentes de análisis y auditoría.
6. [06 — Inventario y almacén](06-inventario-y-almacen.md): almacenes, zonas, posiciones y muelles; productos,
   categorías, lotes y series; inventario (saldos, Kárdex con cantidad con signo, ajustes, transferencias,
   genealogía, rastro de serie y conciliación ledger↔saldo); recepción (avisos de llegada y recibos, incluida la
   recepción contra orden de compra); cola de tareas de almacén (putaway dirigido y reabasto); conteo cíclico en
   modo informado; recolección y empaque ad hoc (Pick & Pack); compras mínimas (proveedores, órdenes de compra y
   resolución de faltantes); cruce de muelle en modo demo (citas y planes), estatus y transiciones, permisos y
   módulos.
   Lote 11 (sección 1, Almacenes y ubicaciones): **cupo máximo** de la posición y su **estado de ocupación**
   (vacía, parcial, llena, ocupada sin cupo), ocupación calculada por zona, código de zona editable, listado de
   posiciones paginado con búsqueda por código, zona, pasillo, rack, nivel o posición, y el catálogo de **localidades
   postales** (42.522 ZIP de EE. UU. y Puerto Rico) para llenar ciudad, estado, código postal y país.
   Lote 12 (secciones 1.2, 2 y 8): **marca y modelo** del producto, filtros nuevos de la lista de productos (varios
   almacenes, SKU, nombre, marcas, "con serie" y "series incompletas") y endpoint de marcas, filtros de Kárdex por marca y
   nombre, filtros múltiples de órdenes de compra, **asignación de cupo en bloque** de posiciones y resumen del **cupo
   estimado** que trae la migración de Advance Depot.
7. [07 — Pulso del día y Actividad reciente (Lote 7A: Almacén)](07-pulso-y-actividad.md): panel de eventos
   recientes de Almacén (catálogo de 23 eventos, obligatorio/opcional, ventana 24h/48h/hoy, filtro por módulo),
   filtro "bajo mínimo" de productos, e indicadores y gráfico nuevos de Almacén en el Pulso del día (Unidades
   recibidas, Conteos con diferencia, Movimientos de inventario por tipo). Operación (7B) y Contabilidad (7C)
   agregan su propia pestaña más adelante. Lote F8a (sección 3): **Pulso del día: paneles, permisos y orden** —
   permisos `pulse.*` por panel, registro de paneles, qué indicadores y gráficos ve cada usuario (fuente legible y
   módulo encendido) y orden/ocultos en dos niveles (compañía y usuario).
8. [8A — Backend de la app de almacén](08-aparatos-y-sincronizacion.md): idempotencia de escrituras
   (`Idempotency-Key`), aparatos de confianza (alta con código de registro, enroll y heartbeat anónimos,
   desactivar/reactivar), PIN por usuario y login por aparato (bloqueo por intentos), sincronización por
   diferencia (productos, posiciones, órdenes de compra, avisos de llegada, tareas, categorías), búsqueda por
   código de barras, y las operaciones atómicas de la cola del aparato (recibo, recolección y empaque, conteo en
   lote y a ciegas). Es solo el backend: la app instalable no se construye en este lote.
9. [9 — App de almacén](09-app-almacen.md): la app instalable en sí (`app-almacen/`, Android/Expo) — registrar el
   aparato y entrar con PIN, cómo funciona sin señal (documentos propios del aparato vs. recursos compartidos, un
   documento a la vez), Inicio, Recibir, Acomodar, Despacho (solo clientes 3PL por ahora), Conteo (a ciegas según
   permiso), Consultar (con caché para responder sin señal) y Sincronización (pendientes, con error, reintentar).
10. [10 — Migración de datos heredados](10-migracion-de-datos.md): comando de línea de comandos para aprovisionar
    una compañía desde QuickBooks Desktop y el WMS heredado MSWM, simulación (`--dry-run`), refresco de maestros sin
    tocar el saldo inicial (`--update`) y recrear la base en blanco para repetir la migración (`db-reset`). Lote 12
    (sección 4): **cupo estimado de las posiciones** de Advance Depot desde el historial del WMS anterior (regla, reporte
    `-cupos.csv` y comportamiento de `--update`).

## Manual de pantallas (frontend web)

Capítulos escritos para el usuario final y soporte sobre las pantallas reales del frontend web (`web-app/`), con
capturas. Formato y decisiones de cada lote de frontend en `docs/frontend/loteFN-decisiones.md`.

1. [F1 — Acceso, menú, Pulso del día y Mi cuenta](frontend/f1-nucleo-y-mi-cuenta.md): iniciar sesión, verificación en
   dos pasos, elegir compañía, menú y cabecera (grupos por permisos y módulos, cambio de idioma), Pulso del día
   (indicadores, gráficos, mi rango de fecha), Mi cuenta (perfil, contraseña, verificación en dos pasos, sesiones
   activas), reautenticación (AAL2), pantallas "Sin permiso" y "Módulo apagado".
2. [F6 — Almacén e inventario (mínimo) + consulta de órdenes](frontend/f6-almacen-e-inventario.md): panel "Almacén" en
   Pulso del día, almacenes (zonas, posiciones y muelles), productos y categorías, inventario (saldos, Kárdex, ajustes,
   transferencias, genealogía, rastro de serie y conciliación), recepción (recibos y avisos de llegada), tareas de
   almacén, conteo cíclico, recolección y empaque, proveedores, órdenes de compra, citas de muelle, cruce de muelle
   (planes) y consulta de órdenes de transporte de solo lectura.
   Lote 11: pie común de todas las tablas (rango, "Filas por página" y **Exportar** a Excel, CSV y PDF), lista de
   Almacenes en maestro-detalle con "Zonas de este almacén", ficha con Zonas y Posiciones filtrables (cupo y
   ocupación) y la pantalla **Ubicaciones** (recuadros de ocupación por zona). Capturas pendientes.
   Lote 12: la pantalla se llama ahora **Posiciones** (clic en la fila para editar, botón **Asignar cupo**), **Productos e
   inventario** (indicadores clicables, filtros, marca y modelo, bloque "Añadir ajuste" con nota obligatoria y reportes PDF
   de inventario y de ajustes), **Proveedores** (antes de Compras, filtros, teléfono con máscara, baja con ícono) y
   **Compras** (filtros múltiples, alta con almacén obligatorio y reglas de líneas). Capturas pendientes.
3. [F7A — Pulso: panel Almacén con filtro y Actividad reciente](frontend/f7a-pulso-almacen-y-actividad.md): filtro de
   almacén y de categoría o producto en el panel "Almacén" de Pulso (con enlaces a Inventario ya filtrado), y panel
   nuevo "Actividad reciente" (pestañas por módulo, ventana de tiempo, interruptor "Solo obligatorios", buscador libre
   y enlaces a la ficha de cada evento).
4. [F8a — Menú completo, marca, Pulso por paneles, Sistema y Análisis](frontend/f8a-menu-sistema-analisis-y-marca.md):
   los 7 grupos del menú de la maqueta y la pantalla "pendiente", la marca Teikem, paleta de comandos, Pulso del día
   organizado en dos niveles ("Organizar mi Pulso" / "Organizar el de la compañía"), Indicadores y Gráficos, Roles y
   usuarios (con el PIN de los aparatos de almacén y la contraseña temporal), Aparatos móviles (código autogenerado),
   Catálogos de valores (ajustar/restaurar) y Mi cuenta → PIN de la app.

## Preguntas frecuentes

Ver [faq.md](faq.md) para las preguntas y mensajes de error acumulados de cada lote. La sección "Lote 11" recoge los mensajes nuevos de Almacén (cupo, ocupación, código de zona, catálogo de
ciudades, exportación) y las preguntas sobre "sin cupo configurado", exportar tablas, ciudades en mayúsculas y por qué el
código y la zona de una posición no cambian. La sección "Lote 12" recoge los mensajes de marca y modelo, del ajuste con
nota, del teléfono de proveedor, de la orden de compra, del cupo en bloque y de los reportes PDF, y las preguntas sobre el
reporte de inventario (productos con 0, valor "—"), el reporte de ajustes (saldos iniciales), el origen del cupo de las
posiciones y cómo corregirlo en bloque.
