# Lote 199 — Permisos «ver todo» y «reasignar» en el aparato (2026-10-21)

## Qué se construyó
- Permisos `warehouse.app.viewall` y `warehouse.app.reassign` (catálogo + sección de `Diseño/logistica-db-update.sql`, que los da a los roles de administrador; al supervisor los asigna el administrador).
- Servidor: guardia `ReassignGuard` en los cinco `assign` (política `warehouse.manage|warehouse.app.reassign`; solo con reasignar exige ver todo) y `GET /warehouse-tasks/assignees`.
- App: alcance nuevo (`assignScope`, `dispatchScope`), «Ver lo asignado a otras personas» en Recibir, Acomodar, Despacho y Mis tareas, `ReassignModal` y `assignApi`.

## Decisiones de Luis
`pick.any` sin efecto (opción B: lo sin asignar lo ve cualquiera); reasignar algo en curso, permitido; solo almacén activo; sin aviso al reasignar.

## A revisar
- Los operadores ya no ven todo el despacho del almacén (antes sí, por `pick.any`).
- `warehouse.manage` (que trae la plantilla de operador) sigue permitiendo asignar en el servidor; en el aparato el botón depende de los dos permisos nuevos.
- Sin prueba automática del guardia contra una base real (cubierto por la prueba de seguridad de controladores).
