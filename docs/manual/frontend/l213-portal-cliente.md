# L213 — Portal del cliente: pie fijo, filtro de fecha, Nuevo envío igual al del personal, Mensajes de escritorio y marcar varios consignatarios

Capturas de la verificación con Playwright del 2026-10-23 (`app-cliente`, `npm run visual`; API simulado). Funcionalidad: capítulos [27](../27-app-cliente.md), [30](../30-nueva-orden-del-cliente.md), [34](../34-avisos-al-consignatario.md), [35](../35-acceso-del-consignatario.md) y [37](../37-portal-facturas-avisos-comprobante.md); decisiones en [lote213-portal-decisiones.md](../../lote213-portal-decisiones.md).

## Inicio con «Hoy» y el pie pegado abajo
![Inicio](img/l213-inicio-hoy-pie.png)
El período de los indicadores ofrece **Hoy** además de 7, 30 y 90 días. El pie (Privacidad · Términos, derechos, Powered by) es un pie de página de verdad: a todo el ancho y pegado al borde de abajo cuando el contenido no llena la ventana.

## Mis envíos con filtro de fecha y total
![Mis envíos](img/l213-envios-filtro-fecha.png)
Desplegable de rangos (aquí en **Personalizado**, con Desde/Hasta del calendario del navegador) junto al buscador; la tabla termina con **«9 de 9»**.

## Nuevo envío — Rápida (web ancha), toda con el teclado
![Rápida](img/l213-rapida.png)
![Rápida recorrida con el teclado](img/l213-rapida-teclado.png)
Columnas del personal (Fecha, Factura si el cliente la asigna, Consignatario, Dirección · Pueblo, Paquete, COD, Servicio, Comentario). La segunda captura se hizo **sin ratón**: Tab hasta el consignatario, «farm» + Enter (lo escoge y el foco pasa al desplegable Paquete), ↓ (cambia a «Sobre»), Tab, «40» en COD, Tab al desplegable Servicio, ↓ («Expreso») y **Enter en el desplegable guarda la fila**: quedó guardada (verde, con Sobre, $40.00 y Expreso) y la nueva espera con la misma fecha, servicio y paquete; en el buscador se ve la lista con el número del consignatario y «+ Nuevo».

## Nuevo envío — Detallada (web ancha y celular)
![Detallada](img/l213-detallada.png)
![Detallada en el celular](img/l213-detallada-celular.png)
Pasos 01 Servicio y fechas, 02 Consignatario (buscador con autocompletar y Número, Dirección, Pueblo, Código postal y Teléfono de solo lectura) y 03 Paquetes y COD, con el **Ticket del envío** a la derecha. En el celular es la única forma de capturar.

### Lo capturado: rango Desde/Hasta, búsqueda y resumen en el pie
![Rápida con rango y resumen](img/l213-portal-rapida-rango-resumen.png)
La tabla de debajo de la fila rápida muestra un **rango Desde/Hasta** (los dos «hoy» al abrir; una fecha capturada más adelantada o más vieja lo ensancha sola) y se puede buscar en cualquier dato; el pie resume «N × paquete · servicio», **Total de paquetes**, **Facturas distintas** (cada coma de un campo de factura es otro número) y el COD total de lo visible. Igual en la web del personal (`img/l213-personal-rapida-rango-resumen.png`). Verificado con el API real en `e2e/lote213-capturado-filtros-resumen.spec.ts`.

### Mis envíos: píldora de intentos fallidos
![Mis envíos con intentos fallidos](img/l213-portal-envios-intentos-fallidos.png)
Un envío que salió a la calle, no se entregó y volvió al almacén ya no parece uno «retrasado»: bajo su estatus lleva una **píldora ámbar con «1 intento fallido» / «2 intentos fallidos»**. Sale en la tabla de Mis envíos y del Inicio, y en la barra de progreso de las tarjetas del celular, la ficha del envío y el espacio del consignatario. Las órdenes terminadas (entregada, devuelta, cancelada) no la llevan y las que nunca fallaron tampoco. En el Excel/CSV/PDF de la tabla el estatus sale con la cuenta («Sin asignar · 1 intento fallido»).

## Una vista por aparato (2026-10-24)
| Computadora (web) | Tableta (1194 px, táctil) | Celular |
|---|---|---|
| ![Computadora](img/l213-computadora-nuevo-envio.png) | ![Tableta](img/l213-tableta-nuevo-envio.png) | ![Celular](img/l213-celular-nuevo-envio.png) |

La **web en computadora** (puntero fino, 900 px o más) ve la forma ancha: barra superior, tablas y «Nuevo envío» con la entrada Rápida. La **app instalada, las tabletas y los celulares** (también en el navegador) ven la forma móvil: pestañas, tarjetas y «Nuevo envío» solo detallado, aunque la tableta mida más de 900 px. Verificado con Playwright emulando una tableta táctil (`e2e/lote213-vista-por-dispositivo.spec.ts`).

**Exportar es igual en todas partes: Excel, CSV y PDF** (web, tableta, celular, app instalada e Informes). En el navegador se baja el archivo; en la app instalada se guarda y se abre «Compartir» (necesita el APK nuevo con `expo-print`, `expo-sharing` y `expo-file-system`).

## Exportar lo filtrado
![Exportar en Mis envíos](img/l213-envios-exportar.png)
![Exportar en Mi almacén](img/l213-almacen-exportar.png)
Toda tabla termina con su total («9 de 9») y **Exportar**: baja un CSV con todas las filas que cumplen el filtro vigente, como en la web del personal. En el celular es un botón bajo el buscador.

## Exportar a Excel, CSV y PDF — con el logo de la compañía o el de Teikem
![PDF con el logo de Teikem](img/l213-pdf-con-logo-teikem.png)
![PDF con el logo de la compañía](img/l213-pdf-con-logo-compania.png)
![PDF filtrado](img/l213-pdf-filtrado.png)
La compañía sin logo subido sale con el de Teikem (arriba); con logo subido, con el suyo (segunda). Debajo: la compañía, el título, «Generado el …» y los filtros. El Excel trae el mismo encabezado. La tercera imagen es un PDF exportado con el buscador puesto: solo las filas filtradas.

### Con la marca de la compañía
![PDF del portal con la marca «Vino»](img/l213-pdf-portal-marca-compania.png)
![PDF de la web de la compañía con la misma marca](img/l213-pdf-personal-marca-compania.png)
El encabezado de la tabla, el título y la línea de abajo salen en los colores que la compañía definió (aquí la marca «Vino»); sin logo propio, el de Teikem. Lo mismo en el portal y en la web de la compañía.

## Consignatario nuevo en la misma pantalla, sin ratón
![Rápida: consignatario nuevo](img/l213-consignatario-en-linea-rapida.png)
![Rápida: guardada, el consignatario ya existe en la lista](img/l213-rapida-guardada.png)
![Detallada: consignatario nuevo](img/l213-consignatario-en-linea-detallada.png)
Escribir un nombre que no existe + Enter abre el bloque (el cursor cae en lo que falta); Enter graba el consignatario en el servidor, queda escogido y el cursor sigue en el envío. Segunda imagen: tras guardar el envío, escribir otra vez el nombre ya lo encuentra en la lista.

### Lo mismo en la web del personal
![Personal, rápida](img/l213-personal-rapida-consignatario-en-linea.png)
![Personal, rápida guardada](img/l213-personal-rapida-guardada.png)
![Personal, detallada](img/l213-personal-detallada-consignatario-en-linea.png)
![Personal, detallada guardado](img/l213-personal-detallada-guardado.png)
Nombre, dirección, pueblo (el selector de pueblos y ZIP) y teléfono opcional; Enter graba; el foco sigue en el Paquete (rápida) o en el tipo del primer paquete (detallada).

## Mensajes en la web ancha
![Mensajes](img/l213-mensajes.png)
Dos columnas: conversaciones a la izquierda (Servicio al cliente y los consignatarios con lo sin leer) y la conversación abierta a la derecha, del mismo ancho que su cabecera.

## Mis consignatarios — marcar varios
![Marcar varios](img/l213-consignatarios-varios.png)
Tabla con casilla por fila, «Seleccionar los 4 que se ven», barra de acciones y la advertencia **una sola vez** para el grupo («Esto aplica a 4 consignatarios (correo)…»).
