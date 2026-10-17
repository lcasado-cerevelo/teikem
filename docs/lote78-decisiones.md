# Lote 78 — Pantalla de exportación a contabilidad (P5, 7 de 7) (2026-10-16)

Último lote de la facturación (P5). Pantalla web de las plantillas y del archivo de exportación del Lote 77. Siguen: los alcances de compras, despachos y liquidación con el mismo motor, el cargo por intento de entrega (P7) y P6 (devoluciones y notas de crédito).

## Qué se construyó
- **Sección «Exportar a contabilidad»** en la corrida aprobada o exportada (`billing.export`): selector de plantilla, **Configurar…** y **Archivo de exportación** (`ExportParts.tsx`, nuevo).
- **Editor de la plantilla**: nombre, formato, delimitador, fecha, encabezado y las columnas con su encabezado propio, subir, bajar, quitar y agregar del catálogo; Guardar, Guardar como plantilla nueva y Desactivar (con confirmación y no si es la única). Valida antes de enviar lo que el servidor valida.
- **Vista previa y descarga**: la vista previa no cambia nada; **Descargar y marcar como exportada** llama a `POST export`, baja el archivo (UTF-8 con BOM para que Excel lo abra bien) y la corrida queda Exportada; en una exportada, **Descargar de nuevo**.
- Hooks en `billingApi.ts`, reglas puras en `billingRules.ts`, textos `billing.exp.*` en es/en, estilos en `billing.css`; `openapi.json` y el cliente generado con las 5 rutas y los 6 esquemas nuevos.
- Manual de pantallas `docs/manual/frontend/l78-exportacion.md` con 3 capturas y entrada en el README.

## Cómo se probó
- vitest: reglas (validación, cuerpo, mover columnas, campos disponibles, qué corridas se exportan) y la pantalla con un fetch simulado (9): la sección con su selector, oculta sin permiso o sin estar aprobada, vista previa con la plantilla elegida sin llamar a exportar, descarga que marca la corrida, de nuevo en una exportada, editor con el cuerpo completo del PUT, validaciones y alta de plantilla nueva, error 409 del servidor y desactivar con confirmación (38 pruebas del módulo). `npm run check` completo.
- Playwright contra el API real (SQL Server), escritorio y móvil (360 px, sin scroll horizontal): aprobar la corrida con reautenticación, crear una plantilla TXT con tabulador y fecha `MM/DD/YYYY` con tres columnas y un encabezado propio, vista previa con el texto esperado (la corrida sigue Aprobada), descarga del archivo `facturacion-corrida-N.txt`, corrida Exportada y «Descargar de nuevo».

## Decisiones a revisar con Luis
1. El archivo se baja con **BOM UTF-8** desde la pantalla (para que Excel respete acentos y eñes); el servidor entrega el texto sin BOM. Si el programa de contabilidad no tolera el BOM, se quita en la pantalla.
2. La descarga la hace el navegador con el texto que devuelve el servidor (no hay un enlace de descarga directo): así reutiliza la reautenticación y los permisos del API.
3. La plantilla se edita sobre la corrida; no hay todavía una pantalla aparte de «Plantillas» (útil cuando haya más alcances: compras, despachos y liquidación).
4. «Guardar como plantilla nueva» no copia el nombre actual: hay que escribir otro (si no, el servidor responde 409).

## Cierre de P5
Con este lote P5 queda completo en lo que depende del producto actual: corridas, venta de producto, flete, pagos, estado de cuenta, cuentas por cobrar y exportación. Quedan por otros pasos: la contabilización de compras y de despachos (mismo motor), el cargo por intento de entrega (P7) y las notas de crédito (P6).
