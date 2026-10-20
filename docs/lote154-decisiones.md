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
