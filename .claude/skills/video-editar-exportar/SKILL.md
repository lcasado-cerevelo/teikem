---
name: video-editar-exportar
description: Receta de edición con ffmpeg para unir tomas de Playwright con los audios de Luis, agregar rótulos de bloque y subtítulos, y exportar el MP4 final con su ficha. Úsalo al montar o exportar un video de Teikem.
---
# Editar y exportar

Lee `docs/video/REGLAS.md` (R5, R6, R9). Todo lo pesado va a `F:/Download/TeikemVideo/<id>/`; nada binario al repositorio.

## Herramienta
ffmpeg **9.0.2 completo** (H.264 `libx264`, `aac`, `subtitles`, `drawtext`, `silencedetect`, `loudnorm`, `tpad`, `xfade`; NVENC disponible):
`C:\Users\Luis\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe` (y `ffprobe.exe`). Las terminales abiertas antes de la instalación no lo tienen en el `PATH`: usa la ruta completa. El ffmpeg recortado de Playwright solo convierte webm; no sirve para mezclar.

## Recursos
ffmpeg siempre con `-threads 4 -preset medium` y lanzado con `scripts/demo/video-solutions-proceso/guardia-recursos.ps1 -Archivo <script.ps1>` (prioridad baja y 8 hilos): Luis trabaja en la misma máquina. Una escena a la vez.

## Por escena (`montaje/Enn.mp4`)
1. Medir el audio con `ffprobe -show_entries format=duration` y la toma igual.
2. Recortar el arranque de la toma (~0.6 s antes de que la página esté lista; el `tomas/Enn.json` dice cuánto).
3. Si la toma es más corta que el audio: congelar el último cuadro (`tpad=stop_mode=clone:stop_duration=<s>`); si es más larga: recortar el final **de la imagen**. **Nunca se recorta la voz.**
4. Mezclar: `-map 0:v -map 1:a`, `-c:v libx264 -crf 18 -preset medium -pix_fmt yuv420p -r 25 -c:a aac -b:a 192k -movflags +faststart`. **25 fps** (los nativos; evita cuadros duplicados).
5. Normalizar la voz a **−16 LUFS** (`loudnorm=I=-16:TP=-1.5:LRA=11`). Sin música.
6. Tapar lo que el guion marca con `> TAPAR:` (costos de compra, contraseñas) con un recuadro (`drawbox`) o difuminado (`boxblur`) en el tramo exacto.

## Rótulos y subtítulos
- **Rótulo de bloque** de 2 s al inicio de cada bloque, sobre la pantalla (`drawtext` con una fuente del sistema, fondo semitransparente), sin ocultar lo importante.
- **Subtítulos**: `.srt` generado de los textos limpios con los tiempos medidos de cada audio; quemados con `subtitles=` y `force_style='FontSize=10'` (libass escala a 288 px de alto: para 1080p usa 9 a 11; 22 sale enorme) y además el `.srt` aparte.
- Partir un audio único en bloques: `silencedetect=noise=-35dB:d=0.5` (exacto con pausas de 0.7 s); pero se prefiere **un archivo por escena**.

## Unir y exportar
- Unir escenas con `concat` (cortes secos) y los rótulos de bloque; exportar `salida/<id>_vNN.mp4` (+ `salida/<id>_vNN.srt`).
- `ffprobe` de la salida: 1920×1080, 25 fps, duración, tamaño; escribir `salida/ficha.json` `{ version, fecha, duracionSeg, tamanoMB, escenas: [...], base: "Teikem_video", notas }`.

## Después
Pasa el MP4 al `video-revisor`. Si hay hallazgos altos, arregla (tu parte) o pide la regrabación de la escena al grabador; máximo 2 rondas.
