---
name: video-revision-calidad
description: Método y lista de revisión para dar por bueno un video o una toma MIRANDO cuadros (nunca sin abrirlos), con la plantilla del informe de hallazgos. Úsalo antes de dar por terminada una escena o el video.
---
# Revisión de calidad por cuadros

Es la regla R7 de `docs/video/REGLAS.md` aplicada: **nada se aprueba sin haber abierto cuadros con Read**, y el informe lista los que se vieron.

## Método
1. Extraer cuadros: uno cada 5 s y uno en cada cambio de pantalla (`ffmpeg -i X -vf "fps=1/5" cuadros/Enn-%04d.png`; para cambios, `select='gt(scene,0.2)'`). Mínimo **3 por escena** (inicio, medio, final).
2. **Abrir cada cuadro con Read** y mirarlo con ojo de usuario: ¿se ve bien y decente?
3. Medir lo que importe (con la toma abierta en el navegador o con las medidas del propio spec): alto de las filas de tablas, tamaño de miniaturas, texto cortado, scroll horizontal, cursor fuera de cuadro, toasts encima, cargas de más de 2 s.
4. Buscar datos sensibles: nombres reales, correos, teléfonos, **costos de compra**, contraseñas, llaves, TEST o producción en la barra.
5. Correspondencia voz-imagen: cada etiqueta que la voz nombra aparece tal cual en ese instante; la voz no adelanta a la imagen más de 0.5 s; el orden de los bloques es el dictado.
6. Audio (sin oírlo): duración y silencios por medición (`ffprobe`, `silencedetect`); volumen (−16 LUFS con `loudnorm` en modo medición). Decir que no se pudo oír.

## Lista (10 puntos)
1 Entorno · 2 Datos sensibles · 3 Imagen · 4 Medidas · 5 Correspondencia · 6 Ritmo · 7 Audio · 8 Honestidad (solo lo que el software hace hoy) · 9 Orden de bloques · 10 Archivos. El detalle de cada punto está en `docs/video/REGLAS.md`, sección «Lista de revisión».

## Modo guion
Sin cuadros: cada etiqueta existe en el mapa, el orden coincide, no hay marcas en los textos limpios, frases de 8 a 18 palabras, números y siglas escritos para el oído, ningún dato sensible, ninguna promesa que el mapa no confirme.

## Plantilla del informe (`docs/video/<id>/revision/rNN-<fecha>.md`)
```
# Revisión rNN - <escena o video> - <fecha>
Veredicto: aprobado | con hallazgos | rechazado
## Cuadros que vi
| Archivo | Tiempo | Qué muestra |
## Hallazgos
| # | Escena | Tiempo | Qué se ve | Qué debía verse | Arreglo | Severidad (alta/media/baja) |
## Mediciones
## Lo que no pude comprobar (y por qué)
```
Severidad **alta**: dato sensible a la vista, entorno equivocado, texto ilegible, la voz nombra algo que no se ve, escena cortada. Una escena con hallazgos altos vuelve al grabador o al editor (máximo 2 rondas).
