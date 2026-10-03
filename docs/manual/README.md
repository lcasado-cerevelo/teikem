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
   Región y formatos (2026-10, sección 11.1): región Puerto Rico o Estados Unidos con su juego de valores, zona horaria de la
   compañía (con la que cuenta "hoy"), moneda y formatos de fecha, hora, números y teléfono, cada uno cambiable por separado,
   con sus validaciones y mensajes.
   Lote F10 (secciones 1.5, 9 y 9.1): **sesiones de toda la compañía** (listar con `admin.audit`, revocar una o las demás con
   `admin.users`, IP de la sesión) y la **actividad unificada** con total real, búsqueda en la base y códigos de tipo y resultado.
   Lote 19 (secciones 11 y 11.2): **marca por compañía en el servidor** — los colores se validan con las mismas reglas que la
   pantalla (temas, contraste WCAG, separación de matiz, campos y tamaño) y los **cuatro logos** (lockup y marca cuadrada, cada uno
   con su variante para fondo oscuro; SVG, PNG, JPG o WebP de hasta 512 KB) se guardan en la base y se validan por su contenido real
   (SVG sin contenido activo ni referencias externas), con sus mensajes 400/413/415; y el **409 al repetir un feriado**.
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
   Lote 21 (sección 6, Conteo cíclico): **conteo por producto**, captura original y **corrección del supervisor** con evidencia,
   **vista previa** de la reconciliación, lista **Por revisar**, **cierre en bloque** de los conteos que cuadran y **posiciones
   provisionales** creadas desde el conteo (servidor; la web y la app lo usan en sus propios lotes).
   Lote 11 (sección 1, Almacenes y ubicaciones): **cupo máximo** de la posición y su **estado de ocupación**
   (vacía, parcial, llena, ocupada sin cupo), ocupación calculada por zona, código de zona editable, listado de
   posiciones paginado con búsqueda por código, zona, pasillo, rack, nivel o posición, y el catálogo de **localidades
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
   validaciones, estatus y lo que bloquea; el acomodo pendiente en almacenes directos; el selector de posiciones de Recolección (solo donde hay existencia); el cruce de muelle que no aplica a
   recibos directos ya confirmados; y el formato de los números (coma de miles y `$`).
7. [07 — Pulso del día y Actividad reciente (Lote 7A: Almacén)](07-pulso-y-actividad.md): panel de eventos
   recientes de Almacén (catálogo de 23 eventos, obligatorio/opcional, ventana 24h/48h/hoy, filtro por módulo),
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
   documento a la vez), Inicio, Recibir, Acomodar, Despacho (solo clientes 3PL por ahora), Conteo (a ciegas según
   permiso), Consultar (con caché para responder sin señal) y Sincronización (pendientes, con error, reintentar).
   Lote 16: Recibir en un almacén **directo a posición** (paso "Escanea la posición destino", validación sin señal, bloqueo por dos destinos del mismo producto), descarga de las posiciones del
   almacén y heartbeat en cada pasada de sincronización.
   Lote A3: lector del Zebra sin teclado ("escanear = Aceptar" en todas las pantallas, botón ⌨, indicador "Lector: …" en
   Sincronización), margen para la barra del aparato, Inicio en dos columnas, letras más grandes, tocar un producto en el
   conteo, y los formatos de la compañía (Región y formatos) guardados en el aparato.
10. [10 — Migración de datos heredados](10-migracion-de-datos.md): comando de línea de comandos para aprovisionar
    una compañía desde QuickBooks Desktop y el WMS heredado MSWM, simulación (`--dry-run`), refresco de maestros sin
    tocar el saldo inicial (`--update`) y recrear la base en blanco para repetir la migración (`db-reset`). Lote 12
    (sección 4): **cupo estimado de las posiciones** de Advance Depot desde el historial del WMS anterior (regla, reporte
    `-cupos.csv` y comportamiento de `--update`).
    La marca de QuickBooks (columna `Brand`) pasa a la marca del producto (sección 1).

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
   Catálogos de valores (ajustar/restaurar) y Mi cuenta → PIN de la app.
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

## Preguntas frecuentes

Ver [faq.md](faq.md) para las preguntas y mensajes de error acumulados de cada lote. La sección "Lote 11" recoge los mensajes nuevos de Almacén (cupo, ocupación, código de zona, catálogo de
ciudades, exportación) y las preguntas sobre "sin cupo configurado", exportar tablas, ciudades en mayúsculas y por qué el
código y la zona de una posición no cambian. La sección "Lote 12" recoge los mensajes de marca y modelo, del ajuste con
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
feriado repetido, y las preguntas sobre cuándo se guardan los logos, qué variante se usa en cada tema y por qué la barra colapsada puede seguir mostrando el símbolo de Teikem.
