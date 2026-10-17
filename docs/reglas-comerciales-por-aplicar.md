# Reglas comerciales por aplicar (paquetes, límites y cobro por uso)

> **Estado: PENDIENTES. Ninguna está programada todavía.** Las anotó Luis (2026-10-16) pensando en salir a mercadear Teikem pronto. Cuando se diseñen los planes y el cobro por uso (límites de llamadas del API, usuarios y choferes), hay que aplicarlas. Este archivo es el recordatorio; si se aplica una regla, se marca abajo con la fecha y el lote.

## Las reglas (tal como se dictaron)

1. **Aviso automático cuando llegue al 80 % del límite.**
2. **No cobrar excedentes sorpresivos el primer mes.**
3. **Permitir un pequeño margen de tolerancia.**
4. **No contar llamadas fallidas provocadas por problemas de la plataforma** (de Teikem).
5. **Limitar la frecuencia** para evitar que una integración defectuosa consuma recursos sin control.
6. **Usuarios de consulta o portales externos podrían ser gratuitos;** cobrar principalmente a los usuarios operativos.
7. **Si los choferes usan la aplicación, evitar cobrar $25 por cada chofer.** Conviene incluirlos en paquetes o usar una categoría móvil más económica.

## Cómo encajan con lo que ya existe (apuntes para cuando se diseñe)

- **Avisos al 80 % (regla 1):** el motor de avisos (capa J: eventos, reglas por usuario, correo y Kemi) ya permite publicar un evento nuevo; haría falta el contador de uso por compañía y un trabajo programado que compare contra el límite.
- **Tolerancia y primer mes (reglas 2 y 3):** son parámetros del plan (porcentaje de margen y meses de gracia), no código fijo.
- **Llamadas fallidas por la plataforma (regla 4):** el conteo debe excluir las respuestas 5xx y los errores de infraestructura; hay que decidir cómo se distingue un error de Teikem de uno del cliente (4xx) y dónde se mide (middleware del API).
- **Límite de frecuencia (regla 5):** limitador por compañía e integración (cuota por minuto); el API ya tiene reautenticación y políticas por permiso, pero no un limitador de tasa por compañía.
- **Tipos de usuario (reglas 6 y 7):** hoy un usuario es de la compañía, del portal de clientes o chofer (app); el cobro por tipo requiere distinguir **operativo**, **consulta/portal** y **móvil/chofer** como categorías del plan.
- **Dónde guardarlo:** en el módulo de planes y módulos por compañía (los módulos ya se encienden por tenant); las reglas son configuración del plan, no condiciones en el código.

## Registro

| Regla | Aplicada (fecha y lote) |
|---|---|
| 1. Aviso al 80 % | — |
| 2. Sin excedentes sorpresivos el primer mes | — |
| 3. Margen de tolerancia | — |
| 4. No contar llamadas fallidas por la plataforma | — |
| 5. Limitar frecuencia | — |
| 6. Usuarios de consulta o portal gratis | — |
| 7. Choferes en paquetes o categoría móvil económica | — |
