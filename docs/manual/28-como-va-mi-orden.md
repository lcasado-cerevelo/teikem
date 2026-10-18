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

## 2. La página pública «¿Cómo va mi paquete?» (Lote 116: solo por enlace)
> **Cambio del Lote 116 (2026-10-18, dictado por Luis):** la búsqueda manual por número de conduce y el «identificador de seguimiento» de la compañía **se quitaron**. La página es **solo de consulta por enlace**: el aviso que recibe el consignatario trae un enlace con un **token propio de la orden, largo e imposible de adivinar**, y al abrirlo se ve el resultado. La página **no recibe datos**.
- **Dirección:** `<app del cliente>/track/<token>`. Sin sesión; el idioma sale del enlace/navegador (o el que elija en la barra de preferencias).
- **Qué contesta:** el nombre de la compañía (el de la marca), el conduce, las etapas con cuál es la actual, el estatus y **cuándo fue el último movimiento**. Nada más: **nunca nombres, direcciones, teléfonos ni montos**.
- **Quién recibe el enlace:** el consignatario, en los avisos que el **cliente** prendió (capítulo 34).

## 3. Ya no hay ajuste de la compañía
**Ajustes de la compañía → General → Seguimiento público** ya no existe (no hay nada que encender: el enlace viaja en cada aviso). Un enlace que no existe responde 404.

## 4. Endpoints
| Endpoint | Para qué | Quién |
|---|---|---|
| `GET /api/v1/public/track/link/{token}?lang=es\|en` | El avance del paquete (`step` de `total`, `state`, `statusLabel`, `steps[]`, `lastUpdateUtc`) | Público, **20 por minuto por IP** |

`state`: `IN_PROGRESS`, `COMPLETED`, `ALERT` (detenida) o `CANCELLED`. El portal del cliente (`/api/v1/portal/orders` y `/portal/orders/{id}`) trae el mismo avance en `progress` (y `stepLabels` en la ficha).

## 5. Mensajes y errores
| Situación | Respuesta |
|---|---|
| Enlace que no existe, mal escrito o de una orden dada de baja | **404 `No encontramos ese enlace de seguimiento.`** (el mismo en todos los casos: no se revela cuál fue) |
| Más de 20 consultas por minuto desde una IP | 429 `Demasiados intentos; espere un minuto e intente de nuevo.` |

## Casos frecuentes
- **«La página dice que no encuentra mi paquete»:** el enlace está incompleto o no es de un aviso real; use el enlace tal cual viene en el correo. La página contesta lo mismo en todos los casos.
- **«¿Por qué no sale el nombre ni la dirección?»:** a propósito: cualquiera con un número puede consultar, así que solo se muestra el avance.
- **«La barra tiene más (o menos) etapas que antes»:** las etapas son los estatus del camino normal de la compañía; si se agregó, quitó o apagó un estatus, cambia la barra.
- **«Mi paquete tuvo un intento fallido»:** la barra se queda donde iba y el aviso dice «Intento fallido»; cuando la compañía lo reprograme, sigue avanzando.
- **El token no se puede adivinar:** por eso ya no hay búsqueda por número; aun así hay tope de consultas por IP y la página solo revela el avance, nunca datos de personas.
