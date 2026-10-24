# Lote 154 — Pantallas del portal del cliente de almacén (2026-10-20)

## Qué se construyó
- `app-cliente`: pestaña **Almacén** (Existencias, Productos, Movimientos, Valor) con buscador, paginación «Cargar más» y selector de sección; el menú se arma con `capabilities` de la compañía activa (`features/capabilities.ts`); Inicio redirige a Almacén si la compañía solo tiene almacén; Ajustes muestra «Cliente · Compañía» al cambiar de acceso.
- Servidor: `PortalCapabilitiesDto` agrega `Transport` (módulo LTL_GROUND); `Warehouse` = módulo WMS_LOTSERIAL.

## Cómo se probó
- `tsc` sin errores; jest completo (135 + 5 nuevos de `capabilities`). La suite .NET no pudo correr en este contenedor (sin SDK): el árbitro es el CI.

## Decisiones a revisar
- Mientras no se sabe qué ofrece la compañía se asume transporte sin almacén (la app de siempre).
- El cliente ve el valor de su inventario (base del seguro) solo con rol ADMIN/OPERADOR.

## Fuera de este lote
Crear productos, avisos de llegada y de salida desde el portal (Lote E); precarga/aviso de diferencia de precio del producto; capturas Playwright.

## Verificación de punta a punta (2026-10-24)
- **Recorrido Playwright nuevo** `web-app/e2e/lote154-portal-almacen.spec.ts` (API real + app del cliente, escritorio y móvil): crear y editar un producto; crear, editar y cancelar un aviso de llegada; crear, editar (datos y cantidad de la línea) y cancelar un aviso de salida con existencia, comprobando por el API la cantidad guardada, el reajuste y la liberación de la reserva, y que otro cliente no ve ni edita lo ajeno. Capturas en `docs/manual/frontend/img/l154-*.png` y capítulo `docs/manual/frontend/l154-portal-almacen.md`. Se salta solo si la app del cliente no está servida.
- **Defecto encontrado y corregido** (`app-cliente/src/kernel/ui/desk.tsx`): en una tabla sin acción por fila (`onRowPress` ausente) cada fila era un `Pressable` deshabilitado, y eso marcaba como deshabilitado todo lo de adentro (el botón «Editar» de la tabla de Productos aparecía `disabled` para lectores de pantalla y para las pruebas). Ahora la fila sin acción es una vista normal; con acción sigue igual.
- La edición **sí funciona** de punta a punta; no hubo defectos de servidor.
- **Sigue sin hacerse**: adjuntar documentos desde el portal (propuesta de documentos y prioridad en la conversación; falta que se decida).
