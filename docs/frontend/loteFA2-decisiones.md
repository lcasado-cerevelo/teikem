# Lote F-A2 — Contratos del cliente: Contrato, Tarifas, SLA y Servicios especiales (2026-10-10)

Segundo bloque de «Clientes y contratos» en la web, encima de [F-A1](loteFA1-decisiones.md). Solo se tocó `web-app/` y `docs/`: **el servidor no cambió** (`openapi.json` y
`schema.d.ts` quedan igual; `npm run api:types` no genera diferencias). Manual de pantallas: [fa2-contratos.md](../manual/frontend/fa2-contratos.md); FAQ: sección «Lote F-A2» de
[faq.md](../manual/faq.md). Referencia: manual 02 (caps. 4 a 6 y 8), `ContractsController`, `ContractRatesController`, `SpecialServicesController` y la maqueta (`clientes()`).

**Lo ya hecho en F-A1 no cambió** (lista, cabecera, perfil, teléfonos, personas, numeración, campos personalizados, historial): solo se reemplazó el panel de solo lectura
`ClientContractsPanel` por una sección con pestañas dentro de la misma ficha, con los mismos paneles y estilos del kit.

## Qué se construyó (`web-app/src/features/clients/`)

| Pieza | Qué hace | Archivo |
|---|---|---|
| Sección | Panel **Contratos** con `Tabs` (Contrato · Tarifas · SLA · Servicios especiales), resumen «Facturación:», selector de contrato (por omisión `currentContract`) y **Nuevo contrato** (`contracts.create`). Sin `contracts.read` solo avisa y muestra el resumen (no pide nada de contratos). | `ClientContractsSection.tsx` |
| Nuevo contrato | Modal `POST /contracts`: título, número (vacío = automático), «Cliente desde» (hoy), fecha fin, renovación, moneda (lookup `Currency`), disparador (lookup `BillingModel`), notas. Sin SLA. El contrato nuevo queda elegido. | `ContractCreateModal.tsx` |
| Pestaña Contrato | `StatusPipeline` `ContractStatus`/`CONTRACT` (transición por `POST …/status`); datos generales (`PATCH` con `rowVersion`, `clearEndDate`, solo lo cambiado); modelo de facturación (los 5 checks por `PATCH billing-model`, y con su check marcado el cargo por despacho `PATCH dispatch-fee` y el de COD FIXED/PERCENT `PATCH cod-fee`); `StatusHistory`. | `ContractTab.tsx` |
| Pestaña SLA | Borrador local, un renglón por `ServiceType` con 4 campos (horas máximas de tránsito, ventana de recogido en min, meta de puntualidad %, penalidad); empieza vacío; **Guardar SLA** = `PUT service-levels` con los renglones con datos; **Descartar cambios**. | `ContractSlaTab.tsx` |
| Pestaña Tarifas | Por servicio (servicio + paquete + tarifa) y pieza extra por tramos; alta con «Vigente desde» (hoy por omisión), **Editar** = versión nueva, **Quitar** = cerrar, **Ver historial**; secciones visibles según el check del modelo de facturación (filas existentes siguen visibles, sin escrituras). | `ContractRatesTab.tsx` |
| Pestaña Servicios especiales | Tabla del cliente con alta (tipo existente o «+ Nuevo tipo de servicio especial…» con nombre), tarifa, editar = versión nueva, quitar = cerrar, **Ver historial**. Sin baja/reactivación de tipos. | `ContractSpecialTab.tsx` |
| Hooks | `useContract`, `useRateComponents`, `useSpecialServices`, `useSpecialServiceTypes` y las escrituras; cada escritura del contrato deja la ficha devuelta en caché e invalida la ficha del cliente y la lista (el `billingSummary` cambia). | `contractApi.ts` |
| Lógica pura | Esquemas zod espejo del servidor, `buildContractPatch`, `planBillingSave`, borrador del SLA y renumeración de sus errores, tramos, servicios especiales, fecha por omisión. | `contractRules.ts` |
| Modal de formulario | `FormModal` (Modal + Form con Cancelar/Guardar en el pie). | `contractUi.tsx` |
| Textos / estilos | `clients.contracts.*`, `clients.sla.*`, `clients.rates.*`, `clients.special.*` en `es.json` y `en.json`; `.cl-ctr-*`, `.cl-sla-*`, `.cl-comp*` en `clients.css`. | `kernel/i18n/`, `clients.css` |

Permisos exactos: ver = `contracts.read`; escribir = `contracts.update` **y** `canEdit` del contrato (el servidor responde 422 en Vencido/Cancelado); crear = `contracts.create`.
La ruta ya estaba bajo `CATALOG` + `clients.read` (F-A1). Los errores del servidor salen siempre con su `title` y los `errors` por campo (`Form`/`applyProblemDetails`); el 409 de
`rowVersion` relee el contrato y conserva lo escrito.

## Cómo se probó

| Prueba | Qué cubre |
|---|---|
| `contractRules.test.ts` (22) | fecha por omisión (UTC vs compañía), contrato por omisión, esquema y PATCH de datos generales (`clearEndDate`), alta sin SLA, validación y plan de guardado del modelo de facturación (monto obligatorio al marcar, por ciento 0–100, desmarcar no manda el monto), SLA (renglones, request solo con datos, errores `serviceLevels[k].campo` renumerados al renglón), `rethrowRenamed`, tramos (rango, `clearToUnit`, texto), servicios especiales |
| `ClientContracts.test.tsx` (43) sobre un `fetch` simulado | sección y permisos (sin `contracts.read` no pide nada, `contracts.create`, cliente sin contratos, selector); nuevo contrato (éxito, validación, 409 de número, 409 de cliente de baja); pestaña Contrato (campos, guardar con `rowVersion`, validación, 400 y 422, 409 con relectura, `canEdit=false`, despacho, COD, desmarcar, transición con 422); SLA (vacío, guardar solo con datos, vaciar, validación, descartar, 400 por renglón, solo lectura); Tarifas (secciones por check, alta, 409, versión nueva y fecha pasada, cerrar, historial, tramos: alta/rango/traslape/edición/cierre, componente, `canEdit`); Servicios especiales (lectura, alta con tipo, tipo nuevo, 409, editar/quitar, componente apagado, sin contrato, error de lectura) |
| `ClientListScreen.test.tsx` | la prueba de contratos de F-A1 ahora comprueba el caso «sin `contracts.read`» (la ficha no se rompe) |
| `e2e/loteFA2-clientes.spec.ts` (Playwright, API real) | F-A1 pendiente: alta por la pantalla, perfil, teléfono, persona, numeración con ejemplo, baja y reactivación, buscador; F-A2: datos, los 5 checks con despacho y COD (y el rechazo de 150 %), transición a Vigente, SLA, tarifas por servicio (409, versión nueva, historial, cierre) y por pieza extra (rango, traslape), servicios especiales (tipo nuevo, 409, versión nueva, cierre), nuevo contrato y el 422 de activar un segundo; sin scroll horizontal a 360 px |
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: tipos generados sin cambios, oxlint sin avisos en `features/clients`, vitest **150 archivos / 1504 pruebas** (3 archivos nuevos: 22 + 43 pruebas y la de F-A1 ajustada), build |
| Playwright `loteFA2-clientes.spec.ts` (`escritorio` y `movil`, API en :5000) | **4/4 pasaron** (F-A1 y F-A2 en cada proyecto; capturas `fa1-*` y `fa2-*` en `docs/manual/frontend/img/`) |

Hallazgo de la prueba: en `useForm({ values, resetOptions: { keepDirtyValues: true } })` el `form.reset(x)` explícito hereda `keepDirtyValues` y no limpia lo escrito; en F-A2 los
`reset` van con `{ keepDirtyValues: false }` (anotado en KIT.md). Los paneles de F-A1 siguen como estaban.

## Decisiones tomadas (a revisar)

1. **Interruptores en vez de casillas.** Los 5 checks del modelo de facturación son `Toggle` del kit (la maqueta usa casillas); su nombre accesible es el texto de la maqueta.
2. **«Vigente desde» = hoy**, pero «hoy» es el día **más reciente** entre la zona de la compañía y UTC: el servidor valida con su fecha UTC y rechaza fechas pasadas en versiones nuevas
   y cierres, así que de noche en Puerto Rico (UTC−4) el «hoy» local ya sería «ayer» para él. En una versión nueva la fecha no puede ser anterior al día UTC (error en pantalla con el
   mismo texto del servidor); el alta de una tarifa nueva acepta fechas pasadas. **Quitar** manda el cierre sin fecha (el servidor usa hoy).
3. **Monto obligatorio al marcar despacho/COD.** El servidor acepta marcar sin monto; la pantalla lo pide al encender el interruptor (un contrato que ya estaba así no se bloquea).
4. **Desmarcar no manda el monto** y no lo borra (regla R7b del servidor); el monto reaparece al volver a marcar.
5. **Pieza extra en dos pasos.** «Agregar pieza extra» crea el bloque (servicio + paquete, sin monto) y los tramos se agregan después con «Agregar tramo» (así lo modela el API). Quitar
   el bloque cierra también sus tramos abiertos.
6. **Servicios especiales por cliente, no por contrato.** La pestaña no usa el selector: el API los cuelga del contrato vigente. Se escribe solo con contrato vigente, componente
   encendido y `canEdit` del vigente; si no, las filas se ven con el aviso que corresponde. Sin baja ni reactivación de tipos (decisión del dueño).
7. **Selector de contrato**: marca «(contrato vigente)» el que devuelve `currentContract` (activo o, si no hay, el borrador más reciente). Tras crear un contrato, queda elegido cuando la
   ficha se relee (un instante).
8. **Sin cotización** (decisión del dueño): `POST /billing/contract-rate-quote` no se usa; se hará con las órdenes de venta.
9. **Sin paginación ni exportación** en las tablas internas (pocas filas): `pagination={false}`, `exportable={false}`, tarjetas bajo 640 px de ancho del panel.
10. **Estatus del contrato**: la etiqueta de `ACTIVE` en el sembrado es «Vigente» (no «Activo»); los textos de la pantalla usan siempre las etiquetas del catálogo de la compañía.

## No pude verificar / pendiente

- El flujo con un rol real que tenga `clients.read` sin `contracts.read` solo está cubierto por prueba de componente (el recorrido Playwright entra como administrador).
- Que el servidor acepte `POST …/close` sin cuerpo: la pantalla manda `{}`; el controlador lo permite en cualquier caso (`EmptyBodyBehavior.Allow`).

## Qué quedó fuera

Cotización (`/billing/contract-rate-quote`), baja/reactivación de tipos de servicio especial, usuarios del portal del cliente, alta y edición de direcciones/localizaciones, exportar la
lista de clientes.
