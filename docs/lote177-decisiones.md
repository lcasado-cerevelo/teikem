# Lote 177 — Recolección maestra en la web (C3/C4, pantallas; pedido de Luis, 2026-10-21)

## Qué se construyó
- Cliente del API: contrato regenerado (`openapi.json` con los endpoints y esquemas del Lote 176 —solo lo mío, sin tocar lo que la otra sesión aún no publica— y `schema.d.ts`), ganchos `usePickGroups`, `usePickGroup`, `useCollectPickGroup`, `usePackPickGroup`, `useDeletePickGroup` y `exportPickGroups`.
- «Recolección y empaque»: la lista de la derecha tiene una tercera vista, **Maestras** (`PickGroupsPanel`, tabla paginada por el servidor con `exportRows`, filtros estatus y buscador), y en «Por documento» el botón **«Recolectar juntos»** (`PickGroupCollectModal`: lista de documentos con reservado por sacar, selección de 2 a 20 del mismo cliente —los de otro cliente se deshabilitan—, nota opcional).
- **Ficha de la maestra** (`PickGroupModal`): resumen, documentos y todos los movimientos agrupados por documento (producto, posición de donde salió, lote, cantidad); Empacar reutiliza `PackModal` con `group` (un solo envío) y Eliminar deshace todo. i18n es/en, estilos con la paleta del tema.
- Reglas puras en `pickGroupView.ts` (qué se puede juntar, bloqueo por cliente, cuerpo de la solicitud, movimientos por documento).

## Cómo se probó
- vitest: `pickGroupView.test.ts` (5), `PickGroupsPanel.test.tsx` (elegir documentos con bloqueo por cliente y la solicitud exacta; la vista Maestras y la ficha con movimientos y acciones), más las pruebas de recolección por documento, empaque y i18n (89 en total); `tsc` y oxlint.

## Decisiones a revisar
1. El cliente de la selección se compara por **nombre** (el listado por documento no trae el id del cliente); el servidor valida por id y responde 400 si difiere.
2. «Recolectar juntos» recolecta **todo lo reservado**; elegir cantidades por línea y por posición queda para el despacho normal de cada documento (el API ya lo admite con `lines`).
3. La lista «Por documento» no marca todavía a qué maestra pertenece cada recolección; se ve en la ficha de la recolección y en «Maestras».

## No se probó
- **El empaque de la maestra y su eliminación empacada contra el API real** (heredado del Lote 176; las pruebas de pantalla usan un fetch simulado). Falta el recorrido Playwright / smoke.
- Capturas del manual de pantallas.

## Qué quedó fuera
- App de almacén (Lote 178): elegir varios documentos y recolectar juntos desde el aparato.
