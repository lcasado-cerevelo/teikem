# Plan de trabajo — dashboard de almacén y app del Zebra (aprobado por Luis, 2026-09-27)

Secuencia única (no en paralelo: los lotes comparten SQL, registro de servicios, contrato del API, humo y manual, y esta
sesión corre como máximo dos agentes a la vez). Cada paso cierra con commit, CI verde y su documento de decisiones. El
reporte a Luis se hace **al final del paso 6**; entre medio solo hay puntos de control en la rama `claude/great-davinci-t2nvtl`.

| Paso | Qué | Plan | Workflow | Tokens estimados | Cierre |
|---|---|---|---|---|---|
| 1 | 7A backend: catálogo de eventos, servicio y endpoint de actividad, índice, `belowMin`, seed de indicadores | `docs/lote7A-plan.json` | `lote-implementar` | 3 a 4 M | `docs/lote7A-decisiones.md`, manual 07 |
| 2 | 7A frontend: panel Almacén con filtro, panel Actividad reciente | `docs/frontend/loteF7A-plan.json` | `fe-implementar` (tras regenerar `web-app/openapi.json`) | 1.5 a 2.5 M | `docs/frontend/loteF7A-decisiones.md`, manual de pantallas |
| 3 | 8A backend: idempotencia, dispositivos y PIN, sincronización por diferencia, código de barras, operaciones atómicas | `docs/lote8A-plan.json` (se deriva de `docs/mobile/app-almacen-plan.md` §3 antes de arrancar) | `lote-implementar` | 3 a 4 M | `docs/lote8A-decisiones.md`, manual 08 |
| 4 | 8A app, primera entrega: núcleo, motor sin señal, Recibir; APK desde GitHub Actions | `docs/mobile/loteA1-plan.json` (piezas A0, A1) | `app-implementar` (nuevo, = fe-implementar sobre `app-almacen/`) | 4 a 5 M | `docs/mobile/loteA1-decisiones.md` |
| 5 | Luis prueba el APK en el Zebra (en paralelo con el paso 6; sus hallazgos entran como correcciones) | — | — | 0 | lista de hallazgos |
| 6 | 8A app, segunda entrega: Acomodar, Despacho, Conteo, Consultar, Sincronización | `docs/mobile/loteA1-plan.json` (piezas A2 a A5) | `app-implementar` | 4 a 6 M | `docs/mobile/loteA2-decisiones.md`, manual de la app |

Total estimado: 15 a 21 M tokens. Si la cuota se agota a mitad de un paso, el workflow se reanuda donde quedó.

Lo que no se puede hacer en este entorno: probar en el aparato físico (paso 5, Luis). Lo que sí: compilar el APK, pruebas
unitarias del motor de sincronización y recorrido en emulador Android en GitHub Actions con el API apagado a mitad del flujo.

Al terminar el paso 6: reporte único a Luis con lo hecho, decisiones a revisar, consumo real por paso, enlaces de CI y el
APK; Luis actualiza su copia local con `git pull origin claude/great-davinci-t2nvtl`.

## Bitácora

- 2026-09-27: plan escrito; arranca el paso 1.
