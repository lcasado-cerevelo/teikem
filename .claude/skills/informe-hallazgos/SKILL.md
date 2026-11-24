---
name: informe-hallazgos
description: "Formato y método para registrar hallazgos de seguridad, integridad y rendimiento en un informe: identificadores, severidad, estados, cómo reproducir y recomendación. Úsalo al confirmar, agregar o reverificar un hallazgo."
---

# Informe de hallazgos

Mantén un único informe vivo, con una tabla de resumen y el detalle de cada hallazgo, sincronizados.

## Identificadores
`S-nn` seguridad, `I-nn` integridad de datos, `R-nn` rendimiento. Continúa la numeración existente y
no reutilices un ID.

## Severidad
| Severidad | Criterio |
|---|---|
| Crítica | Explotable sin credenciales o con cualquier usuario; compromete datos, servidor o registros críticos. Atender de inmediato. |
| Alta | Explotable por un usuario autenticado, expone datos sensibles o afecta la confiabilidad de los registros. Atender en la fase actual. |
| Media | Requiere condiciones adicionales o tiene impacto limitado. Planificar. |
| Baja | Buenas prácticas; riesgo bajo. |

## Estados
**Código** (visto en el código) → **Confirmado** (reproducido en un ambiente controlado) → **Validado**
(reproducido por el responsable de TI del cliente) → **Corregido** (reverificado tras el parche). Un
hallazgo no avanza de estado sin evidencia.

## Cada hallazgo incluye
- **Dónde:** archivo y línea, o pantalla, URL o consulta.
- **Qué pasa:** el comportamiento, en lenguaje comprensible.
- **Cómo reproducir:** pasos; lo que escribe datos o simula ataques, solo en un ambiente controlado.
- **Impacto:** qué se expone o se puede alterar.
- **Recomendación:** inmediata y de fondo.

## Reglas
- Sin credenciales, cadenas de conexión ni datos personales en el informe.
- Cada hallazgo corregido tiene una prueba asociada.
