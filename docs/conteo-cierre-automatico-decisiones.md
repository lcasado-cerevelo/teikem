# Conteo cíclico: cierre automático y seguir contando en la app (2026-10-10)

## Por qué
En el inventario masivo de Advance Depot (≈3,000 posiciones) cada posición es un conteo (CC-#####) y confirmarlos uno por uno en la web no es viable.
Confirmar un conteo que **cuadra** no asienta ningún movimiento: solo cambia el estatus a Concordancia, la fecha/usuario y completa la tarea COUNT. Es un trámite sin decisión de por medio;
la decisión (aceptar un ajuste) solo existe cuando hay diferencia. Además, la app mandaba a Inicio al terminar, obligando a volver a entrar a Conteo en cada posición.

## Qué se construyó
- **Ajuste de compañía `Tenant.CountAutoCloseMatching`** (apagado por defecto; columna agregada en `Diseño/logistica-db-update.sql`, sección 2026-10-10). Pantalla: Sistema → Ajustes → Operación → «Cerrar solo los conteos que cuadran».
- **`CycleCountService.FinishAsync`** → `TryAutoCloseAsync`: tras dejar el conteo en Contado (su propia transacción), si el ajuste está encendido llama a `ReconcileCoreAsync(requireMatching: true)`, **la misma ruta** de «Cerrar los que cuadran» (plan compartido `BuildPlanAsync`, saldos bloqueados, re-verificación). Si no cuadra o cambió, queda Contado; terminar nunca falla por esto. Comentario del historial: `Cierre automático: el conteo cuadra.`
- **App del aparato** (`app-almacen/src/app/count.tsx`): `completeBin` y `finishProduct` ya no hacen `router.replace('/home')`; llaman `stayInCount()` (limpia el estado, refresca los conteos abiertos y muestra `count.finishedNote`). Sin conteo abierto, la pantalla de Conteo vuelve a su partida con la misma forma de contar y el `ScanField` con foco. «Guardar y seguir después» no cambia.
- Web: interruptor en `OperationsTab.tsx`; `openapi.json` y `schema.d.ts` (web y app) con `countAutoCloseMatching`.
- Manual: 06 §6.9, 09 §7.6, `faq.md`.

## Cómo se probó
- Pruebas xunit nuevas en `CycleCountByProductTests` (ajuste encendido: cuadra → Concordancia sin movimientos y tarea DONE; con diferencia → Contado; ajuste apagado → Contado).
- **No se pudo compilar ni correr nada localmente** (el contenedor no tiene el SDK de .NET ni `node_modules`): el árbitro es el CI de GitHub Actions al hacer push.

## Decisiones a revisar
1. Apagado por defecto (producción no cambia sola). Para el inventario masivo hay que encenderlo en la compañía.
2. «Confirmó» (`ReconciledBy`) queda a nombre de quien terminó el conteo, con el comentario que marca que fue automático.
3. Solo se cierra solo lo que cuadra contra el saldo **actual** (criterio de «Cerrar los que cuadran»); la foto vieja no cuenta.
4. La app muestra el mismo aviso en «Por posición» y «Por producto» y no dice si el servidor lo cerró solo (el cierre va por la cola de salida; el estatus se ve en la web).

## Fuera de alcance
Un solo conteo con varias posiciones (se descartó: una posición a medias retendría el cierre de las demás).
