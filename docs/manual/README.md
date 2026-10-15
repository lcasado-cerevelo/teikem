# Manual funcional de Teikem

## Qué es Teikem

Teikem es una plataforma de logística para varias compañías (multi-tenant): cada empresa que la usa (cada
"tenant") ve solo sus propios datos, con su propia configuración de listas, estatus, usuarios y roles. Un
mismo usuario puede pertenecer a más de una compañía y elegir con cuál trabajar al iniciar sesión.

La plataforma se construye por lotes. Cada lote agrega un módulo de negocio (órdenes, almacén, flota, alquiler
de equipos, compras, portal de clientes, etc.) sobre una base común de seguridad, listas, estatus,
auditoría, campos personalizados y análisis (vistas, indicadores, gráficos). Este manual documenta, capítulo
por capítulo, lo que cada lote deja disponible para el usuario final y para soporte.

## Capítulos

1. [01 — Plataforma y seguridad](01-plataforma-y-seguridad.md): acceso y sesión, MFA, mi perfil y permisos,
   usuarios y roles del tenant, listas, estatus, puntos de contacto, campos personalizados, vistas,
   indicadores, gráficos y Pulso del día, auditoría y seguridad, módulos por tenant, configuración de la
   compañía, administración de plataforma.
   Región y formatos (2026-10, sección 11.1): región Puerto Rico o Estados Unidos con su juego de valores, zona horaria de la
   compañía (con la que cuenta "hoy"), moneda y formatos de fecha, hora, números y teléfono, cada uno cambiable por separado,
   con sus validaciones y mensajes.
   Lote F10 (secciones 1.5, 9 y 9.1): **sesiones de toda la compañía** (listar con `admin.audit`, revocar una o las demás con
   `admin.users`, IP de la sesión) y la **actividad unificada** con total real, búsqueda en la base y códigos de tipo y resultado.
   Lote 19 (secciones 11 y 11.2): **marca por compañía en el servidor** — los colores se validan con las mismas reglas que la
   pantalla (temas, contraste WCAG, separación de matiz, campos y tamaño) y los **cuatro logos** (lockup y marca cuadrada, cada uno
   con su variante para fondo oscuro; SVG, PNG, JPG o WebP de hasta 512 KB) se guardan en la base y se validan por su contenido real
   (SVG sin contenido activo ni referencias externas), con sus mensajes 400/413/415; y el **409 al repetir un feriado**.
   Lote 42 (sección 3.6): **aislamiento entre compañías** — cómo se garantiza que cada compañía vea solo lo suyo, por qué desde el
   2026-10-12 las listas grandes son más rápidas sin cambiar lo que se ve, y qué hacer si la lista de órdenes sigue lenta (`db-update`).
2. [02 — Clientes y contratos](02-clientes-y-contratos.md): expediente del cliente (alta compuesta, perfil, numeración,
   contactos, estatus, baja), consignatarios y localizaciones (direcciones física/postal, almacenes, compartidas; importación
  de consignatarios por CSV con plantilla, validar → confirmar, Lote 38; Excel, «(No usar)», columnas unidas, cliente por defecto y persona de contacto, Lote 45, §2.2; pueblo, estado y ZIP con la lista de códigos postales y variantes de mapeo, Lote 46, §2.3), contratos
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
6. [06 — Inventario y almacén](06-inventario-y-almacen.md): almacenes, zonas, posiciones y plataformas; productos,
   categorías, lotes y series; inventario (saldos, Kárdex con cantidad con signo, ajustes, transferencias,
   genealogía, rastro de serie y conciliación ledger↔saldo); recepción (avisos de llegada y recibos, incluida la
   recepción contra orden de compra); cola de tareas de almacén (putaway dirigido y reabasto); conteo cíclico en
   modo informado; recolección y empaque ad hoc (Pick & Pack); compras mínimas (proveedores, órdenes de compra y
   resolución de faltantes); cruce de plataforma en modo demo (citas y planes), estatus y transiciones, permisos y
   módulos.
   Lote 21 (sección 6, Conteo cíclico): **conteo por producto**, captura original y **corrección del supervisor** con evidencia,
   **vista previa** de la reconciliación, lista **Por revisar**, **cierre en bloque** de los conteos que cuadran y **posiciones
   provisionales** creadas desde el conteo (servidor; la web y la app lo usan en sus propios lotes).
   Lote 25 (sección 6.x, Conteo cíclico; app §7.2): **conteo abierto con varios productos** — `allowEmpty` sin producto abre un conteo vacío,
   `GET /cycle-counts/{id}/product-bins` dice dónde está cada producto (sin cantidades) y agregar una línea **sin posición** usa la única con
   existencia (o pide elegir); app y web arman UN conteo escaneando productos.
   Lote 26 (sección 2.1, Productos; Rentas R0): **convertir a serie** — `POST /products/{publicId}/convert-to-serial` pasa un producto
   sin seguimiento con movimientos a seguimiento por serie, capturando una serie por unidad en mano en cada posición (ajustes `−`/`+` con
   el motivo de sistema `TRACKING_CONVERSION`, neto cero, en una sola transacción), con sus mensajes 400/403/404/409/422.
   Informe **Productos por posición** (sección 1.5, Almacenes y ubicaciones; reemplaza a las hojas de posición del Lote 23): consulta
   `GET .../bin-products` con los filtros del listado de posiciones, los productos con existencia de cada posición (SKU, nombre y código de
   barras) para imprimir y escanear desde el papel; sin estado (servidor y web).
   Lote 11 (sección 1, Almacenes y ubicaciones): **cupo máximo** de la posición y su **estado de ocupación**
   (vacía, parcial, llena, ocupada sin cupo), ocupación calculada por zona, código de zona editable, listado de
   posiciones paginado con búsqueda por código, zona, pasillo, rack, nivel o posición, y la lista de **localidades
   postales** (42.522 ZIP de EE. UU. y Puerto Rico) para llenar ciudad, estado, código postal y país.
   Lote 12 (secciones 1.2, 2 y 8): **marca y modelo** del producto, filtros nuevos de la lista de productos (varios
   almacenes, SKU, nombre, marcas, "con serie" y "series incompletas") y endpoint de marcas, filtros de Kárdex por marca y
   nombre, filtros múltiples de órdenes de compra, **asignación de cupo en bloque** de posiciones y resumen del **cupo
   estimado** que trae la migración de Advance Depot.
   Lote 13 (secciones 4, 5 y 7): **ciclo de estatus del recibo** (Esperado, Recibiendo, Discrepancia, Completado, Completado con
   diferencia y Acomodado) con sus transiciones y lo que bloquea cada uno, encabezado del recibo editable (transporte, referencia,
   posición de recepción por defecto), esperado en ciegos y devoluciones (entra al inventario lo recibido), filtros de la lista
   (`variance`, `phase`) y de los avisos de llegada, dónde se trabaja cada tipo de tarea, y la recolección y el empaque con su
   tabla de transiciones, validaciones y predeterminados de la compañía.
   Lote 14 (secciones 3, 6 y un aviso en la 8): **Kárdex** con filtros nuevos (dueño, motivo, dirección, almacén de origen y de destino, "solo
   manuales"), **resumen** de entradas y salidas, **detalle de un movimiento** con su documento de origen y días locales de Puerto Rico;
   **ajustes Subir/Bajar** con motivos por dirección y transferencias con lote y serie; **conciliación automática** en segundo plano y
   **descuadres** Kárdex ↔ saldo (estatus Pendiente, Resuelto, Descartado y Se corrigió solo, sus transiciones, lo que bloquea cada uno y los
   mensajes); **conteo cíclico** con estatus Pendiente, Contado, Concordancia y Diferencia, confirmación en un solo paso, lista con total,
   asignación y **"Conteo de lo cambiado"** (ventana, tope de 200 posiciones y sus mensajes). La pantalla "Ajustes de inventario" (faltantes
   de compra) salió del menú: los faltantes se resuelven en la ficha de la orden de compra.
   Lote 16 (secciones 1.4, 4.1, 5, 7 y 9): **modo de recepción del almacén** (con acomodo o directo a posición) y **posición de recepción por defecto**; **recibo directo a posición**
   (posición destino por línea, sugerencias con cupo, "Usar posiciones sugeridas", cuarentena en devoluciones, cupo que solo avisa, Completado → Acomodado en el mismo momento, sin tareas) con sus
   validaciones, estatus y lo que bloquea; el acomodo pendiente en almacenes directos; el selector de posiciones de Recolección (solo donde hay existencia); el cruce de plataforma que no aplica a
   recibos directos ya confirmados; y el formato de los números (coma de miles y `$`).
   2026-10-11 (sección 7b): **Despacho manual** `DMA-#####` — salida de inventario sin entrega con motivo obligatorio (lista editable
   «Motivo del despacho manual») y nota libre, permiso nuevo `warehouse.issue`, ficha consultable, filtro `kind` de la lista de
   recolecciones, Kárdex `DMA-… · motivo`, y sus mensajes 400/403/404/409/422.
   2026-10-11 (sección 7c): **Motivo por default del despacho manual** — ajuste de la compañía (`defaultManualIssueReason`,
   `admin.tenant`) que la app y la web preseleccionan (`isDefault` en `/manual-issues/reasons`); el servidor sigue exigiendo motivo; un
   default deshabilitado se ignora.
7. [07 — Pulso del día y Actividad reciente (Lote 7A: Almacén)](07-pulso-y-actividad.md): panel de eventos
   recientes de Almacén (lista de 23 eventos, obligatorio/opcional, ventana 24h/48h/hoy, filtro por módulo),
   filtro "bajo mínimo" de productos, e indicadores y gráfico nuevos de Almacén en el Pulso del día (Unidades
   recibidas, Conteos con diferencia, Movimientos de inventario por tipo). Operación (7B) y Contabilidad (7C)
   agregan su propia pestaña más adelante. Lote F8a (sección 3): **Pulso del día: paneles, permisos y orden** —
   permisos `pulse.*` por panel, registro de paneles, qué indicadores y gráficos ve cada usuario (fuente legible y
   módulo encendido) y orden/ocultos en dos niveles (compañía y usuario). Lote 14 (sección 4): el panel **Necesita tu atención**
   (`pulse.attention`, los 5 descuadres más antiguos, "Todo en orden") y la cifra de **Conteos abiertos** con el total real.
   Lote 15 (sección 5): la franja **Almacén hoy** (recibido, salida y conteos con diferencia de hoy y de los últimos 7 días en hora de Puerto
   Rico, bajo mínimo de ahora; `GET /inventory/pulse/days`, panel `WAREHOUSE_DAY`), las **filas fijas**, los indicadores en una **fila por módulo**,
   los gráficos **siempre dibujados** (con "Otras" pasado el 8.º grupo), los **2 gráficos de almacén de la compañía** (editables por
   `analytics.manage`, no vuelven si se borran), el indicador "Descuadres pendientes" y los **días de Puerto Rico** en todos los rangos.
8. [8A — Backend de la app de almacén](08-aparatos-y-sincronizacion.md): idempotencia de escrituras
   (`Idempotency-Key`), aparatos de confianza (alta con código de registro, enroll y heartbeat anónimos,
   desactivar/reactivar), PIN por usuario y login por aparato (bloqueo por intentos), sincronización por
   diferencia (productos, posiciones, órdenes de compra, avisos de llegada, tareas, categorías), búsqueda por
   código de barras, y las operaciones atómicas de la cola del aparato (recibo, recolección y empaque, conteo en
   lote y a ciegas). Es solo el backend: la app instalable no se construye en este lote.
9. [9 — App de almacén](09-app-almacen.md): la app instalable en sí (`app-almacen/`, Android/Expo) — registrar el
   aparato y entrar con PIN, cómo funciona sin señal (documentos propios del aparato vs. recursos compartidos, un
   documento a la vez), Inicio, Recibir, Acomodar, Despacho (inventario propio o de un cliente 3PL; «Completar despacho» sin empacar o «Empacar»), Conteo (a ciegas según
   permiso), Consultar (con caché para responder sin señal) y Sincronización (pendientes, con error, reintentar).
   Lote 16: Recibir en un almacén **directo a posición** (paso "Escanea la posición destino", validación sin señal, bloqueo por dos destinos del mismo producto), descarga de las posiciones del
   almacén y heartbeat en cada pasada de sincronización.
   Lote A3: lector del Zebra sin teclado ("escanear = Aceptar" en todas las pantallas, botón ⌨, indicador "Lector: …" en
   Sincronización), margen para la barra del aparato, Inicio en dos columnas, letras más grandes, tocar un producto en el
   conteo, y los formatos de la compañía (Región y formatos) guardados en el aparato.
   Lote A5: contar por producto exige al menos una cantidad escrita (0 vale); en Despacho la cantidad va antes de escanear la
   posición (sin cantidad, la lectura no agrega la línea y avisa).
   Lote A6: Sincronización explica la captura de conteo rechazada porque el supervisor ya corrigió una línea (409) y ofrece
   "Actualizar el conteo".
   Lote A7: si el lote trae líneas corregidas, las libres se guardan y Sincronización muestra un aviso con las no guardadas (el 409 queda
   solo cuando todas están corregidas).
   Lote A9: pantalla chica del Zebra — la pantalla del PIN se desplaza (y compacta el teclado si el aparato es bajo), el campo que se escribe
   queda encima del teclado en pantalla en todas las pantallas con campos, y la calculadora del conteo tiene un icono claro, el título en una
   línea y una flecha ← para volver (antes el enlace "Cantidad directa").
10. [10 — Migración de datos heredados](10-migracion-de-datos.md): comando de línea de comandos para aprovisionar
    una compañía desde QuickBooks Desktop y el WMS heredado MSWM, simulación (`--dry-run`), refresco de maestros sin
    tocar el saldo inicial (`--update`) y recrear la base en blanco para repetir la migración (`db-reset`). Lote 12
    (sección 4): **cupo estimado de las posiciones** de Advance Depot desde el historial del WMS anterior (regla, reporte
    `-cupos.csv` y comportamiento de `--update`).
    La marca de QuickBooks (columna `Brand`) pasa a la marca del producto (sección 1).
    Lote 30 (Rentas RM, sección 6): **seguimiento al migrar** — opción `products.trackingFromColumns` (casillas `Serial`/`Lot` de
    QuickBooks, apagada por defecto), un producto con saldo inicial se crea sin seguimiento y queda como "Candidato a Convertir a serie",
    uno existente nunca cambia; evaluación de las series de Depot en `docs/migracion/depot-series-y-rentas.md`.
11. [11 — Rentas (submódulo de Almacén)](11-rentas.md): Lote 27 (Rentas R1) — rentas de **equipos propios con número de serie** a una
    localidad del cliente: alta en Borrador (REN-#####, contrato, transporte estimado, tarifa fija o por tiempo por equipo, solo como dato),
    edición, equipos por serie, **programar** (reserva las series), **despachar** (transferencia a la posición **EN-RENTA**, serie "En renta":
    en mano igual, disponible 0), **extender** la fecha de recogido con bitácora y tarifa nueva (`rental.extend`), **cancelar** (solo antes
    del despacho), "por vencer" y "vencidas", estatus y transiciones, permisos (`rental.extend`, `rental.return`) y el módulo "Rentas" que
    depende de Inventario y trazabilidad.
    Lote 28 (Rentas R2, secciones 5, 6 y 7): **devolución de renta** DRN-##### (`rental.return`; total o parcial, al término o anticipada,
    con motivo, condición, posición de destino —también otro almacén— y si pasa por proceso), **proceso configurable** del equipo devuelto
    (Pendiente → Inspección → Limpieza → Pruebas → Lista, con Reparación y Esperando piezas; `rental.maintenance`; "Lista" libera el equipo
    y "Dar de baja" lo saca del inventario con motivo Daño y pide además `inventory.adjust`) y los equipos en renta en el **conteo
    cíclico** (D7: EN-RENTA no se cuenta, 422; una serie en renta en un conteo, 409).
    Lote 29 (Rentas R3, sección 10): **reportes, indicadores y avisos** — fuentes de Análisis Rentas, Devoluciones de renta y Proceso de
    equipos devueltos (campos, fechas y relaciones), vistas de sistema "Equipos en renta por cliente", "Rentas por vencer (7 días)",
    "Rentas vencidas", "Devoluciones de renta por motivo" y "Equipos en proceso", indicadores "Rentas por vencer (7 días)" y "Rentas
    vencidas", gráfico "Devoluciones de renta por motivo" y el aviso de rentas vencidas o por vencer en "Necesita tu atención"; todo con
    `rental.view` y oculto con el módulo Rentas apagado.
12. [12 — Impresoras y labels](12-impresoras-y-labels.md): Lote 43 (F-G1, 2026-10-12) — **impresoras de la compañía** (perfil: uso labels o
    documentos fijo, tamaño 4×6/4×4/4×2 o carta, conexión **directo con QZ Tray** o PDF del navegador, nombre en el sistema, girar 90°,
    predeterminada por uso), **plantillas de label** (los 9 elementos del constructor con posición y tamaño; el código de barras es el
    **número de cada paquete**, un label por paquete), opciones de impresión de la **estación**, ajuste de la compañía **«Imprimir labels
    directo a la impresora»**, bandera del cliente **«Imprimir la factura automáticamente»** (solo la preferencia), permiso
    `printers.manage`, mensajes 400/404/409 y, para soporte, **QZ Tray y el certificado de Teikem** (generar, configurar `Printing:Qz` e
    instalar `override.crt`). El servidor nunca se conecta a una impresora.
    Lote 44 (F-G2, sección 10): **labels de las órdenes** (uno por pieza con el número del paquete de su línea; canceladas y piezas sin número
    avisadas; sin tope), **waybill summary**, **packing list** e **informe de faltantes** en carta, y el **escaneo y la búsqueda por número de
    paquete** (cuarta precedencia, mensaje propio si se repite; índice nuevo).

## Manual de pantallas (frontend web)

Capítulos escritos para el usuario final y soporte sobre las pantallas reales del frontend web (`web-app/`), con
capturas. Formato y decisiones de cada lote de frontend en `docs/frontend/loteFN-decisiones.md`.

1. [F1 — Acceso, menú, Pulso del día y Mi cuenta](frontend/f1-nucleo-y-mi-cuenta.md): iniciar sesión, verificación en
   dos pasos, elegir compañía, menú y cabecera (grupos por permisos y módulos, cambio de idioma), Pulso del día
   (indicadores, gráficos, mi rango de fecha), Mi cuenta (perfil, contraseña, verificación en dos pasos, sesiones
   activas), reautenticación (AAL2), pantallas "Sin permiso" y "Módulo apagado".
2. [F6 — Almacén e inventario (mínimo) + consulta de órdenes](frontend/f6-almacen-e-inventario.md): panel "Almacén" en
   Pulso del día, almacenes (zonas, posiciones y plataformas), productos y categorías, inventario (saldos, Kárdex, ajustes,
   transferencias, genealogía, rastro de serie y conciliación), recepción (recibos y avisos de llegada), tareas de
   almacén, conteo cíclico, recolección y empaque, proveedores, órdenes de compra, citas de plataforma, cruce de plataforma
   (planes) y consulta de órdenes de transporte de solo lectura.
   Lote 11: pie común de todas las tablas (rango, "Filas por página" y **Exportar** a Excel, CSV y PDF), lista de
   Almacenes en maestro-detalle con "Zonas de este almacén", ficha con Zonas y Posiciones filtrables (cupo y
   ocupación) y la pantalla **Ubicaciones** (recuadros de ocupación por zona). Capturas pendientes.
   Lote 12: la pantalla se llama ahora **Posiciones** (clic en la fila para editar, botón **Asignar cupo**), **Productos e
   inventario** (indicadores clicables, filtros, marca y modelo, bloque "Añadir ajuste" con nota obligatoria y reportes PDF
   de inventario y de ajustes), **Proveedores** (antes de Compras, filtros, teléfono con máscara, baja con ícono) y
   **Compras** (filtros múltiples, alta con almacén obligatorio y reglas de líneas). Capturas pendientes.
   Lote 13: **Recibo** en una sola pantalla (lista de recibos, detalle con captura de líneas en la tabla, encabezado editable en un
   modal, pestañas Recibos, Avisos de llegada y Acomodo pendiente), acciones de las tareas de almacén como íconos (ya no hay una cola
   única), **Recolección y empaque** en dos paneles con barra arrastrable (captura de varias líneas, Empacar y Eliminar en la fila,
   detalle en un modal) y los ajustes del 2026-09-30 (proveedor y almacén de la orden en Borrador, nota obligatoria en todo ajuste
   manual, "Unidades totales" y predeterminados al empacar). Capturas nuevas de esas pantallas; pendiente la de Avisos de llegada.
   Lote 14: **Transferencias y ajustes** (pestañas Ajustes y Transferencias, modal Subir/Bajar, Reporte de ajustes), **Kárdex de movimientos**
   (filtros compartidos, resumen, detalle del movimiento y Conciliación con descuadres) y **Conteo cíclico** en dos paneles (escáner, cantidad
   en la fila, Confirmar conteo y ajustar y Conteo de lo cambiado); "Ajustes de inventario" salió del menú. Capturas `l14-*`.
   Lote 16: **modo de recepción** en la ficha del almacén (Datos → Recepción) y en la lista, **recibo directo a posición** (columna Posición destino, "Sugerida", aviso de cupo, "Usar
   posiciones sugeridas"), aviso en Acomodo pendiente, selector de Posición de Recolección y, en "Todas las tablas", **encabezado y filtros de las exportaciones**, fechas como fecha y formato
   de números. Capturas `l16-*`.
3. [F7A — Pulso: panel Almacén con filtro y Actividad reciente](frontend/f7a-pulso-almacen-y-actividad.md): filtro de
   almacén y de categoría o producto en el panel "Almacén" de Pulso (con enlaces a Inventario ya filtrado), y panel
   nuevo "Actividad reciente" (pestañas por módulo, ventana de tiempo, interruptor "Solo obligatorios", buscador libre
   y enlaces a la ficha de cada evento). Lote 14: panel **Necesita tu atención** (descuadres pendientes, Revisar, Ver todos, "Todo en
   orden") y **Conteos abiertos** con el total real. Lote 15: la franja **Almacén hoy** (tarjetas, barritas, tooltip, clic al detalle, almacén
   compartido), las **filas fijas**, indicadores por módulo, gráficos siempre dibujados y gráficos "De la compañía". Capturas `l15-*`.
4. [F8a — Menú completo, marca, Pulso por paneles, Sistema y Análisis](frontend/f8a-menu-sistema-analisis-y-marca.md):
   los 7 grupos del menú de la maqueta y la pantalla "pendiente", la marca Teikem, paleta de comandos, Pulso del día
   organizado en dos niveles ("Organizar mi Pulso" / "Organizar el de la compañía"), Indicadores y Gráficos, Roles y
   usuarios (con el PIN de los aparatos de almacén y la contraseña temporal), Aparatos móviles (código autogenerado),
   Listas de valores (ajustar/restaurar) y Mi cuenta → PIN de la app.
5. [F9 — Ajustes de la compañía y región y formatos](frontend/f9-ajustes-de-la-compania.md): cómo se ven fechas, horas,
   números, dinero y teléfonos con la región de la compañía (no con el idioma), y la pantalla Sistema → Ajustes de la
   compañía con sus pestañas General, Región y formatos (vista previa, Personalizada, Restaurar), Calendario (días
   laborables y feriados), Módulos (dependencias y reautenticación), Operación (valores por defecto, pipeline de estatus,
   recepción por almacén) y Marca (temas, colores propios y validaciones). Capturas `f9-*`.
6. [F10 — Seguridad y auditoría](frontend/f10-seguridad-y-auditoria.md): la pantalla Sistema → Seguridad y auditoría con sus
   pestañas Actividad (bitácora de cambios y eventos de seguridad en una tabla: tipo Cambio/Evento/Alerta, fecha, buscador,
   orden por columna y Exportar CSV de lo filtrado) y Sesiones y MFA (sesiones activas de toda la compañía, revocar una o las
   demás, y la política: MFA obligatorio, ventana de reautenticación y duración de las sesiones). Capturas `f10-*`.
7. [F11 — Marca por compañía: logos y colores validados por el servidor](frontend/f11-marca-por-compania.md): la pestaña Marca de
   Ajustes de la compañía conectada al servidor: subir, reemplazar y quitar los cuatro logos, dónde se ven (barra lateral por
   tema, colapsada), los errores 400/413/415 junto a cada ranura, el error del servidor al guardar colores y el 409 del feriado
   repetido en Calendario. Capturas `f11-*`.
8. [F12 — Conteo cíclico por producto: "Por revisar", corrección y vista previa](frontend/f12-conteo-por-producto.md): la pestaña
   Por revisar (estado Cuadra / Con diferencia / Con errores / Faltan líneas, filtros, "Cerrar los que cuadran" con su resumen y
   motivos), el detalle con solo las líneas que fallan, la evidencia *Contó X · Corregido a Y* y la corrección en la fila, la vista
   previa del efecto antes de confirmar y las posiciones pendientes de revisión. Capturas `f12-*`.
9. [F13 — Nuevo conteo "Por producto" desde la web](frontend/f13-nuevo-conteo-por-producto.md): las dos pestañas del modal "Nuevo
   conteo" (Por posiciones / Por producto), cómo crear el conteo de un producto en una sola pantalla, el 400 de un producto sin
   existencia junto al selector y adónde va el usuario al crearlo. Capturas `f13-*`.
10. [F14 — Códigos de barras para el conteo](frontend/f14-codigos-de-barras.md): los reportes **Códigos de barras de productos**
   (Productos e inventario) y **de posiciones** (Posiciones y la ficha del almacén): un código Code 128 por elemento con el SKU o el
   código exacto, en rejilla de 3/2/1 columnas, agrupados por categoría o por el primer número del código, con lo filtrado de la
   pantalla; sus avisos ("no cabe", omitidos, truncado) y cómo imprimirlos para que el lector los lea. Capturas `f14-*` y un PDF de ejemplo.
11. [F15 — Productos por posición](frontend/f15-productos-por-posicion.md): en **Posiciones**, el filtro **Pasillo**, las casillas para marcar
   posiciones y el botón **Productos por posición**: PDF con una posición por página y el código de barras de cada producto (hasta 10 por
   página, tamaño adaptable, "Hoja 2 de 2", "Sin productos") para escanear desde el papel en un rack alto; la exportación de la tabla trae todo
   lo filtrado; mensajes y casos frecuentes. Reemplaza a las hojas de posición. Capturas `f15-pdf-*`.
12b. [Etiquetas de producto](frontend/f17b-etiquetas-de-producto.md): en **Productos e inventario**, el botón **Etiquetas de producto**: una etiqueta adhesiva por producto (código de barras del SKU, SKU en grande y nombre) en 4 × 2, 4 × 4 o 4 × 6 pulgadas, solo la etiqueta.
12. [F16 — Etiquetas de posición](frontend/f16-etiquetas-de-posicion.md): en **Posiciones**, el botón **Etiquetas de posición**: una
   etiqueta por posición (código de barras Code 128 lo más grande posible, el código en letra grande y los datos de la ubicación) para
   impresoras de etiquetas de **4 × 2, 4 × 4 o 4 × 6 pulgadas** (una página del PDF del tamaño exacto por etiqueta, orientación Automática
   o Girar 90°), del filtro actual o de las marcadas; sin estado (se reimprime cuando se quiera); diferencia con Productos por posición;
   mensajes y casos frecuentes. Capturas `f16-*`.
13. [F17 — Rentas (web) y "Convertir a serie"](frontend/f17-rentas.md): Almacén → **Rentas**: la lista (filtros por estatus, cliente,
   "vencen en N días", "solo vencidas" y buscador; vencimiento calculado; Exportar), la ficha (datos, equipos por serie con su tarifa y
   estado, extensiones, historial) con Programar, Despachar, Extender y Cancelar, el alta con el **selector de equipos por serie** y la
   tarifa por equipo, el aviso de rentas vencidas o por vencer en "Necesita tu atención", y el botón **Convertir a serie** de la ficha del
   producto (captura por posición y confirmación de neto cero). Capturas `f17-*`.
14. [F18 — Devoluciones de renta, proceso de equipos y reportes de rentas](frontend/f18-devoluciones-y-proceso-de-rentas.md): **Registrar
   devolución** desde la ficha de una renta En renta (motivo, fecha, destino, devolución parcial, condición y proceso por equipo), Almacén →
   **Devoluciones de renta** (lista con filtros y ficha, enlaces renta ↔ devolución), Almacén → **Proceso de equipos** (cola con Avanzar,
   Completar y Dar de baja con confirmación fuerte), las tarjetas de resumen de la lista de rentas y **Reportes de rentas** (vistas,
   indicadores y gráfico de Análisis). Capturas `f18-*`.
15. [F-A1 — Clientes: lista, alta y ficha](frontend/fa1-clientes.md): Listas → **Clientes**: lista maestra con buscador y «Mostrar inactivos», alta con contrato
   inicial opcional (código duplicado 409) y la ficha con perfil (409 de `rowVersion`), teléfonos y correos, personas de contacto, numeración con ejemplo en vivo,
   campos personalizados, contratos, estatus y baja/reactivación; permisos de cada acción. Capturas `fa1-*`.
16. [F-A2 — Contratos del cliente](frontend/fa2-contratos.md): en la ficha del cliente, la sección **Contratos** con pestañas **Contrato** (datos, estatus con historial, los 5 checks del
   modelo de facturación, cargo por despacho y por COD, selector de contrato y **Nuevo contrato**), **Tarifas** (por servicio y pieza extra por tramos, versión nueva y cierre, historial),
   **SLA** (por tipo de servicio, borrador y guardado) y **Servicios especiales** (tipos nuevos, tarifa, versión nueva, cierre); permisos `contracts.*` y todos los mensajes del servidor.
   Capturas `fa2-*`.
17. [F19 — Despacho manual en la web](frontend/f19-despacho-manual.md): en **Recolección y empaque**, el interruptor **Despacho manual (sin entrega)** del panel
   Recolección (motivo obligatorio, nota, líneas como una recolección, `Idempotency-Key`, 409 por existencia), la columna y el filtro **Tipo**, la ficha del
   DMA (motivo, nota, Kárdex, sin Empacar) y **Eliminar** con reversa (`warehouse.issue`). Capturas `f19-*`.
18. [F-B — Consignatarios](frontend/fb-consignatarios.md): Listas → **Consignatarios**: lista y ficha, alta y edición, teléfono, baja y reactivación, e **Importar** consignatarios desde un archivo CSV con plantillas
   (vista previa fila por fila, errores y avisos, confirmar o descartar); permisos `locations.*`.
F-B2. [F-B2 — Importar consignatarios: Excel, «(No usar)», columnas unidas, cliente por defecto y persona de contacto](frontend/fb2-importacion-consignatarios.md): archivo **CSV o Excel (.xlsx)** con **Hoja del libro**,
   ZIP con sus ceros, CSV de Windows con ñ, **(No usar)**, una columna a dos campos, **Campos con varias columnas** (orden y separador, con resultado), **Cliente por defecto** que se
   guarda en la plantilla (la fila con su propio cliente manda) y **Recordar este cliente en la plantilla**, teléfonos en formatos mezclados y **Persona de contacto** en la ficha. Capturas `fb2-*`.
F-B3. [F-B3 — Importar consignatarios: pueblo, estado y ZIP con la lista de códigos postales, y variantes de mapeo](frontend/fb3-variantes-y-codigos-postales.md): pueblo y estado tomados
F-B4. [F-B4 — Consignatarios en la ficha del cliente y Exportar](frontend/fb4-consignatarios-en-la-ficha-del-cliente.md): paneles **desplegables** con resumen en la ficha del cliente y panel **Consignatarios** al final (tabla paginada de 10 filas por página, buscador, **Exportar**, **Importar** y **Nuevo consignatario** sin salir de la ficha, **Ver todos**) y **Exportar**, **Importar** y **Nuevo consignatario** juntos en la cabecera de la pantalla Consignatarios (Exportar baja lo que la lista tiene filtrado).
F-B5. [F-B5 — Direcciones, Perfil fiscal y paneles con resumen](frontend/fb5-direcciones-y-perfil-fiscal.md): panel **Direcciones** de la ficha del cliente (física, postal, puntos de recogida y **Se recoge en**), «Perfil del cliente» pasa a **Perfil fiscal**, interruptor **Imprimir los labels automáticamente** y el resumen de cada panel cuando está cerrado.
   del ZIP, ZIP de entrega asignado por el pueblo, **Código postal ajustado** (ZIP de apartados o militar → de entrega de la misma ciudad), marca **de la lista**, filas rechazadas
   con el mensaje exacto; **variantes de mapeo** con **Se usa cuando** (igual a, contiene, no está vacía, está vacía), columna **Variante** en la vista previa; selector de pueblo del
   alta solo con ZIP de entrega; fuentes USPS y GeoNames (CC BY 4.0). Capturas `fb3-*`.
19. [F-C1 — Órdenes: lista y alta](frontend/fc1-ordenes-alta.md): Operación → **Órdenes**: la lista con sus filtros, **Nueva orden** (guardar como borrador o confirmar, crédito excedido, factura repetida,
   entrega especial) y eliminar un borrador; permisos `orders.*` y quién ve los precios (`orders.view_price`).
20. [F-C2 — Órdenes: el expediente de la orden](frontend/fc2-ordenes-expediente.md): al abrir una orden: **Detalle** que se edita ahí mismo, **Confirmar** (con crédito excedido), **Re-cotizar**,
   **Cancelar la orden**, **Eliminar la orden**, estatus laterales (En espera, Parcial, Fallida) y su regreso, **Asignar chofer** a una entrega especial, **Cotizar** (vista previa), COD, campos
   personalizados y contactos. Capturas `fc2-*`.
21. [F-C3 — Órdenes: entrada rápida e importación desde CSV](frontend/fc3-ordenes-rapida-importacion.md): el interruptor **Rápida / Detallada** de **Nueva orden**, la rejilla de captura
   (Enter guarda una orden por fila) y **Importar**: plantillas, vista previa del archivo, casillas **Confirmar las órdenes al importar**, **Autorizar crédito excedido** y **Aceptar facturas repetidas**,
   **Descartar archivo** y resultado. Capturas `fc3-*`.
22. [F-D1 — Choferes y tarifas](frontend/fd1-choferes.md): Listas → **Choferes y tarifas**: lista y ficha, **Nuevo chofer**, **Activo** / **Disponibilidad** / **Eliminar chofer**, licencias y certificaciones con su
   chip de vencimiento, tarifas por entrega, intento y viaje (con vigencia, **Cerrar tarifa** e historial), fórmula de pago con vista previa, **Viajes pagados**, dispositivos y contactos; permisos
   `fleet.*` y `driverpay.*`. Capturas `fd1-*`.
23. [F-D2 — Flota y mantenimiento](frontend/fd2-flota.md): Listas → **Flota y mantenimiento**: vehículos (lista y ficha, **Nuevo vehículo**, **Activo** / **Disponibilidad** / **Dar de baja definitiva**, documentos con su chip de vencimiento), **Documentos por vencer**, **Zonas de despacho** y **Disponibilidad** para despacho con sus motivos. Capturas `fd2-*`.
24. [F-D3 — Mantenimiento, órdenes de trabajo y combustible](frontend/fd3-mantenimiento-combustible.md): pestañas **Mantenimiento** (estado de cada programa por vehículo: Al día, Por vencer, Vencido, Sin historial; alta, edición, **Desactivar** y **Reactivar** programas), **Órdenes de trabajo** (lista, **Nueva orden de trabajo**, ficha con tareas y costos, **Iniciar**, **Cerrar orden** con odómetro y **Cancelar orden**) y **Combustible** (bitácora con Km/L y Costo/km, **Registrar carga**, resumen por vehículo, odómetro coherente). Capturas `fd3-*`.
25. [F-E1 — Estación de escaneo (salida)](frontend/fe1-escaneo.md): Operación → **Estación de escaneo**: escanear con lector o teclado (número de orden, empaque o factura), resultado grande con el mensaje del servidor y qué hacer (**Asignada**, **Ya estaba**, **No encontrada**, **No elegible**, **Sin asignar**), **Ruta del día**, voz Sí/No/Ya, medidor «N de M», **Escaneados en esta sesión**, **Pendientes por escanear** con filtros Cliente, Zona, Código postal y Pueblo, y ventana de la orden. Capturas `fe1-*`.
26. [F-E2a y F-E2b — Sala de despacho (con plano de ruta, arrastrar y pin manual)](frontend/fe2a-despacho.md): Operación → **Sala de despacho**: **Planificar el día**, **Nueva ruta**, **Reasignar por zona**, **Órdenes sin asignar** con filtros y **Añadir a la ruta**, **Ruta activa** con chofer y vehículo (disponibilidad y errores del servidor), avisos y bloqueantes, ▲ ▼ y quitar, **Optimizar** con «No cupieron», **Rutas por despachar** (bloqueantes, avisos y lote), **Iniciar ruta**, **Cerrar ruta**, **Eliminar ruta**, **Choferes** y **Rutas de hoy**; F-E2b: **plano de la ruta** esquemático (pines exacto, aproximado y manual, sin ubicación), **reordenar arrastrando** el asa, y **Poner pin** manual con sus errores. Capturas `fe2a-*` y `fe2b-*`.
27. [F-E3 — Monitoreo de ruta](frontend/fe3-monitoreo.md): Operación → **Monitoreo de ruta** (solo lectura): insignia **EN VIVO** / **Sin señal de choferes**, el «río» de totales (**Rutas activas**, **Paradas restantes**, **Progreso promedio**, **Próxima en completar**, **Sin ping**, **Sobre el máximo de paradas**), la lista **Rutas activas** con búsqueda, zona e **Incluir completadas**, la ruta elegida con su plano, **Velocidad**, **Rumbo**, último ping y **Paradas de la ruta**, refresco cada 30 s con **Actualizar** y **Siguiendo en vivo**, **Abrir en Sala de despacho**, y qué llegará con la app del chofer.
28. [F-F1 — Campos personalizados](frontend/ff1-campos-personalizados.md): Análisis → **Campos personalizados**: entidades con contador, tabla con **Incluir inactivos**, **Nuevo campo** (los 8 tipos, clave, etiquetas es/en, validación según el tipo, valor por defecto, Requerido, Único, Mostrar en listados, Orden), **Lista de valores** con **Nueva lista…**, **Editar**, **Desactivar** y **Reactivar**, y los mensajes del servidor. Capturas `ff1-*`.
29. [F-F2 — Vistas e informes](frontend/ff2-vistas.md): Análisis → **Vistas e informes**: galería con un panel por fuente de datos (informes **Por defecto** y de usuarios, **+** para **Nueva vista**), resultado con rango de fechas, búsqueda, orden, totales y **Exportar**, constructor (nombre, fuentes combinadas, columnas con campos personalizados, filtros con 16 operadores, agrupar por varios campos con agregados y totales, orden, visibilidad y personas con las que se comparte, vista previa con datos reales), **Editar**, **Eliminar** y los mensajes del servidor. La sección «Lote F-F2» del FAQ recoge cada mensaje.
F-A3. [F-A3 — Usuarios de portal y cotizador del cliente](frontend/fa3-portal-cotizador.md): en la ficha del cliente, el panel **Usuarios de portal** (**Invitar usuario**, **Reenviar invitación**, **Suspender**,
   **Reactivar**, **Dar de baja** y **Volver a invitar**, con estatus Invitado, Activo, Suspendido e Inhabilitado; permiso `portalusers.manage` y módulo Portal de clientes) y la pestaña **Cotizador** de
   Contratos (líneas de servicio, paquete y piezas, monto COD y fecha; desglose con tarifa base, piezas extra, despacho, COD y total, y de dónde salió cada monto; `contracts.read`). Todos los mensajes del
   servidor con qué hacer. Capturas `fa3-*`.
F-G2. [F-G2 — Labels y documentos](frontend/fg2-labels-y-documentos.md): en **Órdenes** la columna **Elegir**, **Imprimir label** (marcadas), **Todos los labels**
   (todo lo filtrado), **Waybill summary** y **Packing list**; **Imprimir label** en la ficha; **Label al guardar** en la entrada rápida y detallada (un label por pieza con el
   número de su paquete); en **Escaneo** el **Informe de faltantes** y el escaneo del número de paquete. Capturas `fg2-*`.
F-G1. [F-G1 — Impresoras y labels](frontend/fg1-impresoras.md): Sistema → **Impresoras y labels** (`printers.manage`): **Esta computadora** (QZ Tray conectado o no
   detectado, **Detectar impresoras de esta computadora**, **Instalar QZ Tray**), **Esta estación — labels / documentos** (impresora, impresora de esta computadora,
   template o formato Carta, **Imprimir al guardar**, **Confirmar antes**, **Prueba** con vista y **Descargar PDF**, **Guardar** en este navegador), **Construir el label**
   (9 elementos, posición, tamaño, como código de barras, vista previa en vivo, plantillas nueva/duplicar/predeterminada/desactivar, **Actualizar construcción del label**),
   **Imprimir labels directo a la impresora** de la compañía e **Impresoras de la compañía** (alta y edición, predeterminada, desactivar y reactivar). Capturas `fg1-*`.

13. [13 — Trabajos programados](13-trabajos-programados.md): Lote 50 (P2.2, 2026-10-14) — **tareas automáticas por compañía** (servicio propio, sin Hangfire): interruptor, horario (cada cierto tiempo de 1 minuto a 7 días, o todos los días a una hora), **Ejecutar ahora**, historial y reintentos (1, 2 y 5 minutos); tres trabajos de fábrica (limpieza del historial, revisión diaria de inventario y uno de prueba), permiso `jobs.manage`, mensajes 400/404/409 y datos para soporte.

14. [14 — Avisos (notificaciones)](14-avisos.md): Lote 51 (P2.1, 2026-10-14) — **motor de avisos**: reglas por compañía (evento + canales EMAIL/SMS/APP/PUSH con plantilla es/en y `{variables}` + destinatarios por rol, usuario, correo o teléfono), cola que se escribe en la misma transacción de la operación y se procesa con reintentos (1, 5 y 30 minutos), registro idempotente de cada envío, bandeja del usuario con silenciar eventos opcionales, canales SMS y push «no configurados» (sin proveedor), evento de prueba, permiso `notifications.manage`, mensajes 400/404/409 y datos para soporte.

15. [15 — Mensajería interna](15-mensajeria.md): Lote 53 (P2.1, 2026-10-15) — **hilos de mensajes** entre personas de la compañía (entre dos, único por pareja; grupos de varias personas o de un rol «en foto»), se contestan y el hilo se conserva; entrega **en vivo** (SignalR) y, sin sesión, queda pendiente; **no leídos**, silenciar, salirse de un grupo, **presencia «en línea»**; permiso `messages.use` en todos los roles existentes (los roles nuevos y los del portal de clientes no lo reciben por omisión), mensajes 400/403/404/409 y datos para soporte (WebSockets, una sola instancia).

18. [18 — Órdenes de venta](18-ordenes-de-venta.md): Lotes 57 y 58 (2026-10-15) — **parte S1**: la orden de venta (OV-#####) que viaja de Ventas a Almacén; borrador con precio de venta (cambiarlo exige `sales.price`) e impuesto copiado en cada línea; **solicitar = validar y reservar en la misma transacción**, faltante pedido/disponible/faltante, backorder, crédito del cliente, cancelar libera lo reservado y modificar una línea; permisos `sales.*`, rol Ventas y módulo SALES_ORDERS; mensajes 400/403/404/409/422 y datos para soporte.

17. [17 — Impuestos (IVU)](17-impuestos.md): Lote 55 (2026-10-15) — mecanismo completo de impuestos **como configuración**: tasas con vigencia y componentes (no se editan: se cierra y se crea otra), reglas de la compañía (precio con impuesto incluido, redondeo por línea o documento, tasa por omisión, cargos gravables), categoría de impuesto del producto, exenciones de clientes con certificado y cálculo (simulador) con la copia de la tasa por línea; permiso `tax.manage` solo para el Admin de compañía y módulo TAX; mensajes 400/403/404/409 y datos para soporte.

16. [16 — Enviar avisos](16-enviar-avisos.md): Lote 54 (2026-10-15) — **avisos manuales**: quien tiene `notifications.send` envía un aviso a personas, a roles o a **todas** las personas de la compañía; llega a la campanita («De Ana»), en vivo si tiene Teikem abierto, y **no se responde**; historial con a quién se envió y cuántas personas lo leyeron; permiso solo para el Admin de compañía (el administrador lo asigna), mensajes 400/403 y datos para soporte.

L56. [L56 — Impuestos](frontend/l56-impuestos.md): Contabilidad → **Impuestos** (cinco pestañas): tasas con vigencia y componentes (cerrar y crear la nueva desde la anterior), reglas de la compañía, categoría de impuesto de los productos, exenciones de clientes con certificado y **simulador** de cálculo. Capturas `l56-*`.

L54. [L54 — Enviar avisos](frontend/l54-enviar-avisos.md): Sistema → **Enviar avisos**: buscador de personas y roles con **Seleccionar todo (N)** (solo lo filtrado), enviar a todos con confirmación, historial con leídos. Capturas `l54-*`.

L53. [L53 — Mensajes](frontend/l53-mensajes.md): pantalla de chat (lista de conversaciones, burbujas, escribir con Enter, personas del grupo), **Nuevo mensaje** (buscador de nombre y correo, varias personas o un rol) y **Kemi** («Te llegó un mensaje de… → Ver», «Enviar un mensaje a…», «Mis mensajes»). Capturas `l53-*`.

L52. [L52 — Reglas de aviso, campanita y Kemi](frontend/l52-avisos-y-kemi.md): Sistema → **Reglas de aviso** (`notifications.manage`): reglas con evento, canales (Correo/SMS/Campanita/Push, los no configurados apagados con su explicación) con plantilla es/en y fichas de {variables}, destinatarios por rol, **usuario con buscador de nombre y correo**, correo o teléfono, **Enviar prueba**, **Registro** con filtros y **Disparar aviso de prueba**; la **campanita** de la cabecera (contador, lista, marcar leído, silenciar opcionales) y **Kemi** (globo «Te llegó un aviso → Ver», opciones, dormir en la cabecera y despertar). Capturas `l52-*`.

L50. [L50 — Trabajos programados](frontend/l50-trabajos-programados.md): Sistema → **Trabajos programados** (`jobs.manage`): lista con horario, próxima corrida y último resultado, **Ejecutar ahora**, **Cambiar horario** e **Historial**. Capturas `l50-*`.

## Preguntas frecuentes

Ver [faq.md](faq.md) para las preguntas y mensajes de error acumulados de cada lote. La sección "Lote 11" recoge los mensajes nuevos de Almacén (cupo, ocupación, código de zona, lista de
ciudades, exportación) y las preguntas sobre "sin cupo configurado", exportar tablas, ciudades en mayúsculas y por qué el
código y la zona de una posición no cambian. La sección "Lote 27" recoge los mensajes de las rentas (alta, equipos, tarifa, programar, despachar, extender y cancelar) y de una serie en renta, y las
preguntas sobre por qué el en mano no baja al despachar, dónde ver lo rentado, qué es una renta vencida, si la tarifa se cobra y si una extensión necesita
aprobación. La sección "Lote 28" recoge los mensajes de la devolución de renta, del proceso del equipo devuelto y del conteo con equipos en renta
(D7), y las preguntas sobre por qué un equipo devuelto sigue "No disponible", devolver solo una parte o a otro almacén, quitar pasos del
proceso y qué pasa con un equipo dado de baja. La sección "Lote 29" recoge los 404 de los reportes de rentas (sin `rental.view` o con el módulo
apagado) y las preguntas sobre dónde ver las rentas que vencen, por qué una Programada aparece vencida, el día de la compañía y el aviso
`RENTAL_DUE` mientras llegan las pantallas. La sección "Lote F17" recoge los mensajes que las pantallas de Rentas y "Convertir a serie" revisan antes de enviar (el mismo texto del
servidor), los propios de la web (almacén de origen, serie ya elegida o no disponible, tope de 200 equipos) y preguntas sobre el selector
de equipos, "vencida" y la conversión neta cero. La sección "Lote F18" recoge los mensajes de Registrar devolución y de la cola de proceso
(los que la web revisa antes de enviar, los del servidor que se ven ahí y la confirmación de la baja) y preguntas sobre la devolución
parcial, el equipo "No disponible" después de devolverlo y los indicadores de rentas en el Pulso. La sección "Lote 30" recoge el error de configuración `products.trackingType`, las tres
advertencias de seguimiento de la migración y las preguntas sobre si se pueden traer las series de Depot, por qué un producto marcado de
serie quedó sin seguimiento y si conviene recrear la base para esto. La sección "Lote 12" recoge los mensajes de marca y modelo, del ajuste con
nota, del teléfono de proveedor, de la orden de compra, del cupo en bloque y de los reportes PDF, y las preguntas sobre el
reporte de inventario (productos con 0, valor "—"), el reporte de ajustes (saldos iniciales), el origen del cupo de las
posiciones y cómo corregirlo en bloque. La sección "Lote 13" recoge los mensajes del ciclo de estatus del recibo, del encabezado
editable, del esperado en ciegos, de los filtros de recibos y avisos, los mensajes que solo se ven en las pantallas de Recibo y de
Recolección y empaque, y las preguntas sobre Discrepancia, qué entra al inventario con una diferencia, cómo editar el encabezado, la
barra 60/40, dónde quedó la cola de acomodo y por qué "Unidades totales" cuenta solo productos activos. La sección "Lote 14" recoge los mensajes del Kárdex (dirección, dueño, detalle
del movimiento), de los descuadres (acción, nota, ya cerrado, total del producto, Kárdex negativo o menor que lo reservado, revisión
simultánea), de "Conteo de lo cambiado" (31 días, sin movimientos, conteos pendientes, más de 200 posiciones), de los estatus del conteo y del
permiso `pulse.attention`; los mensajes que solo se ven en pantalla (ajuste Subir/Bajar, escáner del conteo, botón Confirmar apagado,
revisión automática); y las preguntas sobre qué es un descuadre y por qué un ajuste no lo arregla, qué cuenta "lo cambiado", por qué un
conteo dice Diferencia, dónde quedó "Ajustes de inventario" y por qué no se ve "Necesita tu atención". La sección "Lote 15" recoge el mensaje nuevo (`Los días deben estar entre 1 y 14.`), los que solo se ven en pantalla
(confirmación de borrar un gráfico de la compañía, gráficos sin datos, "…" y "—") y las preguntas sobre por qué el Kárdex suma distinto que las
tarjetas de la franja, por qué aparece "Otras", por qué un indicador de "últimos 7 días" dio distinto (días de Puerto Rico), cómo quitar la
franja fija, si vuelve un gráfico de la compañía borrado, por qué la salida de un día sale negativa y por qué no se ve la franja. La sección "Lote 16" recoge los mensajes del recibo directo (modo de recepción desconocido, falta la posición destino, zona de recepción
o de cruce, posición que no existe, id y código a la vez, mismo producto con dos destinos, posición o zona desactivada, modo de la configuración de migración), los que solo se ven en pantalla y en la app
(falta la posición destino, excede el cupo, sugerida, posiciones del aparato) y las preguntas sobre cómo poner un almacén en directo, qué pasa con los recibos abiertos, si se puede pasar del cupo, por
qué la pistola no deja enviar, por qué los números llevan coma, por qué el PDF dice "Sin filtros" y cómo abrir las fechas del Excel. La sección "Lote F9" recoge los mensajes de la pantalla Ajustes
de la compañía (feriados, días laborables, separadores, colores de la marca, máximo de paradas, teléfono) y las preguntas sobre por qué
cambió el formato de las fechas y la hora, cómo volver a día/mes/año, qué pasa con los teléfonos guardados y por qué US no cambia la hora. La sección "Lote F10" recoge los mensajes de las sesiones de la compañía (sesión
actual, sesión no encontrada, reautenticación, 403 sin `admin.users`), los de la política y la exportación que solo se ven en pantalla, y las
preguntas sobre Evento/Alerta, el total que cambiaba al paginar, la búsqueda, la IP como ubicación, por qué revocar una sesión puede cerrar todas
y qué hace "Cerrar las demás sesiones". La sección "Lote 19" recoge los mensajes de la marca (tamaño, JSON, campos desconocidos, colores de estado, tipos,
hexadecimales, tema inexistente, contraste y matiz), los de los logos (ranura, archivo, 413, 415, imagen dañada y cada motivo por el que un SVG se rechaza) y el 409 del
feriado repetido, y las preguntas sobre cuándo se guardan los logos, qué variante se usa en cada tema y por qué la barra colapsada puede seguir mostrando el símbolo de Teikem. La sección "Lote F12" recoge los mensajes que se ven en la pantalla de revisión del conteo (reservado, conflicto al confirmar,
conteo ya reconciliado, posición ya confirmada), los que solo se ven en pantalla y las preguntas sobre qué cierra "Cerrar los que
cuadran", por qué una corrección no mueve inventario y la diferencia entre Varianza y Ajuste. La sección "Productos por posición" recoge los mensajes del informe (más de 200 posiciones por consulta, almacén no encontrado, los avisos del modal) y las preguntas sobre qué código lleva cada producto, cómo imprimir un pasillo, por qué el papel no se desactualiza y por qué Exportar trae todo lo filtrado. La sección "Lote F-A2" recoge los mensajes de los contratos del cliente (datos, `rowVersion`, modelo de facturación, despacho y COD, estatus, SLA, tarifas por servicio y pieza extra, tramos y servicios especiales) y las preguntas sobre permisos, `canEdit`, historial de tarifas y por qué un contrato no se puede activar. La sección "Lote F-A1" recoge los mensajes de Clientes (código duplicado, nombre, límite de crédito, `rowVersion`, punto de recogido, teléfonos y correos, personas de contacto, patrones de numeración y reglas de estatus) y las preguntas sobre permisos, bajas, dirección, SLA y la diferencia entre estatus y baja.
