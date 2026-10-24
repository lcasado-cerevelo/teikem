# Portal del cliente — dictado de Luis (2026-10-23)

> Pedido de Luis, textual en lo esencial: «todo esto escríbelo para que lo hagas bien, porque por eso es que no se hizo bien
> de la primera, porque no se escribió». Este archivo es la referencia de lo que se construye en este lote del portal
> (`app-cliente/`, web de escritorio y celular). Cada punto lleva su estado al final del documento.

## 0. Reglas generales que salen del dictado
- **Celular (app instalada o web angosta): «Nuevo envío» existe, pero SOLO el formulario detallado.** La entrada rápida es
  exclusiva de la web ancha (computadora). Hoy el código ya lo hace así (`useIsWide()` = web y ≥ 900 px); queda por decidir
  si una tablet apaisada en el navegador (≥ 900 px) puede usar la rápida o si se exige ratón (ver Decisiones).
- **Toda tabla se puede exportar, como en la aplicación del personal: se exporta lo que está filtrado** (todas las filas del filtro, no solo las de la pantalla). Dictado 2 del mismo día.
- **En la rápida, con Tab se pasa de campo (también por los desplegables, donde se escoge con las flechas) y con Enter se graba desde cualquier campo.** Misma pantalla y mismos campos que el personal; cotejo en lote213-portal-decisiones.md.
- **Toda tabla dice cuántas filas tiene en total**: «N de M» (las que se ven de las que hay). Si la lista se trae por partes
  («Cargar más»), el total es el de TODO lo que cumple el filtro, no el de la pantalla. No hace falta paginar con números de
  página; sí hace falta el total.
- Textos en es/en con `t('clave')`, tema claro/oscuro, responsive desde 360 px (reglas del proyecto).

## 1. Página principal (Inicio)
- Está bien como está.
- «Envíos recientes» con 5 envíos: está bien así.
- **Indicadores: hoy hay 7, 30 y 90 días. Agregar «Hoy».**
- Las tablas/listas de la página muestran su total (regla general).

## 2. Pie de página (footer): privacidad, términos, derechos reservados, Powered by Teikem
- **Web de escritorio:** debe ser un **footer de verdad**, pegado al borde inferior de la ventana cuando el contenido no llena
  la página. NO puede quedar «flotando» a media pantalla, justo debajo del contenido. Si el contenido es más largo que la
  ventana, el pie queda al final del contenido (se llega a él bajando), como en cualquier web.
- **Celular:** hasta abajo, al final del contenido, como está.
- Hoy el pie (`LegalFooter`) lo pinta `Screen` dentro del `ScrollView`, después del contenido: por eso flota. Hay que
  hacer que el contenido del scroll crezca hasta el alto de la ventana y el pie se alinee abajo (`flexGrow` +
  `justifyContent: 'space-between'`), o moverlo a la raíz de la web ancha (`_layout.tsx`).

## 3. Mis envíos
- El listado y el buscador están bien.
- **Agregar filtro por FECHA igual que en la aplicación web del personal:** un desplegable con rangos (Todas las fechas,
  Hoy, Ayer, Últimos 7 días, Esta semana, Semana pasada, Últimos 30 días, Este mes, Mes pasado, Este año, **Personalizado**)
  y, al escoger Personalizado, los dos campos Desde/Hasta (ver `docs/filtro-fecha-diseno.md`: mismo juego «pasado»).
  El API del portal ya acepta `fromUtc`/`toUtc` en `GET /api/v1/portal/orders`.
- Buscar + fecha se combinan. El subtítulo dice el total («N envíos») y la tabla «N de M».

## 4. Nuevo envío (web ancha y celular)
**Regla central: los MISMOS campos y las MISMAS funcionalidades que «Nuevo envío» de la aplicación web del personal**
(`web-app/src/features/orders/OrderEntryForm.tsx` y `OrderQuickEntry.tsx`), salvo lo que no aplica al cliente (el cliente
sale de la sesión; no hay «Label al guardar» ni autorización de crédito). «Aquí me faltan un montón de cosas de allá».

### 4.1 Dos maneras de entrar: Rápida y Detallada (en la web ancha; en celular solo Detallada)
Selector Rápida/Detallada arriba, recordado, como hoy.

### 4.2 Entrada DETALLADA — igual a la del personal
Hoy la pantalla lista TODOS los consignatarios hacia abajo como radios («mira qué despingue… todo el listado hacia abajo
para escoger un consignatario»). Eso se elimina. Debe lucir **igual** que la del personal:
- Paso **01 Servicio**: tipo de servicio (catálogo de la compañía, con el predeterminado del cliente o de la compañía),
  fecha lista (`readyDate`, mínimo hoy), fecha límite (`dueDate`), N.º de factura del cliente (solo si el cliente numera sus
  facturas; si no, apagado con «Automático»). N.º de orden solo si el cliente asigna sus números.
- Paso **02 Consignatario**: **buscador con autocompletar** (se escribe y se escoge; «+ Nuevo» para crear uno con nombre,
  dirección, pueblo, estado, código postal) y, debajo, los datos del elegido **de solo lectura**: número, dirección, pueblo,
  código postal, teléfono. Recogida (si el cliente tiene puntos de recogida) como hoy.
- Paso **03 Paquetes y COD**: por paquete, **tipo de paquete** (catálogo, con predeterminado), descripción, piezas (y peso,
  que el portal ya tenía); agregar/quitar paquete; COD.
- Notas (como hoy). Servicio especial: NO aplica al portal (lo cotiza la compañía) — ver Decisiones.
- «Ticket» a la derecha (resumen sin precio) como en el personal, en la web ancha.
- Un solo «Guardar»; la orden nace Sometida.

### 4.3 Entrada RÁPIDA — igual a la del personal y TODO con el teclado
- Columnas iguales a la del personal: Fecha, N.º de orden (solo si el cliente los asigna), N.º de factura (solo si los
  asigna), Consignatario (autocompletar), Dirección · Pueblo (solo lectura), Tipo de paquete, COD, Servicio, Comentario.
  Las que el portal tenía de más (descripción, piezas, peso) se conservan solo si no rompen la igualdad: se decide en
  Decisiones; por omisión se quitan para que sea igual.
- **Nada de ratón:** Tab/Enter recorren los campos; flechas arriba/abajo escogen en el autocompletar y en los desplegables;
  Enter guarda la fila; la fila nueva trae la fecha y el servicio de la anterior y el foco vuelve a la fecha. Todo el
  recorrido se puede hacer sin tocar el ratón, y se prueba así (Playwright con teclado).
- Fila guardada de solo lectura con su número; total de filas y COD acumulado arriba.

### 4.4 Lo que hay que cambiar en el servidor para que sea igual
`PortalOrderCreateRequest`/`PortalOrderFormDto` hoy solo traen consignatarios, recogidas, paquetes (descripción, piezas,
peso), COD, factura, fecha pedida y notas. Falta: tipo de servicio, tipo de paquete por línea, N.º de orden (si el
cliente lo asigna), fecha límite, y en el formulario los catálogos (servicios y tipos de paquete con etiqueta en el idioma
de la persona), los predeterminados del cliente/compañía y las banderas `clientAssignsOrderNumber` /
`clientAssignsInvoiceNumber`. Se reutiliza `OrderService.CreateAsync` como hoy.

## 5. Almacén
- Está bien. No hay mucho que buscar.
- **Sugerir campos de búsqueda adicionales** (propuesta para Luis): código de barras y marca/categoría en Productos; lote y
  almacén en Existencias; referencia y tipo (entrada/salida) en Movimientos; y filtro de fecha (el mismo control del punto 3)
  en Movimientos, Llegadas y Salidas.

## 6. Informes
- Están bien (no probados por Luis). Mostrar total de filas en sus tablas (regla general).

## 7. Avisos
- Están bien.

## 8. Mensajes
- La pantalla de Mensajes con sus conversaciones **no convence**: «está como para el teléfono». En la web ancha, el botón
  de «ver las conversaciones (de consignatarios)» ocupa el ancho completo de la pantalla mientras la conversación de abajo
  ocupa solo una parte (la columna de 720 px). Hay que revisarlo con Playwright y rediseñarlo para escritorio: dos columnas
  (lista de conversaciones a la izquierda, conversación abierta a la derecha) o, como mínimo, que la cabecera y la
  conversación tengan el mismo ancho. «No montes maceta»: verificar en pantalla, no suponer.

## 9. Ajustes → Avisos al consignatario
- **Marcar varios consignatarios a la vez** (correo y/o texto) sin ir uno a uno: casillas por fila + «Seleccionar los que
  se ven» + acciones «Marcar correo / Marcar texto / Quitar» para los seleccionados, con UNA sola aceptación de la
  advertencia de privacidad para todo el grupo. Hace falta un endpoint del servidor que marque varios en una llamada
  (`PUT /api/v1/portal/consignee-notices/consignees/bulk`), registrando en la bitácora cada consignatario y avisando a cada uno
  como hoy. Diseño a proponer a Luis antes/junto con la implementación.

## 10. Verificación
- Playwright contra el portal compilado (`npm run export:visual` + `npm run visual`) en 1440×900 y 390×844: pie fijo abajo,
  filtro de fecha, nueva entrada rápida solo con teclado, detallada con autocompletar, mensajes en escritorio, marcado
  múltiple. Capturas en `app-cliente/visual-shots/`.
- `npm run check` en `app-cliente`; pruebas del servidor del área (portal).

## Decisiones para Luis
1. **Tablet apaisada en el navegador (≥ 900 px):** ¿puede usar la rápida o la rápida exige computadora (ratón)?
2. **Rápida del portal:** ¿se quitan descripción, piezas y peso para que las columnas sean exactamente las del personal, o se
   quedan como columnas extra al final?
3. **Servicio especial** (del personal) en el portal: se asume que NO (lo cotiza la compañía).
4. Campos de búsqueda extra del almacén (punto 5): cuáles de los propuestos.
5. Diseño del marcado múltiple de consignatarios (punto 9): casillas + barra de acciones, o «Marcar todos los que tienen
   correo/teléfono» además.

## Estado (2026-10-23, Lote 213 — ver [lote213-portal-decisiones.md](lote213-portal-decisiones.md))
| Punto | Estado |
|---|---|
| 0 Celular solo detallada | Hecho; desde el dictado 6 (2026-10-24) la forma móvil es para app instalada, tabletas y celulares, y la ancha solo para web en computadora |
| 0 Toda tabla con total «N de M» | Hecho (`DataTable total`; Mis envíos, Mis consignatarios, conversaciones; texto en celular) |
| 1 Inicio: indicador «Hoy» | Hecho (API `days=0` desde la medianoche de la compañía) |
| 2 Pie fijo abajo en la web | Hecho (`Screen` + `LegalFooter` a todo el ancho con línea) |
| 3 Mis envíos: filtro de fecha | Hecho (`DateRangeFilter`, mismo juego «pasado» del personal; semana desde el domingo) |
| 4.2 Detallada igual al personal | Hecho (01/02/03 + ticket; autocompletar; datos de solo lectura; servicio, fechas, números, tipo de paquete, recogida) |
| 4.3 Rápida igual al personal y con teclado | Hecho (columnas del personal; Tab campo, Enter guarda, flechas en la lista; verificado con Playwright solo con teclado) |
| 4.4 Servidor | Hecho (formulario con catálogos y predeterminados; alta con servicio, n.º de orden, fecha límite y tipo de paquete; contrato regenerado) |
| 5 Almacén: campos de búsqueda | Propuesta en decisiones (nada construido: decisión 4) |
| 6 Informes | Sin cambios |
| 8 Mensajes en escritorio | Hecho (dos columnas: lista + conversación) |
| 9 Marcar varios consignatarios | Hecho (selección + barra de acciones + una advertencia; endpoint `bulk`) |
| Exportar tablas (dictado 2) | Hecho: «Exportar» en toda tabla y lista (lo filtrado, hasta 5000 filas, CSV): envíos, facturas, almacén, consignatarios; informes ya exportaban |
| Rápida = personal, Tab/desplegables/Enter (dictado 2) | Cotejado campo por campo (tabla en lote213-portal-decisiones.md) y verificado con Playwright sin ratón |
| Exportar también a PDF y Excel con logo de la compañía o de Teikem (dictado 3) | Hecho en la web y en la app instalada (Excel, CSV, PDF; dictado 6: «el estándar es CSV, PDF y Excel»); Informes incluidos. App: necesita APK nuevo y prueba en un aparato |
| Consignatario nuevo en la misma pantalla, sin ratón, en el portal y en la web del personal (dictado 3) | Hecho: portal (rápida y detallada, endpoint `POST /api/v1/portal/consignees`) y personal (rápida y detallada); verificado con el API real |
| 10 Verificación | `npm run check` y capturas Playwright en `app-cliente/visual-shots/`; pruebas del servidor del área verdes (build normal bloqueado por DLL en uso; se compiló aparte) |
| Tabla de lo capturado: filtro de un día (hoy), búsqueda libre y resumen por paquete/servicio/COD en el pie (dictado 4) | Hecho en la rápida del portal y en la del personal; verificado con el API real (`lote213-capturado-filtros-resumen.spec.ts`) |
| Mis envíos: píldora de intentos fallidos en lo que volvió al almacén (dictado 5) | Hecho (portal: tabla, Inicio, barra de progreso, ficha y consignatario); verificado con el API real |
| Lo capturado: rango Desde/Hasta que se ensancha solo; resumen con total de paquetes y facturas distintas (cada coma = otra factura) (dictado 5) | Hecho en la rápida del portal y en la del personal |
