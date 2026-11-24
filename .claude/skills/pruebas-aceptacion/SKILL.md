---
name: pruebas-aceptacion
description: "Plan de pruebas en un ambiente de pruebas y de aceptación con el cliente (UAT): casos desde los hallazgos y las tareas del plan, datos de prueba sin información personal, guiones por rol y acta de aceptación. Úsalo antes de cada despliegue y en las pruebas finales."
---

# Pruebas y aceptación

## Qué probar
1. **Lo entregado en cada despliegue:** por tarea, "cómo se verifica" y "quién valida". Cada una
   produce al menos un caso con resultado esperado.
2. **Regresión:** los flujos principales del sistema. Las pruebas de caracterización se vuelven a correr.
3. **Seguridad:** por cada hallazgo corregido, la reproducción original ya no funciona; acceso sin
   sesión, rol incorrecto y registros de otro usuario son rechazados.
4. **Rendimiento:** medición con el skill `medicion-rendimiento`, comparada con la línea base.

## Datos
El ambiente de pruebas usa una copia de datos reales dentro del ambiente del cliente. **Al
repositorio solo van guiones y resultados agregados**, nunca datos personales.

## Guion de aceptación (uno por rol)
Pasos numerados, resultado esperado, resultado real, estado (aprobado o rechazado) y observaciones.

## Acta de aceptación
Entrega, fecha, alcance probado, resultados, defectos abiertos con su severidad, quién valida y firma.
Respeta el plazo que el cliente tenga acordado para reportar revisiones.

## Reglas
- Sin datos personales en capturas ni archivos.
- Distingue lo que se ejecutó de lo que solo se escribió.
