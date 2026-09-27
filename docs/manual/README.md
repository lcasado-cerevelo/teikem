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

## Preguntas frecuentes

Ver [faq.md](faq.md) para las preguntas y mensajes de error acumulados de cada lote.
