# Tecnologia de grabacion, sincronizacion y edicion

Informe de investigacion (solo lectura, 2026-11-12). Todas las pruebas se hicieron con una pagina estatica (`page.setContent`, tabla densa tipo lista de Productos de Solutions) y con ffmpeg; no se levanto ningun servidor ni se toco ninguna base. Carpeta de pruebas: `C:/Users/Luis/AppData/Local/Temp/claude/26063e9da1fa915f/d671983b-2b8a-41be-94ca-7b134cdc7625/scratchpad/video-spike` (en adelante `SPIKE`).

**Resumen para decidir:** grabar con Playwright es viable y queda legible. Hay que (1) fijar `recordVideo.size` IGUAL al viewport, (2) inyectar un cursor falso, (3) no usar `showActions`, (4) un recorrido por escena y (5) editar con el ffmpeg completo que ya esta instalado. La calidad de `recordVideo` es "buena, no perfecta" (VP8 a ~1,1 Mbps, PSNR ~30 dB frente a PNG): el texto de 12 a 14 px se lee, pero algo blando al ampliar. Si Luis quiere nitidez maxima hay una ruta mejor (cuadros JPEG por `page.screencast` y codificar directo a H.264), con mas trabajo.

## 1. Resultado de las pruebas

Pagina de prueba: barra lateral oscura, tabla de ~60 productos (codigo, nombre, categoria, existencias, precios, estatus), texto de 12 a 14 px, con una banda de cuadros que cambia en cada frame para contar fps reales. Tema claro y oscuro. Datos crudos: `SPIKE/resultados-02.json` (recordVideo) y `SPIKE/resultados-03.json` (alternativas); scripts `01-grabar.cjs`, `02-medir.cjs`, `03-alternativas.cjs`, `pagina.cjs`.

### 1.1 recordVideo (webm VP8, Playwright 1.63.0, Chromium headless shell 153)

| Id | Viewport CSS | dsf | `size` del video | Archivo | Duracion | fps (nominal / distintos medidos) | Nitidez observada | Cursor |
|---|---|---|---|---|---|---|---|---|
| A | 1920x1080 | 1 | 1920x1080 | 1314 KB | 9,7 s | 25 / 25 | Legible; blando al ampliar (PSNR 30,3 dB) | No (sin cursor falso: Playwright no dibuja cursor) |
| B | 1920x1080 | 1 | 1920x1080 | 1286 KB | 9,6 s | 25 / 25 | Igual que A (PSNR 31,1 dB) | Si, el falso se ve bien |
| C | 1920x1080 | 1 | 1920x1080 | 1356 KB | 9,9 s | 25 / 25 | Igual, tema oscuro (30,4 dB) | Si |
| D | 1280x720 | 1 | 1280x720 | 1002 KB | 9,8 s | 25 / 25 | Letras mas grandes en pantalla, pero video 720p (hay que ampliarlo a 1080: blando) | Si |
| E | 1280x720 | 1 | 1280x720 | 1032 KB | 9,8 s | 25 / 25 | Igual, oscuro | Si |
| F | 1920x1080 | 2 | 1920x1080 | 1240 KB | 9,4 s | 25 / 25 | Bordes de letra algo mas suaves (el navegador reduce de 3840 a 1920); mejora leve | Si |
| G | 1920x1080 | 2 | 1920x1080 | 1212 KB | 9,0 s | 25 / 25 | Igual que F, oscuro | Si |
| H, I | 1280x720 | 1,5 | 1920x1080 | 936 KB | 9,0 s | n/a | MALO: el contenido sale a 1280x720 pegado arriba a la izquierda y el resto gris. No escala | Si |
| J | 1920x1080 | 2 | 3840x2160 | 1140 KB | 9,2 s | n/a | MALO: igual, el contenido ocupa 1920x1080 de un lienzo 4K gris; el bitrate sigue en ~1 Mbps | Si |
| K | 1920x1080 | 1 | sin `size` | 558 KB | 9,7 s | 25 | MALO: sale 800x450 (escala por defecto) | Si |
| L | 1920x1080 | 1 | 1920x1080 + `showActions` | 6234 KB | guion de 10 s; la corrida tardo 64 s | n/a | Rotulos en INGLES ("Mouse move") y corrida ~6 veces mas lenta | Cursor y circulo propios de Playwright |

Conclusiones medidas:
- Formato fijo: VP8 en webm, 25 fps, ~1,1 Mbps a 1080p (unos 130 KB por segundo). Sin duracion en la cabecera (medir con `ffprobe -show_entries format=duration`).
- **El video empieza ~0,57 s antes de que la pagina este lista** (1,5 s en la corrida A, arranque en frio). Hay que recortar el inicio o marcar el instante con un cuadro de sincronia.
- Tema claro y oscuro quedan igual de bien; el oscuro pesa 5 % mas.
- `size` MAYOR que el viewport NO escala: pega el contenido arriba a la izquierda (captura `capturas/J-2160-dsf2-claro.clic.completo.png`). Regla: `size` = viewport CSS. La resolucion real del video la fija el viewport, no el dsf.
- dsf 2 con `size` 1920x1080 no cambia la resolucion de salida; solo suaviza un poco y cuesta CPU (screenshot 6,5 fps frente a 15).

### 1.2 Alternativas (misma pagina, 1920x1080; `resultados-03.json`)

| Variante | fps medidos | Tamano por cuadro | Observacion |
|---|---|---|---|
| `page.screencast.start({onFrame, path, quality:100})` (API publica de 1.63) | 59,8 cuadros/s entregados (JPEG con marca de tiempo en reloj de pared) | 491 KB (q100), 253 KB (q90) | El webm que escribe a la vez sigue siendo VP8 a 25 fps (~0,9 Mbps): igual que recordVideo. Con dsf 2 entrega 1920x1080 |
| CDP crudo `Page.startScreencast` PNG | 59,9 | 224 KB | PNG sin perdida de 1920x1080; con dsf 2 sigue 1920x1080 |
| CDP crudo JPEG q100 | 59,8 | 491 KB | |
| `page.screenshot` en bucle, PNG 1080p | 15 (66 ms) | 169 KB | |
| `page.screenshot` JPEG q90/q100 1080p | 20,1 (50 ms) | 253 / 492 KB | |
| `page.screenshot` PNG dsf 2 (3840x2160) | 6,5 (154 ms) | 594 KB | 4K reales, pero lento |
| CDP `Page.captureScreenshot` con `optimizeForSpeed` | 20 a 30 | | |

Las cifras de 60 fps son sobre una pagina simple; con la web real (React, tablas) bajaran. No se probo contra la web de Teikem (prohibido levantar servidores).

### 1.3 ffmpeg (completo, `C:/Program Files/ffmpeg/bin`) sobre el video B

- webm a MP4 H.264 CRF 18 (libx264, medium): 1,3 s para 9,6 s de video, 2,9 MB. Con `h264_nvenc` (RTX 4060 Ti): 1,7 s, 4,5 MB (mas grande a esa calidad; no hace falta).
- Mezcla completa en una pasada (recorte de 0,6 s, congelar el ultimo cuadro, audio AAC 192k, rotulo, subtitulos .srt quemados): 1 s; MP4 1920x1080 25 fps h264+aac de 7,0 s (`SPIKE/ff/out.mp4`).
- Acercamiento: `scale=w=...:eval=frame` + `crop` funciona (`SPIKE/ff/z4.png`) pero amplia pixeles ya comprimidos: texto blando. (`crop` con `w/h` dependientes de `t` NO funciona: se fijan al inicio; `ff/f3.5.png` mostro solo desplazamiento.)
- `silencedetect` (-40 dB, 0,4 s) separo un audio unico de 3 bloques de tono: silencios en 1,80-2,50 s y 5,10-5,80 s, exactos a 0,0001 s (`SPIKE/ff/unico.wav`).

### 1.4 Capturas que mire (Read)

- `SPIKE/capturas/B-1080-dsf1-claro.clic.completo.png` (pagina completa con cursor y anillo de clic)
- `SPIKE/capturas/B-1080-dsf1-claro.quieto.zoom.png` y `F-1080-dsf2-claro.quieto.zoom.png` (arriba video, abajo referencia PNG; F algo mas suave)
- `SPIKE/capturas/D-720-dsf1-claro.quieto.zoom.png`
- `SPIKE/capturas/H-720css-dsf15-claro.clic.completo.png` y `J-2160-dsf2-claro.clic.completo.png` (fallos de `size`)
- `SPIKE/alt/sc-1080-dsf2-q100.cuadro.jpg` (cuadro de screencast: nitido)
- `SPIKE/ff/f1.png`, `f3.5.png` (rotulo y subtitulos), `z4.png` (zoom), `L4.png` (showActions)

Juicio: en 1080p el texto de 12 a 14 px se lee sin esfuerzo; las letras pequenas tienen halo de compresion VP8 al ampliar. Los cuadros JPEG q100 del screencast se ven claramente mejor.

## 2. Opciones de captura comparadas

| Opcion | Ventajas | Limites / riesgo |
|---|---|---|
| **A. `recordVideo` del contexto** (`web-app/node_modules/playwright-core/types/types.d.ts:11560-11595`: `dir`, `size`, `showActions`) | Lo mas simple: un webm por contexto al cerrar; 25 fps estables | VP8 ~1,1 Mbps; el archivo solo existe al cerrar el contexto (si el proceso cae, se pierde); desfase inicial de 0,57 s; sin cursor |
| **B. `page.screencast.start({path, onFrame, size, quality})`** (`types.d.ts:18621-18740`, API de 1.63; ademas `showActions`, `showChapter`, `showOverlay`) | Se inicia y para por escena en la misma pagina; cuadros con marca de tiempo real; rotulos de capitulo nativos | Con `path` el webm sigue siendo igual de modesto |
| **C. B con `onFrame` (JPEG q100, hasta 60 cuadros/s) y codificar con ffmpeg** | La mejor calidad (sin VP8); tiempos exactos; MP4 a fps constante con `-vsync cfr` | Mas codigo; ~490 KB por cuadro (~30 MB/s si se guardan los 60: tirar a 30 fps o JPEG q90); CPU de dos procesos |
| **D. CDP `Page.startScreencast` PNG** | Sin perdida | Pesado; no mejora lo visible frente a JPEG q100 |
| **E. `page.screenshot` en bucle** | 4K reales con dsf 2 | 6 a 20 fps: entrecortado; solo para pasos fijos tipo diapositiva |
| **F. Grabar la ventana real (OBS, ffmpeg `gdigrab`)** | Cursor real, 60 fps | No reproducible ni sincronizable con el guion; depende de DPI y notificaciones. Descartada |

**Recomendacion:** empezar con **A o B (con `path`) a 1920x1080 / dsf 1**, una escena por archivo, cursor falso inyectado. Si en una escena real el texto no convence, pasar a **C** solo para esas escenas (mismo guion de acciones; cambia la captura). Prefiero B porque abre y cierra la grabacion por escena sin recrear el contexto y deja la puerta abierta a C. No usar `showActions` (rotulos en ingles, 6 veces mas lento).

Tamano del texto: a 1920x1080 CSS las letras de la web (12 a 14 px) salen chicas en video. Opciones: (a) zoom de la pagina (`document.documentElement.style.zoom`, NO probado con la web real); (b) viewport 1536x864 o 1280x720 con video del mismo tamano (letras mas grandes, ampliado en la edicion, algo blando); (c) acercamientos puntuales. Decision de Luis (seccion 8).

## 3. Cursor, clics, resaltados y acercamientos

- **Cursor falso (probado, queda bien)**: `SPIKE/pagina.cjs` exporta `CURSOR_FALSO`: un elemento fijo que sigue `mousemove` y un anillo naranja al hacer clic (`B-...clic.completo.png`: flecha negra con anillo sobre la fila). Debe inyectarse con `page.addInitScript` para que sobreviva a las navegaciones de la web (no probado con la web real).
- **Movimiento suave**: `page.mouse.move(x, y, {steps})` manda todos los pasos de golpe; para que se vea el recorrido hay que repartirlos en el tiempo (funcion `mover` de `01-grabar.cjs`: ease-in-out cada ~16 ms). Un movimiento de 0,5 s da un trayecto natural.
- **Tecleo**: `keyboard.type` con `delay` de 60 a 80 ms se ve humano.
- **Resaltados**: borde o fondo amarillo inyectando CSS (`page.addStyleTag`) o un `div` absoluto sobre el elemento.
- **Acercamientos**: (1) en la edicion con ffmpeg (`scale eval=frame` + `crop`): funciona, pero blando; (2) en la pagina con `zoom`/`transform: scale()` sobre un contenedor mientras graba: mantiene el texto nitido porque se vuelve a renderizar (recomendado para tablas y botones; sin probar en la web real). Playwright no tiene API de zoom de camara.
- **Rotulos de capitulo**: `page.screencast.showChapter(titulo, {description, duration})` (nativo) o un overlay propio.
- **Pausas de lectura**: `waitForTimeout` por bloque, calculado de la duracion del audio (seccion 4).

## 4. Audio: metodo de sincronizacion recomendado

**Audio primero.** Luis entrega el audio, yo lo mido y el recorrido de Playwright se acompasa a esas duraciones. Es mejor que estirar video: el video no se acelera mucho sin que se note y el audio no se estira.

1. Yo escribo el guion en bloques numerados (un bloque por accion visible, por ejemplo "01-03 Buscar un producto").
2. Luis pasa cada bloque por el TTS y devuelve **un archivo por bloque** (o uno por escena con pausas de ~1 s entre bloques), WAV o MP3, 44,1 o 48 kHz, sin musica, nombrados `01-03.wav`.
3. Yo mido con `ffprobe` y genero un `tiempos.json` con la duracion de cada bloque.
4. El script de Playwright de cada escena usa esas duraciones: por cada bloque ejecuta la accion y espera `duracion_audio - lo_que_tardo_la_accion` (colchon minimo 0,4 s). Si la accion tarda mas que el audio, se agrega silencio al audio (`apad`).
5. El script registra marcas de tiempo (`Date.now() - t0`, como hace `01-grabar.cjs`) y la edicion coloca cada audio en su marca.

**Si Luis solo puede entregar UN audio largo:** se parte por silencios con `silencedetect` (probado: separo 3 bloques con silencios de 0,7 s con error de 0,1 ms; con voz real usar -35 a -40 dB y `d=0.5`). Condiciones: pausas de al menos 0,6 s entre bloques y que yo conozca el numero de bloques para verificar la cuenta; si no coincide, se ajusta el umbral. Mas robusto: un parrafo en blanco entre bloques en el texto que se le da al TTS.

**Que pedirle a Luis:** un archivo por bloque o escena; WAV o MP3 a 128 kbps o mas; mismo narrador y velocidad en todo; ~0,3 s de silencio al inicio y al final; entregar tambien el texto exacto que leyo (para los subtitulos). Si se reescribe un bloque, solo se rehace ese archivo.

**Video primero (plan B):** grabar natural y luego ajustar cada escena al audio con `setpts` (hasta 1,25x es aceptable), congelando el ultimo cuadro (`tpad=stop_mode=clone`, probado) o recortando pausas. Menos preciso en clics y tecleo.

## 5. Edicion y exportacion

**Ya instalado (verificado con `-version`):**
- ffmpeg 9.0.2 full (winget): `C:/Users/Luis/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.2-full_build/bin` (ffmpeg, ffprobe, ffplay).
- Otro ffmpeg full (2026-04) en `C:/Program Files/ffmpeg/bin` (el de mis pruebas; trae `libx264`, `libx265`, `libvpx`, `aac`, `h264_nvenc`, `h264_qsv`, `h264_amf`, filtros `subtitles`, `drawtext`, `zoompan`, `xfade`, `silencedetect`, `loudnorm`, `amix`). Ninguno esta en el PATH: usar la ruta completa.
- ffmpeg recortado de Playwright (`%LOCALAPPDATA%/ms-playwright/ffmpeg-1013/ffmpeg-win64.exe`, n7.0.1): lee webm/matroska y h264, escribe webm VP8/VP9, png, mjpeg, apng; NO tiene x264 ni aac ni mp4. No sirve para exportar el MP4.
- Hardware: RTX 4060 Ti y 32 hilos (`h264_nvenc` disponible; no hace falta, libx264 es rapido).
- Python 3.12 (pip: solo pillow; sin moviepy, whisper ni imageio-ffmpeg), Node 24, Playwright 1.63.0. Fuentes Arial y Calibri en `C:/Windows/Fonts`.
- Voces de Windows (SAPI): solo ingles (David, Zira); no hay voz en espanol, asi que el TTS real lo pone Luis.

**Falta instalar:** nada para el MP4 con voz, titulos y subtitulos. Opcional (decision de Luis): un editor grafico (DaVinci Resolve gratis o Clipchamp de Windows) para retocar a mano, y `faster-whisper` (pip) solo si se quisieran tiempos por palabra; no es necesario porque el .srt sale de las duraciones medidas.

**Receta (probada):**
`ffmpeg -ss 0.6 -i escena.webm -i voz.wav -filter_complex "[0:v]tpad=stop_mode=clone:stop_duration=3,scale=1920:1080,subtitles=g.srt:force_style='FontName=Arial,FontSize=10',drawtext=...[v]" -map "[v]" -map 1:a -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -shortest salida.mp4`. Unir escenas con `concat` (mismos parametros) o `xfade` para transiciones. Normalizar la voz con `loudnorm=I=-16:TP=-1.5`. Salida: MP4 1920x1080 25 o 30 fps, H.264 CRF 18, AAC 192k: unos 3 MB por 9 s de tabla en movimiento; un video de 6 a 8 min pesara unos 80 a 150 MB.

## 6. Subtitulos y rotulos

- **.srt desde guion y duraciones**: inicio de cada bloque = suma de duraciones previas mas pausas; fin = inicio + duracion del audio; texto = lo que leyo el TTS. Lo genera un script de Node en milisegundos (probado `SPIKE/ff/g.srt`, quemado con el filtro `subtitles`). Se entrega tambien el `.srt` suelto.
- **Estilo**: libass escala `FontSize` a una pagina de referencia de 288 px de alto: `FontSize=22` salio enorme (`f1.png`, letras de ~80 px). Para 1080p usar `FontSize` 9 a 11, `Outline` 1 a 2, `MarginV=30`. Probar siempre con una captura.
- **Acentos y enes**: archivo UTF-8; Arial resolvio bien las tildes (`f3.5.png`).
- **Titulos de seccion**: (a) en la edicion con `drawtext` y `enable='lt(t,2)'` o `between(t,a,b)` (probado: "1. Productos e inventario" sobre caja semitransparente, `f1.png`); (b) en la pagina con `showChapter` o un overlay. Recomiendo (a): mejor control de duracion y diseno, y no ensucia el recorrido.
- **Rotulos de accion** ("Haga clic en Importar"): overlays inyectados con textos tomados de `web-app/src/kernel/i18n/es.json`, para que coincidan con la pantalla.

## 7. Robustez en esta maquina

- **Una escena por archivo, un contexto por escena**: el webm se guarda al cerrar el contexto, por eso las escenas deben ser cortas (30 a 90 s) y cada una en su `try/finally` que cierre el contexto.
- **Reintentos**: hasta 2 por escena si el proceso cae (0xC0000005) o expira; borrar el webm parcial antes de reintentar. Reanudable: un `manifiesto.json` marca las escenas terminadas y el orquestador salta las listas.
- **Sin paralelismo**: un navegador a la vez, `workers: 1` (la regla es una corrida pesada a la vez, maximo 2 workers). La corrida con `showActions` (64 s para 10 s de guion) confirma que conviene no cargar la maquina.
- **Estado reproducible**: cada escena arranca desde datos conocidos (base local sembrada, nunca TEST ni produccion); los pasos dependientes (importar la OC antes de recibirla) se encadenan con comprobacion de precondicion.
- **Sin Brevo**: levantar el API de grabacion con `scripts/api-para-pruebas.ps1` (sin llave de Brevo, `Auth__Onboarding__Enabled=false`); los correos no se muestran en el video.
- **Esperas por estado, no por tiempo**: `expect` y `waitForResponse`; la duracion del audio solo para pausas de lectura.
- **Disco**: un MP4 de escena pesa 1 a 3 MB; con la ruta C (cuadros JPEG) se necesitan ~2 GB por minuto si se guardan todos: limpiar al terminar cada escena.
- Rutas con espacios (`F:/Visual Studio 2022/...`): citar siempre.

## 8. Dudas y decisiones para Luis

1. **Calidad**: A/B (VP8, "buena") o C (cuadros JPEG, "la mejor", mas complejo)? Propuesta: empezar con B y pasar a C solo si una escena de prueba no convence.
2. **Tamano del texto**: 1920x1080 CSS (letras chicas, realista) o zoom de 1,25 a 1,5 para leer mejor? No pude probarlo con la web real.
3. **Cursor**: confirmar el cursor falso con anillo naranja (captura B).
4. **Audio**: un archivo por bloque (propuesta) o uno por escena; WAV o MP3; pausas de 0,6 s o mas entre bloques.
5. **Subtitulos**: quemados, `.srt` aparte o ambos; solo espanol?
6. **fps final**: 25 (nativo) o 30 (solo duplica cuadros).
7. **Titulos de seccion**: texto sobre fondo oscuro 2 s o pantalla completa de 3 s?
8. **Editor**: basta ffmpeg automatico o quiere un editor grafico para retocar?
9. **Sin probar**: rendimiento real del screencast con la web React de Teikem; que el cursor falso (`addInitScript`) sobreviva a modales y rutas; PSNR de la ruta C.

## 9. Fuentes y comandos usados

- Tipos: `web-app/node_modules/playwright-core/types/types.d.ts` (`recordVideo` ~11560-11595; `interface Screencast` 18621-18745: `start`, `stop`, `showActions`, `showChapter`, `showOverlay`, `hideOverlays`).
- Scripts en `SPIKE`: `01-grabar.cjs` (variantes A a L), `02-medir.cjs` (PSNR frente a PNG de referencia, desfase, fps distintos por la banda de cuadros), `03-alternativas.cjs` (screencast, CDP, screenshot en bucle), `pagina.cjs` (pagina de prueba y cursor falso). Se ejecutan desde `F:/Visual Studio 2022/Projects/teikem/web-app`: `node <SPIKE>/01-grabar.cjs L`.
- ffmpeg: `-codecs`, `-formats`, `-encoders`, `-filters`, `libx264 -crf 18`, `h264_nvenc -preset p7 -cq 19`, `sine`/`anullsrc`/`concat` (audio de prueba), `silencedetect=noise=-40dB:d=0.4`, `tpad`, `subtitles`, `drawtext`, `scale eval=frame`, `crop`; `ffprobe -show_entries stream=...`.
- Entorno: `nvidia-smi -L`, `pip list`, SAPI (`GetInstalledVoices`), busqueda de `ffmpeg*.exe`.
- No se uso Brevo, ni se levanto API o Vite, ni se toco base alguna, ni se instalo ni descargo nada.
