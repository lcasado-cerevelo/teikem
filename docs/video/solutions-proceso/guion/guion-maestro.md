# Guion maestro: el proceso de Advance Solutions en Teikem

Siete escenas, **un audio por escena**. Narración de **usted**, tono cercano. Pantalla: 1920×1080, tema claro, español, zoom 1.25, base de demostración (clientes ficticios, costos de compra difuminados, contraseñas solo como puntos).

Lo que usted lee para el text-to-speech está en `texto-tts/E0n-clave.txt` (una frase por línea, sin ninguna marca). **Este archivo es solo para revisar el orden y lo que se ve en pantalla.** Cada audio se guarda con el mismo nombre que su texto: `E01-productos`, `E02-compras`, y así.

Duraciones: las tomas ensayadas duran de 57 a 116 segundos; la duración real saldrá de su audio, y la toma se ajusta al audio (nunca al revés). Los textos tienen entre 147 y 232 palabras (1,367 en total): a ritmo normal salen unos 60 a 100 segundos por escena, unos 9 minutos y medio en conjunto. Donde la voz dure más que la toma ensayada (E01 y E02), se alarga la toma con pausas de lectura.

> LUIS: hable sin prisa y deje un respiro al final de cada frase. Si una frase le suena rara, cámbiela en el texto y dígame; el guion se ajusta, no la voz.

---

## BLOQUE 1 - Productos e inventario

### E01 - productos | pantalla: /warehouse/products | duracion-estimada: 82 s
> RUTA: /warehouse/products
Vamos a recorrer cómo trabaja Advance Solutions en Teikem, desde que un producto existe hasta que se factura.
> ACCION: se abre Almacén › Productos e inventario; cargan los indicadores.
> RESALTAR: los cinco indicadores («SKUs activos», «Unidades totales», «Bajo mínimo»)
> RESALTAR: columnas «Disponible», «Reservado», «Total»; fila del monitor 070120 (reservado 5)
[PAUSA 1s]
> ACCION: abre el filtro «SKU o nombre», escribe Prodigy, marca el monitor; luego «Limpiar».
> ACCION: clic en el producto → ventana «Editar producto»; se abre «Dónde está (existencia por posición)».
> ACCION: cambia «Punto de reorden» a 900 y «Guardar» → aviso «Producto actualizado.»
> ACCION: «Nuevo producto» → SKU 151515, nombre CAJA CORRUGADA 15 X 15 X 15, categoría BOXES, costo, precio → «Guardar» → «Producto creado.»
> TAPAR: el costo de compra mientras se escribe el nuevo producto (campo «Costo de compra»)

## BLOQUE 2 - Órdenes de compra

### E02 - compras | pantalla: /warehouse/purchase-orders | duracion-estimada: 60 s
> RUTA: Contabilidad › Compras
> ACCION: «Importar»; origen QuickBooks; estatus «Completada (enviada, lista para recibir)»; elige el archivo; «Revisar» → «2 órdenes en el archivo: 2 listas…»; «Importar 2»; «Cerrar».
> RESALTAR: chips «Lista», luego «Creada» con PO-00001 y PO-00002; en la lista, chip «Enviada»
> ACCION: abre la orden de Padimont; se señala la nota «Importada de QuickBooks…».
> TAPAR: columna «Costo unitario» en la ficha de la orden (difuminada)
> LUIS: no se muestra una orden bloqueada (el texto solo dice que existe esa protección).

## BLOQUE 3 - Recibo

### E03 - recibo | pantalla: /warehouse/receipts | duracion-estimada: 85 s
> RUTA: /warehouse/receipts
> ACCION: «Nuevo recibo»; origen «Contra orden de compra»; almacén ALM-SOLUTIONS; orden de Advance Logistics; «Crear recibo».
> RESALTAR: líneas del recibo, «Esperado» y «Recibido» iguales
> ACCION: «Usar posiciones sugeridas»; «Confirmar recibo» (dos veces: botón y ventana) → «Recibo REC-00001 confirmado.»
> TAPAR: columna «Costo unitario» del recibo confirmado
> ACCION: repite rápido con la orden de Padimont; abre Compras (dos «Recibida») y Productos e inventario (unidades totales 84,900).

## BLOQUE 4 - Venta con el asistente

### E04 - venta | pantalla: /sales-orders | duracion-estimada: 95 s
> RUTA: Contabilidad › Ventas
> ACCION: «Nueva orden» (abre el asistente); busca el cliente FARMACIA LA ESPERANZA; «Siguiente».
> RESALTAR: la oferta OFERTA1 («Se pueden armar: …», «Limita»)
> ACCION: agrega una oferta; pestaña PADIMONT; escribe 10 en «Cantidad de NECH-1002»; abre el carrito.
> ACCION: «Siguiente»; elige «Recogido»; resumen; «Siguiente»; nombre de quien autoriza; firma con el ratón; «Enviar pedido»; confirma con la contraseña.
> TAPAR: la contraseña se ve solo como puntos (no tocar el ojito)
> ACCION: «Ver en Órdenes de venta» → ventana de la orden: «Solicitada», firma, componentes del kit.

## BLOQUE 5 - Almacén: recolección y salida

### E05 - despacho | pantalla: /warehouse/pick-batches | duracion-estimada: 70 s
> RUTA: Almacén › Recolección y empaque
> RESALTAR: la fila de la orden de venta («Solicitada»)
> ACCION: abre el documento; se señala «Lo que hay que recolectar»; «Despachar»; confirma; elige «Retiro».
> ACCION: vuelve a la lista: «Despachada»; abre Kárdex de movimientos y se señalan los tres «Despacho».
> LUIS: esta escena usa el botón «Despachar» que se añadió a la ficha de Recolección y empaque (Lote 242).

## BLOQUE 6 - Facturar y exportar a QuickBooks

### E06 - quickbooks | pantalla: /money/billing y /sales-orders?tab=quickbooks | duracion-estimada: 80 s
> RUTA: /money/billing
> ACCION: «Nueva corrida»; cliente; «Generar»; «Marcar como revisada»; «Aprobar y emitir»; confirma identidad con la contraseña (solo puntos).
> RUTA: Ventas › pestaña QuickBooks
> ACCION: «Exportar a QuickBooks» → «Exportar y bajar el archivo»; se señala «¿Ya importaste estas 1?»; «Sí, ya están».
> LUIS: QuickBooks Desktop no se graba: en la edición va una lámina con el archivo y los pasos Archivo › Utilidades › Importar › Archivos IIF (mientras dice la frase correspondiente).

## BLOQUE 7 - Análisis de ventas

### E07 - analisis | pantalla: /analytics/sales | duracion-estimada: 120 s
> RUTA: Análisis › Análisis de ventas
> RESALTAR: las tres tarjetas del mes; el comparativo por ruta y representante; la conciliación («Diferencia por revisar»)
> ACCION: pestaña «Movimiento de productos»; «Inventario y cobertura»; «Reorden»; pestaña «Gráficos» (venta por mes, venta contra cuota, participación por línea).
> ACCION: «Configuración › Cuotas» (sin editar); «Importar historial»; elige el archivo; «Simular» → «cuadra».
> LUIS: los nombres de representantes, rutas y clientes son de demostración; las cifras son las del historial de QuickBooks al 80 %.
