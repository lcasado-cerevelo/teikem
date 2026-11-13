---
name: video-guion-tts
description: Reglas para escribir el guion maestro y los textos limpios de un video narrado con voz sintética en español (text-to-speech). Úsalo al escribir o corregir cualquier guion de video de Teikem.
---
# Guion para text-to-speech

Lee primero `docs/video/REGLAS.md` (marcas del guion, R3 datos, R6 narración, R12 honestidad).

## Voz y estilo
- **Usted**, tono cercano, como un compañero que enseña el sistema. Frases de **8 a 18 palabras**, una idea por frase, sin paréntesis, sin guiones largos, sin listas dentro del texto narrado.
- Hablas de lo que **el usuario ve y hace**, no del código. Nombres de pantalla y botones **exactamente** como se ven en español (de `es.json`, citados en el mapa). En el guion maestro van entre comillas «así»; en el texto limpio, sin comillas.
- **La voz va un instante después de la acción**: nunca nombres algo que todavía no aparece. Cada frase narrada enlaza con una acción visible o con una pantalla ya cargada.
- Cierra cada bloque con una frase de por qué le sirve a Solutions (qué problema evita), no con un resumen de botones.

## Escribir para el oído
- Números con letras («cuarenta y dos productos»), montos con la unidad («cuarenta y un dólares con noventa y cuatro centavos»), fechas dichas («doce de noviembre»), cantidades grandes sin separadores.
- Siglas: deletreadas o con su pronunciación en `guion/pronunciacion.md` (tabla de apoyo; ver abajo). SKU se dice «ese ka u»; CSV «ce ese ve»; IVU «i ve u»; Teikem «téi-kem» según la voz que use Luis (preguntar).
- Evita palabras que un text-to-speech lee mal: abreviaturas (Ud., Dpto.), códigos con guiones largos (PDC-04292026: se dice «la compra PDC cero cuatro dos nueve dos cero dos seis» solo si hace falta; mejor «la primera compra»), signos raros y emojis.
- Pausas: una línea en blanco y la marca `[PAUSA 1s]` solo en el guion maestro; en el texto limpio, un punto y aparte. **El text-to-speech no debe recibir ninguna marca.**

## Ritmo
- Referencia **140 a 150 palabras por minuto** solo para estimar; la duración real sale del audio que devuelve Luis.
- Cada escena trae su duración estimada y lo que se puede recortar. Si la escena pasa de lo estimado, se acorta el texto, nunca se acelera el video.

## Prohibido
Datos personales reales (clientes, correos, teléfonos), costos de compra, contraseñas, nombres de funciones que el mapa no confirme, promesas («pronto podrá…»), y decir que algo «se hace solo» si requiere un clic.

## Salidas
- `docs/video/<id>/guion/guion-maestro.md`: cabeceras de bloque y de escena, narración, `> ACCION:`, `> RESALTAR:`, `> RUTA:`, `> TAPAR:`, `> LUIS:` y `[PAUSA 1s]`.
- `docs/video/<id>/guion/texto-tts/Enn-<clave>.txt`: solo la voz, una frase por línea, sin marcas. Un archivo por escena; el mismo nombre que el audio que devuelve Luis.
- `docs/video/<id>/guion/pronunciacion.md`: tabla «como se escribe → como se dice» (QuickBooks, SKU, CSV, IVU, Teikem, Advance Solutions, OFERTA1, nombres de productos), con las dudas para Luis.

## Antes de entregar
Relee el guion contra el mapa: cada etiqueta existe, cada paso está en el orden del mapa, ninguna frase dura más de lo que dura la acción, y el texto limpio no trae marcas ni comillas.
