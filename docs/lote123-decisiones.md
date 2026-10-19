# Lote 123 — Kemi: «Pregúntame cómo usar el sistema» (demostración, P12)

## Qué se construyó
- Opción nueva en el panel de Kemi (web del personal): **«Pregúntame cómo usar el sistema»**. Abre una caja de pregunta (máx. 300 caracteres, botón Preguntar inactivo sin texto); Kemi muestra la pregunta y responde «esta función está en construcción»; Volver regresa a las opciones. Español e inglés, con los colores del tema (claro/oscuro).
- Sin llamadas al API y sin IA: es la interfaz de la demostración, como dictó Luis (2026-10-18).

## Cómo se probó
- vitest: prueba nueva en `NotificationsUi.test.tsx` (botón inactivo sin texto, pregunta visible, respuesta «en construcción», Volver). Los 59 tests de notificaciones/i18n pasan; tsc limpio.

## Decisiones a revisar / lo que sigue
1. **Buscador en el manual y la FAQ (sin IA):** falta decidir dónde vive (índice generado del manual al compilar la web vs. endpoint en el API), qué responde cuando no hay resultado y cómo se mantiene es/en (el manual hoy está solo en español). Pendiente de Luis.
2. «¿Te contesté?» y buscar por otro lado dependen del punto 1.
3. Hacer operaciones por la persona y la IA quedan para después.
