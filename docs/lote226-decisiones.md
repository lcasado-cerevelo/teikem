# Lote 226 — Pedido de vendedores, P4: el pedido en la app móvil del personal (`app-movil`) (2026-11-07)

Parte P4 de [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) (§1.5, §1.6, §3 fila P4, §4.1 fila P4). Solo `app-movil`: sin cambios en el API,
la base, `web-app` ni `shared/`. Usa los contratos de los Lotes 221 (catálogo), 222 (orden, envío, prospecto) y 223 (borrador por pasos, firma, `/submit`).

## Qué se construyó
| Pieza | Dónde |
|---|---|
| Home → **Pedidos** (con `sales.create`; `sales.create` entra en `STAFF_PERMS`) | `src/app/(auth)/(tabs)/index.tsx`, `src/hooks/access.ts` |
| **Mis pedidos**: compañía (si hay varias), estatus (Todos, Borradores, Solicitados, Parciales, Despachados, Cancelados), buscador, distintivos (urgente, cliente por aprobar, consignación, recogido, backorder). Un borrador se abre en el pedido donde se quedó; lo demás en una hoja de solo lectura (líneas y totales) | `src/app/(auth)/SalesOrders/index.tsx` |
| **Nuevo pedido** con los 4 pasos, barra de progreso y borrador en el servidor en cada «Siguiente» (POST/PATCH con `step`); cliente con buscador y alta mínima (con fotos de cámara o galería), urgente + nota, forma de pago prellenada con la marca «cambió»; productos por marca con ofertas (foto, + / −), lista con cantidad y carrito fijo; entrega/recogido con resumen y envío calculado por el servidor; autorización con nombre y **firma con el dedo** (el mismo `react-native-signature-canvas` del chofer, fuera del ScrollView); **Enviar pedido** con la huella/rostro y, si no, contraseña (+MFA); hoja de faltante/crédito; pantalla final OV-#####, total, «Nuevo pedido» | `src/app/(auth)/NewSalesOrder/index.tsx`, `src/components/sales/*` |
| Llamadas al API | `src/services/salesApi.ts` |
| Reglas puras | `src/utils/salesOrderRules.ts` |
| `silentForbidden` en la configuración de axios (un 403 de esas llamadas lo muestra la pantalla, sin el aviso general) | `src/config/axios.ts` |
| Textos es/en (`SALES_ORDER.*`) | `src/lang/es.json`, `src/lang/en.json` |
| Pruebas | `scripts/test-sales-order.mjs` (`npm run test:sales`, dentro de `npm test`) |

## Decisiones (para que Luis las ratifique)
1. **Reglas copiadas, no compartidas (DEUDA).** `salesOrderRules.ts` es copia de `web-app/.../wizard/wizardRules.ts` y de las funciones de `salesRules.ts` que usa. No se movieron a
   `shared/` (el patrón existe: `shared/kemi`) porque otra parte estaba editando `web-app/src/features/sales` al mismo tiempo. La prueba repite caso por caso
   `wizardRules.test.ts` para que no se separen. Pendiente: extraerlas a `shared/sales/` y que las dos apps las importen.
2. **Pruebas con node, no jest.** `app-movil` no tiene jest ni Testing Library (sus pruebas son `node --experimental-strip-types scripts/test-*.mjs`, y eso corre el CI);
   no se agregó otra infraestructura de pruebas. Lo de pantalla que se prueba sin pintar: que es/en tengan las mismas claves, que existan todas las claves que las
   pantallas usan (también las que se arman con un código) y que los mensajes que repiten al servidor sean exactos. Los recorridos en un teléfono quedan para P8.
3. **Reautenticación al enviar**: se reusa `authenticate()`/`isBiometricAvailable()` de `biometricAuth.ts` (lo mismo que usa el `LockScreen`), con el PIN del teléfono como
   respaldo; con la huella se manda `authMethod=BIOMETRIC`. Se pide la contraseña (`authMethod=PASSWORD`, más el código si `/me` dice `mfaEnabled` o el servidor lo
   pide) si el teléfono no tiene huella, si la huella no se confirma (o se cancela) o si el servidor responde **403 `aal2_required`** (la verificación de la sesión venció).
   No se usa `/auth/reauth`: `/submit` ya valida la contraseña y el MFA, y una contraseña mala es 400 (no 401), así que el reintento de sesión de axios no la repite.
4. **«Mis pedidos» sin filtro del servidor (DEUDA).** El API no tiene «las que capturé yo»; la lista trae `requestedByName` (quien creó la orden) y la app la compara con el
   nombre o el correo de la sesión, sobre las 200 más nuevas del filtro. Dos personas con el mismo nombre se verían juntas. Pendiente: `GET /sales-orders?mine=true`.
   Ver la lista exige además `sales.view` (lo trae el rol Ventas); sin él la pantalla deja hacer el pedido nuevo y lo dice.
5. **Sin modo sin señal** (lo dice el plan): sin señal no se avanza y se avisa «Se necesita señal»; lo guardado queda como borrador.
6. **Lo que la app no hace todavía** (se hace en la web): ajustar las piezas de un kit ajustable (se pide con las del catálogo), crear un consignatario nuevo (con
   `locations.read` se escoge otro de los del cliente), escoger el pueblo de una lista (se escribe; el servidor no lo valida, decisión 9 del Lote 222) y modificar una
   orden ya solicitada. La fecha requerida se escribe AAAA-MM-DD y se valida antes de guardar.
7. **La firma**: cada trazo deja la firma lista (`onEnd` → `readSignature`); al volver al paso se ve como imagen con «Firmar de nuevo». Sube como PNG en multipart con un
   `data:` URI (en la web de prueba, como Blob). Si se borra la que estaba guardada, se quita (`/signature/remove`).
8. **Varias compañías**: la de la pantalla es la primera con `sales.create` o la escogida en los botones de compañía; se puede cambiar solo antes del primer guardado.
9. Los mensajes de error de los pasos son los mismos del servidor (en «usted»); los textos propios de la app van en «tú», como el resto de la app.

## Cómo se probó (resultados literales)
- `npm install --legacy-peer-deps` (como el CI; `package-lock.json` no se versiona).
- `npx tsc --noEmit`: sin errores.
- `npx eslint` sobre los archivos de este lote: `✖ 1 problem (0 errors, 1 warning)` (el aviso es de `axios.ts`, de antes). `npx expo lint` completo: `✖ 37 problems (7 errors, 30 warnings)`,
  todos en archivos que este lote no tocó (p. ej. `useRouteTracking.ts`); el CI no corre el lint.
- `npm test`: todas `ok`, incluida `test-sales-order: ok`. `node scripts/check-theme-contrast.mjs`: «Contraste AA correcto en ambos temas.»
- `npx expo export --platform web` (como el CI): `Exported`.
- **No probado**: en un teléfono o emulador contra un API (huella, cámara, subida multipart de la firma y las fotos); queda para el recorrido de P8.

## Pendiente
- Extraer las reglas a `shared/sales/` (decisión 1) y el filtro `mine` del API (decisión 4).
- En el inicio de la app, «Necesita tu atención» todavía muestra los códigos nuevos del Lote 222 (`SALES_CLIENT_PENDING`, `SALES_ORDER_URGENT`) como código y cantidad.
- Manual de pantallas de la app (capítulo del pedido con capturas) y FAQ: en el cierre (P8).
