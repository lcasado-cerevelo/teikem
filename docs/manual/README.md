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

## Preguntas frecuentes

Ver [faq.md](faq.md) para las preguntas y mensajes de error acumulados de cada lote.
