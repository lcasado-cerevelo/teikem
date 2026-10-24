# Lote 218 — Kemi en el portal del cliente, hablarle, y un solo cerebro (2026-10-24)

## Decisión de Luis
Kemi tiene que estar en el portal del cliente (no en la vista del consignatario) y **hacer lo mismo que en la web del personal**; solo cambia el índice de ayuda (el del portal es el del manual del cliente). Cuando Kemi tenga inteligencia artificial, **el Kemi del portal también la usa** ("eso no lo despinta nadie"). Y hay que poder **hablarle**, no solo escribirle. Lo de adjuntar documentos al portal se descartó.

## Qué se construyó
- **Compartido (`shared/kemi/`)**: el motor de búsqueda (movido desde la web), las reglas puras de la cara, los globos y la posición (`rules.ts`), la voz (`speech.ts`: idioma, errores, limpieza y el reconocimiento del navegador) y **el cerebro** (`brain.ts`: la interfaz `KemiBrain.ask` y su implementación de hoy, `createSearchBrain`). La web conserva sus archivos con el mismo nombre como re-exportaciones y su almacenamiento propio.
- **Kemi en el portal (`app-cliente/src/features/kemi/`)**: mascota con `react-native-svg` (mismo dibujo), vida (parpadeo, miradas, guiños, muecas; el puntero la hace mirar en la web), arrastrable y con su lugar recordado por persona, globo de mensaje nuevo con «Ver», panel de opciones (preguntar, mis mensajes, mis avisos, llevarla a su esquina, dormirla), dormida/despertar, caja de pregunta con «¿Te contesté?» y «busca por otro lado», contexto de la pantalla del portal, español e inglés. Se monta en el layout de la app solo con sesión de una persona del cliente (no consignatario), con el documento legal aceptado y sin el bloqueo de la huella.
- **Un índice por público**: `npm run help:index` arma el del personal (todo, sin cambios) y el del portal con **solo lo marcado `<!-- kemi:portal -->`** (10 capítulos y 14 secciones de la FAQ, más una sección nueva de FAQ de crear, editar y cancelar en el almacén del portal y otra de Kemi); `<!-- kemi:staff -->` saca un apartado o pregunta y los apartados «Endpoints» se omiten. 128 entradas en el portal contra 3.073 del personal. Lo que no se marca no llega al cliente.
- **Hablarle**: micrófono junto al campo en la web del personal y en el portal. La web usa el reconocimiento del navegador (Chrome, Edge, Safari); el portal usa `expo-speech-recognition` (el del aparato en el teléfono, el del navegador en la web). Lo dicho llena el campo mientras se habla y, al terminar, pregunta solo. Idioma según la pantalla (es-PR / en-US), errores explicados (sin permiso, no te escuché, sin soporte, red, otro). El audio no pasa por Teikem: solo el texto.
- **Un solo cerebro para la IA**: toda pregunta pasa por `KemiBrain`; la IA futura será una implementación en el servidor detrás de **un** endpoint que decide el público por la sesión (nunca por lo que mande el cliente) y le sirve a las dos pantallas sin cambiarlas. **No se construyó la IA**; solo se dejó el punto de enchufe.
- **Ajuste en las dos Kemi**: al cerrar y volver a abrir a Kemi se ven sus opciones, no la pregunta de la vez pasada.
- **Infraestructura**: carpeta `shared/` fuera de las dos apps → `metro.config.js` nuevo en `app-cliente` (vigila `../shared` y busca paquetes en su `node_modules`), `server.fs.allow` en Vite/Vitest y `roots`/`modulePaths` en jest; las dependencias nuevas de `app-cliente` son `react-native-svg` y `expo-speech-recognition` (con su plugin de permisos del micrófono y el reconocimiento de voz en `app.config.ts`).
- **Manual**: capítulo 46, capítulo de pantallas `frontend/l218-kemi.md` con capturas, FAQ de Kemi y de crear/editar/cancelar en el portal, README, y la regla de los marcadores en `CLAUDE.md`.

## Cómo se probó
- `shared/kemi/rules.test.ts` (12 pruebas, corre en vitest y en jest con el mismo archivo): voz, cerebro (carga una vez, «otro lado», reintento si falla), cara de Kemi, temas por pantalla del portal.
- Portal (jest): 25 pruebas nuevas (caja de pregunta con cerebro y micrófono simulados, el panel/globo/dormir/despertar/arrastre de posición, el índice real del portal: encuentra lo del cliente y no trae lo interno); suite completa 277 en verde; `tsc` limpio.
- Web (vitest): 5 pruebas nuevas de voz; las existentes de Kemi siguen en verde con el cerebro compartido.
- Playwright con el API real: `lote218-kemi-portal.spec.ts` (globo del mensaje real, panel, preguntar escribiendo, «No/Sí», preguntar hablando con el reconocimiento simulado, error del micrófono, lo interno no aparece, dormir, recargar y despertar, y el consignatario no ve a Kemi) y `lote218-kemi-web.spec.ts` (escritorio y móvil). Capturas `l218-*`.
- **No probado**: el reconocimiento de voz **real** (micrófono de verdad) en ningún navegador ni teléfono; Kemi del portal en el teléfono (solo web); la compilación nativa con `expo-speech-recognition` (requiere un build de desarrollo, no Expo Go).

## Decisiones a revisar
1. En el portal, el globo anuncia **mensajes** (la bandeja de avisos del portal no tiene «sin leer»). ¿Se quiere que anuncie también los avisos nuevos? Habría que agregar «sin leer» a esa bandeja.
2. En el teléfono, **la voz exige un build nuevo** de la app del cliente (módulo nativo): el APK actual no la trae.
3. El portal arrastra a Kemi en el celular sobre las pestañas (se queda 64 px encima de ellas).
4. El índice del portal ya incluye lo del almacén del cliente; si se agregan pantallas al portal hay que **marcar** su manual y FAQ para que Kemi las conozca.
5. Para la IA: decidir qué modelo y dónde, y qué se le deja leer (el índice del público que pregunta) y hacer (por ahora, solo contestar).
