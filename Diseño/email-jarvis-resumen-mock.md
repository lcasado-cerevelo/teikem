# Correo a Jarvis — envío del mock

**Asunto:** Teikem — prototipo navegable del diseño (léeme antes de abrirlo)

---

Jarvis,

Te adjunto el prototipo del sistema. Es un solo archivo HTML: lo abres con doble clic en cualquier navegador, no hay que instalar nada ni conectarse a ningún lado.

**Lo más importante antes de que lo toques:** todo funciona de verdad —puedes crear órdenes, recolectar, empacar, generar facturas, cambiar configuraciones— pero **nada se guarda**. En cuanto refresques la página o la cierres, vuelve al estado inicial. No es un error ni algo pendiente: el prototipo no tiene base de datos a propósito, su función es que veamos y discutamos el diseño antes de programar. Así que juega con confianza, no puedes romper nada.

Lo que sigue es el resumen de lo que estoy proponiendo.

---

## La idea que organiza todo

El sistema está armado alrededor de **dos corrientes que corren en paralelo**: la mercancía que sale a la calle y el dinero que regresa del cliente. Casi toda la interfaz refleja esa dualidad —incluso los colores: azul para lo operacional, naranja para lo monetario— porque en la práctica son los dos frentes que hay que vigilar todos los días y rara vez se ven juntos en un mismo lugar.

La pantalla de inicio, "Pulso del día", es exactamente eso: las dos corrientes en vivo, más lo que necesita una decisión tuya hoy.

---

## Qué cubre

**Operación.** Órdenes (lista maestra con filtros, y la captura de órdenes nuevas integrada en la misma pantalla), estación de escaneo, sala de despacho para armar rutas, y monitoreo de ruta en vivo.

**Almacén.** Almacenes y ubicaciones, productos e inventario, compras y recibo, ajustes de inventario, recolección y empaque, conteo cíclico, cruce de muelle, y el kárdex de movimientos. Un principio de fondo: **el kárdex manda**. Ningún balance se edita a mano; el inventario se reconstruye siempre de los movimientos, así que todo cuadre tiene una explicación rastreable.

**Contabilidad.** Contabilización de compras y de despachos, procesar entregas (COD, cuadre y remesas), facturación y liquidación a choferes. Facturación y liquidación son los dos únicos flujos con aprobación humana, y ambos siguen la misma máquina de estados: generar → revisar → aprobar → exportar. Una vez generada, la corrida **congela** los montos calculados: si mañana cambia una tarifa, las facturas viejas no se recalculan solas.

**Catálogo.** Clientes y contratos, consignatarios, choferes y tarifas, flota y mantenimiento.

**Análisis.** Vistas e informes, campos personalizados, indicadores y gráficos — todo definible por el usuario, sin programar. La idea es que cuando pidas un informe nuevo no haya que esperar a que alguien lo desarrolle.

**Sistema.** Impresoras y etiquetas, roles y usuarios, integraciones/API, seguridad y auditoría, y ajustes de la compañía.

**Portal de clientes.** Un espacio donde el cliente entra a ver sus propias órdenes, crear entradas y administrar sus consignatarios, sin ver nada de otro cliente.

---

## Decisiones que quiero que revises

Estas son las que más me interesa que valides, porque son de operación y tú la conoces mejor que yo:

**Una orden puede llevar varios paquetes de tipos distintos.** Una orden es una solicitud de envío: un consignatario, un COD que se cobra una vez, una firma. Puede llevar 2 cajas y 1 sobre. En el listado aparece desglosada por tipo de paquete, pero sigue siendo una sola orden. Facturación cobra cada tipo con su tarifa y el cargo de COD una sola vez.

**La facturación sale del contrato del cliente, no de una tarifa general.** Cada cliente tiene sus componentes: tarifa por servicio y tipo de paquete, pieza extra con escalonamiento por rangos, cargo por despacho y cargo por COD (fijo o por ciento). El prototipo ya calcula con esos datos.

**El cargo por despacho se cobra por orden, pero contabilidad manda al facturar.** Se calcula solo desde el contrato del cliente y aparece como una columna más de la corrida, así que al revisar la factura se puede ajustar el monto o dejarlo en cero sin tocar el contrato ni la orden.

**Las órdenes en estatus Entrada se pueden corregir**; una vez que avanzan, no. La idea es permitir arreglar un error de captura sin abrir la puerta a modificar algo que ya está en la calle.

**El consignatario decide si acepta facturas repetidas.** Hay clientes donde repetir un número de factura es un error y hay que bloquearlo, y otros donde es normal. Es un campo por consignatario: o bloquea, o avisa y te deja decidir.

**Cada compañía puede tener su propia identidad visual** —temas de color y su logo— pero los colores de estado (verde bien, rojo mal) no se pueden cambiar, porque si se rompe esa convención la interfaz se lee al revés.

---

## Qué NO está en el prototipo, y es a propósito

- **Nada se guarda** (ya lo dije arriba, pero es lo que más confunde).
- **La app móvil del chofer** está diseñada pero no prototipada.
- **Los mapas** son una representación, no un motor de ruteo real.
- **Algunos módulos** (marítimo, equipos en alquiler) están en el catálogo de módulos activables pero sin pantalla construida todavía.
- **La pantalla para mantener los catálogos de listas** está diseñada pero no construida, para no alargar esta ronda.

---

## Lo que necesito de ti

1. **Que lo recorras como si fuera tu día normal** y me digas dónde el flujo no se parece a como trabajan de verdad. Eso es más valioso que cualquier comentario sobre cómo se ve.
2. **Que revises las decisiones de arriba**, sobre todo la de los paquetes múltiples y la de facturación por contrato.
3. **Un detalle concreto:** reproduje una de sus facturas reales dentro del prototipo para verificar que el cálculo cuadra. Los totales y la cantidad de líneas dan exactos, pero el conteo de piezas me da 70 y en mis notas tenía 75. ¿Puedes cotejarlo contra el documento original y decirme cuál es el bueno?

Cualquier cosa que te choque, por pequeña que sea, dímela. Estamos justo en el momento donde cambiar algo cuesta una conversación en vez de semanas de trabajo.

Quedo pendiente.

Luis

---

## Notas para ti (no van en el correo)

- **Ojo con el adjunto:** muchos servidores de correo bloquean archivos `.html` por seguridad. El archivo pesa 762 KB. Lo más seguro es comprimirlo en `.zip` antes de adjuntarlo, o subirlo a Drive/OneDrive y mandarle el enlace. Si lo mandas suelto hay buena probabilidad de que no le llegue o le llegue en cuarentena.
- Si prefieres que llegue más corto, los dos bloques que se pueden recortar sin perder lo esencial son "Qué cubre" (dejando solo los encabezados de área) y "Qué NO está en el prototipo".
- Los cuatro puntos de "Lo que necesito de ti" son los que van a generar respuesta. Si el correo se acorta, esos se quedan.
