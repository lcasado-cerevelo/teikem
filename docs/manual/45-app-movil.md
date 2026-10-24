# 45 — App móvil (chofer y personal)

> Lote 213 (2026-10-24). La app del chofer (capítulo 04, Lotes 203-212) es ahora **la app móvil de Teikem**: la misma app para choferes y personal. Lo que ve cada persona lo deciden sus permisos.

## 1. Quién entra
- Entra quien tiene el permiso **«App móvil: usarla»** (`mobile.use`) **o es chofer activo** en alguna compañía. Sin ninguna de las dos: «Su usuario no tiene acceso a la app móvil en ninguna compañía.» (403).
- `mobile.use` lo traen las plantillas **Chofer** y **Despachador** (y TenantAdmin). El administrador se lo da a quien quiera en Sistema → Roles.
- **Una sesión, todas sus compañías**: igual que el chofer, la persona entra una vez y la app trabaja con cada compañía donde tiene acceso (una compañía que exige MFA lo exige en el ingreso).
- **Usuario «Chofer (solo app)»**: ve únicamente lo del chofer, aunque su rol tuviera otros permisos. Un empleado que también maneja es un usuario normal con permisos de chofer: ve las dos cosas.

## 2. Qué ve el personal (v1)
- **Inicio**: «**Necesita tu atención**» (los mismos avisos del Pulso de la web, por compañía, con `pulse.attention`) y «**Pulso del día**» (los indicadores que puede ver, con `analytics.view`). Si además es chofer, debajo «Mi trabajo de chofer» con sus tarjetas de siempre.
- **Mensajes** (chat con fotos, tarjetas de orden, sugeridos), **Avisos** y **Perfil**: lo mismo que el chofer (capítulo 15 §8-9 y capítulo 04).
- **Manifiestos**: solo para choferes.
- Lo que sigue (Lotes 214-216): órdenes (buscar, ficha, completar), rutas de hoy y asignar en la calle, clientes y almacén en consulta.

## 3. Avisos emergentes
- La app registra el teléfono en `POST /api/v1/mobile/device` para cada compañía. Un chofer queda en su ficha (`DriverDevice`); el personal, en su usuario (`UserPushDevice`). Los mensajes del chat y los avisos llegan por cualquiera de los dos.

## 4. Mensajes exactos
| Situación | Mensaje | HTTP |
|---|---|---|
| Sin `mobile.use` y sin ser chofer | `Su usuario no tiene acceso a la app móvil en ninguna compañía.` | 403 |
| Sesión que no es de la app | `Esta sesión no es de la app móvil.` | 403 |
| Registro del teléfono sin token / plataforma inválida | `Falta el token del teléfono.` / `La plataforma debe ser ANDROID o IOS.` | 400 |
