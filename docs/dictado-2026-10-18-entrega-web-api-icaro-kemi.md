# Dictado de Luis, 2026-10-18 — entrega desde la web, portal, app de choferes, API mínima, integraciones tipo Icaro y Kemi

Notas textuales de lo que Luis pidió mientras se discutía P10 / P7 / P11. Es discusión abierta: nada de esto está construido ni decidido en detalle. Lo marcado **(pregunta)** hay que resolverlo con él antes de diseñar.

## 1. Registrar la entrega desde la web (sin depender de la app del chofer)
- Hay que **inventar una forma de registrar la entrega en la aplicación web**. No debe depender de la app de choferes (P7).
- Lo hace el personal desde la web y **requiere un permiso nuevo** («registrar entrega»).
- Si la entrega **exige firma**, la firma va **automática**: como quien registra tiene el permiso, está autorizado; la firma queda **a nombre del usuario que lo hizo, no del cliente**, y así se ve registrado.
- **Geolocalización:** si la entrega no trae ubicación, se usa el **servicio de Google** (geocodificación de la dirección) para obtenerla. La información de cómo hacerlo puede estar en una carpeta de `F:\Visual Studio 2022\Projects` llamada **`RentComparable`** (Luis ya la indicó).
- Con esto, los **documentos y el comprobante de entrega (POD)** ya no dependen de la app del chofer: pueden venir de la pantalla web.
- **(pregunta)** ¿Qué trae el comprobante cuando lo registra el personal: fecha y hora, quién recibió (nombre), nota, fotos opcionales? ¿Se distingue en pantalla «registrada por el personal» de «registrada por el chofer»?

## 2. Portal del cliente (P10) — lo que Luis espera
- **Lista de consignatarios del cliente:** «no veo por qué no». Se hace.
- **Preferencias de aviso del portal:** Luis espera que ya esté hecho, aunque esté vacío, y que haya **una pantalla para configurarlo**. Verificar qué existe (la tabla `PortalNotificationPref` figuraba sin entidad en el plan del 2026-10-10).
- **Facturas en la app del cliente:** se muestran; **no se pueden marcar como pagadas**.
- **Seguimiento en vivo:** sí depende de la app móvil (pings).
- Aclarar «ajustes» del Lote 102: el plan dice que la app del cliente trae «ajustes» (probablemente idioma, tema y huella), no necesariamente las preferencias de aviso. Verificar en el código.

## 3. App de choferes (P7)
- Debe **verse exactamente igual** a la que Luis ya usa: carpeta **`Colavora_Courier`** en `F:\Visual Studio 2022\Projects` (Luis la nombró «Colabora Web Courier»), dentro de la subcarpeta que él llamó «Colabora New», una aplicación **web Expo / React Native** que es la que se usa hoy.
- Cambia solo a qué API llama (el de Teikem); **las pantallas deben ser las mismas**.
- Luis va a dar/confirmar la carpeta. **(pregunta)** Revisar esa app antes de diseñar P7.

## 4. API de integraciones (P11): versión mínima
- Luis quiere **dar acceso a información** y quizá **ejecutar algunos procesos**, pero con una **versión mínima y controlada** del API: **no un libro abierto**.
- **Más lecturas que escrituras**; la escritura mínima: **crear órdenes** (lo mismo que hace la pantalla) y poco más.
- Luis lo va a analizar: qué se da y cómo. **(pregunta)** Lista de endpoints candidatos, credenciales por cliente con permisos acotados, límites de uso, y qué procesos se pueden disparar.

## 5. Integraciones tipo «Ícaro» (en Teikem)
- En `F:\Visual Studio 2022\Projects` hay una carpeta **`Icaro_Main`**: un software que Luis hizo como **integrador entre dos sistemas**, moviendo información de un lado al otro (entrada o salida).
- Para las integraciones de Teikem quiere **algo parecido a Ícaro**, con la diferencia de que **una de las dos fuentes es Teikem** (la controla él).
- Importa el **concepto de tipos de comunicación** (archivo, y otros) y poder **definir lo que se quiera hacer** sin programar cada vez.
- Ejemplo: **AxisCare** (Luis dijo «AsicsCare»; en la carpeta de proyectos existe `AxisCareDelivery`, a confirmar), cliente de Advance Logistics, **envía archivos** para actualizar las órdenes que se llevan con ellos; Luis quiere configurarlo en Teikem.
- Encaja con lo ya anotado en `plan-de-desarrollo-pendiente.md` (P11 y «procesos manuales de Advance»: COD por lotes, entregas marcadas por documento, órdenes de Truss).
- **(pregunta)** Revisar Ícaro para ver qué conceptos reutilizar (fuentes, mapeos, programación, bitácora).

## 6. Kemi (asistente) con el manual y la FAQ
- Kemi debe **contestar como una persona**, usando lo que ya hay en el **manual** y la **FAQ**.
- Para el usuario debe ser **transparente de dónde saca la respuesta**: Kemi simplemente contesta.
- Si la persona **no queda satisfecha**, Kemi **pregunta si le contestó** y **busca por otro lado**.
- Luis quiere analizar cómo hacerlo con el manual y la FAQ que ya existen. **(pregunta)** Proveedor de IA y dónde viven los datos (ya listado como pendiente del dueño), y qué pasa cuando no hay respuesta en el manual.

## Orden vigente
Sin cambio: P11 y P12 antes; luego P13; P7 y lo que depende de él al final. Esta discusión puede cambiar el alcance de P7 y P10 (la entrega registrada por la web).
