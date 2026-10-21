# Lotes 164–166 — RCL-, estatus en masculino, nombres sembrados, documento legal y teléfonos de texto

Fecha: 2026-10-21. Respuestas de Luis a las decisiones del Lote 163.

## Lote 164 — Prefijo de reclamación `RCL-`
- `ClaimRules.NumberOf` y el número provisional de `ClaimService` usan `RCL-#####`; el recibo conserva `REC-#####`.
- `Diseño/logistica-db-update.sql`: renombra las reclamaciones existentes (`REC-` → `RCL-`), idempotente.
- Manual 31, capítulo de pantallas, FAQ y pruebas actualizados.

## Lote 165 — Estatus del envío en masculino
- Sometido, Recogido, Asignado, Entregado, Cancelado y Devuelto (Sin asignar, En tránsito, Intento fallido y En almacén no cambian). Sección SQL idempotente que solo cambia la etiqueta en español de `StatusCode` (`OrderStatus`); los códigos no cambian. Una etiqueta personalizada por compañía se conserva.
- Textos de pantalla (confirmar, cancelar, asignar, devolver, entregar, escaneo, integraciones) y mensajes del servidor concordados.

## Lote 166 — Nombres sembrados, permisos y documento legal
- **Permisos** (`PermissionCatalog`): «Ver envíos», «Crear envíos», «Editar envíos»… (el sembrador de permisos actualiza la etiqueta de los que ya existen al arrancar).
- **Indicadores, gráficos y reporte de sistema** (`SystemAnalyticsSeeder`): «Envíos», «Envíos en curso», «Envíos sin chofer asignado», «Envíos en excepción», «Envíos con SLA en riesgo», «Envíos por estatus», «Envíos por estado del SLA». Sección SQL que renombra las filas de sistema ya creadas (no pisa un nombre que la compañía ya tenga).
- **Pulso diario** (`KpiSnapshots`): «Envíos nuevos del día», «Envíos en almacén»…
- **Documento legal** (`LegalText`): «envíos» en español e inglés. **No se cambió la versión** (`LegalRules.CurrentVersion`): nadie tiene que volver a aceptar. Falta decidir si se sube.

## Teléfonos que quedaban (texto libre)
- Los teléfonos extra de aviso del cliente y los destinatarios tipo teléfono de las reglas de aviso se guardan ahora con prefijo, en E.164 (`+17875550100`); si no traen «+», el de la compañía. `PhoneRules.ToE164Text`.
- El teléfono del usuario (`User.PhoneNumber`) no tiene pantalla ni código que lo escriba, así que no se tocó.

## Cómo se probó
Solo lo que cambió (C#: reclamaciones, escaneo/rutas, devoluciones, permisos y catálogo, análisis y sembradores, KPI, legal, avisos del cliente y reglas, PhoneRules; web: reclamaciones y escaneo). SQL: sintaxis comprobada (`PARSEONLY`).

## Pendiente
- Subir o no la versión del documento legal (obliga a todos a aceptar de nuevo).
- Prosa del manual que sigue diciendo «orden».
- Búsqueda global («Buscar o ejecutar»), siguiente paso (ver `docs/busqueda-global-dictado-2026-10-20.md`).
