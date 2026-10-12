# Lote F-A3 — Usuarios de portal y Cotizador del cliente (2026-10-12)

Cierra la fila **F-A** del plan (Clientes y contratos) encima de [F-A1](loteFA1-decisiones.md) y [F-A2](loteFA2-decisiones.md). Solo se tocó `web-app/` y `docs/`: **el servidor no cambió**
(`openapi.json` y `schema.d.ts` quedan igual). Manual de pantallas: [fa3-portal-cotizador.md](../manual/frontend/fa3-portal-cotizador.md); FAQ: sección «Lote F-A3» de
[faq.md](../manual/faq.md). Referencia: manual 02 (cap. 5.5 y 7), `PortalUsersController`, `BillingQuoteController`.

Son pantallas **dentro de la ficha del cliente** (`/catalog/clients?client=<id>`). Las rutas `/portal/*` (portal del cliente, P10) no se tocaron y siguen pendientes.

## Qué endpoints y DTOs existen (confirmado en `openapi.json` y en los controladores)

| Endpoint | Permiso / módulo | DTO |
|---|---|---|
| `GET /clients/{id}/portal-users` | `portalusers.manage` + `CLIENT_PORTAL` | `PortalUserDto[]` |
| `POST …/portal-users/invite` (`[SkipIdempotency]`) | ídem | `PortalInviteRequest` {email, fullName?, role} → `PortalInviteResultDto` {user, inviteToken?, expiresAtUtc?} |
| `POST …/portal-users/{id}/resend-invite` (`[SkipIdempotency]`) | ídem | → `PortalInviteResultDto` |
| `POST …/portal-users/{id}/suspend` · `reactivate` · `remove` | ídem | 204 |
| `POST /portal-users/accept-invite` | anónimo | **no se usa en esta pantalla** (es del portal) |
| `POST /billing/contract-rate-quote` | `contracts.read` + `CATALOG` | `RateQuoteRequest` {clientPublicId, lines[{serviceType, packageType, pieces}], codAmount?, asOf?} → `RateQuoteDto` |

Lo que **no existe** y por eso no hay botón: editar el rol o el nombre de un usuario de portal ya creado, borrar (la baja es `remove` = estatus DISABLED), cambiar su correo, restablecer su contraseña
desde la ficha, ni un `rowVersion` en el usuario de portal (no lo trae el DTO: no hay 409 de `rowVersion` aquí). El servidor no devuelve «No hay tarifa vigente…» como error: ver decisión 6.

## Qué se construyó (`web-app/src/features/clients/`)

| Pieza | Qué hace | Archivo |
|---|---|---|
| Panel «Usuarios de portal» | Tabla Usuario (correo + nombre) · Rol · Estatus (`StatusChip` del dominio `PortalUserStatus`) · Último ingreso, con acciones por estatus; **Invitar usuario**; avisos exactos; se pinta solo con `portalusers.manage` y `CLIENT_PORTAL` | `ClientPortalPanel.tsx` |
| Invitar / Volver a invitar | Modal (`FormModal`) con correo, nombre y rol (lookup `PortalRole`); «Volver a invitar» trae correo (solo lectura), nombre y rol de la fila dada de baja | `ClientPortalPanel.tsx` |
| Datos | `usePortalUsers`, `usePortalMutations` (invitar y reenviar sin `Idempotency-Key`; suspender, reactivar y baja con una nueva por intento) | `portalApi.ts` |
| Reglas puras | `inviteSchema`, `buildInviteRequest`, `portalActions`, `sortPortalUsers`, `wasAddedWithoutLink` | `portalRules.ts` |
| Pestaña «Cotizador» | Quinta pestaña de la sección Contratos: líneas (`useFieldArray`), COD, «Cotizar a la fecha»; resultado con contrato usado, tabla por línea (base, piezas extra, subtotal, origen), despacho, COD y total | `ContractQuoteTab.tsx`, `ClientContractsSection.tsx` |
| Datos y reglas del cotizador | `useQuote` (`Idempotency-Key` por intento); `quoteSchema`, `buildQuoteRequest`, `remapQuoteErrors`, `lineSubtotal`, `linesWithoutRate` | `quoteApi.ts`, `quoteRules.ts` |
| Integración | Panel después de «Contratos» en la ficha | `ClientDetailPanel.tsx` |
| Textos / estilos | `clients.portal.*` y `clients.quote.*` (+ `clients.contracts.tabs.quote`) en `es.json` y `en.json`; `.cl-portal-*`, `.cl-quote-*` | `kernel/i18n/`, `clients.css` |

Reglas aplicadas: invitación = correo, nombre opcional y rol (lo que pide el contrato); mensajes del servidor tal cual (`Form`/`applyProblemDetails` y `ConfirmDialog`); el token de la invitación
**no se muestra nunca**; D39: con el cliente de baja no se invita ni se reenvía ni se cotiza (la pantalla lo avisa y el servidor responde 409 con el mismo texto si la ficha estaba vieja).

## Cómo se probó

| Prueba | Qué cubre |
|---|---|
| `portalRules.test.ts` (9) | esquema de la invitación (correo obligatorio/válido/150, nombre 150, rol), request (minúsculas, nombre vacío → null), «volver a invitar», acciones por estatus y con cliente de baja, orden, cuenta ya existente |
| `quoteRules.test.ts` (9) | esquema (servicio, paquete, piezas ≥ 1 y enteras, COD ≥ 0, par repetido en la 2.ª línea), request (COD y fecha → null), renombrado de errores `lines[i]…`, `rethrowQuoteError`, subtotal sin despacho ni COD, origen y líneas sin tarifa |
| `ClientPortal.test.tsx` (19) sobre `fetch` simulado | permisos y módulo (sin ellos no se pinta ni se pide nada), lista con estatus y acciones por estatus, vacío y error de lectura, cliente de baja (aviso, invitar desactivado, sin reenviar/volver a invitar), invitar (validación, cuerpo, sin `Idempotency-Key`, aviso con vencimiento, token no visible, 409 correo no disponible, 409 cliente de baja, 400 por campo, cuenta ya activa sin enlace), volver a invitar, reenviar (+409), suspender/reactivar con claves distintas (+409), dar de baja (+409) |
| `ContractQuote.test.tsx` (12) | pestaña con `contracts.read` y sin él, cotizar (cuerpo, `Idempotency-Key`, contrato usado, desglose, origen, despacho, COD, total), sin COD ni fecha, línea sin tarifa, sin contrato vigente, validación, par repetido, 400 por línea, 409 de cliente de baja, 404, agregar/quitar/limpiar |
| `ClientContracts.test.tsx` | la prueba de pestañas ahora espera cinco (…, «Cotizador») |
| `e2e/loteFA3-portal-cotizador.spec.ts` (Playwright, API real) | portal: invitar por la pantalla (validación, el token no aparece), 409 de correo repetido y de usuario interno, reenviar (el enlace anterior da 400 `Invitación inválida o vencida.`), aceptar por el API anónimo, suspender, reactivar, dar de baja, volver a invitar, los cuatro estatus en la lista, cliente dado de baja con la ficha abierta (409 exacto, luego aviso y botones fuera); cotizador: validación, 21.25 con tarifa de contrato y COD (despacho 3.00, COD 5.00), sin COD 16.25, línea sin tarifa, par repetido, fecha sin contrato, cliente sin tarifas, 409 de cliente de baja; sin scroll horizontal a 360 px |
| Resultado | `tsc -b` limpio; oxlint sin avisos en `features/clients` y en el spec; `vite build` bien; vitest completo **201 archivos / 2555 pruebas** (con las del otro lote en curso); Playwright `escritorio` **2/2** y `movil` **2/2** |

## Diferencias con la maqueta (`Diseño/teikem-mockups.html`)

La maqueta **no tiene** estas pantallas (no hay «usuarios de portal», invitaciones ni cotizador en `clientes()`); se siguió el patrón de F-A2: panel apilado en la ficha y pestaña en la sección
de contratos, con los mismos componentes del kit (`Panel`, `DataTable`, `FormModal`, `ConfirmDialog`, `StatusChip`, `Tabs`) y las mismas tarjetas bajo 640 px.

## Defectos hallados

| Hallazgo | Qué se hizo |
|---|---|
| **D44 no se cumple en el servidor.** La decisión del dueño (lote 36) dice que invitar un correo que ya es usuario de portal de **otro cliente** «se queda en 409»; el código (Lote 3, ajuste B: portal multi-cliente) responde **200** y agrega la fila al otro cliente (verificado en vivo con dos clientes). El capítulo 03 sección 6 documenta ese 200; el capítulo 02 sección 7 decía 409 | **No se tocó el servidor** (revertir es quitar la función multi-cliente: `clientsCount`, activar todas las filas con un solo enlace, etc.). Se corrigió el capítulo 02 al comportamiento real y la pantalla muestra lo que responda el servidor («También en N cliente(s) más»). **Decisión del dueño pendiente: ver 1** |
| Las tablas de mensajes del capítulo 02 (secciones 5.5 y 7) no listaban el 409 de cliente de baja de la cotización y del invitar/reenviar, ni el 404 de cliente; la fila de «correo ya registrado» describía el comportamiento anterior al Lote 3 | Corregido en el manual 02 |
| El brief hablaba de «No hay tarifa vigente para X/Y…» como error del servidor: **el servidor no lo manda**; una línea sin tarifa sale con `baseSource: NONE` y 0 | La pantalla lo avisa con texto propio (`clients.quote.noRate`) y lo documenta |
| (Pantalla, detectado en la primera captura) correos largos se cortaban a mitad de palabra en una columna angosta y había 7 columnas | Se juntó correo + nombre en una columna «Usuario» y se quitó la columna «Clientes» (el dato sale como «También en N cliente(s) más») |

## Decisiones tomadas por default para presentar al dueño al final de P1

1. **D44 / multi-cliente:** la pantalla acepta el comportamiento actual del servidor (200 al invitar a otro cliente a una cuenta de portal existente). Si el dueño quiere de verdad el 409, hay que
   cambiar `PortalUserService.InviteAsync` (con prueba xunit) y retirar «También en N cliente(s) más».
2. **«Desactivar/reactivar» = «Suspender / Reactivar»** (el servidor tiene suspender, reactivar y dar de baja; el estatus visible es «Suspendido»). **Dar de baja** (DISABLED) pide confirmación y se
   revierte con **Volver a invitar**.
3. **Etiquetas de estatus del catálogo de la compañía** (Invitado, Activo, Suspendido, Inhabilitado), no «pendiente/activo/desactivado».
4. **Rol sin valor por omisión**: hay que elegirlo (mínimo privilegio; el servidor lo exige).
5. **Sin token en pantalla** aunque el servidor lo devuelva en Development; el aviso solo dice a quién se envió y cuándo vence.
6. **Cotizador en la sección Contratos (pestaña), no en un panel aparte ni dentro de la orden.** Esto levanta la decisión de F-A2 («cotización fuera hasta las órdenes de venta, P4»): el uso dentro de la
   orden sigue en P4. «Sin tarifa» se avisa con un texto de la pantalla porque el servidor no lo manda.
7. **Permisos del cotizador:** `contracts.read` y nada más; el servidor no oculta estos precios por `orders.view_price` porque son del contrato. Sin `contracts.read` toda la sección Contratos (y el
   Cotizador) se reduce al aviso de F-A2.
8. **El panel de usuarios de portal no se muestra** (en lugar de mostrar un aviso) sin `portalusers.manage` o con el módulo apagado: se decide por los datos de acceso, sin pedir nada al servidor.
9. **Con el cliente de baja** se desactiva «Invitar usuario» y se ocultan «Reenviar invitación» y «Volver a invitar» (el servidor daría 409); suspender, reactivar y dar de baja quedan (el servidor los
   permite). El 409 igual se muestra tal cual si la ficha estaba vieja.
10. **Idempotency-Key:** invitar y reenviar sin clave (`[SkipIdempotency]`); suspender, reactivar, dar de baja y cotizar con una clave nueva por intento.
11. **«Cotizar a la fecha» vacía = hoy** (se manda `asOf: null` y el servidor usa su fecha); monto COD vacío = sin COD (`null`).
12. **Desglose:** el subtotal de cada línea es base + piezas extra; el despacho y el COD se muestran aparte aunque el servidor los incluya en el total de la primera línea.
13. **Moneda del resultado:** la del contrato usado (el DTO de la cotización no la trae); sin contrato, la de la compañía.

## No pude verificar / pendiente

- El flujo con un rol real que tenga `contracts.read` sin `portalusers.manage` (o al revés) solo está cubierto por pruebas de componente; el recorrido Playwright entra como administrador.
- Con el módulo `CLIENT_PORTAL` apagado en una compañía real: solo por prueba de componente (la compañía demo lo tiene encendido).
- El envío real de correo: en Development el «correo» es un registro en el log (`LoggingInvitationSender`); no se probó contra un proveedor de correo (no existe aún).
- Dos corridas simultáneas de Playwright (la de otro lote) borran el directorio `test-results` compartido y rompen la que esté en curso: se corrió con `--output` propio.

## Qué quedó fuera

Las rutas `/portal/*` (portal del cliente, P10), aceptar la invitación en pantalla (`/portal-users/accept-invite`), editar rol o nombre de un usuario de portal, el cotizador dentro de la orden (P4).
