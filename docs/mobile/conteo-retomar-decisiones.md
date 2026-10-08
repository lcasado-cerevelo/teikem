# App de almacén — Conteo: retomar, terminar con faltantes, conteos abiertos y «Por producto» (2026-10-07)

## Qué se construyó
1. **Retomar en vez de duplicar (servidor + app).** `POST /api/v1/cycle-counts` acepta `resumeOpen`. Con una posición y un conteo abierto de esa posición, devuelve ese conteo (`resumed: true`, con `countedQty` y
   `checkState` por línea). Si lo tiene otra persona: 409 con su nombre. Si no tiene asignado, queda a nombre de quien lo retoma. La app vuelve a poner lo ya contado y su estado de verificación.
2. **Terminar con líneas sin contar.** La app pregunta: **Seguir contando** / **Guardar y seguir después** (lote sin cierre) / **Dejar en 0 y terminar** (confirmado por el dueño). Con todo contado, cierra directo.
3. **Conteos abiertos.** Lista «Conteos abiertos» en Conteo (por id, sirve con duplicados), bloqueo de las demás acciones en Inicio mientras haya uno abierto del usuario en el almacén activo, y bloqueo de abrir
   otro conteo. La copia (`openCountHints`) vive en la base de la compañía; con señal se corrige con `GET /cycle-counts/page?status=OPEN` (asignados al usuario, de una posición).
4. **Aviso inmediato si el cierre falla.** Tras «Terminar» se sincroniza al momento y un rechazo se muestra en el acto.
5. **«Por producto».** Las posiciones ya contadas de un producto no se ofrecen otra vez; tocar un producto de la lista lo pone en el campo de escaneo (se confirma con Aceptar) y la pantalla sube hasta el campo
   (`KeyboardScreen.scrollToTopKey`; también en «Por posición»).

## Cómo se probó
- Servidor: `CycleCountResumeTests` (5): retoma con lo contado, sin `resumeOpen` crea otro, 409 con nombre, toma el sin asignar, no retoma varias posiciones ni terminados. Suite completa 3252 pruebas.
- App: `countApi.test` (retomar, cargar por id, guardar sin cerrar), `countResumeFinishScreen` (retomar + dejar en 0), `countSaveLaterScreen` (guardar + Inicio bloquea), `countOpenListScreen` (lista, bloqueo,
  continuar) y los de «Por producto» ajustados. `npx tsc --noEmit` limpio; jest 101 suites / 529 pruebas. Web: `npm run check` verde (contrato regenerado).

## Decisiones a revisar
1. **No se retoman** los conteos de varias posiciones ni los de por producto (su pantalla no es la de una posición). Si hacen falta, es un lote aparte.
2. **El bloqueo cuenta solo conteos asignados al usuario, de una posición, abiertos (Pendiente).** Los duplicados que ya existen (CC-00003/4/5 de GENERAL en Solutions) bloquearán a su dueño hasta
   terminarlos, cancelarlos o darlos de baja en la web.
3. **Cancelar sigue exigiendo `warehouse.count`.** Un contador a ciegas sale con «Dejar en 0 y terminar».
4. **«Dejar en 0» no verifica** esas líneas contra lo esperado (el reconteo con segunda oportunidad solo aplica a lo que se acepta una por una).
5. **La web no cambia:** crear un conteo de una posición que ya tiene uno abierto sigue permitido desde la web (sin `resumeOpen`).
