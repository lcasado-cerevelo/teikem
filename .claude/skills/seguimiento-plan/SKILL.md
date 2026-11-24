---
name: seguimiento-plan
description: "Seguimiento de un plan de trabajo: comparar la versión actual contra una anterior, redactar el mensaje de commit del plan, revisar la bitácora, proponer hitos y reportar avance real contra lo planificado. Úsalo cuando el plan cambie o cuando pidan el estado del plan."
---

# Seguimiento del plan

El plan solo lo edita el responsable del proyecto. Este skill solo lee y compara.

## 1. Comparar versiones
Recupera la versión anterior del plan (por ejemplo desde el historial del repositorio) y compárala con
la actual. Reporta: estados y avance, horas o esfuerzo, fechas de inicio y fin, responsables, tareas
agregadas, eliminadas o reordenadas, y totales.

## 2. Mensaje de commit del plan
- Asunto: `Plan: <resumen breve>`.
- Cuerpo: lista corta y concreta de los cambios, agrupada por estado, esfuerzo, fechas y totales.
- Si cambian fechas o esfuerzo, la bitácora debe justificarlo; si no hay entrada, avisar.

## 3. Reporte de estado
1. Estado general: avance, fecha final y esfuerzo total.
2. Por entrega o fase: tareas completadas, en progreso y atrasadas (fecha final pasada y sin completar).
3. Qué cambió desde el último hito o desde la versión indicada.
4. Esfuerzo planificado contra el consumido, si hay datos.
5. Riesgos: tareas que dependen de terceros y siguen pendientes.

## 4. Hitos
Al cerrar una entrega o fijar una línea base, proponer una etiqueta (por ejemplo `plan-base-v1`,
`entrega-1-cerrada`) para poder comparar lo planificado contra lo real.
