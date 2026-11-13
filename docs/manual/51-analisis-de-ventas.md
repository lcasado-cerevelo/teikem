# 51 — Análisis de ventas (representantes, cuotas, historial de QuickBooks, movimiento de productos y gráficos)

<!-- kemi:staff -->

Lote 241 (2026-11-12). Una pantalla para saber **cuánto vendió cada representante y cada ruta contra su cuota**, **qué se mueve y qué se va a acabar** de cada producto, y **gráficos listos para presentar**. Reemplaza los dos Excel que hoy se arman a mano (`Analisis_Ventas_<Mes>.xlsx` y `Movimiento_de_productos.xlsx`) y los puede exportar con las mismas hojas. Se alimenta de **dos fuentes**: el **historial de QuickBooks** (lo que se vendió antes de Teikem, que se importa de dos reportes) y las **facturas de Teikem** (desde la fecha de corte).

> Capturas pendientes: los pantallazos de este capítulo no se han tomado todavía. Faltan: el aviso «Para que el análisis cuadre falta», la pestaña «Por representante» (tarjetas, Comparativo, «Totales y conciliación», Líneas), «Movimiento de productos» (Resumen con colores y las matrices por mes), «Gráficos» y la hoja impresa, «Configuración» (Cuotas, Líneas y marcas, Importar historial con una simulación, Emparejar a mano) y el panel «Representante de ventas» de la ficha del cliente.

## 1. Qué es, dónde está y quién puede

- **Dónde:** menú **Análisis → Análisis de ventas** (`/analytics/sales`, quinto ítem del grupo Análisis, después de Gráficos). Cuatro pestañas: **Por representante**, **Movimiento de productos**, **Gráficos** y **Configuración**. La pestaña queda en la dirección (`?tab=movement`, `?tab=charts`, `?tab=setup&sec=quotas`…), así que se puede mandar el enlace.
- **Módulo:** **Órdenes de venta (`SALES_ORDERS`)**. Con el módulo apagado todo el API responde 403 con `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.`
- **Permisos** (categoría Facturación; los dos implican poder ver Órdenes de venta, `sales.view`):

| Permiso | Qué permite | Quién lo trae |
|---|---|---|
| `sales.analysis.view` | Ver las tres vistas, el estado del historial, la configuración (solo lectura), exportar a Excel, imprimir, el panel «Representante de ventas» de la ficha del cliente y la cifra «Venta del mes vs cuota» del Pulso | Admin de compañía, Supervisor de ventas, Oficina de ventas |
| `sales.analysis.manage` | Todo lo anterior **más** cambiar rutas, representantes, cuotas, líneas y marcas, el representante de cada cliente, los ajustes, **importar el historial de QuickBooks** y **emparejar a mano** | Admin de compañía, Supervisor de ventas |

- En las compañías que ya existían, el Admin de compañía (`TenantAdmin`) y el Supervisor de ventas (`SalesSupervisor`) recibieron los dos permisos y la Oficina de ventas (`SalesOffice`) el de ver. **Ningún otro rol los recibe solo**: lo asigna el administrador en Sistema → Roles. Las plantillas de las compañías nuevas ya los traen.
- La pantalla y las rutas aceptan **cualquiera** de los dos permisos (`view|manage`). Con solo `.view`, **Configuración** es de solo lectura (aviso «Solo lectura: para cambiar la configuración del análisis hace falta el permiso “Administrar el análisis de ventas”.»), y no aparecen **Importar historial** ni **Emparejar a mano**.
- El `TenantId` sale de la sesión: cada compañía ve solo lo suyo.

## 2. Puesta en marcha, paso a paso

Hágalo en este orden. Mientras falte algo, la pantalla lo avisa arriba: **«Para que el análisis cuadre falta:»** con un enlace a la sección donde se arregla («Asignar representantes», «Asignar líneas», «Emparejar a mano», «Poner la fecha de corte», «Importar el historial»). El aviso solo sale si hay algo pendiente.

Todo esto es **Configuración** (necesita `sales.analysis.manage`). Las secciones: **Rutas · Representantes · Cuotas · Líneas y marcas · Clientes sin representante · Ajustes · Importar historial · Emparejar a mano**.

### 2.1 Rutas

Una **ruta** agrupa representantes en el Comparativo (Cadenas, Norte, Metro, Este, Sur, Oeste…). «Cadenas» es una ruta más.

- **Nueva ruta**: **Nombre** (obligatorio, hasta 100 caracteres, único entre las rutas activas) y **Orden** (menor primero; es el orden en que salen en el Comparativo; por omisión 100).
- **Editar**, **Desactivar** y **Reactivar**. No se borra nada: se desactiva. Una ruta **con representantes activos** no se puede desactivar (pase primero sus representantes a otra ruta).
- API: `GET /api/v1/sales-analysis/routes?includeInactive=false`, `POST …/routes` con `{ "name": "Norte", "sortOrder": 20 }`, `PATCH …/routes/{id}`, `POST …/routes/{id}/deactivate` y `…/reactivate`.

### 2.2 Representantes

Un **representante** es quien vende: nombre, **ruta** (opcional) y, si entra a Teikem, su **usuario** (opcional, de esta compañía).

- **Nuevo representante**: **Nombre** (obligatorio, hasta 100, único entre los activos), **Ruta** y **Usuario de Teikem (opcional)**. Si usted no puede ver los usuarios, el selector avisa «No tiene permiso para ver los usuarios.»
- La lista muestra la ruta, el usuario, cuántos **clientes** activos tiene y su **cuota vigente** de este mes.
- Un representante **con clientes activos asignados** no se puede desactivar: pase sus clientes a otro. Uno desactivado sigue saliendo en los meses en que vendió.
- **Pasar el campo «Representante»**: si los clientes vienen de la migración de QuickBooks, traen un campo personalizado «Representante». Este botón **crea un representante por cada valor distinto** y **enlaza a los clientes que todavía no tienen representante**; lo que ya existe o ya está enlazado no cambia, así que se puede repetir. El campo personalizado se deja como estaba. Resultado: «Representantes creados: N; clientes enlazados: N (ya tenían: N).» Los representantes nacen **sin ruta**: póngasela después. En las compañías que ya existían, la migración se hizo una vez al instalar este lote.
- API: `GET/POST /api/v1/sales-analysis/reps`, `PATCH …/reps/{id}` (`{ "name", "routeId", "clearRoute", "userId", "clearUser" }`), `…/deactivate`, `…/reactivate`, `POST …/reps/from-custom-field`.

### 2.3 Asignar representante a los clientes

Es lo que decide **de quién es cada venta** (ver sección 3.4).

- **Clientes sin representante** (en Configuración): lista paginada de clientes activos. Filtre **Sin representante**, **Todos los clientes** o **De «representante»**, busque por nombre o código, marque con las casillas (o **Elegir los de esta página**), escoja **Asignar a…** (o **Quitar el representante**) y pulse **Asignar**. Se pueden asignar hasta 2,000 a la vez. Resultado: «N cliente(s) asignado(s) a X.»
- Un cliente **nuevo** nace sin representante. También se cambia de uno en uno en la **ficha del cliente** (sección 9).
- API: `GET …/clients?repId=&unassigned=true&q=&skip=0&take=100`, `PUT …/clients/{clientId}/rep` con `{ "repId": 3 }` (`null` lo quita), `POST …/clients/assign-rep` con `{ "clientIds": [12, 13], "repId": 3 }`.

### 2.4 Líneas y marcas

Una **línea** agrupa **marcas** de producto (por ejemplo «Global Imports» = A.Belle, Global, Yardley…). Así las vistas agrupan por línea en vez de por cada marca.

- **Nueva línea**: **Nombre** (obligatorio, hasta 100, única entre las activas) y **Orden**.
- **Marcas de la línea**: en el editor de marcas salen **las marcas que no tienen línea y las de esta**. **Una marca va en UNA sola línea**; compara sin distinguir mayúsculas ni acentos. Para cambiar una marca de línea, quítela primero de la otra.
- **Marcas de los productos**: tabla con todas las marcas de los productos propios activos, cuántos productos tiene cada una y su línea. Las que **no tienen línea salen resaltadas**; en la fila, **Asignar a…**.
- **Una marca sin línea cuenta como su propia línea**, y un producto **sin marca** sale en la línea **«Sin marca»**. Por eso conviene que todos los productos tengan marca (se escribe en Productos e inventario) y que cada marca esté en una línea.
- Desactivar una línea la deshace: cada una de sus marcas vuelve a salir como su propia línea. No se puede cambiar las marcas de una línea desactivada.
- API: `GET/POST …/lines`, `PATCH …/lines/{id}`, `…/deactivate`, `…/reactivate`, `PUT …/lines/{id}/brands` con `{ "brands": ["ABELLE", "GLOBAL"] }` (reemplaza la lista) y `GET …/brands`.

### 2.5 Cuotas por mes

La **cuota** es la meta de venta mensual de un representante, en dólares.

- **Cuotas** (Configuración): rejilla **representante × 12 meses** del **año** escogido. Escriba el monto (sin `$`) en cada mes. **Deje vacío para que valga la cuota del mes anterior más reciente**: en la celda vacía se ve tenue la cuota heredada («Vale la cuota de 2026-08»). **Copiar anterior** (por columna) copia las cuotas del mes anterior.
- **Nada se guarda hasta «Guardar (n)»**, que manda solo lo que cambió (una llamada por mes). **Descartar cambios** lo deshace. Si hay un monto inválido, el botón no guarda y se lee «Hay cuotas que no se pueden leer: la cuota debe ser cero o mayor, con hasta 2 decimales.»
- **Quitar la cuota de un mes** (borrar la celda y guardar) hace que vuelva a valer la anterior; no se pierde el historial.
- Un mes **sin ninguna cuota anterior** no tiene cuota: el cumplimiento sale vacío («Sin cuota»).
- API: `GET …/quotas?month=2026-10`, `PUT …/quotas/2026-10` con `{ "items": [ { "repId": 1, "amount": 16500.00 }, { "repId": 2, "amount": null } ] }` (`null` = quitar la del mes), `GET …/reps/{id}/quotas`.

### 2.6 Fecha de corte (Ajustes)

QuickBooks y Teikem **conviven**: las facturas que Teikem emite se pasan a QuickBooks ([49](49-quickbooks.md)), así que un reporte de QuickBooks exportado después de que Teikem empezó a facturar **ya trae las mismas ventas** que Teikem. La **fecha de corte** evita contarlas dos veces:

- **El historial de QuickBooks cuenta ANTES de la fecha de corte (ese día no) y las facturas de Teikem cuentan DESDE ese día.**
- **Cómo escogerla:** el **primer día cuyas facturas se emiten en Teikem** (y ya están completas). Revise que no quede ningún día sin fuente: el historial debe llegar **hasta el día anterior** a la fecha de corte (o Teikem tener las facturas de esos días). El panel **Historial importado** (sección 2.7) muestra las fechas de cada lote; el aviso de traslape sale cuando las dos fuentes se cruzan.
- **Sin fecha de corte** cuentan **las dos fuentes completas**: si se traslapan, esos días se suman dos veces. La pantalla avisa «No hay fecha de corte y el historial importado (hasta AAAA-MM-DD) se traslapa con las facturas de Teikem (desde AAAA-MM-DD): las ventas de esos días se pueden estar sumando dos veces. Ponga la fecha de corte en Ajustes del análisis.»
- Con fecha de corte, las líneas del historial importado **del día de corte en adelante no cuentan**: el aviso dice «N línea(s) del historial importado son del AAAA-MM-DD o después: no cuentan (desde la fecha de corte cuentan las facturas de Teikem).» Es normal si exportó el reporte de QuickBooks más allá de la fecha.
- Se pone en **Ajustes** (**Fecha de corte** / **Quitar la fecha**) o al importar (campo opcional «Fecha de corte», sección 2.7).
- Los mismos Ajustes guardan **Días de entrega** (1 a 365; cuánto tarda en llegar un pedido del suplidor) y **Nivel de servicio** (90, 95, 97.5, 99 % o un valor z a mano mayor que 0 y hasta 4; 95 % = z 1.645 es lo común). Se usan para el punto de reorden sugerido (sección 4.2). Por omisión 5 días y z = 1.645.
- API: `GET/PUT …/settings` con `{ "cutoffDate": "2026-11-16", "clearCutoff": false, "leadTimeDays": 5, "serviceLevelZ": 1.645 }`.

### 2.7 Importar el historial de QuickBooks

**Quién:** `sales.analysis.manage`. **Dónde:** Configuración → **Importar historial**.

**1) Exporte los dos reportes de QuickBooks Desktop**, siempre **completos (todos los ítems)**:

- Reports → Sales → **Sales by Item Detail**.
- Reports → Purchases → **Purchases by Item Detail**.
- Rango: **desde el inicio hasta el último día que quiera traer**, con todas las columnas por omisión.
- Exportar a Excel → **Create New Worksheet → «Create a comma separated values (.csv) file»**. Se acepta el CSV en Windows-1252 o UTF-8. (Con solo ventas o solo compras también sirve: el otro archivo es opcional.)
- **Siempre el reporte completo**: un archivo nuevo **reemplaza todo lo ya importado de su rango de fechas** (ver «Reimportar»).

**2) Simule primero.** Suba los archivos (**Reporte de ventas** y/o **Reporte de compras**), si quiere escriba la **Fecha de corte (opcional)** y pulse **Simular**. **No se guarda nada.** Revise en el resultado:

| Qué mirar | Qué debe pasar |
|---|---|
| **Líneas leídas / Se guardan** | Las compras muestran «Cantidad 0 (omitidas)» (QuickBooks las trae vacías) y «Duplicadas (omitidas)» (ver abajo) |
| **Total** contra **TOTAL del reporte** | Dice **«cuadra»**. Si dice «no cuadra», el archivo está incompleto o lo leyó mal: no importe |
| **Fechas** (primera y última) | Es el rango que va a reemplazar |
| **Por clase** (ventas) o **por tipo de transacción** (compras) | Producto, Descuento, Envío, Otro cargo (ventas); Bill, Item Receipt, Credit (compras) |
| **Totales por mes** | Compárelos con QuickBooks mes por mes |
| **Lo ya importado** | «Nuevo», «Reemplaza el mismo archivo (N líneas)» o «Reemplaza su rango de fechas (N líneas)» |
| **Clientes / Ítems sin emparejar** | Idealmente «Todos emparejados.» Lo que no empareja se ve en la sección 2.8 |
| Avisos del archivo | «Línea N: el total de «X» dice A y la suma de sus renglones da B.», «Línea N: «Total X» no cierra ningún encabezado abierto.», «Línea N: fecha «X» no válida; se omite.», «Línea N: transacción fuera de un ítem; se omite.» |

**3) Importe de verdad.** El botón **Importar de verdad** solo se habilita **después de simular los mismos archivos** («Simule primero con estos mismos archivos.») y pide confirmación («¿Importar de verdad? … Lo ya importado del mismo rango de fechas se reemplaza. ¿Seguir?»). Cada archivo se guarda en **una sola transacción**: o entra completo o no entra. Si pone la fecha de corte, **se guarda al importar de verdad** (no al simular). La casilla **Pasar el campo «Representante»…** hace lo de la sección 2.2.

**4) Verifique.** El panel **Historial importado** lista los lotes vigentes (tipo, archivo, fechas, líneas, total, cuándo y quién). El aviso de arriba debe haber perdido lo de «Importar el historial».

**Reimportar reemplaza el rango.** Cada archivo lleva una huella (SHA-256):

- **El mismo archivo** otra vez: reemplaza su propio lote (`REPLACE_SAME_FILE`); no duplica.
- **Un archivo distinto del mismo tipo**: borra **todas** las líneas ya importadas de ese tipo cuyas fechas caen entre la **primera y la última** del archivo nuevo (vengan del lote que vengan) y guarda las nuevas (`REPLACE_RANGE`). Lo que está fuera de ese rango no se toca. Por eso hay que subir **el reporte completo**: uno parcial (un solo cliente, un solo ítem) borraría el resto de su rango.
- Las líneas reemplazadas se **borran de verdad** (son la copia de un archivo externo); el lote anterior queda marcado como reemplazado con su fecha. Los enlaces hechos a mano (sección 2.8) **se conservan**.

**Compras: Item Receipt y Bill.** En QuickBooks un *Item Receipt* que luego se factura se convierte en *Bill*. Si el reporte trajera los dos con el mismo suplidor, número, ítem y cantidad, se cuenta solo el *Item Receipt* (la fecha en que llegó la mercancía) y se descarta el *Bill* (sale en «Duplicadas (omitidas)»). Las líneas de cantidad 0 se ignoran y los *Credit* restan.

**Cómo se empareja** cada línea del archivo con Teikem:

- **Cliente**, en este orden: el enlace hecho a mano → el campo «Código QuickBooks» que dejó la migración → el nombre normalizado (sin acentos, en mayúsculas, sin puntos ni comas) → la razón social normalizada. **Si un nombre normalizado coincide con dos clientes, no empareja** (hay que enlazarlo a mano).
- **Ítem**: el enlace a mano → el **SKU** sin espacios y en mayúsculas («NECH 1003» = «NECH1003»), contra los productos propios (activos primero).

**Clase de cada línea de ventas:** una sección de inventario (*Inventory*, *Assembly*) o un ítem que empareja con un producto = **Producto**; una sección o ítem de descuento (*Discount*, «descuento») = **Descuento**; un ítem de envío (*shipping*, *delivery*, *envío*, *flete*, *freight*) = **Envío**; servicios, «Parts», impuestos y otros cargos sin producto = **Otro cargo**. Una sección desconocida se toma como Producto (y si no empareja se reporta). El precio en porcentaje (descuento de 2 %) se guarda vacío: manda el monto.

**API:** `POST /api/v1/sales-analysis/import` (multipart): `salesFile`, `purchasesFile`, `dryRun` (por omisión **true**), `cutoffDate`, `repsFromCustomField`. La respuesta es el mismo reporte de la pantalla. Es **síncrona** (unos 5 segundos para 13,000 líneas) y acepta hasta 20 MB por archivo.

### 2.8 Emparejar a mano

**Quién:** `sales.analysis.manage`. **Dónde:** Configuración → **Emparejar a mano**. Tres listas: **Clientes sin emparejar**, **Ítems de ventas sin emparejar** e **Ítems de compras sin emparejar**, cada una con el nombre «tal como viene en QuickBooks», las líneas, el monto y el rango de fechas.

- **Enlazar** abre un buscador (clientes por nombre; productos por nombre o SKU). Al elegir, las líneas ya importadas con ese nombre se re-enlazan al instante y **el enlace queda para las próximas importaciones**. Resultado: «“X” quedó enlazado a Y (N línea(s)).»
- Un ítem enlazado pasa a contar como **Producto** si estaba como Otro cargo.
- **Ítem combinado** («53310/53350»): no es un producto, queda sin emparejar hasta que alguien lo enlace, y solo se puede a **uno** de los dos.
- Mientras un **cliente** no empareje, su venta sale en **«Sin representante»**; mientras un **ítem** no empareje, sale en la línea **«Producto sin emparejar»** (cuenta en los totales).
- API: `POST …/unmatched/link` con `{ "kind": "CLIENT", "raw": "Farmacia Central Drug.", "clientId": 120 }` o `{ "kind": "PRODUCT", "raw": "NECH 1003", "productId": 39 }`.

### 2.9 El comando `import-sales-history` (para quien lo corre en el servidor)

Hace lo mismo que la pantalla (misma lógica, con simulación). Útil para la carga inicial grande o si prefiere correrlo en el servidor.

1. Copie los dos `.CSV` a una carpeta del servidor y, al lado, un `config.json` (admite comentarios y comas finales):

```json
{
  "company": "Advance Solutions",
  "sales": "SalesByItemDetail.CSV",
  "purchases": "PurchasesByItemDetail.CSV",
  "cutoffDate": "2026-11-16",
  "repsFromCustomField": true
}
```

| Campo | Regla |
|---|---|
| `company` | Obligatorio. El nombre **exacto** de la compañía en Teikem (no se crea) |
| `sales`, `purchases` | Al menos uno. Ruta absoluta o relativa a la carpeta del `config.json` |
| `cutoffDate` | Opcional. Se guarda como fecha de corte al importar de verdad |
| `repsFromCustomField` | Opcional, **verdadero por omisión** (en la pantalla, la casilla también viene marcada; por el API sin casilla, `repsFromCustomField` es falso salvo que se mande) |

2. **Simule:** en la carpeta del API, `dotnet Teikem.Api.dll import-sales-history C:\ruta\config.json --dry-run`. Imprime el mismo reporte de la pantalla y deja un `historial-ventas-AAAAMMDD-HHMMSS-simulacion.json` junto al `config.json`.
3. Revise (sección 2.7, paso 2) y, si hay clientes o ítems sin emparejar, corríjalos o enlácelos después en la pantalla.
4. **Importe:** el mismo comando **sin** `--dry-run`. Salida **0** = bien; **1** = error (el mensaje lo dice; no queda nada a medias); **2** = mal escrito el comando.
5. Correrlo otra vez con el mismo archivo no duplica (`REPLACE_SAME_FILE`).

Mensajes del comando (texto exacto en la sección 10.2).

### 2.10 Lista de comprobación

1. Rutas → 2. Representantes (o «Pasar el campo «Representante»») → 3. Asignar representantes a todos los clientes → 4. Líneas y marcas (que ninguna marca quede sin línea) → 5. Cuotas del año → 6. Importar el historial (simular, revisar, importar) → 7. Emparejar a mano lo que sobre → 8. Fecha de corte → 9. Confirmar que el aviso de arriba desapareció y que **«Diferencia por revisar»** de la conciliación es 0.

## 3. Por representante

Pestaña **Por representante**: lo que vendió cada representante y cada ruta en un mes, contra otro mes y contra su cuota. Es el libro `Analisis_Ventas_<Mes>.xlsx`. **Quién:** `sales.analysis.view` o `.manage`. API: `GET /api/v1/sales-analysis/by-rep?month=2026-10&compareMonth=2026-09`.

### 3.1 El mes

- **Mes** (por omisión **el último mes completo**) y **Comparar con** (por omisión **el mes anterior** al escogido). Un mes se escribe `AAAA-MM`.
- «Venta» = **producto + descuentos** (la venta neta). **El envío y los otros cargos no son de ningún representante**: van aparte en la conciliación.
- Fecha de cada venta: la de la **factura**, por mes. La nota de crédito, el día en que se aprobó.

### 3.2 Cada tabla y cifra

| Pieza | Qué muestra |
|---|---|
| **Cifras del mes** (tarjetas) | **Venta de {mes}** (con «Total del mes con envío y sin representante»), **Cambio contra {mes}** (en $ y «antes $») y **Cumplimiento de la cuota total** («de una cuota de $»), con barra |
| **Comparativo por ruta y representante** | Una fila por representante activo (y los inactivos que vendieron en alguno de los dos meses), ordenadas por el **Orden** de su ruta, luego ruta y nombre: **Ruta, Representante, venta del mes de comparación, Venta, Cambio $, Cambio %, Cuota $, Cumplimiento**. Más el **total de las rutas**. Cumplimiento = Venta ÷ Cuota (100 % = cumplió). Sin cuota vigente: vacío. Color: verde 100 % o más, ámbar 90 % o más, rojo por debajo |
| **Totales y conciliación** | Ver 3.3 |
| **Líneas** | La venta de **producto** de cada línea del mes, **% venta neta** (sobre producto + descuentos), **% antes de descuentos** (sobre la venta de producto), y el **líder por dólares** (el representante que más vendió de esa línea) con su ruta y su venta. «Descuentos sin línea» va aparte, con «No aplica» en lo que no corresponde |
| **Línea por representante** | Cuánto de la venta de cada representante es de cada línea (y su %). Incluye «Sin representante» si vendió. **Ocultar las que están en cero** |
| **Producto por representante** | Lo que vendió cada representante de cada producto (**Unidades netas**, **Venta neta $**), incluidos los descuentos del historial (clase «Descuento») |
| **Producto por ruta** | **Cada producto vendido en el mes por cada ruta, también donde no vendió** (en cero, atenuado). Filtros por ruta, representante, línea y buscador |
| **Transacciones del mes** | Cada línea de factura, nota de crédito o historial que cuenta en el mes: Fecha, **Fuente** (QuickBooks, Factura Teikem, Nota de crédito), Tipo, Número, Cliente, Representante, Producto, Línea, Clase, Unidades, Precio, Venta $. Buscador por cliente, número, SKU o producto; filtro por representante. Paginada por el servidor (hasta 500 por página); al exportar, trae **todo** el mes |

### 3.3 La conciliación («Totales y conciliación»)

Dice si lo que se ve **cuadra con el total del mes**:

```
Total de las rutas   (la venta de todos los representantes)
+ Sin representante  (venta de clientes sin representante o del historial sin emparejar a un cliente)
+ Envío y otros cargos
= Total del mes      (todas las líneas del mes)
```

- Abajo el desglose del total: **Producto**, **descuentos**, **envío** y **otros**.
- **«Diferencia por revisar»** debe ser **0.00**. Si no lo es, avise a soporte con el mes.
- Lo que cae en «Sin representante» se arregla con la sección 2.3 (clientes sin representante) o la 2.8 (historial sin emparejar).
- **«Total contable»** del mes (el que se compara con la contabilidad) es el **Total del mes**: incluye envío y otros cargos.

### 3.4 De quién es la venta

**La venta es del representante que el cliente tiene asignado HOY** (`Cliente → Representante`), en cualquier mes, también la del historial (el reporte de QuickBooks no trae representante). Consecuencias:

- Si **cambia a un cliente de representante, sus meses pasados se ven con el nuevo**. No hay "foto" histórica del representante.
- Si un cliente no tiene representante, su venta sale en **«Sin representante»** (con una fila aparte en las tablas), también la de los meses de comparación.
- Una nota de crédito resta al representante **actual** del cliente, aunque el Excel de antes la pusiera en «Crédito sin representante».
- QuickBooks tiene el reporte «Sales by Rep Detail»; **no se importa**.

## 4. Movimiento de productos

Pestaña **Movimiento de productos**: por cada producto, qué tanto se vendió, cuánto queda y cuánto durará. Es el libro `Movimiento_de_productos.xlsx`. **Quién:** `sales.analysis.view` o `.manage`. API: `GET /api/v1/sales-analysis/product-movement?fromMonth=2026-01&toMonth=2026-10`.

- **Rango de meses** (**Desde / Hasta**): por omisión **los 12 meses que terminan en el mes de hoy**; máximo 36 meses. Las existencias son las **de hoy** (todos los almacenes). Las **tarjetas** resumen el rango (venta de producto, unidades, productos con venta y cuántos tienen menos de 2 semanas de cobertura).
- Se listan **todos los productos propios activos que no son kits** (un kit se reparte entre sus piezas; un kit armado cuenta como cualquier producto), vendan o no, más los ítems del historial que no emparejaron (marcados «sin emparejar»).
- **Venta $ y unidades** de esta vista son **de producto** (sin descuentos, sin envío), con las notas de crédito restando. Por eso puede diferir de la «venta neta» de Por representante en el monto de los descuentos.
- La tabla **Resumen por producto** tiene 34 columnas. Se agrupan para que quepan: **Venta · Inventario y cobertura · Reorden · Precio y margen · Por mes · Clientes · Todas · Elegir…** (SKU y producto siempre están). Se ordena por cualquier columna y se busca por producto, SKU, marca, línea o cliente. Hay filtros por ABC, estado y cobertura.
- **«Qué significa cada columna»** está plegado en la misma pantalla (es la hoja «Notas» del Excel).

### 4.1 Cada columna, con su definición exacta

| Columna | Definición |
|---|---|
| **Clasif. ABC** | Se ordenan los productos de mayor a menor **venta $ de producto** del rango. Un producto es **A** si lo acumulado **antes de él** es menos del **80 %** del total; **B** si es menos del **95 %**; **C** el resto (el 5 % final). Un producto sin venta positiva es **«-»**. Así el que cruza el 80 % sigue siendo A. Los kits no cuentan (se reparten) |
| **Estado** (se evalúa en este orden) | **Sin ventas**: no vendió nada en el rango. **Parado**: su última venta fue hace **más de 45 días** (hasta hoy). **Nuevo**: su primer mes con venta (dentro del rango) es el último mes completo, el anterior a ese o el mes en curso. **Subiendo / Bajando**: la **tendencia** es de **+25 % o más** / **−25 % o menos**. **Estable**: lo demás |
| **Precio, Costo, Margen %** | Precio de venta y costo de compra del producto; Margen = (precio − costo) ÷ precio (solo con precio mayor que 0 y costo) |
| **Existencia hoy, Reservado, Disponible** | Suma de todos los almacenes. **Disponible = existencia − reservado** |
| **Unidades, Venta $, % de la venta $** | Neto del rango (las notas de crédito restan). El % es sobre la venta $ de todos los productos |
| **Promedio mensual, Mes mínimo, Mes máximo, Mejor mes** | Unidades vendidas por mes con los **meses COMPLETOS desde el primer mes con venta** del producto. **Los meses sin venta cuentan 0. El mes en curso no cuenta** (está incompleto). Mejor mes = el de más unidades (empate: el primero) |
| **Promedio semanal (8 sem.)** | Unidades por semana de las **8 últimas semanas completas (lunes a domingo)**, que terminan el domingo anterior a la semana de hoy (hoy jueves 5-nov-2026: del lunes 7-sep al domingo 1-nov). Se usan **las semanas desde la primera semana con venta** de esa ventana, y el promedio es de esas semanas. Además de la desviación (poblacional) para el punto de reorden |
| Confianza del promedio semanal | **sin base**: sin venta en las 8 semanas. **pocas semanas**: menos de 3 semanas de historia. **esporádico**: vendió la mitad de las semanas usadas o menos. **ok**: lo demás |
| **Semanas de cobertura** | **Disponible ÷ promedio semanal** (1 decimal): cuánto dura lo que hay si la venta sigue igual. Disponible negativo cuenta 0. Sin promedio semanal: «sin base». **Colores**: rojo **menos de 2 semanas**, ámbar **de 2 a 4**, verde **4 o más**, gris sin base. En meses = semanas ÷ 4.348 |
| **Saldo en 1 / 2 / 3 meses** | **Disponible − promedio semanal × 4.348 × meses**, sin bajar de 0 y redondeado a unidades. No cuenta lo pendiente por recibir ni devoluciones |
| **Mínimo (reorden)** | El **mínimo guardado en el producto** (Productos e inventario). Vacío si el producto no tiene |
| **Punto de reorden sugerido** | **⌈ (promedio semanal ÷ 7) × días de entrega + z × (desviación semanal ÷ √7) × √días de entrega ⌉**, con los días de entrega y la z de Ajustes (5 y 1.645 por omisión). Va **en rojo si lo disponible ya está en el sugerido o por debajo**: toca pedir. Sin promedio semanal: vacío |
| **Tendencia** | El **último mes completo contra el promedio de los dos anteriores** (−23.5 % = vendió 23.5 % menos). **Solo se calcula si vendió en los tres meses**; si no, vacía |
| **Clientes distintos, Facturas** | Clientes y facturas (tipo Invoice) que llevan el producto en el rango, **aunque la línea venga en cantidad 0** |
| **Cliente principal, Unidades al cliente principal** | El cliente al que más unidades **facturadas** se le vendieron (**sin restar notas de crédito**); empate: por nombre. Nombre como se llama el cliente en Teikem |
| **Última venta** | Fecha de la última línea con cantidad positiva |
| **Pendiente por recibir** | Lo pedido menos lo recibido de las órdenes de compra **Enviadas** o **Recibidas en parte** |

Ejemplo (datos de prueba del lote, 5-nov-2026): *Padimont Gloves Large 1/100ct.* tiene disponible 835, promedio semanal 106.25 y desviación 39.67. Cobertura = 835 ÷ 106.25 = **7.9 semanas** (1.8 meses). Saldo en 1 mes = 835 − 106.25 × 4.348 = **373**; en 2 meses sería negativo, por eso **0**. Punto de reorden sugerido con 5 días y z = 1.645: 15.18 × 5 + 1.645 × (39.67 ÷ √7) × √5 = 75.9 + 55.2 = 131.04, que se redondea hacia arriba a **132**.

### 4.2 Las matrices y los clientes

- **Unidades por mes**, **Venta $ por mes** y **Compras por mes**: una fila por producto y una columna por mes del rango, con **escala de color** (marca los meses de más unidades), total por mes y, en unidades, el mínimo, el máximo y el promedio de los meses completos. Se desplazan dentro del cuadro.
- **Compras**: el historial de QuickBooks (*Purchases by Item Detail*) antes de la fecha de corte y, desde ella, **lo recibido en Teikem contra órdenes de compra** (al costo de la línea de la orden y por el día en que se recibió). Solo entran los recibos contra el aviso de llegada de una orden de compra.
- **Clientes por producto**: los 5 clientes (por omisión) que más unidades facturadas compraron de cada producto (**Puesto, Cliente, Unidades, % del producto**).
- **Gráficos de la pestaña**: cobertura (los 15 productos de menos cobertura) y unidades por mes de los 6 principales por venta $.

## 5. Gráficos e «Imprimir presentación o guardar PDF»

Pestaña **Gráficos**: cinco gráficos para presentar a los accionistas. **Quién:** `sales.analysis.view` o `.manage`. API: `GET /api/v1/sales-analysis/charts?fromMonth=&toMonth=&month=&topProducts=6`.

- **Período de los gráficos** (por omisión los **12 meses que terminan en el último mes completo**; máximo 36) y **Mes de las cuotas** (por omisión el último del período).
- Los cinco gráficos: **Venta por mes** (venta neta total, o apilada **por línea** con los descuentos debajo de cero), **Venta contra cuota por representante** (verde cumplió, ámbar 90 % o más, rojo por debajo del 90 %), **Participación por línea** (sobre la venta de producto, sin descuentos; las líneas chicas se juntan en «Otras»), **Unidades por mes de los 6 principales** (por venta $) y **Cobertura por producto** (semanas que dura lo disponible al ritmo de las últimas 8 semanas, al día de hoy). Cada uno trae **Ver los datos** (la tabla detrás del dibujo).
- **«Imprimir presentación o guardar PDF»** abre el diálogo de impresión del navegador con una **hoja limpia**: logo claro de la compañía (si tiene), nombre, «Análisis de ventas», el período («Venta de … a … · cuotas de … · preparado el …») y **un gráfico por bloque**; sin menú ni la asistente Kemi. En el diálogo escoja **«Guardar como PDF»** para el archivo. La hoja sale **siempre en tema claro**, sin animación, con el dibujo completo.

## 6. Exportar a Excel

Botón **Exportar a Excel** en «Por representante» y en «Movimiento de productos». Genera un `.xlsx` en el navegador (el nombre lleva el mes o el rango y el día: `analisis-de-ventas-2026-10-AAAA-MM-DD.xlsx`). Usa los textos del idioma de la pantalla. Lleva encabezados, anchos, formatos de moneda y porcentaje, fechas como fecha de Excel, SKU como texto, autofiltro y **fórmulas con el valor ya calculado** (`D5-C5`, `IF(C5<>0,E5/C5,"")`, `SUM(…)`, `MAX(0,F4-G4*1)`…).

| Libro | Hojas |
|---|---|
| **Por representante** (el mes escogido) | **Comparativo** (con TOTAL RUTAS y la conciliación), **Marcas**, **Representante producto**, **Ventas marca por representante** (con TOTAL REPRESENTANTE y TOTAL del mes), **Variedades por ruta**, **Inventario** y **Transacciones** (todo el mes) |
| **Movimiento de productos** (el rango) | **Resumen**, **Unidades por mes**, **Venta $ por mes**, **Compras por mes**, **Clientes por producto**, **Gráfico** y **Notas** |

Lo que hay que saber:

- **El Excel NO lleva gráficos dibujados.** La hoja **Gráfico** trae **los datos** del gráfico «Unidades por mes de los 6 principales» y una nota: para verlo, seleccione la tabla e **Insertar → Gráfico** en Excel, o use **Imprimir presentación** en Teikem (sección 5). Tampoco lleva estilos (negritas, rellenos) ni paneles fijos, y **los colores de ABC, estado y cobertura no van** en el Excel.
- La hoja **Inventario** (libro por representante) trae la existencia de **hoy** y las unidades del mes escogido; **cobertura y saldos se calculan con las unidades de ESE mes** (cobertura = disponible ÷ unidades del mes; saldo tras 1, 2 y 3 meses = disponible − unidades del mes × n), como el Excel de antes. **No es la cobertura semanal de la pantalla** («Movimiento de productos»), por eso los números pueden diferir. Si el movimiento del mes no se pudo leer al exportar, esa hoja no se incluye.
- **Resumen** lleva las 34 columnas, y **Notas** la definición de cada una y de dónde salen los datos.
- La exportación de **Transacciones** trae todo el mes (no solo la página en pantalla) y tarda unos segundos si el mes tiene miles de líneas.

## 7. Cómo cuentan descuentos, envío, notas de crédito y kits

| Concepto | Cómo cuenta |
|---|---|
| **Facturas de Teikem** | Solo las **emitidas, vencidas o pagadas** (activas), por su **fecha de factura**, y solo las líneas **de una orden de venta**. Importe = cantidad × precio de la línea (el descuento ya va en el precio). Cuentan **desde la fecha de corte** (o todas, sin fecha de corte) |
| **Descuentos** | Del **historial**: ítems de descuento de QuickBooks (clase Descuento); restan de la venta neta del representante y salen aparte como «Descuentos sin línea». En Teikem el descuento ya está en el precio de la línea |
| **Envío** | El cargo de envío de la orden (Teikem) o el ítem de envío (historial). **No es de ningún representante**: va en la conciliación («Envío y otros cargos») y cuenta en el «Total del mes». Otros cargos manuales en una factura de venta no entran |
| **Otros cargos** | Servicios, impuestos y demás del historial sin producto. Igual que el envío: conciliación, no representante |
| **Notas de crédito** | Las **aprobadas o aplicadas**, en el día de la compañía en que se **aprobaron**. **Restan** unidades y monto de sus líneas de producto (o de las que acreditan una línea de venta o de envío). Las de **concepto libre sin producto** (transporte, almacén) **no entran** |
| **Kits** | Una línea de un **kit se reparte entre sus piezas**: las unidades de cada pieza = kits × cantidad por kit, y el monto se prorratea con **la misma regla del archivo de QuickBooks** ([49](49-quickbooks.md)): peso = cantidad por kit × precio de lista, cierre al centavo. Se usa la composición de la línea de la orden o, si no la tiene, la receta del catálogo. **El kit en sí no sale** en Movimiento de productos. Un **kit armado** (con existencia propia) se vende como cualquier producto |

## 8. Pulso: venta del mes contra la cuota

En el panel **Ventas** del Pulso del día aparece la primera cifra **«Venta del mes vs cuota»** (para quien tiene `pulse.sales` y `sales.analysis.view` o `.manage`; si no, no sale):

- **Venta**: la venta neta (producto + descuentos) **del mes en curso hasta hoy**, de todos los clientes. **Cuota**: la suma de las **cuotas vigentes de los representantes activos** de este mes. Se muestra como «$venta» y debajo «de $cuota · N %», con una barrita: verde 100 % o más, ámbar 90 % o más, naranja por debajo. Sin cuotas dice «sin cuotas puestas».
- Un clic abre **Análisis → Análisis de ventas**.
- Como la venta incluye a los clientes sin representante pero la cuota solo a los representantes, la cifra se ve un poco más alta que la suma de los representantes. Asigne representantes (2.3) para que cuadre.

## 9. Ficha del cliente: panel «Representante de ventas»

En la ficha del cliente ([02 §1.9](02-clientes-y-contratos.md#19-representante-de-ventas-en-la-ficha-lote-241)) hay un panel plegable **«Representante de ventas»** con el representante y su ruta. Con `sales.analysis.manage` se cambia ahí mismo (**Cambiar el representante**). Se ve con `sales.analysis.view` o `.manage` y el módulo Órdenes de venta encendido; si no, no aparece.

## 10. Validaciones y mensajes

### 10.1 Mensajes del API

El API responde en español; el mensaje va en el `title` del error. Los de validación traen además `errors` con el campo.

| Campo o situación | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Mes (`month`, `compareMonth`, `fromMonth`, `toMonth`, `{month}` de la cuota) | `AAAA-MM` (también se lee `AAAA-MM-DD`) | `El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).` | 400 |
| Rango de meses | Final no anterior al inicial (Movimiento, Gráficos) | `El mes final no puede ser anterior al inicial.` | 400 (`toMonth`) |
| Rango de meses | Hasta 36 meses | `El rango no puede pasar de 36 meses.` | 400 (`fromMonth`) |
| Ruta: nombre | Obligatorio | `Escriba el nombre de la ruta.` | 400 (`name`) |
| Representante: nombre | Obligatorio | `Escriba el nombre del representante.` | 400 (`name`) |
| Línea: nombre | Obligatorio | `Escriba el nombre de la línea.` | 400 (`name`) |
| Nombre de ruta, representante o línea | Hasta 100 caracteres | `El nombre no puede pasar de 100 caracteres.` | 400 (`name`) |
| Ruta al crear o editar un representante | Activa | `La ruta está desactivada: actívela o escoja otra.` | 400 (`routeId`) |
| Representante al asignar clientes | Activo | `El representante está desactivado: actívelo o escoja otro.` | 400 (`repId`) |
| Marcas de una línea | La línea debe estar activa | `La línea está desactivada: actívela antes de cambiar sus marcas.` | 400 (`brands`) |
| Usuario del representante | De esta compañía | `El usuario no pertenece a esta compañía.` | 400 (`userId`) |
| Cuota | Cero o más, hasta 2 decimales | `La cuota debe ser cero o mayor, con hasta 2 decimales.` | 400 (`items`) |
| Renglones por llamada (cuotas, marcas, clientes) | Hasta 2,000 | `No se pueden mandar más de 2000 renglones en una sola llamada.` | 400 (`items`, `brands`, `clientIds`) |
| Asignar a varios clientes | Al menos uno | `Indique los clientes (clientIds).` | 400 (`clientIds`) |
| Marca vacía | Obligatoria | `Escriba la marca.` | 400 (`brands`) |
| Marca larga | Hasta 100 | `La marca no puede pasar de 100 caracteres.` | 400 (`brands`) |
| Días de entrega | De 1 a 365 | `Los días de entrega deben estar entre 1 y 365.` | 400 (`leadTimeDays`) |
| Nivel de servicio (z) | Mayor que 0 y hasta 4 | `El nivel de servicio (z) debe ser mayor que 0 y hasta 4.` | 400 (`serviceLevelZ`) |
| Transacciones: `skip` | 0 o más | `La página debe ser 1 o mayor.` | 400 (`skip`) |
| Transacciones y clientes: `take` | De 1 a 500 (salvo `all=true`) | `El tamaño de página debe estar entre 1 y 500.` | 400 (`take`) |
| Emparejar: tipo | `CLIENT` o `PRODUCT` | `El tipo debe ser CLIENT o PRODUCT.` | 400 (`kind`) |
| Emparejar: nombre | Obligatorio | `Indique el nombre tal como viene en el historial (raw).` | 400 (`raw`) |
| Emparejar: destino | `clientId` (cliente) o `productId` (producto) | `Indique el cliente o el producto de Teikem al que se enlaza.` | 400 (`clientId` o `productId`) |
| Importar: sin archivos | Al menos uno | `Suba al menos uno de los dos archivos (ventas o compras).` | 400 (`files`) |
| Importar: tamaño | Hasta 20 MB por archivo | `El archivo no puede pasar de 20 MB.` | 400 (`files`) |
| Importar: archivo equivocado | Debe traer las columnas Type, Date, Qty y Amount | `El archivo X no parece un reporte «Sales by Item Detail» o «Purchases by Item Detail» de QuickBooks (faltan las columnas Type, Date, Qty y Amount).` (X = el nombre del archivo) | 400 (`files`) |
| Importar: archivo en el lugar equivocado | El de ventas en `salesFile`, el de compras en `purchasesFile` | `El archivo X es un reporte de compras pero se indicó como de ventas.` (o `… de ventas pero se indicó como de compras.`) | 400 (`files`) |
| Cuerpo vacío | Los `PATCH`/`PUT`/`POST` con cuerpo | `El cuerpo de la solicitud es obligatorio.` | 400 (`body`) |
| Ruta inexistente o de otra compañía | — | `Ruta no encontrada.` | 404 |
| Representante inexistente (también en las cuotas en bloque) | — | `Representante no encontrado.` | 404 |
| Línea inexistente | — | `Línea no encontrada.` | 404 |
| Cliente inexistente (asignar, enlazar) | — | `Cliente no encontrado.` | 404 |
| Producto inexistente (enlazar) | Producto propio | `Producto no encontrado.` | 404 |
| Ruta repetida | Nombre único entre las activas (también al reactivar) | `Ya hay una ruta activa con ese nombre.` | 409 |
| Representante repetido | Ídem | `Ya hay un representante activo con ese nombre.` | 409 |
| Línea repetida | Ídem | `Ya hay una línea activa con ese nombre.` | 409 |
| Desactivar una ruta | Sin representantes activos | `La ruta tiene representantes activos: páselos a otra ruta antes de desactivarla.` | 409 |
| Desactivar un representante | Sin clientes activos asignados | `El representante tiene clientes asignados: páselos a otro representante antes de desactivarlo.` | 409 |
| Marca en dos líneas | Una marca va en una sola línea | `La marca «X» ya está en la línea «Y»: quítela de ahí primero.` | 409 |
| Edición simultánea (cuotas, clientes, enlaces) | — | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Sin el permiso | `sales.analysis.view` o `.manage` (administrar: `.manage`) | El API responde 403 (política de permisos; el texto exacto del cuerpo no está documentado para estas rutas) | 403 |
| Módulo apagado | `SALES_ORDERS` | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |

Avisos de la pantalla (no son errores; vienen en `warnings`): `N cliente(s) activo(s) sin representante: su venta sale aparte («Sin representante»).` · `N marca(s) sin línea: cada una cuenta como su propia línea.` · `N cliente(s) del historial sin emparejar: enlácelos a mano para atribuir su venta.` · `N ítem(s) del historial sin emparejar con un producto.` · el de traslape y el de líneas del historial que no cuentan (sección 2.6).

Pantalla: `Simule primero con estos mismos archivos.` (no se puede importar de verdad sin simular los mismos archivos) y `Hay cuotas que no se pueden leer: la cuota debe ser cero o mayor, con hasta 2 decimales.`

### 10.2 Mensajes del comando `import-sales-history`

| Situación | Mensaje exacto | Salida |
|---|---|---|
| Comando mal escrito (sin ruta, con opciones desconocidas o más de una ruta) | `Uso: dotnet run --project src/Teikem.Api -- import-sales-history <config.json> [--dry-run]` | 2 |
| No existe el `config.json` o un archivo | `Error de configuración: El archivo no existe: {ruta}.` | 1 |
| JSON mal escrito | `Error de configuración: El archivo de configuración no es un JSON válido: …` | 1 |
| JSON vacío | `Error de configuración: El archivo de configuración está vacío.` | 1 |
| Sin compañía | `Error de configuración: company es obligatorio.` | 1 |
| Sin archivos | `Error de configuración: Indique al menos uno de los dos archivos: sales o purchases.` | 1 |
| Compañía inexistente | `No existe la compañía «X» (se busca por nombre exacto).` | 1 |
| Falla al importar (archivo que no es el reporte, etc.) | `La importación se detuvo: {mensaje}` (el mensaje es uno de la tabla 10.1) | 1 |

## 11. Altas y bajas, y lotes importados

El análisis no usa el motor de estatus: rutas, representantes, líneas y cuotas se **activan y desactivan** (nunca se borran), y los lotes importados quedan **vigentes** o **reemplazados**.

| De → a | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|
| Ruta **activa → inactiva** | `sales.analysis.manage` | Sin representantes activos | Deja de salir en el Comparativo | Asignarla a un representante |
| Ruta **inactiva → activa** | `sales.analysis.manage` | Que no haya otra activa con el mismo nombre | Vuelve a contar | — |
| Representante **activo → inactivo** | `sales.analysis.manage` | Sin clientes activos asignados | Deja de salir (salvo en los meses en que vendió) y de sumar su cuota al Pulso | Asignarle clientes |
| Representante **inactivo → activo** | `sales.analysis.manage` | Nombre no repetido | Vuelve a contar | — |
| Línea **activa → inactiva** | `sales.analysis.manage` | — | Sus marcas vuelven a salir como su propia línea | Cambiar sus marcas |
| Línea **inactiva → activa** | `sales.analysis.manage` | Nombre no repetido | Vuelve a agrupar sus marcas | — |
| Cuota de un mes: **quitarla** | `sales.analysis.manage` | — | Vuelve a valer la del mes anterior más reciente | — |
| Lote importado **vigente → reemplazado** | `sales.analysis.manage` (al importar) | Mismo archivo, o un archivo del mismo tipo cuyo rango cubre sus líneas | Sus líneas se borran; el lote queda inactivo con la fecha | — |

## 12. Casos frecuentes y soporte

### «No me cuadra con QuickBooks»

1. Mire la **conciliación** del mes: «Diferencia por revisar» debe ser 0. Compare con QuickBooks el **Total del mes** (incluye envío y otros cargos), no la suma de los representantes.
2. En **Importar historial → Historial importado**, revise las **fechas** y el total del lote contra el reporte, y en una simulación los **totales por mes**.
3. Revise la **fecha de corte**: el historial cuenta antes de ella y Teikem desde ella. Un día sin fuente o con las dos fuentes cambia el mes.
4. «Sin representante» y «Producto sin emparejar» siguen en los totales; solo cambian de fila.
5. En «Movimiento de productos» la venta es **de producto** (sin descuentos ni envío): no es el total de QuickBooks.

### «Un cliente sale sin representante»

Asígnelo en Configuración → **Clientes sin representante** o en la ficha del cliente. Si el cliente es del **historial** y nunca emparejó con un cliente de Teikem, enlácelo en **Emparejar a mano** (su venta sigue en «Sin representante» hasta entonces). Si sale con otro representante del que esperaba, recuerde que **cuenta el representante de HOY** (sección 3.4).

### «Los underpads no cuentan en Padimont»

Los underpads (SKU `PD-…`) **no tienen marca** en QuickBooks, así que caen en la línea **«Sin marca»**. Póngales una marca (en Productos e inventario) y agregue esa marca a la línea **Padimont** en Configuración → **Líneas y marcas** (en la prueba del lote se usó la marca «PADIMONT UNDERPADS»).

### «Septiembre no coincide con mi Excel»

Septiembre es el mes de **comparación**. El Excel de octubre lo tomó de **otro reporte** («referencia por rutas misma fecha») y, en cambio, aquí es el mes completo con las mismas reglas que octubre y **con el representante asignado hoy**. Revise (a) que todos los clientes tengan representante (la parte sin él sale en «Sin representante»), (b) que el historial de septiembre esté completo y (c) la fecha de corte. El total de septiembre del historial es el de QuickBooks.

### «Reimporté y cambió un mes»

El archivo nuevo **reemplaza todas las líneas ya importadas entre su primera y su última fecha**. Si el reporte nuevo trae distinto contenido (transacciones corregidas en QuickBooks, otro rango, filtros distintos) el mes cambia. Compare en la simulación **«Reemplaza su rango de fechas (N líneas)»** y los totales por mes antes de importar de verdad. Un reporte parcial borra el resto de su rango: exporte siempre el completo.

### Otros

- **La Oeste (u otra ruta) sale con unos dólares de diferencia contra mi Excel.** Casi siempre es una nota de crédito de un cliente cuyo representante cambió: aquí resta al representante actual (3.4).
- **Todos los productos salen «Nuevo».** El rango de meses es muy corto: «Nuevo» se mide con el primer mes con venta **dentro del rango**. Amplíe el rango.
- **Un producto está «Parado» pero vendió el mes pasado.** «Parado» = más de 45 días desde su última venta hasta hoy.
- **La tendencia está vacía.** Solo se calcula si el producto vendió en los tres últimos meses completos.
- **Quiero cambiar los días de entrega para ver el reorden.** Ajustes (Configuración); el punto de reorden sugerido se recalcula. No cambia el «Mínimo (reorden)» del producto.
- **No veo la pestaña Configuración completa.** Sin `sales.analysis.manage` es de solo lectura y no tiene Importar ni Emparejar.

Los mensajes están en la [FAQ](faq.md#lote-241--análisis-de-ventas).

## 13. Para soporte

- **Tablas:** `SalesRoute`, `SalesRep`, `SalesQuota` (representante + mes), `ProductLine` + `ProductLineBrand` (marca → línea), `SalesAnalysisSetting` (fecha de corte, días de entrega, z), `SalesHistoryBatch`, `SalesHistoryLine`, `PurchaseHistoryLine`, `SalesHistoryAlias` y `Client.SalesRepId`; índice `IX_Invoice_IssueDate`. Sección «2026-11-12 — Lote 241» de `Diseño/logistica-db-update.sql` (marcas `L241_SALES_ANALYSIS_PERMS` y `L241_SALES_REP_FROM_FIELD`).
- **Código:** reglas puras en `SalesAnalysisRules` (ABC, estados, promedios, cobertura, reorden, cuotas, conciliación, kits), lector de líneas `SalesAnalysisLineReader`, vistas `SalesAnalysisService`, administración `SalesAnalysisSetupService`, importador `SalesHistoryImportService` / `SalesHistoryImportRunner`, lector del CSV `QuickBooksDetailReportParser`, API `SalesAnalysisController` (`/api/v1/sales-analysis/*`, módulo `SALES_ORDERS`).
- **EntityType de auditoría:** `SALES_ROUTE`, `SALES_REP`, `SALES_QUOTA`, `PRODUCT_LINE` y `SALES_ANALYSIS`. **Fuente de datos** de Vistas, Indicadores y Gráficos: `SALES_ANALYSIS_LINE` «Líneas de venta para análisis» (campo de fecha `Date`, lectura `sales.analysis.view`, hasta 20,000 líneas, las más recientes).
- **Límites conocidos:** la venta se atribuye al representante **actual** del cliente (cambia el pasado si se reasigna); las facturas de Teikem aportan solo sus líneas de venta y el envío de la orden; las compras de Teikem, solo los recibos contra órdenes de compra; las vistas se calculan en cada consulta (sin caché; décimas de segundo con 13,000 líneas); la importación por el API es síncrona y acepta 20 MB por archivo; el Excel no lleva gráficos ni estilos; la rejilla de cuotas hace una llamada por mes.
- **No se probó:** el camino de facturas, kits y notas de crédito de Teikem solo está cubierto por una prueba automática (no hay facturas reales de Teikem en la base de pruebas); las capturas de pantalla no se han tomado.
- Auditoría: los cambios a rutas, representantes, cuotas, líneas y ajustes quedan en la bitácora con los EntityType de arriba. Las líneas importadas **no** llevan bitácora (se reemplazan completas).
