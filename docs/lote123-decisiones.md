# Lotes 123–124 — Kemi: «Pregúntame cómo usar el sistema» (P12)

## Qué se construyó
- **Lote 123:** la opción en el panel de Kemi y su caja de pregunta (primero como demostración «en construcción»).
- **Lote 124 (corrección del dueño, 2026-10-18: «Kemi va a buscar las palabras en el FAQ y en el manual», como en SEPHAS/Yudi):** Kemi **busca**, sin IA.
  - `web-app/scripts/build-help-index.mjs` (`npm run help:index`, también en `npm run check`): convierte `docs/manual/*.md` en `src/features/notifications/helpIndex.json` (2.705 entradas: 2.120 de la FAQ —cada pregunta con su respuesta— y 585 apartados del manual, texto limpio y recortado a ~1.100 caracteres).
  - `helpSearch.ts`: el buscador de Yudi portado — palabras sin acentos/plurales (más la vocal final y los 7 primeros caracteres, para juntar «registro/registrar», «cliente/clientes»), palabras vacías fuera, idf (las raras pesan más), título ×1,6 y nombre del capítulo ×0,8, el peso FAQ/manual según la forma de la pregunta, bono si el título coincide palabra por palabra, **suelo de calidad (0,18)**: sin nada parecido contesta «no encontré».
  - `KemiAsk.tsx`: pregunta → hasta 5 opciones (sin decir de dónde salen) → la respuesta ahí mismo → **«¿Te contesté?»**: Sí agradece; No **busca por otro lado** (otras opciones, sin repetir). Índice cargado la primera vez que alguien pregunta (chunk aparte: ~1,4 MB, 391 KB comprimido; no pesa en el arranque).

## Cómo se probó
- vitest: `helpSearch.test.ts` (palabras, intención, búsqueda sobre un índice chico y sobre el manual y la FAQ de verdad, vacío sin resultado) y 2 pruebas de la interfaz en `NotificationsUi.test.tsx` (preguntar → opciones → respuesta → «Sí»; «No» → otro lado → sin resultado → Volver). Suite completa: 3.174 pasan; build con el índice como chunk aparte.

## Decisiones a revisar
1. **El manual solo existe en español:** una pregunta en inglés busca en español (no encuentra casi nada). Habría que traducir manual y FAQ o decidir que Kemi en inglés avise.
2. **Sin contexto de pantalla todavía** (en SEPHAS, lo documentado de la pantalla donde está la persona sube en el ranking); en Teikem necesita un mapa pantalla→capítulo.
3. **La respuesta se muestra recortada** (~1.100 caracteres) y sin enlace al capítulo completo (el manual no está publicado como sitio). Si se publica, se agrega el enlace.
4. **Sin pruebas de recorrido con el navegador** del panel de Kemi (cubierto con vitest sobre el índice real).
