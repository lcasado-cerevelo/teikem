# Decisiones del dueño — cierre de los lotes de región y formatos, seguridad, marca, "hoy" y conteo por producto

Fecha: 2026-10-03. Respuestas de Luis a las decisiones que fueron quedando pendientes en los documentos de decisiones de los lotes 18 a 21, F9 a F12 y A3 a A4. "Se queda" = se acepta lo que ya está construido, sin cambios.

## Cambios que se hacen
| # | Tema | Decisión | Dónde |
|---|---|---|---|
| 1 | Revocar una sesión | **Cambiarlo: solo esa sesión.** Revocar una sesión (por ejemplo un aparato) cierra solo esa; la detección de robo se conserva solo para el reuso real de un token ya rotado. | Servidor (`AuthService`) |
| 2 | Recaptura del operario sobre una línea corregida | **Proteger la corrección.** Una línea ya corregida por el supervisor no se puede recapturar por el operario (se rechaza con mensaje claro); el supervisor sí puede volver a corregirla. | Servidor (`CycleCountService`) |
| 3 | Crear conteos por producto desde la web | **Sí, agregarlo** al "Nuevo conteo" de la web. | Web |
| 4 | Confirmar con todo en blanco en la app | **Exigir al menos un número.** Si no hay nada que contar, el operario escribe 0 al menos en una posición. | App |
| 5 | Despacho: escanear la posición antes de la cantidad | **Exigir la cantidad primero.** Escanear la posición sin cantidad no agrega la línea y avisa; con cantidad escrita, escanear sigue siendo "Aceptar". | App |

## Se queda como está
- Token de acceso de una sesión revocada: vive hasta 15 minutos más.
- "Cerrar las demás sesiones" incluye los aparatos de almacén.
- Receptores de DataWedge abiertos a otras apps del aparato (riesgo bajo en un Zebra administrado).
- Corregir en la web exige `warehouse.count`; el servidor acepta la corrección con `warehouse.count.capture`.
- Quien solo captura puede crear posiciones provisionales (quedan "pendiente de revisión").
- Al tocar un producto de la lista del conteo se escribe su SKU.
- Barra de navegación del aparato: solo el margen inferior, sin modo inmersivo.
- Lote nuevo desde "Otra posición": número obligatorio, vencimiento opcional.
- Pantalla de entrada (login) con el logo de Teikem; sin endpoint público de logos.
- La marca cuadrada no sustituye al lockup.
- Lote 20: sin pruebas adicionales con reloj fijo para los ocho servicios sin prueba propia.
- Firmas de commits: se dejan como están.
- Fecha de Puerto Rico: MM/DD/AAAA, hora de 12 horas (confirmado antes).
- Quién ve las cantidades esperadas al contar: no se toca.
