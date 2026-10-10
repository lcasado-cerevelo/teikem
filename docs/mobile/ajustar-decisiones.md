# Ajustar cantidad desde el aparato — decisiones (2026-10-10)

## Qué se construyó
- **Servidor**: permiso `warehouse.adjust` (sin plantilla de rol: el administrador lo da a un rol propio; TenantAdmin por «todos»), `POST /api/v1/inventory/adjustments/quantity` (`AdjustQuantityAsync` = `AdjustAsync` + rechazo de posiciones de cuarentena, en renta y cross-dock). Sección en `Diseño/logistica-db-update.sql` (idempotente).
- **App**: botón *Ajustar* en Consultar (por posición y por producto) y pantalla Ajustar: dirección, cantidad, motivo según la dirección (como la web), nota obligatoria, confirmación.

## Validado
El ajuste es solo de cantidad (una posición, cantidad con signo, motivo, nota); mover de una posición a otra es una transferencia. Un ajuste «−5 en A, +5 en B» no es una transferencia: quedaría como dos ajustes sin enlace.

## Decisiones a revisar
1. Permiso aparte y sin plantilla: lo asigna el dueño a un rol propio (pedido del dueño).
2. En línea, sin cola, como Transferir y Daño.
3. Series fuera de esta versión (desde la web).
4. La cantidad del sistema («de X pasa a Y») solo se muestra con `warehouse.count`.
