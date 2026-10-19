# Lote 121 — todos los usuarios los controlan los administradores: pestañas Clientes y Consignatarios (dictado de Luis, 2026-10-18)

## Qué se construyó
- **`GET /api/v1/portal-users?kind=&includeRemoved=`** (`PortalUserDirectoryService`, solo lectura): la lista de TODAS las personas de portal de la compañía, con su tipo (CLIENT o CONSIGNEE), su cliente y, si es un consignatario, cuál. Permiso `portalusers.manage` + módulo CLIENT_PORTAL; las dadas de baja no salen salvo que se pidan; nunca las de otra compañía.
- **Pestañas Clientes y Consignatarios** en Sistema → Roles y usuarios (junto a Roles y Usuarios): tabla con búsqueda y paginación, y las acciones de siempre por fila (abrir el cliente, reenviar invitación, suspender, reactivar, dar de baja) con el cliente de cada fila.
- **Corte inmediato de la sesión** (hallazgo del recorrido): al suspender o dar de baja a una persona el sello de seguridad se rotaba, pero la caché de 60 s del sello seguía aceptando su sesión abierta hasta un minuto. `PortalUserStatusEffect` ahora borra esa entrada al suspender, dar de baja y reactivar.

## Cómo se probó
- **xunit:** `PortalUserDirectoryTests` (3: todas las personas con su tipo y cliente sin la dada de baja ni la de otra compañía, filtros de tipo y «incluir dadas de baja», 400 con un tipo inválido, solo lectura). Suite completa verde.
- **vitest:** `PortalUsersTab.test.tsx` (5: lista y búsqueda, acciones con el cliente de cada fila, consignatarios, pestañas con y sin permiso/módulo).
- **Playwright** (`lote121-usuarios-portal.spec.ts`, escritorio y móvil) contra el API y SQL Server reales: las cuatro pestañas, tipo y cliente de cada persona, suspender con confirmación y comprobar que la sesión del consignatario deja de valer, reactivar, dar de baja y verlo con «Incluir dados de baja»; una persona de portal no entra a la lista (403) y un tipo inválido responde 400.

## Decisiones a revisar
1. **El administrador de la plataforma** ve y controla a las personas de la compañía que tenga activa (la misma forma de trabajo de las demás pantallas); no hay una lista de todas las compañías a la vez.
2. **No se invita desde aquí** (la compañía invita a las personas del cliente desde su expediente; el cliente invita a sus consignatarios desde su portal).
3. **«Restablecer acceso»** de una persona de portal sigue siendo «Reenviar invitación» (la contraseña la fija la persona; nadie escribe la de otro).
4. **Usuarios de staff:** siguen en la pestaña Usuarios (ya los controlan el administrador de la plataforma y el de la compañía); no cambió.
