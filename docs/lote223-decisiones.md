# Lote 223 — Pedido de vendedores, P3: lo que el pedido guiado (web) necesita del API (2026-11-07)

Parte P3 de [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) (§1.5, §3 fila P3, §4.1 fila P3). Solo backend; la pantalla
(`features/sales/wizard/*`, ruta `/sales-orders/new-order`) va en su parte de frontend. Sin cambios de base: las columnas (`SignatureFileId`, `AuthorizedByName`,
`SubmittedWithAuth`) ya las puso el Lote 222.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Borrador por pasos | `SalesOrderRequest.Step` (CLIENT, PRODUCTS, DELIVERY, AUTHORIZATION; vacío = guardado de siempre) en `POST /sales-orders` y `PATCH /sales-orders/{id}`. CLIENT guarda el borrador **sin productos**; desde PRODUCTS hace falta al menos uno (400 «Agregue al menos un producto a la orden.»); en DELIVERY y AUTHORIZATION un pedido marcado `DELIVERY` sin consignatario es 400 «Escoja a dónde se entrega el pedido o márquelo como recogido.». Paso desconocido: 400 «El paso del pedido debe ser CLIENT, PRODUCTS, DELIVERY o AUTHORIZATION.». Reglas puras en `Domain/Sales/SalesDraftSteps.cs`. |
| Solicitar sin productos | `POST /request` (y enviar) de un borrador sin líneas: 400 «Agregue al menos un producto a la orden.» (antes no podía existir). |
| Firma de quien autoriza | `POST /sales-orders/{id}/signature` (multipart: `file` PNG/JPG + `authorizedByName` opcional; sales.create; solo borrador, 422) → StoredFile SIGNATURE cifrado de la orden, ligado a `SignatureFileId`. `POST /sales-orders/{id}/signature/remove` la desliga (POST: el controlador de órdenes de venta no tiene DELETE). Se ve por `GET /files/{terms.signatureFilePublicId}` con sales.view (`StoredFileService`: dueño SALES_ORDER = sales.view / sales.create; `SalesOrderOwnedEntityResolver`). Mensajes: 400 «Dibuje la firma de quien autoriza el pedido.», 400 «La firma debe ser una imagen PNG o JPG.». |
| Enviar pedido | `POST /sales-orders/{id}/submit` (sales.create) con `SalesOrderSubmitRequest(authMethod, password, mfaCode, allowBackorder, overrideCredit, comment)`. Valida la orden **antes** de la contraseña (borrador, productos, a dónde entregar, la firma que exige el cliente), reautentica y solicita (`RequestAsync` con `submittedWithAuth`): inventario (409 / allowBackorder), crédito, reserva; un prospecto queda `AwaitingClientApproval`. Deja `SubmittedWithAuth` y un SecurityEvent REAUTH `stage = sales_submit` (método, orden, sesión). Servicio `SalesDraftService`. |
| PASSWORD | Por `AuthService.ReauthAsync`: misma contraseña del usuario, mismo bloqueo por cuenta, **MFA si lo tiene** (TOTP confirmado), renueva el AAL2 de la sesión. Sin contraseña 400 «Escriba su contraseña para enviar el pedido.»; contraseña o código malos = **400** «Contraseña incorrecta.» / «Código MFA inválido.» (campo `password` / `mfaCode`). |
| BIOMETRIC (para P4) | El teléfono valida la huella; el servidor exige que el access token traiga una verificación AAL2 reciente (contraseña + MFA al entrar o `POST /auth/reauth`) dentro de `Tenant.Aal2WindowMinutes` (30 si no hay): si no, **403 `aal2_required`** (el cliente web/app ya sabe pedir la reautenticación con ese código). La huella nunca se salta el MFA. |

## Decisiones (para que Luis las ratifique)
1. **El paso no se guarda** en la base: solo dice qué validar. La pantalla deduce dónde retomar un borrador (sin productos → paso 2, etc.). Sin columna nueva.
2. **El guardado sin paso no cambia**: la ventana «Nueva orden» sigue exigiendo productos al crear y al mandar líneas.
3. **«Entrega» explícita exige consignatario** solo en los pasos DELIVERY/AUTHORIZATION y al enviar; `DeliveryMode` NULL sigue deduciéndose del consignatario (Lote 222). La ventana de siempre deja guardarlo (y solicitarlo por `/request`).
4. **Contraseña mala = 400, no 401**: el cliente del API refresca el token ante un 401 y repetiría la petición (dos intentos contra el bloqueo). El intento sí cuenta para el bloqueo de la cuenta.
5. **Se valida la orden antes de la contraseña** (firma exigida, consignatario, productos, estatus): no se gasta un intento en un pedido que no se puede enviar. Si falla el inventario (409) **después** de la contraseña, la pantalla repite con `allowBackorder` y la misma contraseña que tiene en memoria (no se devuelve un token nuevo).
6. **La firma reemplazada o quitada se desliga, no se borra** (como las fotos del Lote 221). Solo PNG/JPG (lo que dibujan los lienzos). Nombre: `authorizedByName` ausente = sin cambio, "" = quitarlo, hasta 150.
7. **`/request` sigue existiendo sin reautenticación** (`SubmittedWithAuth` = null): la oficina con «Nueva orden» no cambia. El pedido guiado usa `/submit`.
8. **«Desde qué teléfono»**: queda la sesión (RefreshToken) en el SecurityEvent `sales_submit`; la sesión dice el aparato.

## Cómo se probó
`SalesDraftStepTests` (xunit, InMemory, con Identity + `AuthService` reales): borrador por pasos (cliente sin productos, productos, entrega con/sin consignatario, autorización, paso inválido, volver al cliente vaciando el carrito); firma como archivo (StoredFile SIGNATURE cifrado del dueño SALES_ORDER, se ve con sales.view, reemplazo, quitar, PNG/JPG solamente, solo en borrador); enviar exige reautenticación (sin método, método inválido, sin contraseña, contraseña mala = 400 y cuenta para el bloqueo, buena = REQUESTED + PASSWORD + AAL2 de la sesión + SecurityEvent; MFA no se salta; BIOMETRIC sin AAL2 o vencido = 403, reciente = BIOMETRIC; prospecto espera aprobación; faltante 409 y reintento con backorder; la firma exigida y el consignatario antes de la contraseña). Resultado en el reporte de la parte.

## Pendiente / para otras partes
- Frontend (P3): pantallas del pedido guiado; claves i18n de los mensajes nuevos.
- P4 (app): mandar `authMethod: BIOMETRIC` después de la huella; si responde `aal2_required`, pedir contraseña (+MFA) con `/auth/reauth` y repetir.
- Manual (capítulo 18) y FAQ: los mensajes exactos están en `SalesDraftStepRules`.
