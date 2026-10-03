# Decisiones del dueño — cierre de los lotes de región y formatos, seguridad, marca, "hoy" y conteo por producto

Fecha: 2026-10-03. Respuestas de Luis a las decisiones que fueron quedando pendientes en los documentos de decisiones de los lotes 18 a 21, F9 a F12 y A3 a A4. "Se queda" = se acepta lo que ya está construido, sin cambios.

## Cambios que se hacen
| # | Tema | Decisión | Dónde |
|---|---|---|---|
| 1 | Revocar una sesión | **Cambiarlo: solo esa sesión.** Revocar una sesión (por ejemplo un aparato) cierra solo esa; la detección de robo se conserva solo para el reuso real de un token ya rotado. | Servidor (`AuthService`) |
| 2 | Recaptura del operario sobre una línea corregida | **Proteger la corrección.** Una línea ya corregida por el supervisor no se puede recapturar por el operario (se rechaza con mensaje claro); el supervisor sí puede volver a corregirla. | Servidor (`CycleCountService`) |
| 3 | Crear conteos por producto desde la web | **Sí, agregarlo** al "Nuevo conteo" de la web. | Web |
| 4 | Confirmar con todo en blanco en la app | **Exigir al menos un número.** Si no hay nada que contar, el operario escribe 0 al menos en una posición. | App |
| 5 | Despacho: escanear la posición antes de la cantidad | **Exigir la cantidad primero.** Escanear la posición sin cantidad no agrega la línea y avisa; con cantidad escrita, escanear sigue siendo "Aceptar". | App |

## Se queda como está
- Token de acceso de una sesión revocada: vive hasta 15 minutos más.
- "Cerrar las demás sesiones" incluye los aparatos de almacén.
- Receptores de DataWedge abiertos a otras apps del aparato (riesgo bajo en un Zebra administrado).
- Corregir en la web exige `warehouse.count`; el servidor acepta la corrección con `warehouse.count.capture`.
- Quien solo captura puede crear posiciones provisionales (quedan "pendiente de revisión").
- Al tocar un producto de la lista del conteo se escribe su SKU.
- Barra de navegación del aparato: solo el margen inferior, sin modo inmersivo.
- Lote nuevo desde "Otra posición": número obligatorio, vencimiento opcional.
- Pantalla de entrada (login) con el logo de Teikem; sin endpoint público de logos.
- La marca cuadrada no sustituye al lockup.
- Lote 20: sin pruebas adicionales con reloj fijo para los ocho servicios sin prueba propia.
- Firmas de commits: se dejan como están.
- Fecha de Puerto Rico: MM/DD/AAAA, hora de 12 horas (confirmado antes).
- Quién ve las cantidades esperadas al contar: no se toca.


## Implementación de los cambios 1, 2 y 3 (2026-10-03)

| # | Qué se hizo | Dónde |
|---|---|---|
| 1 | Un refresh token revocado a propósito (administrador, Mi cuenta, "cerrar las demás", logout, aparato desactivado) da 401 `Refresh token inválido.` sin cascada; la cascada queda solo para el reuso de un token ya rotado. **No hizo falta columna nueva:** `RefreshToken.ReplacedByTokenHash` ya distingue (lleno solo al rotar; al cambiar de compañía ahora también se llena, para que reusar el token viejo siga siendo señal de robo). Pruebas `RefreshRevocationTests`, smoke, manual 01, FAQ y decisión 3 de F10 marcada resuelta | `AuthService.FindActiveAsync` / `SwitchTenantAsync` |
| 2 | Una línea con corrección solo la vuelve a cambiar quien la corrigió o quien tiene `warehouse.count`; otro → 409 `La línea ya fue corregida por el supervisor; no se puede volver a capturar.` Captura por lote: todo o nada (el 409 agrega `Renglón(es) del lote: n (SKU). No se guardó nada.`). Manual 06, FAQ, decisión 3 del lote 21 marcada cambiada | `CycleCountService`, `CycleCountRules.IsLockedByCorrection` |
| 3 | Pestaña "Por producto" en "Nuevo conteo" de la web. Ver [loteF13-decisiones.md](frontend/loteF13-decisiones.md) | `CreateCountModal.tsx` |

### Decisiones para el dueño (tomadas con el valor más seguro)

1. **Reenviar el valor vigente no se rechaza** (cambio 2): si el operario reenvía el mismo número que ya tiene la línea corregida (un
   reintento de la cola de salida de la app) no cambia nada y responde 200. Lo único que se rechaza es cambiar el valor.
2. **Quien corrigió conserva el derecho** aunque hoy solo tenga `warehouse.count.capture`; una tercera persona con solo capturar queda
   bloqueada igual que el operario. La regla aplica también con el conteo ya Contado.
3. **El cambio 2 no hace que el lote entero se guarde "salvo" la línea bloqueada**: se rechaza todo (consistente con el resto de la
   captura). Si prefiere que las líneas libres se guarden y solo se informe la bloqueada, es un cambio acotado en `CaptureBatchAsync`.
4. **Cambiar de compañía (`switch-tenant`) ahora marca el token viejo como reemplazado**, así que reusar ese token viejo sí cierra todas
   las sesiones de la persona (igual que una rotación). Antes se trataba igual que cualquier revocación; si prefiere que sea una simple
   revocación, es quitar una línea.
5. **La cascada por reuso sigue cerrando todas las sesiones de la persona en todas sus compañías** (sin cambio).

### Pendientes

- **App de almacén (no se editó `app-almacen/`)**: `outbox.ts` trata el 409 (`code: conflict`) como rechazo de negocio (`REJECTION_CODES`):
  la fila pasa a `rejected` con el mensaje y **no se reintenta para siempre**. Pero una fila de captura en lote rechazada pierde **todo
  el lote** (todo o nada): el operario debe volver a capturar las líneas sin la corregida. Pendiente para el agente de la app: que la
  pantalla de rechazados explique que la línea ya la corrigió el supervisor y, tras el 409, refresque el conteo.
  **Resuelto (Lote A6 de la app, [loteA6-decisiones.md](mobile/loteA6-decisiones.md))**: Sincronización muestra la explicación con los
  renglones y SKU, "Actualizar el conteo" (solo al tocarlo) y "Descartar este envío". La app no reabre un conteo ya enviado: queda como
  pendiente en ese documento.
