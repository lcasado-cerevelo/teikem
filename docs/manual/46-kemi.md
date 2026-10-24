# 46 — Kemi: la asistente de Teikem (web del personal y portal del cliente), con voz

Lote 218 (2026-10-24, dictado por Luis). Kemi es la mascota de Teikem y hace lo mismo en los dos lugares donde vive: la **web del personal** y el **portal del cliente** (la app del cliente, en web y en el teléfono). Lo único que cambia es **lo que sabe**: cada uno tiene su propio índice de ayuda. Al consignatario con acceso propio no le sale Kemi.

## 1. Qué hace Kemi (igual en los dos)
| Qué | Cómo |
|---|---|
| **Aparece flotando** abajo a la derecha, con vida (parpadea, mira, guiña, hace muecas; en la web el puntero la hace mirarlo) | Se puede **arrastrar** a otro lugar; se acuerda por persona en ese aparato. «Llevar a Kemi a su esquina» la devuelve |
| **Te avisa** con un globo cuando llega algo nuevo y brinca | Web del personal: avisos (la campanita) y mensajes. Portal: **mensajes** nuevos del equipo de servicio (la bandeja de avisos del portal no tiene «sin leer»). «Ver →» abre lo que llegó. Una vez por sesión saluda con lo pendiente |
| **Panel de opciones** al tocarla | «Pregúntame cómo usar el sistema (o el portal)», «Mis mensajes», «Ver mis avisos», llevarla a su esquina y «Dormir a Kemi» |
| **Dormir y despertar** | Dormida se esconde junto al menú (o en la esquina de arriba en el celular) y no muestra globos. Un toque la despierta. Se acuerda al recargar |
| **Pregúntame cómo usar…** | Escribiendo **o hablando** (§2). Kemi busca las palabras en el manual y la FAQ y trae hasta 5 opciones; al abrir una muestra la respuesta y pregunta «¿Te contesté?» (**No** = busca por otro lado, sin repetir lo ya mostrado; si no hay más, lo dice) |
| **Sabe en qué pantalla estás** | Lo documentado de la pantalla donde estás sube en las opciones |
| **Idioma** | El manual está en español: en inglés Kemi busca con un glosario y avisa que responde en español |

## 2. Hablarle a Kemi
- En el campo de pregunta hay un **micrófono** al lado. Se toca, se dice la pregunta y lo que Kemi entiende **llena el campo mientras hablas**; al terminar de hablar **pregunta sola**. Se puede tocar el micrófono otra vez para dejar de escuchar. Es lo mismo que si lo hubieras escrito.
- **Dónde funciona:** en la web, con los navegadores que traen reconocimiento de voz (**Chrome, Edge y Safari**; Firefox no: ahí el micrófono no se ofrece). En el teléfono (app del cliente), con el reconocimiento de voz del aparato; la primera vez pide permiso para el **micrófono** y el **reconocimiento de voz**.
- **Idioma:** escucha en el idioma de la pantalla (español de Puerto Rico o inglés de Estados Unidos).
- **Privacidad:** el audio lo procesa el servicio de voz del navegador o del teléfono; **Teikem solo recibe el texto** de la pregunta y no guarda el audio.
- **Mensajes cuando algo falla:** «No tengo permiso para usar el micrófono…» (hay que activarlo en la configuración del navegador o del aparato), «No te escuché…», «Este navegador/aparato no sabe reconocer la voz…», «No pude conectarme al servicio de voz…», «No pude entenderte…».

## 3. Qué sabe Kemi: un índice por público
- `npm run help:index` (en `web-app`, también lo corre `npm run check`) arma **dos** índices desde `docs/manual`: el del **personal** (todo el manual y la FAQ, `web-app/src/features/notifications/helpIndex.json`) y el del **portal** (`app-cliente/src/features/kemi/helpIndex.json`), que lleva **solo lo que el cliente puede ver**.
- **Regla para quien escribe el manual:** un capítulo o una sección de la FAQ **llega al portal solo si lo marcas** con el comentario `<!-- kemi:portal -->` (invisible al leer): en un capítulo, en cualquier línea (vale para todo el capítulo); en la FAQ, justo debajo del «## ». Un apartado o una pregunta con `<!-- kemi:staff -->` adentro se queda fuera aunque su capítulo sea del portal, igual que los apartados «Endpoints». **Lo que no se marca no le llega al cliente**: así lo interno del personal (permisos, liquidación de choferes, roles…) no se filtra.
- La FAQ se indexa con la pregunta **en su propia línea en negritas** y la respuesta debajo.
- Capítulos marcados hoy: 24, 25, 26, 27, 28, 29, 30, 34, 35 y 37, y las secciones de la FAQ del portal (acceso seguro, documento legal, app del cliente, idioma y tema, mensajes con el equipo, nuevo envío, avisos al consignatario, facturas y avisos, perfil, almacén y crear/editar/cancelar, Lote 213).

## 4. Un solo cerebro (para la inteligencia artificial que viene)
Toda pregunta pasa por **una interfaz** (`shared/kemi/brain.ts`, `KemiBrain.ask`) que hoy busca en el índice del público de quien pregunta. Cuando Kemi tenga IA, será **un solo cerebro en el servidor**, detrás de **un** endpoint, que decide el público (personal o portal) por la sesión —nunca por lo que mande el navegador— y le responde a las dos pantallas **sin cambiarlas** (ni la caja de pregunta, ni el micrófono, ni los globos). El código compartido vive en `shared/` (motor de búsqueda, reglas de la cara y de los globos, voz y cerebro) y lo usan la web y el portal.

## 5. Dónde está cada cosa
- Web del personal: `web-app/src/features/notifications/` (`Kemi.tsx`, `KemiAsk.tsx`, `useKemiVoice.ts`).
- Portal del cliente: `app-cliente/src/features/kemi/` (`KemiFloating.tsx`, `KemiAsk.tsx`, `useKemiVoice.ts`, `KemiProvider.tsx`, `KemiMascot.tsx`), montada en `app-cliente/src/app/_layout.tsx`.
- Compartido: `shared/kemi/` (`helpSearch.ts`, `rules.ts`, `brain.ts`, `speech.ts`).
- Pantallas con capturas: [frontend/l218-kemi.md](frontend/l218-kemi.md).
