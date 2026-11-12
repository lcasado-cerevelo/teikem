# Video del proceso de Advance Solutions — plan de trabajo (2026-11-12)

Estado: **plan para aprobar**. Lo que ya se hizo para dejar el terreno listo está en la sección 3; los agentes, skills y flujos nuevos todavía **no se crean** (esperan tu visto bueno, sección 7).
Detalle de cada escena: `mapa/01` a `mapa/06` (pantalla por pantalla, con etiquetas exactas y citas). Datos y entorno: `mapa/07`. Tecnología de grabación: `mapa/08`. Equipo, skills, flujos y reglas completos: `mapa/09`.

## 1. Qué se va a entregar
- Un video de **unos 10 minutos** (≈ 9 de pantalla + rótulos), **1920×1080, tema claro, español**, sin música, con voz tuya (la generas con tu text-to-speech), rótulos de bloque de 2 segundos y subtítulos (quemados y un `.srt` aparte).
- **Guion maestro con marcas** (qué se ve, qué se resalta, cuándo hay pausa) y, por escena, un **texto limpio** para pegar en tu text-to-speech. Tú me devuelves **un audio por escena**; **el audio manda**: la toma se ajusta a la voz, nunca al revés.
- Todo lo pesado (audios, tomas, cuadros, montaje, MP4) vive **fuera de git**, en `F:/Download/TeikemVideo/solutions-proceso/`.

## 2. Las seis escenas (en el orden que dictaste)

| # | Bloque | Qué se ve | Seg. |
|---|---|---|---|
| E1 | Productos e inventario | Lista con indicadores y balance (disponible, reservado, total); buscar; abrir y editar un producto (070120 Prodigy Monitor); **alta de la caja corrugada 101010** (existe en QuickBooks y no en Teikem); foto ampliable | 83 |
| E2 | Órdenes de compra | Importar el archivo de QuickBooks recortado a **dos compras** (PDC-04292026 de ADVANCE LOGISTICS: 5,000 tiras y 1,000 monitores; PDC-05282026 de PADIMONT: guantes NECH-1001 a 1004), estatus «Completada», revisar y ver las órdenes creadas; unos segundos de una orden **bloqueada** con su motivo | 70–90 |
| E3 | Recibo | Recibir esas dos órdenes (una completa y la otra acelerada), «Usar posiciones sugeridas», y volver a Productos e inventario para ver que subió | 95 |
| E4 | Venta con el **asistente** | Cliente, productos (**kit OFERTA1** + un producto suelto), entrega/retiro, **firma** y contraseña, pedido enviado | 115 |
| E5 | Almacén | Recolección y empaque: lo que hay que recolectar (el kit por piezas), recolectar, despachar la salida; Kárdex y balance | 85 |
| E6 | QuickBooks en ventas | De la orden despachada a la **factura**, pestaña QuickBooks, **«Exportar a QuickBooks»**, el archivo `.iif` descargado y cómo lo usa la oficina; lámina con los pasos dentro de QuickBooks y el contraste con hoy (Leandra teclea desde un correo) | 85 |

## 3. Lo que ya está preparado (hecho hoy, solo en una **copia**; tu base `Teikem` no se tocó)
- **ffmpeg 9.0.2 completo** instalado (H.264, AAC, subtítulos, rótulos, detección de silencios). Ruta: `C:\Users\Luis\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin`.
- **Base de grabación `Teikem_video`**: copia de `Teikem` (respaldo solo-copia, restaurada en segundos; modelo de recuperación simple). Se le aplicó:
  - los **6 proveedores** de las compras de QuickBooks de Solutions (ADVANCE DEPOT SOLUTIONS, ADVANCE LOGISTICS LLC, FARMACIA CENTRAL DRUG, GLOBAL IMPORT SALES LLC, PADIMONT HOLDINGS CORP, SHIELD LINE);
  - el módulo **BILLING (Facturación) encendido**;
  - los scripts de `scripts/solutions-pedido-vendedores/`: **01** (marcas: pestañas del pedido), **02** (kit **OFERTA1** = 6 × 52800 + 1 × 070120 a $41.94 y su oferta), **03** (roles Ventas, Oficina de ventas y Supervisor de ventas), **08** (envío $12, gratis desde $120; nombres de términos de QuickBooks) y **10** (productos y clientes ya en QuickBooks, para que el archivo lleve solo la factura nueva). El **09** no se corrió (crea reglas con correo);
  - la oferta de prueba «OFERTA DEL DIA» (kit «kIT DE mONITOR gRATIS») **desactivada**;
  - el almacén de Solutions en **recibo directo a posición** y con su nombre corregido («Almacén Solutions»).
- **Fotos**: el almacén de archivos (7,209 archivos, 221 MB) copiado a `F:/Download/TeikemVideo/base/files` para que el API de grabación las sirva sin tocar el de tu desarrollo.
- Los scripts temporales de esta preparación están en la carpeta de trabajo de la sesión; falta dejarlos como `scripts/demo/video-solutions-proceso/` (idempotentes, con guarda que aborta si la base no es `Teikem_video*`).

**Falta preparar** (se hace con el API de grabación levantado, sin correo): usuarios de demostración sin MFA (administrador, ventas, almacén, oficina), uno o dos clientes de demostración, cancelar la orden vieja OV-00002 (libera 35 unidades reservadas), el CSV de QuickBooks recortado a las dos compras, la foto de OFERTA1, y una **instantánea de la base** (SQL Server 2022 Developer la soporta) para volver al punto de partida en menos de 30 s entre tomas.

## 4. Hallazgos que cambian el plan (para que no haya sorpresas)
1. **Salida desde Recolección y empaque (tu escena 5).** Sí había despacho en la web, pero repartido: Ventas → la orden → «Despachar», Llegadas y salidas → Salidas → «Despachar» y el despacho manual del panel «Recolección». La ficha de Recolección y empaque solo tenía «Recolectar», que llena el formulario **manual** (no usa la reserva de la orden). **Ya está resuelto (Lote 242, hecho hoy):** la ficha de la orden tiene **«Despachar»** (retiro o entrega), verificado con Playwright. «Empacar» crea la orden de transporte y solo hace falta si la orden va a entrega.
2. **QuickBooks exporta facturas, no órdenes de venta.** Para E6 la orden despachada pasa por **Facturación**: «Nueva corrida» → «Generar» → «Marcar como revisada» → «Aprobar y emitir» (pide contraseña: se hace fuera de cámara) → Ventas → pestaña «QuickBooks». Por eso encendimos BILLING.
3. **El archivo de compras de QuickBooks trae facturas de compra (Bill), no órdenes de compra.** En la narración se dice «las compras de QuickBooks». Si más adelante tienes un reporte de órdenes de compra de verdad, se ajusta.
4. **La entrega no está configurada en Solutions** (sin contratos ni tarifas, Envíos apagado): el video usa **«Recogido» / «Retiro»**.
5. **Dos detalles de pantalla que se ven mal** (no se arreglan para el video; se anotan): el aviso de que el SKU se igualó no sale en las órdenes «Listas», y la ficha de una orden «Enviada» ofrece un botón azul «Avanzar a Cancelada». Además, un kit creado desde la pantalla queda sin marca (por eso se usa el script 02) y la ayuda del buscador de clientes promete búsqueda por teléfono que no existe.
6. **Orden entre escenas:** E1 antes que E2 (si no, la importación se bloquea por la caja 101010), y E3 antes que E4 (hay que tener lo que se vende).
7. **QuickBooks Desktop no se puede grabar**: la escena usa lámina con los pasos, o una grabación corta tuya.

## 5. Cómo se trabaja (equipo, skills, flujo, reglas)
- **Agentes nuevos (6, prefijo `video-`)**: `video-datos` (prepara y repone la base), `video-mapeador`, `video-guionista` (**Opus**, sin Bash), `video-grabador` (una escena por corrida, 1 worker), `video-editor`, `video-revisor` (**Opus**, solo lectura; **obligado a abrir y listar los cuadros que miró**). El resto en **Sonnet con esfuerzo medio** para cuidar tu cuota; los flujos fijan modelo y esfuerzo de cada agente explícitamente (los primeros agentes heredaron el máximo y por eso gastaron de más).
- **Skills (5)**: `video-guion-tts` (reglas de redacción para voz sintética en español), `video-grabar-escena` (receta acompasada al audio), `video-editar-exportar` (ffmpeg), `video-revision-calidad` (lista de revisión por cuadros) y `video-entorno-seguro` (chequeo antes de levantar o sembrar).
- **Tres flujos con tres pausas tuyas**: `video-guion` (datos → mapa → guion; **PAUSA 1**: apruebas el guion y me devuelves los audios) → `video-grabar` (graba escena por escena, revisa cuadros; **PAUSA 2** opcional: tomas crudas) → `video-montar` (monta, revisión final, documenta; **PAUSA 3**: apruebas el MP4).
- **Reglas** (`docs/video/REGLAS.md`, con una línea en `CLAUDE.md`): R1 solo API **sin correo** (Brevo vacío) y base copia `Teikem_video*`, nunca TEST, producción ni tu base; R3 nada de clientes, personas ni correos reales en pantalla; R4 contraseñas y llaves fuera de cámara; R5 formato; R6 la voz la generas tú y el audio manda; R7 **nada se entrega sin mirar cuadros** (extiende tu regla de pantallas); R8 una grabación a la vez, máximo 2 intentos por escena; R9 qué va a git y qué no; R10 los fallos del producto se anotan, no se arreglan durante la producción; R12 el video solo muestra lo que el software hace hoy.
- **Grabación**: Playwright con `recordVideo` (VP8, 25 cuadros por segundo reales, 1920×1080) más cursor falso inyectado (el de Playwright no existe) y **resaltado de cada clic**; si una escena de prueba no convence, se pasa a cuadros JPEG q100 con `page.screencast` (~60 fps). El texto de 12–14 px se lee pero blando: se prueba **zoom 1.25** en E1.
- **Sincronía con la voz**: audio primero. Mido cada audio con `ffprobe`, el recorrido reparte esa duración entre pasos y espera resultados visibles; si la toma dura menos que el audio, se alarga la pausa final; si dura más, se acorta el guion (nunca se acelera el video). Se puede partir un audio único con `silencedetect` (exacto con pausas de 0.7 s), pero conviene **un archivo por escena**.

## 6. Qué necesito de ti para los audios (cuando llegue la pausa 1)
Un archivo por escena (WAV o MP3), sin música, ~0.3 s de silencio a cada lado, nombrados `E01-productos.mp3`, etc., y el texto exacto que leíste. Yo te doy los textos limpios, sin marcas y con números y siglas escritos para el oído.

## 7. Decisiones que me tocan de ti (con mi recomendación; «sí a todo» vale)
1. ~~Lote 242 (botón «Despachar» en la ficha)~~ **hecho**: ver hallazgo 1.
2. **Clientes en pantalla**: uno o dos clientes de **demostración** (ficticios) y no los 701 reales (el buscador los enseña). Recomiendo **sí**.
3. **Costos de compra** (se ven en la ficha de la orden de compra y de la recolección): **taparlos o no abrir esas fichas**; precios de venta sí se ven. Recomiendo **sí**.
4. **Las dos compras de ejemplo** (PDC-04292026 y PDC-05282026), importadas como **«Completada»**. Recomiendo **sí**.
5. **Retiro/«Recogido»** en lugar de entrega (hallazgo 4). Recomiendo **sí**.
6. **Escena QuickBooks**: la contraseña de aprobar la factura se pone **fuera de cámara**, y QuickBooks Desktop va como **lámina con los pasos** (o una grabación tuya). Recomiendo **sí**.
7. **Narración**: ¿**tú** o **usted**? ¿Tono cercano o de oficina? ¿Qué voz/acento usará tu text-to-speech (para ajustar la pronunciación de QuickBooks, SKU, CSV)? Recomiendo **usted, tono cercano**.
8. **Estilo**: tema claro, 1920×1080, español, sin música, rótulo de bloque de 2 s, subtítulos quemados y `.srt` aparte, corte seco o fundido de 0.3 s. Recomiendo **esos valores**.
9. **Dónde vive lo pesado**: `F:/Download/TeikemVideo/solutions-proceso/` (nada binario en git). Recomiendo **sí**.
10. **Pausas**: guion+audios, tomas (opcional) y MP4 final. Recomiendo **sí**.
11. **Crear ahora** los 6 agentes, 5 skills, 3 flujos y `docs/video/REGLAS.md` (más la línea en `CLAUDE.md`) y **dejar los scripts de preparación en `scripts/demo/video-solutions-proceso/`**. Los agentes y skills nuevos solo se cargan al iniciar una sesión: la primera vez paso las reglas en el prompt. Recomiendo **sí**.

## 8. Siguientes pasos al aprobar
1. Crear las piezas de la decisión 11 y dejar los scripts de preparación en el repositorio.
2. ~~Lote 242~~ (hecho).
3. Levantar el API de grabación (sin correo, base `Teikem_video`, `Files__Root` copiado), crear usuarios y clientes de demostración, cancelar OV-00002, recortar el CSV de QuickBooks, poner la foto de OFERTA1 y tomar la instantánea.
4. **Ensayar E1** completa (con `video-grabador`), mirando cuadros: ahí se decide zoom y calidad de captura.
5. Escribir el guion maestro y los textos limpios → **PAUSA 1** (tú apruebas y me devuelves los audios).

## 9. Notas de cuota y de seguridad
- Los mapas ya están hechos (9 informes); lo que falta consume menos: Sonnet medio para casi todo, Opus solo en guion y revisión final, **una escena a la vez**.
- Nada de esto usa Brevo ni toca TEST, producción ni tu base `Teikem`; si algo falla en la grabación, se restaura la instantánea.
