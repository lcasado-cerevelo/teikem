# Reglas para producir videos de Teikem (las hereda todo agente de video)

Decididas por Luis el 2026-11-12 («sí a todo» al plan de `docs/video/solutions-proceso/PLAN.md`). Se suman a `CLAUDE.md` (ramas, correo, SQL congelado, pantallas verificadas con Playwright mirando la captura).

## Reglas

**R1. Entorno de grabación.** Se graba solo contra un API local levantado **sin correo** (`Brevo__ApiKey` vacía y `Auth__Onboarding__Enabled=false`, como hace `scripts/api-para-pruebas.ps1`) y sobre una **base copia** llamada `Teikem_video*`. Nunca la base de uso diario (`Teikem`), nunca SEPHAS, TEST ni producción. Prohibido Brevo, correo real, invitaciones o avisos. Si hace falta ver un correo, se le pide a Luis. `Brevo__ApiKey` es una variable de usuario de Windows: un API que arranque por `dotnet run`, Visual Studio o `preview_start` la hereda; no se levanta así. El spec se niega a grabar si `API_URL` no es `localhost`.

**R2. Base de grabación.** La prepara `scripts/demo/video-solutions-proceso/preparar-base-video.ps1` (copia por respaldo solo-copia, scripts idempotentes con guarda: abortan si la base no empieza con `Teikem_video`). Se vuelve al punto de partida con una **instantánea** de SQL Server antes de cada toma. Los scripts de la base de video NO van a la base real jamás. No se editan `logistica-db-estructura.sql` ni `logistica-db-seed.sql`. Las fotos y firmas viven fuera de la base: el API de grabación usa `Files__Root` apuntando a la copia `F:/Download/TeikemVideo/base/files`.

**R3. Datos en pantalla.** Nada de clientes, personas, correos ni teléfonos reales: se usan clientes y representantes **de demostración** (ficticios). Los **costos de compra** no se ven: se tapan en la edición o no se abre la ficha que los muestra. Precios de venta sí. Revisar cada escena con tablas por nombres de personas, correos y direcciones. Los correos de la demo usan `cerevelo.com` o `ejemplo.com`.

**R4. Contraseñas y llaves.** El login se hace fuera de cámara (`storageState`). Nunca se teclea ni se ve una contraseña, llave, token o cadena de conexión: ni en pantalla, ni en el spec, ni en logs, ni en el guion. La contraseña de aprobar la factura (escena 6) se pone fuera de cámara.

**R5. Formato.** 1920×1080 (`recordVideo.size` igual al viewport), **25 cuadros por segundo** (el nativo de Playwright), tema **claro**, **español**, zoom 100 % (se prueba 1.25 en la escena 1), sin música. Pausa de lectura de 1.2 s al llegar a cada pantalla; escritura a 60 ms por tecla; cursor falso inyectado y clic resaltado 300 ms antes de pulsar (clases con prefijo `vid-`); nada de movimientos bruscos; sin toasts tapando lo importante. Rótulo de bloque de 2 s sobre la pantalla; cortes secos; subtítulos quemados y un `.srt` aparte. No usar `showActions` de Playwright (rotula en inglés y alarga seis veces).

**R6. Narración.** El texto sale del guion aprobado por Luis; no se improvisa. Narración de **usted**, tono cercano. La voz la genera Luis con su text-to-speech: ningún agente genera audio ni sube nada a un servicio externo. **El audio manda**: la toma se ajusta al audio, nunca al revés.

**R7. Verificación.** Nada se entrega sin mirarlo: cada toma y el video final se revisan **abriendo cuadros con Read** (mínimo 3 por escena y 1 cada 5 s en el final), midiendo lo que importa y comprobando que se ve bien y decente. El informe de revisión **lista los cuadros que vio**. Lo que no queda bien se regraba o se quita.

**R8. Una cosa a la vez y con holgura.** Una grabación por vez, 1 worker, nunca en paralelo con otra corrida pesada (suites, builds). Máximo 2 intentos por escena; después se informa. Esta máquina tiene caídas nativas esporádicas (0xC0000005): repetir con menos paralelismo no es un fallo del código. **Todo lo pesado (API, Vite, Playwright/Chromium, ffmpeg, `dotnet`, restauraciones de SQL) pasa por `scripts/demo/video-solutions-proceso/guardia-recursos.ps1`** (ver «Guardia de recursos» abajo). Luis trabaja en la misma máquina: su memoria y su procesador tienen prioridad.

**R9. Qué se guarda.** En git (rama que indique Luis, normalmente `master`): guion, textos TTS, specs de escenas, mapas, scripts de datos, LEEME y fichas JSON. **Fuera de git**: videos, audios, tomas, cuadros y exportes, en `F:/Download/TeikemVideo/<id>/`. Nada binario de más de 1 MB entra al repositorio. Ningún agente crea ramas ni hace commit sin orden de Luis.

**R10. Hallazgos del producto.** Si al grabar aparece un error, un texto mal traducido o una pantalla fea, **no se arregla durante la producción**: se anota en `LEEME.md` para Luis (otro lote, con las reglas de `CLAUDE.md`). Solo se continúa si el video puede evitarlo con un recorrido distinto y honesto.

**R11. Nombres.** Escenas `E01`, `E02`… con clave corta, estables (si se inserta una, `E04b`; no se renumera para no perder audios). Bloques `B1`…`B7`. Las etiquetas de pantalla se citan tal cual salen de `es.json`.

**R12. Honestidad.** El video solo muestra lo que el software hace hoy. No se muestra una función apagada ni se simula con datos pegados. QuickBooks Desktop no se graba: va como lámina con los pasos (o una grabación de Luis).

**R13. Cuota.** Los agentes y flujos de video fijan `model` y `effort` explícitos: Sonnet con esfuerzo medio por omisión; Opus solo en el guion y en la revisión final. Nunca heredar el máximo de la sesión.

## Guardia de recursos (R8)
Motivo: el 2026-11-12 la máquina se cayó mientras Luis trabajaba y corría una preparación pesada. La máquina tiene 64 GB y 32 hilos; Luis usa parte.
- `guardia-recursos.ps1 -Estado` mide RAM libre, procesador y SQL Server y da un veredicto (OK / PRECAUCION / ALTO). **Antes de cada paso pesado**, y entre escenas, se llama `-Estado`; con ALTO no se arranca nada.
- `guardia-recursos.ps1 -Archivo <script.ps1> [-ArgsArchivo "..."]` (o `-Ejecutar "<comando>"`) **espera holgura** (RAM libre ≥ 16 GB y procesador ≤ 60 %), corre el comando con **prioridad baja**, **8 de los 32 hilos** y un perro guardián que **mata solo ese comando y sus hijos** (nunca las aplicaciones de Luis) si la RAM libre baja de 8 GB, el procesador pasa de 92 % por 15 s o el comando pasa de 8 GB. Sale con 4 si lo mató, 3 si no hubo holgura.
- Dentro de los comandos: Playwright con `--workers=1`; ffmpeg con `-threads 4 -preset medium`; sin builds ni suites en paralelo; un API y un Vite a la vez, apagados al terminar; `dotnet` con `-c Release -p:UseSharedCompilation=false -p:RunAnalyzers=false`.
- Si la guardia mata un comando, **no se reintenta en bucle**: se informa a Luis (puede estar usando la máquina) y se espera a que haya holgura.

## Marcas del guion maestro
- Cabecera de bloque: `## BLOQUE 2 - Órdenes de compra`.
- Escena: `### E02 - compras | pantalla: /warehouse/purchase-orders | duracion-estimada: 70 s`.
- Narración: líneas normales. **Acción en pantalla**: línea que empieza por `> ACCION:` (para el grabador; no se narra). **Pausa**: `[PAUSA 1s]`. **Énfasis visual**: `> RESALTAR: <etiqueta exacta>`. **Cambio de pantalla**: `> RUTA: /ruta`. **Nota para Luis**: `> LUIS:`. **Tapar en edición**: `> TAPAR: <qué>`.
- Texto limpio para el text-to-speech: `texto-tts/E02-compras.txt`, solo la voz, una frase por línea, **sin ninguna marca**.

## Estructura de carpetas
**En el repositorio:**
```
.claude/agents/video-{datos,mapeador,guionista,grabador,editor,revisor}.md
.claude/skills/video-{guion-tts,grabar-escena,editar-exportar,revision-calidad,entorno-seguro}/SKILL.md
.claude/workflows/video-{guion,grabar,montar}.js
docs/video/REGLAS.md
docs/video/<id>/
  PLAN.md  LEEME.md                 plan, estado, datos usados, cómo repetir, hallazgos del producto
  mapa/NN-<tema>.md                 mapa del recorrido
  guion/guion-maestro.md            con marcas
  guion/texto-tts/Enn-<clave>.txt   textos limpios para el text-to-speech
  guion/pronunciacion.md            siglas y nombres
  escenas/Enn-<clave>.spec.ts       un spec por escena
  revision/rNN-<fecha>.md           informes del revisor
scripts/demo/video-<id>/            preparación de la base de video (SQL idempotente y script)
web-app/e2e/video.config.ts         configuración propia de grabación
```
**Fuera del repositorio, `F:/Download/TeikemVideo/<id>/`:**
```
audio/Enn-<clave>.mp3|wav    lo que devuelve Luis (un archivo por escena, mismo nombre que el texto)
tomas/Enn-<clave>.webm|json  grabaciones crudas y su ficha { audioSeg, tomaSeg, intentos, cuadrosVistos, estado }
cuadros/Enn-<clave>/         cuadros extraídos para revisión
montaje/Enn.mp4              escena unida a su audio
salida/<id>_vNN.mp4 (+.srt)  exporte final y salida/ficha.json
```
Ruta de ffmpeg (no está en el `PATH` de las terminales viejas): `C:\Users\Luis\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin`.

## Terminado
**Una escena** está terminada cuando su audio existe y la toma dura igual o más que el audio, el revisor abrió los cuadros y no hay hallazgos altos, todo lo narrado se ve en ese orden y `tomas/Enn.json` dice `estado: ok`. **El video** está terminado cuando todas sus escenas están ok, existe `salida/<id>_vNN.mp4` con audio, la revisión final no deja hallazgos altos ni medios abiertos, `LEEME.md` documenta datos y hallazgos del producto y Luis lo aprobó (pausa 3).

## Lista de revisión (la aplica `video-revisor`, con los cuadros vistos)
1. Entorno: base `Teikem_video*`, API sin correo, localhost; ningún cuadro muestra TEST ni producción.
2. Datos sensibles: ningún nombre de persona real, correo, teléfono, costo de compra, contraseña, llave ni conexión.
3. Imagen: 1920×1080, tema claro, español; sin scroll horizontal, texto cortado, toasts tapando ni cursor fuera de cuadro; sin cargas de más de 2 s.
4. Medidas (tablas y miniaturas): alto de filas razonable, miniaturas legibles, columnas sin truncar el dato clave.
5. Correspondencia: cada etiqueta que la voz nombra aparece tal cual en pantalla en ese instante (la voz no adelanta a la imagen más de 0.5 s).
6. Ritmo: pausa de lectura en cada pantalla nueva; ninguna escena con más de 8 s sin movimiento ni voz.
7. Audio: sin recortes, volumen parejo (−16 LUFS), sin música.
8. Honestidad (R12).
9. Orden: los siete bloques en el orden dictado y rótulos correctos.
10. Archivos: nada binario en el repositorio; salida con nombre y versión; ficha completa.
