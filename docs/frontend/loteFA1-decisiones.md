# Lote F-A1 — Clientes y contratos: lista, alta y ficha (2026-10-10)

Primer bloque de «Clientes y contratos» en la web (Catálogo → **Clientes**, `/catalog/clients`): lista maestra, alta y ficha/expediente del
cliente. Solo se tocó `web-app/` y `docs/`: **el servidor no cambió** (`openapi.json` y `schema.d.ts` quedan igual; `npm run api:types` no genera
diferencias). Manual de pantallas: [fa1-clientes.md](../manual/frontend/fa1-clientes.md); FAQ: sección «Lote F-A1» de [faq.md](../manual/faq.md).
Referencia de la maqueta: `Diseño/teikem-mockups.html`, `clientes()` y `clientProfileHtml`.

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/clients/`) |
|---|---|---|
| Pantalla | Maestro-detalle en un `SplitPane` (`storageKey="clients"`, ~27 %): panel **Clientes** (contador, buscador `QBox` que consulta al API con `search` tras 250 ms, «Mostrar inactivos» = `includeInactive`) y la ficha a la derecha. Cada fila: nombre, código, `StatusChip`, chip «Inactivo» si `!isActive` y `billingSummary`. Selección en la URL `?client=<publicId>` (sin ella, el primero). Bajo 720 px la ficha queda debajo y se lleva a la vista. | `ClientListScreen.tsx` |
| Alta | Modal **Nuevo cliente** (`clients.create`): nombre (obligatorio), código (vacío = lo genera el servidor), razón social, identificación fiscal, término de pago, moneda, límite de crédito y el interruptor **Crear contrato inicial** (encendido; «Cliente desde» = `tenantToday()`, título «Contrato marco»). Errores del servidor por campo (incluido `contract.*`); el 409 se repite bajo el Código. Al crear, el cliente queda elegido. | `ClientCreateModal.tsx` |
| Ficha | Paneles apilados (no pestañas) en el orden de la maqueta: cabecera → perfil → teléfonos y correos → personas → numeración → campos personalizados → contratos → historial. | `ClientDetailPanel.tsx` y un archivo por panel |
| Cabecera | Nombre, código, `StatusPipeline` (`ClientStatus` / `CLIENT`, transición por `POST …/status`) y **Dar de baja / Reactivar** con `ConfirmDialog` (`clients.update`). | `ClientDetailPanel.tsx` |
| Perfil | Nombre (solo lectura), razón social, identificación fiscal, límite, término, moneda, punto de recogido por defecto (con «quitar» = `clearDefaultPickup`); direcciones física y postal **solo lectura**. `PATCH …/profile` con `rowVersion`; el 409 muestra el mensaje exacto del servidor y relee la ficha (se conservan los campos que el usuario tocó). | `ClientProfilePanel.tsx` |
| Teléfonos y correos | `ContactPoint` del propio cliente: se leen de la ficha (`contactPoints`) y se escriben por `/api/v1/contacts/CLIENT/{id}` (alta), `PUT/DELETE /contacts/{id}`. Máscara de la compañía (`PhoneInput`) y `normalizePhone` al guardar; un principal por tipo (lo resuelve el servidor). | `ClientContactPointsPanel.tsx` |
| Personas de contacto | Tabla Nombre · Puesto · Teléfono · Correo · principal; alta y edición en modal; **Quitar** = `PATCH {isActive:false}`; un solo principal activo. Teléfono y correo de la persona son `ContactPoint` de `CLIENT_CONTACT`. | `ClientPeoplePanel.tsx` |
| Numeración | ¿Quién asigna el número de orden / de factura? (El cliente / Teikem = `clientAssigns*` false) y los tres patrones con **Ejemplo** en vivo (`GET /clients/number-format/preview` con pausa de 300 ms; mientras responde, los `*Preview` de la ficha o el cálculo local). Ayuda «@ = letras, # = números»; vacío = patrón por defecto. `PATCH …/number-settings`. | `ClientNumberingPanel.tsx` |
| Campos personalizados | `CustomFieldsForm entityType="CLIENT"` + `useSaveCustomFields`; el panel no se pinta con el módulo apagado o sin definiciones. | `ClientCustomFieldsPanel.tsx` |
| Contratos | **Solo lectura**: tabla con número/título, estatus y vigencia, el `billingSummary` y la nota «se administran en el siguiente bloque». | `ClientContractsPanel.tsx` |
| Historial | `StatusHistory entityType="CLIENT"`. | `ClientDetailPanel.tsx` |
| Hooks | `useClients`, `useClient`, `useNumberPreview`, `useCreateClient`, `useUpdateClientProfile`, `useUpdateNumberSettings`, `useTransitionClient`, `useSetClientActive`, `useSaveClientContact`, `useSaveContactPoint`; cada escritura deja la ficha devuelta en caché e invalida la lista. | `api.ts` |
| Lógica pura | Espejo de `NumberFormat` (`patternIssue`, `resolvePattern`), esquemas zod espejo del servidor, armado de los requests (alta, perfil, numeración, medios de contacto), orden de contactos, línea de dirección. | `clientRules.ts` |
| Textos | `clients.*` en `es.json` y `en.json`. | `kernel/i18n/` |
| Ruta | `/catalog/clients` pasó de `pending(...)` a pantalla real (mismo `perm` `clients.read`, módulo `CATALOG`, mismo lugar del menú). Se ajustaron `navigation.test.ts` y `Placeholder.test.tsx`, que ahora usan Consignatarios como ejemplo de ruta pendiente. | `app/routes.tsx` |

## Cómo se probó

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: tipos generados sin cambios, tsc, oxlint (solo los avisos que ya existían; ninguno en `features/clients`), vitest **148 archivos / 1439 pruebas** (2 archivos nuevos: 14 + 27 pruebas), build |
| `clientRules.test.ts` (14) | patrones (válidos, vacío, 41 caracteres, sin `#`, carácter no permitido, `Á`), `resolvePattern` (relleno, sobrantes, `@`), patrón por defecto, request de numeración, alta (valores iniciales, validaciones, request con y sin contrato), perfil (punto de recogido, `clearDefaultPickup`, PATCH tal cual), línea de dirección, orden de contactos y puntos, normalización de teléfono y correo |
| `ClientListScreen.test.tsx` (27) sobre un `fetch` simulado | lista (activos por omisión, búsqueda al API, inactivos, selección por la URL); alta (éxito con contrato por omisión y selección, sin contrato, validaciones en pantalla, 409 de código, 400 por campo incluido `contract.title`, sin `clients.create`); ficha (orden de paneles, perfil con `rowVersion`, 409 de `rowVersion` con relectura, límite vacío/negativo, teléfonos y correos, personas, edición de teléfono de una persona, numeración con ejemplo en vivo, 400 del ejemplo, patrón inválido, 400 del guardado, contratos de solo lectura, baja, reactivación, baja rechazada, transición de estatus, solo lectura, sin `contacts.manage`) |

Sin recorrido Playwright en este lote: no hay un precedente corto (los recorridos de `web-app/e2e` siembran datos por API y capturan pantalla); queda
para el cierre del bloque F-A2, cuando la ficha incluya los contratos.

## Decisiones a revisar (desajustes maqueta ↔ servidor)

1. **Direcciones solo lectura.** La maqueta las edita en la ficha; el servidor las modela como `Location` (corporativa, postal, de recogido) y su alta/edición
   pertenece al bloque de **Consignatarios/localizaciones**. Hasta entonces se muestran (física y postal; postal nula = «La dirección postal es la misma que la física»).
2. **SLA y «Cliente desde» pasan al contrato.** En la maqueta eran campos del cliente; en el servidor el SLA es **por tipo de servicio y en horas** y «Cliente desde» es
   la fecha de inicio del contrato. En el alta, «Cliente desde» es `contract.startDate` y el SLA no se pide (va en el bloque de contratos).
3. **Estado Activo/Inactivo de la maqueta = estatus + baja.** El servidor tiene dos cosas: el **estatus** del catálogo `ClientStatus` (pipeline editable por la compañía,
   con `StatusPipeline`) y la **baja lógica** (`isActive`, `deactivate`/`reactivate`). La lista muestra ambos (chip de estatus y chip «Inactivo»).
4. **Teléfonos y correos del cliente se leen de la ficha** (`contactPoints`) y no de `GET /contacts/CLIENT/{id}`: el dato ya viene en `ClientDetailDto` y así la lectura
   no exige otro permiso. La escritura sí usa `/contacts/...`.
5. **Escribir teléfonos y correos pide `contacts.manage` y también `clients.update`**: el servidor exige el permiso de la entidad dueña (`OwnerWritePermission`). Los botones
   se pintan solo con ambos.
6. **Edición de teléfono/correo de una persona**: `ClientContactUpdateRequest` no los trae; se sincronizan como `ContactPoint` de `CLIENT_CONTACT` (crear, `PUT` o `DELETE` según
   el valor). Sin `contacts.manage` el modal edita solo nombre, puesto y principal.
7. **Numeración «Teikem asigna» = `clientAssigns* = false`.** El paquete siempre lo genera Teikem. Un patrón vacío se manda como `""` (vuelve al del sistema).
8. **Búsqueda de la lista al servidor** (código, nombre y razón social), no filtro local, como pidió el dueño. El listado del API no pagina, por eso es una lista y no un `DataTable`
   (no lleva `exportRows`); las tablas internas (personas, contratos) son pequeñas, sin paginación ni exportación.

## Qué quedó fuera (F-A2 y siguientes)

Contratos (alta, edición, vigencia, renovación), niveles de servicio (SLA por tipo de servicio, en horas), tarifas, cargo por COD, cargo por despacho, pieza extra y
servicios especiales por cliente; usuarios del portal del cliente (`/clients/{id}/portal-users`); alta y edición de direcciones/localizaciones; exportar la lista de clientes.
