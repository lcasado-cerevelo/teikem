# 28 — «¿Cómo va mi orden?» y la barra de progreso

Lote 103 (portal de clientes, 2026-10-17). Dos cosas relacionadas: la **barra de progreso** de una orden y la **página pública** donde cualquiera con el número de conduce ve en qué etapa va su paquete. La respuesta pública es **solo el avance**: **nunca nombres, direcciones, teléfonos ni montos** (decidido con Luis).

## 1. La barra de progreso
> **Cambio del Lote 108 (2026-10-17, dictado por Luis):** la barra ya **no** tiene tantas etapas como estatus. Tiene **cuatro etapas fijas** y todos los demás estatus se encajonan en una de ellas. (Reemplaza el criterio del Lote 103.)

| Etapa | Qué estatus caen aquí | Lo que dice debajo de la barra |
|---|---|---|
| 1. **Sometida** | Sometida | «Sometida» |
| 2. **Recibida** | Recogida, Sin asignar, Asignada (ya la tiene la compañía) | «Recibida» |
| 3. **En ruta** | En tránsito (la tiene el chofer) | «En ruta» |
| 3. **En ruta** (en alerta) | Intento fallido | «Intentada» |
| 3. **En ruta** | Sin asignar o Asignada **después de un intento fallido** (volvió al almacén) | «Reprogramada» |
| 4. **Completada** | **cualquier estatus final**: Entregada, Cancelada, En almacén, Devuelta… | el nombre del estatus final que sea (prevalece el suyo) |

- **El camino de un intento fallido:** En ruta → **Intentada** (alerta, ámbar) → cuando llega al almacén y se escanea «Llegó», **Reprogramada** (sigue en la etapa 3) → cuando el chofer la vuelve a coger, **En ruta**. El intento fallido queda en el historial.
- **Qué muestra:** las etapas que ya pasaron en azul, **la actual resaltada («va por aquí»)**, las que faltan apagadas, y debajo «Paso 2 de 4 · Recibida». En la ficha y en la página pública también la **lista de las cuatro etapas**.
- **Completada:** toda la barra en verde con el nombre del estatus final («Entregada», «En almacén»…). **Cancelada:** la barra va **apagada** y dice «Cancelada».
- **Los nombres de las etapas no dependen de los estatus de la compañía** (apagar, agregar o renombrar estatus no cambia la barra). Lo que sale en «Completada» sí es el nombre del estatus final.
- **Dónde está:** en la **lista de órdenes** y el **inicio** del portal del cliente (barra baja), en la **ficha** de la orden (barra + lista de etapas) y en la **página pública**. El portal del consignatario (lote siguiente) usa el mismo componente.

## 2. La página pública «¿Cómo va mi orden?»
- **Dirección:** `<app del cliente>/track/<identificador de la compañía>` (ejemplo `/track/advance`); con `?n=CND-0000415` al final abre ya con la consulta hecha (para los avisos y mensajes). Sin sesión.
- **Por qué lleva el identificador de la compañía:** el número de conduce es único **dentro de cada compañía** pero se repite entre compañías, así que la página tiene que saber de cuál es. La persona solo escribe el número de conduce.
- **Qué contesta:** el nombre de la compañía (el de la marca), el conduce, las etapas con cuál es la actual, el estatus y **cuándo fue el último movimiento**. Nada más.
- **Está APAGADO** para toda compañía hasta que su administrador lo enciende (ver §3).

## 3. Encender o apagar (Ajustes de la compañía)
**Ajustes de la compañía → General → Seguimiento público** (`admin.tenant`; ver puede cualquier usuario): se escribe el **identificador** (3 a 40 caracteres: minúsculas, números y guiones; no puede ser una palabra reservada como `api` o `login`, ni uno que ya use otra compañía) y «Encender»; «Apagar» (o dejarlo vacío) lo quita y la página deja de contestar. El panel muestra el enlace para compartir. Cambiar el identificador deja de servir al anterior.

## 4. Endpoints
| Endpoint | Para qué | Quién |
|---|---|---|
| `GET /api/v1/public/track/{identificador}/{conduce}?lang=es\|en` | El avance del paquete (`step` de `total`, `state`, `statusLabel`, `steps[]`, `lastUpdateUtc`) | Público, **20 por minuto por IP** |
| `GET /api/v1/tenant/tracking` | El identificador de la compañía y si está encendido | Sesión |
| `PUT /api/v1/tenant/tracking` `{ slug }` | Encender (con un identificador) o apagar (vacío) | `admin.tenant` |

`state`: `IN_PROGRESS`, `COMPLETED`, `ALERT` (detenida) o `CANCELLED`. El portal del cliente (`/api/v1/portal/orders` y `/portal/orders/{id}`) trae el mismo avance en `progress` (y `stepLabels` en la ficha).

## 5. Mensajes y errores
| Situación | Respuesta |
|---|---|
| Compañía sin seguimiento, identificador desconocido, número que no existe, mal escrito u orden dada de baja | **404 `No encontramos ese número de conduce.`** (el mismo en todos los casos: no se puede averiguar cuál fue) |
| Más de 20 consultas por minuto desde una IP | 429 `Demasiados intentos; espere un minuto e intente de nuevo.` |
| Identificador no válido | 400 `El identificador debe tener de 3 a 40 caracteres: letras minúsculas, números y guiones (sin empezar ni terminar con guion).` |
| Identificador reservado | 400 `Ese identificador no está disponible.` |
| Identificador de otra compañía | 409 `Ese identificador ya lo usa otra compañía.` |

## Casos frecuentes
- **«La página dice que no encuentra mi número»:** revise que el enlace sea el de su compañía, que el número esté bien escrito (sin espacios) y que la compañía tenga el seguimiento encendido. La página contesta lo mismo en todos los casos.
- **«¿Por qué no sale el nombre ni la dirección?»:** a propósito: cualquiera con un número puede consultar, así que solo se muestra el avance.
- **«La barra tiene más (o menos) etapas que antes»:** las etapas son los estatus del camino normal de la compañía; si se agregó, quitó o apagó un estatus, cambia la barra.
- **«Mi paquete tuvo un intento fallido»:** la barra se queda donde iba y el aviso dice «Intento fallido»; cuando la compañía lo reprograme, sigue avanzando.
- **El conduce es consecutivo:** por eso hay tope de consultas por IP; aun así solo revela el avance, nunca datos de personas.
