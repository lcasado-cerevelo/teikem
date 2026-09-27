# Plan de trabajo — dashboard de almacén y app del Zebra (aprobado por Luis, 2026-09-27)

Secuencia única (no en paralelo: los lotes comparten SQL, registro de servicios, contrato del API, humo y manual, y esta
sesión corre como máximo dos agentes a la vez). Cada paso cierra con commit, CI verde y su documento de decisiones. El
reporte a Luis se hace **al final del paso 5**; entre medio solo hay puntos de control en la rama `claude/great-davinci-t2nvtl`.

| Paso | Qué | Plan | Workflow | Tokens estimados | Cierre |
|---|---|---|---|---|---|
| 1 | 7A backend: catálogo de eventos, servicio y endpoint de actividad, índice, `belowMin`, seed de indicadores | `docs/lote7A-plan.json` | `lote-implementar` | 3 a 4 M | `docs/lote7A-decisiones.md`, manual 07 |
| 2 | 7A frontend: panel Almacén con filtro, panel Actividad reciente | `docs/frontend/loteF7A-plan.json` | `fe-implementar` (tras regenerar `web-app/openapi.json`) | 1.5 a 2.5 M | `docs/frontend/loteF7A-decisiones.md`, manual de pantallas |
| 3 | 8A backend: idempotencia, dispositivos y PIN, sincronización por diferencia, código de barras, operaciones atómicas | `docs/lote8A-plan.json` (se deriva de `docs/mobile/app-almacen-plan.md` §3 antes de arrancar) | `lote-implementar` | 3 a 4 M | `docs/lote8A-decisiones.md`, manual 08 |
| 4 | 8A app, primera entrega: núcleo, motor sin señal, Recibir; APK desde GitHub Actions | `docs/mobile/loteA1-plan.json` (piezas A0, A1) | `app-implementar` (nuevo, = fe-implementar sobre `app-almacen/`) | 4 a 5 M | `docs/mobile/loteA1-decisiones.md` |
| 5 | 8A app, segunda entrega: Acomodar, Despacho, Conteo, Consultar, Sincronización (sin pausa tras la primera) | `docs/mobile/loteA1-plan.json` (piezas A2 a A5) | `app-implementar` | 4 a 6 M | `docs/mobile/loteA2-decisiones.md`, manual de la app |
| 6 | Luis prueba el APK en el Zebra con la lista de pruebas del reporte; los hallazgos entran como un lote de correcciones | — | — | 0 | lista de hallazgos |

Total estimado: 15 a 21 M tokens. Si la cuota se agota a mitad de un paso, el workflow se reanuda donde quedó.

Lo que no se puede hacer en este entorno: probar en el aparato físico (paso 5, Luis). Lo que sí: compilar el APK, pruebas
unitarias del motor de sincronización y recorrido en emulador Android en GitHub Actions con el API apagado a mitad del flujo.

Al terminar el paso 5: reporte único a Luis con lo hecho, decisiones a revisar, consumo real por paso, enlaces de CI y el
APK; Luis actualiza su copia local con `git pull origin claude/great-davinci-t2nvtl`.

## Bitácora

- 2026-09-27: plan escrito; arranca el paso 1. Luis decide probar el aparato solo al final: la prueba pasa a ser el paso 6.
- 2026-09-27 (durante el paso 1): lectura de cuota de Luis: semana general 11 %, Fable 13 % (reinicio viernes 6:00 PM). Regla: si la proyección del siguiente paso supera el 50 % de cualquiera de los dos medidores, no se arranca; se deja todo subido y se reporta.
- 2026-09-27 12:10 UTC: paso 1 (7A backend) cerrado localmente (build, 1807 pruebas, db-init ×2 en base limpia, smoke verde) en el commit 12f8d2a; consumo real del workflow: 7.6 M tokens de agentes (114 agentes, 4 rondas de revisión, 29 correcciones), el doble de lo estimado. Arranca el paso 2.
- 2026-09-27 13:20 UTC: paso 2 (F7A frontend) cerrado localmente; consumo real 1.2 M tokens (14 agentes, 2 rondas). Arranca el paso 3 (8A backend).
- 2026-09-27 17:00 UTC: Luis decide que la verificación no se acota por rondas: se revisa hasta que salga limpio (backend: 2 rondas seguidas sin hallazgos confirmados; frontend: sin hallazgos altos). Los workflows quedan así; el paso 3, que corría con tope de 3 rondas, se reanuda sin tope si terminó con hallazgos pendientes.
- 2026-09-27 17:20 UTC: revisión de las bitácoras de los workflows: el paso 1 (7A backend) terminó en el tope de 4 rondas con correcciones en la cuarta y SIN una ronda limpia posterior; el paso 2 (F7A) cerró en la ronda 2 sin hallazgos altos pero con medias corregidas sin re-revisión. Pendiente: al cerrar el paso 3, reanudar la verificación sin tope sobre todo el backend (7A + 8A) hasta dos rondas limpias, y una ronda más del frontend F7A hasta ronda limpia.
- 2026-09-27 17:30 UTC: Luis: hacerlo bien a la primera. Los implementadores de ambos workflows ahora escriben y corren sus pruebas unitarias, compilan y se auto-revisan con las mismas lentes de los revisores antes de devolver la pieza (lista de salida), para reducir rondas.
- 2026-09-27 17:40 UTC: lectura de Luis: semana general 23 % (12 puntos desde el inicio del paso 1 ≈ 16 M tokens → ~1.3 M por punto). Proyección de cierre del plan: 34 a 40 %.
- 2026-09-27 18:00 UTC: paso 3 (8A backend) terminado con el tope viejo de 3 rondas (53 correcciones, 8.5 M tokens); arranca el paso 3b: re-verificación sin tope hasta dos rondas limpias.
