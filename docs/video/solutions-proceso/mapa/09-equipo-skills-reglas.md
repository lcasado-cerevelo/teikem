# Equipo, skills, flujo y reglas para producir videos

Informe de diseño (solo lectura, 2026-11-12). Nada de lo propuesto está creado. Propone el grupo de trabajo para el video del proceso de Advance Solutions y para los videos que vengan después.

## 1. Lo que ya existe y como esta hecho (convenciones que hay que respetar)

**Agentes (`.claude/agentes`: 11 archivos en `.claude/agents/`).** Todos son Markdown con frontmatter `name`, `description`, `model`, `effort` y `tools` (por ejemplo `.claude/agents/architect.md:1-6`, `fe-implementer.md:1-6`). Patrón de herramientas:
- Lectores, revisores y verificadores: `Read, Grep, Glob, Bash` (solo leen; `spec-reader.md`, `fe-reader.md`, `reviewer.md`, `verifier.md`).
- Implementadores: `Read, Grep, Glob, Bash, Edit, Write` (`implementer.md`, `fe-implementer.md`, `fe-implementer-core.md`).
- Documentador: `Read, Grep, Glob, Bash, Write` sin `Edit` (`scribe.md`).
- Modelos: `sonnet`/medium para lectura y pantallas; `opus` (high o xhigh) para diseñar, revisar y refutar. Descripciones en una frase, en español, que dicen que hace y que NO hace ("Solo lee", "no edita").
- Tono: segunda persona, frases cortas, reglas en viñetas, rutas exactas, "no inventes".

**Flujos (`.claude/workflows/*.js`: `fe-implementar.js`, `lote-diseno.js`, `lote-implementar.js`).**
- Cada archivo exporta `meta = { name, description, whenToUse, phases: [{title, detail}] }` (`fe-implementar.js:1-11`) y lee `args` (`const a = args || {}`, validando lo obligatorio con `throw`, `fe-implementar.js:13-17`).
- Usan `phase('Titulo')`, `agent(prompt, { agentType, model, effort, label, schema })`, `parallel([...])` y `log()`. Las salidas llevan `schema` JSON (`RESULT`, `FINDINGS`, `VERDICT`, `fe-implementar.js:19-21`).
- Encadenan: implementar por grupos, compuerta determinista, revisión por lentes, refutación solo de lo grave, corrección con tope de rondas, recorrido, documentación. Devuelven un objeto resumen.
- Nota del propio flujo (`fe-implementar.js:25-26`): los agentes de `.claude/agents` solo se cargan al iniciar la sesión; mientras tanto se usan agentes base con modelo explícito y las reglas pegadas en el prompt. Aplica a los agentes nuevos: tras crearlos hay que reiniciar la sesión, o el flujo debe pasar las reglas en el prompt.
- Ningún flujo actual se detiene a esperar a una persona: `lote-diseno` "devuelve el plan para revisión humana" y termina; la aprobación ocurre entre dos flujos distintos. Es el patrón a imitar para las pausas de Luis.

**Skills.** No existe `.claude/skills/` (confirmado: `.claude` solo tiene `agents`, `workflows`, `launch.json`). Los flujos aparecen como skills (`fe-implementar`, `lote-diseno`, `lote-implementar`).

**Reglas del dueño en `CLAUDE.md` que afectan al video:**
- Brevo: ningún agente lo usa; todo API de prueba con la llave vacía (`scripts/api-para-pruebas.ps1:27-28` pone `Brevo__ApiKey=''` y `Auth__Onboarding__Enabled='false'`); `Brevo__ApiKey` es variable de usuario de Windows y `preview_start «api»`/`dotnet run`/Visual Studio la heredan, así que no se levanta el API por esas vías.
- Prohibido `e2e` contra API con correo real, contra TEST y contra producción (`web-app/e2e/globalSetup.ts` se niega).
- SQL congelado: `logistica-db-estructura.sql` y `logistica-db-seed.sql` no se tocan; todo cambio en `logistica-db-update.sql`. Recrear base solo en desarrollo.
- Ramas: no se crean ramas ni worktrees sin orden de Luis; todo va en master (CLAUDE.md global del usuario).
- Pantallas: todo pedido que toque pantalla se cierra con Playwright MIRANDO la captura (abrirla con Read), midiendo y comprobando que se vea bien y decente; si no, se arregla o se quita. Prefijos propios de clase.
- Tema claro/oscuro e idiomas es/en con selector; `app-almacen` solo claro.
- Manual y FAQ por lote; commits y documentos en español.
- Nunca correr maestros de `Database/` contra SEPHAS; una corrida pesada a la vez, 2 workers (memoria `feedback-no-congelar-la-maquina`).

**Ayudas de Playwright reutilizables (`web-app/e2e/staffHelpers.ts`).**
- `login(page)` (línea 52): entra por pantalla con el administrador y maneja `/select-tenant`.
- `apiHeaders(request)` (35): sesión del administrador por API (acepta el documento legal), para sembrar datos sin pasar por pantalla.
- `ok(promise, what)` (65): llamada a API que falla con mensaje claro.
- `shot(page, isMobile, name, height, only)` (19): captura de manual; espera `networkidle`, que no haya `.toast.on`, sube todos los scroll a 0 y soporta `MANUAL_LANG=en` (`manualLang.ts`) para capturar en inglés sin recargar.
- `enableModule(request, key)` (134), `LETTERS` (14, sufijo solo de letras), `createClient/createOrder/scan`.
- `playwright.config.ts` usa `baseURL` `http://localhost:5173`, `trace` y `screenshot` solo en fallo; no configura `video`. Una grabación nueva necesita su propio proyecto o config (`recordVideo` por contexto), no tocar el existente.
- Credenciales del administrador de pruebas: están en `staffHelpers.ts:11` con variables `TEIKEM_ADMIN_*`; en el video no se teclean a la vista (ver reglas).

**Cómo se documentó otra demostración (`docs/demo/guia-demo-jose.md`, 140 líneas).** Es una guía y no un libreto: tabla "lo que sí y lo que no", cómo mostrar los móviles, preparación "la noche anterior en este orden" con scripts concretos (`scripts/demo/jose-demo-datos.sql`, `scripts/demo/generar-correos-demo.mjs`), tabla "datos que ya existen en la base local (verificado fecha)". Convención útil: marcar lo "verificado en el código" frente a lo supuesto. Hay precedente de scripts de datos de demo en `scripts/demo/` y de PDFs de códigos de barras en `docs/demo/`.

**Estado de la carpeta del video.** `docs/video/solutions-proceso/mapa/` ya tiene `02`, `03`, `04` y `05` (otros investigadores); este informe es el `09`.

## 2. Agentes propuestos (tabla y ficha de cada uno)

Nombre con prefijo `video-` para que se agrupen y no choquen con los once actuales. Todos en español, mismo frontmatter.

| Agente | Sirve para | Herramientas | Modelo | Escribe en |
|---|---|---|---|---|
| `video-datos` | Dejar la base de grabación lista (scripts SQL idempotentes de demo, archivos de ejemplo) | Read, Grep, Glob, Bash, Write | sonnet / high | `scripts/demo/video-<id>/` y `<VIDEO_DIR>/datos/` |
| `video-mapeador` | Mapear y ensayar el recorrido pantalla por pantalla, con etiquetas exactas | Read, Grep, Glob, Bash, Write | sonnet / medium | `<id>/mapa/` |
| `video-guionista` | Escribir el guion maestro con marcas y los textos limpios por escena para el TTS | Read, Grep, Glob, Write | opus / high | `<id>/guion/` |
| `video-grabador` | Grabar UNA escena con Playwright acompasada al audio | Read, Grep, Glob, Bash, Edit, Write | sonnet / medium | `<id>/escenas/*.spec.ts` y tomas fuera del repo |
| `video-editor` | Ensamblar tomas + audios, exportar | Read, Grep, Glob, Bash, Write | sonnet / medium | `<VIDEO_DIR>/<id>/salida/` |
| `video-revisor` | Revisar el resultado MIRANDO cuadros; reporta hallazgos | Read, Grep, Glob, Bash | opus / high | solo informe devuelto |

### 2.1 `video-datos`
- Para que sirve: prepara la base de grabación (copia, no la de uso diario), siembra inventario, proveedores y clientes necesarios, y deja los archivos de ejemplo (por ejemplo el CSV de QuickBooks con una o dos órdenes). Modo "dejar listo" y modo "reponer" (volver al estado inicial entre tomas).
- Herramientas: Bash para `sqlcmd` y Write para scripts. No necesita Edit.
- NO debe: tocar TEST ni producción ni SEPHAS; correr `db-init`/`db-reset` fuera de la base de grabación; editar `logistica-db-*.sql`; correr maestros de `Database/`; usar Brevo; dejar datos personales reales (clientes, correos, teléfonos) visibles; escribir contraseñas en archivos. Los scripts son idempotentes y llevan `USE` de la base de grabación explícita y una guarda que aborta si el nombre de la base no empieza con `Teikem_video`.
- Salida: lista de lo sembrado (conteos), cómo reponer, y los datos exactos que el guion puede nombrar (SKU, proveedor, cliente).

### 2.2 `video-mapeador`
- Para que sirve: recorre el flujo (lectura de código y manual, y ensayo con Playwright contra el API sin correo) y entrega el mapa: ruta, etiqueta en español con su clave de `es.json`, qué se ve, tiempos reales de carga, validaciones que saltan. Es el heredero de los informes `02`–`05` de esta carpeta.
- NO debe: inventar etiquetas (cita archivo:línea); grabar tomas finales; modificar código ni docs existentes; levantar el API por vías que heredan Brevo.

### 2.3 `video-guionista`
- Para que sirve: convierte el mapa en guion maestro (con marcas) y extrae los textos limpios por escena. Aplica el skill `video-guion-tts`.
- Herramientas sin Bash: no necesita ejecutar nada.
- NO debe: nombrar botones que no estén en el mapa; poner datos reales de personas; usar abreviaturas, siglas sin deletrear, ni números sin escribir; dar duraciones inventadas (usa la regla de palabras por minuto del skill).

### 2.4 `video-grabador`
- Para que sirve: escribe y corre el spec de UNA escena, acompasado a la duración de su audio, y deja la toma. Aplica `video-grabar-escena`.
- NO debe: grabar si el API tiene correo real o apunta a TEST/producción (comprueba antes); teclear contraseñas a la vista; grabar más de una escena por ejecución; correr con más de 1 worker; tocar el `playwright.config.ts` existente ni los specs de otros lotes; dejar la toma sin mirar un cuadro (la escena no se da por buena hasta que él mismo abra al menos tres cuadros con Read).

### 2.5 `video-editor`
- Para que sirve: une tomas y audios por escena, ajusta duraciones, agrega rótulos y exporta MP4 (y subtítulos si se pidió). Aplica `video-editar-exportar`.
- NO debe: cambiar el texto del audio ni recortar la voz; regrabar tomas (pide al grabador); instalar nada de internet sin que Luis lo apruebe (ver duda 1 sobre ffmpeg); mover archivos al repositorio.

### 2.6 `video-revisor`
- Para que sirve: aplica la lista de revisión (sección 7): extrae cuadros del MP4 (o de las tomas) y los abre con Read uno a uno, mide, y compara la narración con lo que se ve. Devuelve hallazgos con escena, tiempo y arreglo.
- Modelo opus por ser el control de calidad (como `reviewer.md`); herramientas solo de lectura más Bash para extraer cuadros.
- NO debe: editar nada; aprobar sin haber abierto cuadros (el informe debe listar los cuadros que miró, con su tiempo); dar por bueno un audio que no puede oír: solo verifica duración y silencios por medición.

**Reutilizados sin cambio:** `scribe` (documentar el lote del video), `reviewer`/`verifier` (si se tocara código). No se propone un agente de "productor": lo hace el flujo.

## 3. Skills propuestos (ficha de cada uno con su contenido esencial)

Ubicación `.claude/skills/<nombre>/SKILL.md`, frontmatter `name` y `description` (la descripción dice cuándo usarlo). Archivos de apoyo en la misma carpeta.

### 3.1 `video-guion-tts` (lo usa `video-guionista`)
Cuándo: al escribir o corregir el guion de cualquier video narrado.
Contenido esencial:
- Voz: tuteo o usted según decida Luis (duda 4), frases de 8 a 18 palabras, una idea por frase, sin paréntesis, sin guiones largos, sin listas dentro del texto narrado.
- Escribir para el oído: números con letras ("cuarenta y dos productos"), montos con la unidad ("dos mil quinientos dólares"), fechas dichas ("doce de noviembre"), siglas deletreadas o con su pronunciación en una tabla de apoyo (`pronunciacion.md`: QuickBooks, SKU, CSV, Teikem, Advance Solutions).
- Nombres de pantalla: el nombre exacto que se ve en español (de `es.json`), entre comillas solo en el guion maestro; en el texto limpio, sin comillas.
- Ritmo de referencia: 140 a 150 palabras por minuto para estimar; la duración real sale del audio que devuelve Luis, no de la estimación.
- Una pausa se marca con una línea en blanco y la marca `[PAUSA 1s]` solo en el maestro; en el texto limpio, un punto y aparte. El TTS no debe recibir marcas.
- Cada frase narrada enlaza con una acción visible: la narración nunca nombra algo que todavía no aparece (la voz va un instante después de la acción, no antes).
- Prohibido: datos personales reales, contraseñas, promesas de funciones que el mapa no confirma.
- Formato de salida: ver sección 6 (`guion-maestro.md` y `texto-tts/Enn-<escena>.txt`).

### 3.2 `video-grabar-escena` (lo usa `video-grabador`)
Cuándo: al grabar una escena.
Receta:
1. Comprobar entorno (regla R1): API en `localhost:5000` levantado con `scripts/api-para-pruebas.ps1` sobre la base `Teikem_video`; sin `Brevo__ApiKey`; web con `VITE_API_URL` apuntando a ese API.
2. Spec propio `<id>/escenas/Enn-<escena>.spec.ts` con `test.use({ viewport: {width:1920,height:1080}, video: {mode:'on', size:{width:1920,height:1080}}, locale:'es' })` y un proyecto/config propios (p. ej. `web-app/e2e/video.config.ts`, a crear aparte; no se toca el actual).
3. Estado inicial por API (`apiHeaders`, `ok`, `enableModule`) antes de empezar a grabar; sesión iniciada con `login(page)` en un contexto previo y guardada con `storageState`, para que el login con contraseña nunca salga en cámara.
4. Acompasar al audio: leer `tomas/Enn.json` (duración del audio en segundos, que escribe el editor al recibir el audio); el spec reparte esa duración entre pasos con `await page.waitForTimeout(ms)` y una función `paso(nombre, msObjetivo)`. Cada paso espera su resultado visible (`expect(...).toBeVisible()`) antes del tiempo muerto, nunca solo tiempo.
5. Ritmo: clics visibles (resaltar el elemento 300 ms antes de pulsar con un contorno inyectado, prefijo de clase propio `vid-`), escribir con `pressSequentially({delay: 60})`, desplazamientos con pasos cortos, sin movimientos bruscos; pausa de 1,2 s en cada pantalla nueva para que se lea.
6. Si dura menos que el audio, se alarga la pausa final del último paso; si dura más, la escena falla y se pide guion más corto o audio más largo: nunca se acelera el vídeo.
7. Al terminar: copiar la toma a `<VIDEO_DIR>/<id>/tomas/Enn.webm`, extraer tres cuadros (inicio, medio, final) y abrirlos con Read (regla de pantallas), medir alto de filas/tamaño si la escena toca una tabla, y anotar en `tomas/Enn.json` el resultado.
8. Ante fallo: no reintentar en bucle; máximo 2 intentos, luego informar.

### 3.3 `video-editar-exportar` (lo usa `video-editor`)
Cuándo: al montar o exportar.
Contenido:
- Herramienta: ffmpeg. No está en el PATH; solo hay el ffmpeg recortado de Playwright (`%LOCALAPPDATA%/ms-playwright/ffmpeg-1013`), que solo sirve para convertir webm; para mezclar audio y cortar hace falta un ffmpeg completo (duda 1). El skill documenta ambos caminos.
- Receta: por escena, `webm` + `wav/mp3` de Luis → MP4 (H.264, AAC, 30 fps, 1920x1080, `-pix_fmt yuv420p`, `-movflags +faststart`); si la toma es más corta que el audio, congelar el último cuadro; si es más larga, recortar el final (nunca la voz).
- Unión de escenas con 0,3 s de fundido o corte seco (decisión de estilo, duda 6); normalizar volumen de las voces a −16 LUFS; música de fondo solo si Luis la entrega, a −28 dB.
- Rótulos: título de bloque de 2 s entre secciones (los 6 bloques del video), con la tipografía de la web; nada que oculte la pantalla.
- Subtítulos: SRT generado de los textos limpios con los tiempos de los audios (opcional).
- Exportar a `salida/` con nombre `<id>_v<NN>.mp4`; calcular tamaño y duración (`ffprobe`), registrar en `salida/ficha.json`.

### 3.4 `video-revision-calidad` (lo usa `video-revisor`)
Cuándo: antes de dar por terminada una escena y el video.
Contenido: la lista de la sección 7, el método de cuadros (extraer 1 cuadro cada 5 s y en cada cambio de pantalla con `ffmpeg -vf "fps=1/5"` o `select`; abrir cada uno con Read), medidas a tomar (alto de filas de tabla, miniaturas, texto cortado, scroll horizontal, toasts atravesando la pantalla, cursor fuera de cuadro) y la plantilla del informe de hallazgos (escena, tiempo, qué se ve, qué debía verse, arreglo, severidad).

### 3.5 `video-entorno-seguro` (usado por datos, grabador y mapeador)
Cuándo: antes de levantar nada o sembrar. Contenido: las reglas R1 a R6 de la sección 5 convertidas en un chequeo ejecutable de 6 pasos (variable `Brevo__ApiKey` vacía en el proceso del API, base `Teikem_video*`, `API_URL` es localhost, no hay `ConnectionStrings` de TEST, `globalSetup` pasa). Si cualquiera falla, se detiene e informa.

## 4. Flujo guardado propuesto (fases, pausas para el dueno, args)

Un flujo no puede esperar a una persona (ninguno actual lo hace), así que se propone una **familia de tres flujos** que terminan en las pausas de Luis, igual que `lote-diseno` termina en revisión humana. Cada uno reanuda por archivos en disco, no por memoria.

### 4.1 `video-guion` (fases 1 a 3, termina con el guion para aprobar)
- args: `{ id: "solutions-proceso", titulo, bloques: [{clave:"productos", titulo, mapa:"mapa/01-...md"}, ...], videoDir: "F:/Download/TeikemVideo", tono: "tu"|"usted", minutos: 12 }`.
- Fase **Datos** (`video-datos`): prepara la base `Teikem_video` y el kit de archivos; devuelve lista de lo sembrado.
- Fase **Mapa** (`video-mapeador`, uno por bloque en paralelo, tope 2 por la regla de no congelar la máquina): completa/actualiza los mapas y ensaya cada bloque sin grabar.
- Fase **Guion** (`video-guionista`): guion maestro con marcas + textos limpios + estimación de duración; luego `video-revisor` (modo guion: etiquetas frente al mapa, reglas TTS) y una corrección.
- **PAUSA 1 (Luis):** lee `guion/guion-maestro.md`, aprueba o pide cambios; pega los `texto-tts/*.txt` en su TTS y deja los audios en `<videoDir>/<id>/audio/Enn-<escena>.mp3|wav`. Los pasa a su orden y avisa.
- Retorno: rutas, lista de escenas y palabras por escena.

### 4.2 `video-grabar` (fases 4 a 5, tras recibir el audio)
- args: `{ id, videoDir, escenas: ["E01"...] (opcional, por omisión todas), reponer: true }`.
- Fase **Audio**: valida que existan los audios de cada escena, mide su duración y escribe `tomas/Enn.json` (si falta uno, se detiene y lo dice).
- Fase **Grabar** (`video-grabador`, una escena a la vez, en serie): para cada escena, `video-datos` en modo reponer, grabar, extraer cuadros, mirar. Máximo 2 intentos por escena.
- Fase **Revisar tomas** (`video-revisor` en modo escena, en paralelo de a 2): cuadros y medidas; las escenas con hallazgos altos vuelven al grabador (máximo 2 rondas, igual que `fe-implementar`).
- **PAUSA 2 (Luis, opcional):** ve las tomas crudas por escena (carpeta `tomas/`) y pide regrabar alguna.

### 4.3 `video-montar` (fases 6 a 7)
- args: `{ id, videoDir, version: 1, subtitulos: true }`.
- Fase **Montar** (`video-editor`): une, mezcla, exporta `salida/<id>_v01.mp4`.
- Fase **Revisión final** (`video-revisor`, modo video completo): cuadros cada 5 s, coherencia audio-imagen por escena, lista de la sección 7. Corrección con el editor o el grabador según el hallazgo, máximo 2 rondas.
- Fase **Documentar** (`scribe`): `docs/video/<id>/LEEME.md` (qué se grabó, con qué datos, cómo repetirlo, hallazgos del producto vistos al grabar) y, si hubo fallos del software, una lista aparte para Luis.
- **PAUSA 3 (Luis):** aprueba el MP4 final.

### 4.4 Comportamiento común
- Cada flujo valida args y entorno al inicio (`throw` si falta algo), usa `schema` en cada `agent`, y devuelve el estado más las rutas. Nombres como skills: `video-guion`, `video-grabar`, `video-montar`.
- Los hallazgos del producto (un botón que falla, un texto mal traducido) NO se corrigen dentro del flujo: se listan en `LEEME.md` para que Luis decida; corregirlos sería un lote aparte con las reglas de CLAUDE.md.
- Un video nuevo reutiliza los tres flujos cambiando `id`, `bloques` y mapas.

## 5. Reglas propuestas (texto listo para pegar)

Para `docs/video/REGLAS.md`, con una línea en CLAUDE.md que la enlace ("Videos: ver docs/video/REGLAS.md; las hereda todo agente").

```
# Reglas para producir videos de Teikem (las hereda todo agente de video)

R1. ENTORNO DE GRABACION. Se graba solo contra un API local levantado con scripts/api-para-pruebas.ps1
    (sin Brevo, Auth__Onboarding__Enabled=false) y sobre una base COPIA llamada Teikem_video* (nunca la
    base de uso diario, nunca SEPHAS, nunca TEST ni produccion). Prohibido correo real, Brevo, mandar
    invitaciones o avisos. Si se necesita ver un correo, se pide a Luis. El spec se niega a grabar si
    API_URL no es localhost o si el API reporta correo configurado.
R2. BASE. La base de grabacion se prepara con scripts idempotentes propios (scripts/demo/video-<id>/),
    con USE explicito de Teikem_video*. Los scripts sueltos solo pueden aplicarse a la base real si su
    contenido ya esta en el maestro (regla existente); estos NO van a la base real jamas. Antes de cada
    toma se repone el estado inicial. No se editan logistica-db-estructura.sql ni logistica-db-seed.sql.
R3. DATOS EN PANTALLA. Nada de clientes, personas, correos ni telefonos reales: se usan empresas ficticias
    o dos o tres nombres de empresa ya aprobados por Luis. Revisar cada escena con tablas (Clientes,
    Ordenes) por nombres de personas, correos y direcciones. Los correos de la demo usan dominio
    cerevelo.com o ejemplo.com.
R4. CONTRASENAS Y LLAVES. El login se hace fuera de camara (storageState). Nunca se teclea ni se ve una
    contrasena, llave, token o cadena de conexion; ni en pantalla, ni en el spec, ni en logs, ni en el
    guion. Los campos de contrasena siguen mostrandose con el ojito cerrado.
R5. FORMATO. Grabacion 1920x1080, 30 fps, tema CLARO, idioma espanol, zoom 100 %, navegador sin barras
    de extensiones, sin notificaciones del sistema. Pausa de lectura de 1,2 s al llegar a cada pantalla;
    escritura a 60 ms por tecla; un clic resaltado antes de pulsar. Nada de movimientos de raton
    bruscos. Sin toasts cortando el cuadro importante (esperar a que se vayan).
R6. NARRACION. El texto narrado sale del guion aprobado por Luis; no se improvisa. La voz la genera Luis
    con su text-to-speech; ningun agente genera ni sube audio a un servicio externo. El audio manda: la
    toma se ajusta al audio, nunca al reves.
R7. VERIFICACION. Nada se entrega sin mirarlo: cada toma y el video final se revisan abriendo cuadros
    con Read (minimo 3 por escena y 1 cada 5 s en el final), midiendo lo que importa y comprobando que
    se ve bien y decente. El informe de revision lista los cuadros vistos. Lo que no queda bien se
    regraba o se quita. Esta regla extiende la REGLA DEL DUENO de pantallas (2026-11-12).
R8. UNA COSA A LA VEZ. Una grabacion por vez, 1 worker, nunca en paralelo con otra corrida pesada
    (suites, builds). Maximo 2 intentos por escena; despues se informa.
R9. QUE SE GUARDA. En git (rama indicada por Luis, normalmente master): guion, textos TTS, specs de
    escenas, mapas, scripts de datos, LEEME y fichas JSON. FUERA de git: videos, audios, tomas, cuadros y
    exportes, en F:/Download/TeikemVideo/<id>/. Nada binario de mas de 1 MB entra al repositorio.
    Ningun agente crea ramas ni commits sin orden de Luis.
R10. HALLAZGOS DEL PRODUCTO. Si al grabar aparece un error, texto mal traducido o pantalla fea, no se
    arregla durante la produccion: se anota para Luis. Solo se continua si el video puede evitarlo con
    un recorrido distinto y honesto.
R11. NOMBRES. Escenas E01, E02... con clave corta; ver docs/video/REGLAS.md seccion de estructura.
    Las etiquetas de pantalla se citan tal cual salen de es.json.
R12. HONESTIDAD. El video solo muestra lo que el software hace hoy. No se muestra una funcion apagada ni
    se simula con datos pegados; si QuickBooks exporta algo que la web aun no genera, se dice en el
    guion o se saca del video.
```

### Convención de marcas del guion maestro
- Cabecera de bloque: `## BLOQUE 2 - Ordenes de compra`.
- Escena: `### E04 - importar-quickbooks | pantalla: /compras | duracion-estimada: 38 s`.
- Narración: líneas normales. Acción en pantalla: línea que empieza por `> ACCION:` (para el grabador, no se narra). Pausa: `[PAUSA 1s]`. Énfasis visual: `> RESALTAR: <etiqueta exacta>`. Cambio de pantalla: `> RUTA: /ruta`. Nota para Luis: `> LUIS:`.
- Texto limpio de TTS: archivo `texto-tts/E04-importar-quickbooks.txt`, solo la voz, una frase por línea, sin ninguna marca.

## 6. Estructura de carpetas y nombres

**En el repositorio (se versiona):**
```
.claude/agents/video-{datos,mapeador,guionista,grabador,editor,revisor}.md
.claude/skills/video-guion-tts/{SKILL.md, pronunciacion.md}
.claude/skills/video-grabar-escena/{SKILL.md, plantilla-escena.spec.ts}
.claude/skills/video-editar-exportar/SKILL.md
.claude/skills/video-revision-calidad/{SKILL.md, plantilla-informe.md}
.claude/skills/video-entorno-seguro/SKILL.md
.claude/workflows/{video-guion,video-grabar,video-montar}.js
docs/video/REGLAS.md
docs/video/<id>/
  LEEME.md                 estado, datos usados, como repetir, hallazgos del producto
  mapa/NN-<tema>.md        mapa del recorrido (ya empezado: 02 a 05, este 09)
  guion/guion-maestro.md   con marcas
  guion/texto-tts/Enn-<escena>.txt   textos limpios para pegar en el TTS
  guion/pronunciacion.md   siglas y nombres (apoyo)
  escenas/Enn-<escena>.spec.ts   un spec por escena
  revision/rNN-<fecha>.md  informes del revisor
scripts/demo/video-<id>/   SQL idempotente y archivos de ejemplo (CSV de QuickBooks de demostracion)
web-app/e2e/video.config.ts   configuracion propia de grabacion (a crear, aparte)
```
**Fuera del repositorio (no se versiona), `F:/Download/TeikemVideo/<id>/`:**
```
audio/Enn-<escena>.mp3|wav     lo que devuelve Luis (un archivo por escena, mismo nombre que el texto)
tomas/Enn-<escena>.webm        grabaciones crudas de Playwright
tomas/Enn-<escena>.json        { audioSeg, tomaSeg, intentos, cuadrosVistos:[...], estado }
cuadros/Enn-<escena>/t0005.png cuadros extraidos para revision
montaje/Enn.mp4                escena unida a su audio
salida/<id>_vNN.mp4  (+ .srt)  exporte final
salida/ficha.json              duracion, tamano, fecha, version
```
**Nombres:** `<id>` en minúsculas con guiones (`solutions-proceso`); escenas `E01`... numeradas por el guion maestro y estables (si se inserta una, se usa `E04b`, no se renumera, para no perder audios); bloques `B1`-`B6` en el mismo orden que dictó Luis (productos e inventario, órdenes de compra, recibo, venta con asistente, almacén, QuickBooks en ventas). Versiones `v01`, `v02`.

## 7. Definicion de terminado y lista de revision

**Una escena está terminada cuando:** (1) su audio existe y su toma dura igual o más que el audio, sin cortarlo; (2) el revisor abrió los cuadros y no hay hallazgos altos; (3) todo lo narrado se ve en pantalla en ese orden; (4) `tomas/Enn.json` dice `estado: ok`.

**El video está terminado cuando:** todas sus escenas están ok, existe `salida/<id>_vNN.mp4` con audio y revisión final sin hallazgos altos ni medios abiertos, `LEEME.md` documenta datos y hallazgos del producto, y Luis lo aprobó en la pausa 3.

**Lista de revisión (la aplica `video-revisor` y se registra con los cuadros vistos):**
1. Entorno: la ficha dice base `Teikem_video*`, API sin correo, localhost. Ningún cuadro muestra TEST/producción en la barra o en el contenido.
2. Datos sensibles: ningún nombre de persona, correo, teléfono, contraseña, llave o conexión en ningún cuadro.
3. Imagen: 1920x1080, tema claro, español; sin scroll horizontal, sin texto cortado, sin toasts tapando, sin cursor fuera de cuadro, sin parpadeos de carga más largos de 2 s (si los hay, se recorta la espera con congelado o se vuelve a grabar).
4. Medidas (cuando hay tabla o miniaturas): alto de filas razonable (sin el error de `min-height` heredado del Lote 240), miniaturas legibles, columnas sin truncar el dato clave.
5. Correspondencia: cada etiqueta que la voz nombra aparece tal cual en pantalla en ese instante (voz no adelanta a la imagen más de 0,5 s).
6. Ritmo: pausa de lectura en cada pantalla nueva; ninguna escena con más de 8 s sin movimiento visible ni voz; sin silencios largos de audio.
7. Audio: sin recortes, volumen parejo (−16 LUFS), sin música que tape la voz.
8. Honestidad (R12): nada se muestra de lo que el software no hace hoy; los errores del producto no aparecen sin querer.
9. Orden: los seis bloques en el orden dictado por Luis y rótulos de bloque correctos.
10. Archivos: nada binario en el repositorio; salida con nombre y versión; ficha completa.

## 8. Dudas y decisiones para Luis

1. **ffmpeg completo.** Sin él no se pueden unir audio y video ni medir duraciones. El de Playwright no basta. Recomendación: que Luis instale ffmpeg él mismo (por ejemplo `winget install Gyan.FFmpeg`); los agentes no instalan nada de internet. Alternativa: que Luis monte el video en su propio editor y los agentes solo entreguen tomas + tiempos.
2. **Editar con agentes o con Luis.** El mensaje dice "yo edito el video". Si el montaje lo hace el editor-agente se necesita (1); si lo hace Luis, `video-editor` y `video-montar` se reducen a entregar tomas, SRT y una tabla de tiempos. Recomiendo agente para el montaje base y Luis para el pulido.
3. **Base de grabación.** Nombre propuesto `Teikem_video` restaurada de una copia de `Teikem` (que es copia de TEST). ¿Se permite crearla (restaurar un backup en local es fuera del alcance de este informe) y a quien le toca? La memoria menciona `Teikem_verif213` como base de verificación existente; no la usé ni la verifiqué: conviene decidir si se reutiliza.
4. **Tuteo o usted** y tono (formal de oficina o cercano) de la narración; y nombre de la voz/idioma del TTS (acentos latinoamericano o de Puerto Rico) para ajustar la pronunciación de siglas.
5. **Nombres que se pueden ver en pantalla.** ¿Se usan los clientes y productos reales de Solutions (42 productos activos) o ficticios? Recomiendo productos reales de Solutions y clientes ficticios/dos o tres empresas aprobadas.
6. **Estilo de transición** (corte seco o fundido de 0,3 s) y si lleva música de fondo y rótulos de bloque.
7. **Ubicación de lo pesado:** propuesta `F:/Download/TeikemVideo/<id>/`. ¿Aceptas, o prefieres otra unidad? Nunca la raíz de F: (memoria).
8. **Tema claro y resolución 1920x1080** como estándar; ¿también versión en inglés (hay soporte `MANUAL_LANG=en` para capturas, pero no para video)? Recomiendo solo español ahora.
9. **Quién corrige los hallazgos del producto** que aparezcan al grabar (R10): lote aparte con tus reglas, no dentro del flujo. Confirmar.
10. **Pausas:** propuesta de tres (guion y audio; tomas; MP4 final). ¿Quieres una más, p. ej. aprobar los datos de demostración antes de grabar?
11. **Reinicio de sesión:** los agentes y skills nuevos solo se cargan al iniciar la sesión (nota en `fe-implementar.js:25-26`); el primer uso en la misma sesión pasaría las reglas en el prompt.
12. **No verificado:** que los scripts `scripts/solutions-pedido-vendedores/` y `scripts/demo/` sean reutilizables tal cual para sembrar; no los abrí. Tampoco comprobé si `api-para-pruebas.ps1` acepta apuntar a otra base (solo vi sus líneas 27-28); si no, habría que parametrizarlo (cambio de script, a pedir).

## 9. Fuentes

- `.claude/agents/*.md` (los once; frontmatter en líneas 1-6 de cada uno), `.claude/workflows/fe-implementar.js:1-91`, `lote-diseno.js:1-14`, `.claude/launch.json`.
- `CLAUDE.md` del proyecto (reglas de ramas, correo/Brevo, SQL congelado, pantallas, tema/idioma, manual) y `CLAUDE.md` global del usuario (respuestas en español, sin ramas sin preguntar, pantallas con Playwright mirando la captura).
- `web-app/e2e/staffHelpers.ts:10-141`, `web-app/e2e/manualLang.ts:1-22`, `web-app/playwright.config.ts` (config sin `video`, `globalSetup`, proyectos).
- `scripts/api-para-pruebas.ps1:1-2,27-28`.
- `docs/demo/guia-demo-jose.md:1-60` (formato de guía, preparación y datos verificados).
- Memoria del proyecto: `feedback-no-usar-brevo`, `feedback-no-congelar-la-maquina`, `feedback-verificar-en-pantalla-con-playwright`, `reference-entorno-verificacion-e2e`.
- Carpeta hermana `docs/video/solutions-proceso/mapa/` (`02` a `05`, existentes).
