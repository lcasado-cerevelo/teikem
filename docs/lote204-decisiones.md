# Lote 204 — App del chofer, etapa 2a: acceso (2026-10-23)

## Qué se construyó (`app-choferes`)
- Se sustituyó Auth0 por el acceso de Teikem: correo y contraseña, paso MFA (incluida la configuración inicial con códigos de recuperación), renovación de sesión, cierre de sesión, cambio de compañía y documento legal (primer ingreso y cada cambio).
- «Abrir con huella»: bloqueo de la app (al arrancar y tras 2 min en segundo plano). **No guarda la contraseña ni reemplaza el MFA** (la versión de Colavora guardaba usuario y contraseña y los reenviaba con la huella).
- La sesión respeta el vencimiento que fija la compañía (`refreshExpiresAtUtc`); un error de red no la cierra, un rechazo del servidor sí.
- El inicio ahora pone las recogidas primero y solo muestra los botones que tienen algo.

## Cómo se probó
Compila sin errores de tipos, arma el paquete web (`expo export`) y la pantalla de inicio de sesión se vio en un navegador. **No se probó contra el servidor ni en un teléfono** (no hay base de datos en este entorno): el flujo completo (login, MFA, huella, legal, cambio de compañía) está por verificar en el teléfono con el API. La app no tiene todavía corredor de pruebas.

## Decisiones a revisar
- **Huella = abrir la sesión guardada, no reemplazar el MFA** (como el portal del cliente). Recomendado; es decisión de Luis si prefiere otra cosa.
- «Confirmar el teléfono por correo» y el primer ingreso (correo y contraseña propia) se completan fuera de la app: la app solo avisa.
- Tema oscuro y modo claro/oscuro: la app copiada de Colavora es solo clara; la regla del proyecto pide los dos temas salvo la excepción de `app-almacen`. Falta decidir si `app-choferes` es excepción.
- Los datos (manifiestos, envíos, entrega) siguen apuntando al servidor anterior: es la siguiente parte.
