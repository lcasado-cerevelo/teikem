# Lote 217 — App móvil: clientes, consignatarios y almacén en consulta

## Decisión de Luis (2026-10-24)
La app móvil del personal lleva **clientes y consignatarios en lectura** y el **almacén solo para consultar**; **sin contabilidad ni facturación**. Los teléfonos, correos y direcciones web son siempre enlaces que se tocan.

## Qué se construyó
- **Servidor**: nada nuevo. Las pantallas usan los endpoints de la web (el servidor ya exige permiso y módulo): clientes, localizaciones y sus medios de contacto, productos (incluido por código de barras), saldos de inventario y posiciones.
- **App** (`app-movil`): tres pantallas nuevas con entrada en el inicio según permiso: **Clientes** (`clients.read`), **Consignatarios** (`locations.read`) y **Almacén (consulta)** (`inventory.view`; por producto —con lector de código de barras— y por posición). Reglas puras en `utils/lookupRules.ts` (dirección en una línea, enlace de mapa, `tel:`/`mailto:`/WhatsApp, ficha del cliente sin datos de dinero, existencias por almacén). Los permisos nuevos cuentan como «personal» para mostrar el inicio del personal.
- **Lo que no sale** (aunque el servidor lo mande): límite de crédito, identificación fiscal, condiciones de pago, contratos, facturación, costo, precio y valor.
- **Manual**: `docs/manual/45-app-movil.md` §2e; FAQ y README.

## Cómo se probó
- `tsc`, lint de lo tocado (los 7 errores de lint que quedan ya existían antes del lote), `npm test` con la prueba nueva `test:lookup` (enlaces, ficha sin campos de dinero, agrupación de existencias, código vs. texto) y `expo export`. Sin cambios de servidor no hay pruebas nuevas de servidor.
- **No probado en teléfono ni contra el API real.**

## Decisiones a revisar
- Se consulta **una compañía a la vez** (fichas arriba si la persona tiene varias), no mezcladas.
- Consignatarios no lista nada hasta escribir (pueden ser miles); Clientes lista los primeros 200 al abrir.
- Almacén no muestra datos de lotes por vencimiento ni series; se puede agregar si lo piden.
- Siguen pendientes de Luis: que la web arranque sin acción escogida, pruebas en teléfono real, llave de Firebase y aplicar `db-update`.
