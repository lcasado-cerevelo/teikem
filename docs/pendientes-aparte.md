# Pendientes aparte (se hacen cuando Luis lo diga)

Anotados 2026-10-18, después de arreglar los recorridos de Playwright de escritorio (commit `b7e4b7e7`). Ninguno se empieza sin la orden de Luis.

1. **Las fallas intermitentes de Playwright** (última corrida limpia: 6 distintas, cambian en cada corrida; cada una pasa sola). Investigarlas una por una para saber si el problema está en el API o en que los recorridos comparten datos de la misma compañía (rutas por zona, Pulso, empaque con tarifa, avisos). Después decidir: aislar el recorrido o corregir el código.
2. **`scripts/smoke.sh`** (5.449 líneas, flujo de estatus anterior al Lote 106: `DRAFT`, `CONFIRMED`, `INBOUND`). Su paso del CI saldría en rojo. Opciones: arreglarlo o reemplazarlo en el CI por uno corto y vigente (recomendación: reemplazarlo).
3. **Recorridos en móvil**: repasarlos uno por uno (solo se revisó el que falló: lote 15 a 360 px).
4. **Cierre**: corrida completa final sobre base limpia (`db-init`), y dejar apagados el API y la base de pruebas.

Otros pendientes ya conocidos: la ficha de la orden no muestra el cargo por devolución; app móvil de entregas y de choferes (Luis dijo olvidar lo móvil por ahora).

Cómo se corre local: API con `--no-launch-profile`, `Legal__RequireAcceptance=false`, `Auth__Sessions__SingleSessionPerBrowser=false`, base limpia con `db-init` + `Diseño/logistica-db-zip-catalog.sql`, módulos encendidos con reautenticación (TAX apagado), `defaultServiceType=STANDARD` y `defaultPackageType=BOX` en los ajustes de la compañía.
