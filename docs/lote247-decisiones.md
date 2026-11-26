# Lote 247 — Precios especiales y ofertas por cliente y por cadena (Lote C del plan de Solutions): qué se construyó y decisiones a revisar

Fecha: 2026-11-26. Módulo: Órdenes de venta (`SALES_ORDERS`). Permisos nuevos: ninguno (se usan `sales.view` y `sales.pricelist`; el conteo sigue en 163). Fuente funcional: `docs/plan-rutas-cadenas-precios-ivu-modificaciones.md` §3.2 (el documento maestro no habla del Lote C y no se editó, como en los lotes 241 y 245).

> Este documento lo escribió un agente que **no corrió** build, pruebas, servidores ni el smoke: leyó el diff sin commitear, el diseño aprobado (`planC.json`) y las capturas. Los resultados de «Cómo se prueba» son los que le pasó quien encargó el cierre. Donde el código difiere del diseño, manda el código y se dice abajo.

> **Pedido del dueño (por confirmar con el contable):** un precio especial o de oferta es un precio **antes del IVU**. Las exenciones del cliente (certificado estatal, municipal, los dos, o exención total) se aplican **después**, sobre el precio que ganó. El lote **no cambia el cálculo del impuesto**. La respuesta del contable no bloquea nada.

## Mapa de lo construido

| Capa | Tablas | Código principal | Endpoints |
|---|---|---|---|
| Cadenas en listas de precios | `PriceListChain` (nueva; FK compuestas a la lista y a `ClientChain`; `UX_PriceListChain_Chain`), `UQ_PriceList_IdTenant` | `PriceListChain` (`[AuditEntity(PRICE_LIST)]`), `PriceListRules.BestByLevel`, `SameAudience`, `PriceListService.AddChainsAsync` / `RemoveChainAsync`, tope `PriceListCodes.MaxChains = 500` | `POST /api/v1/price-lists/{id}/chains`, `DELETE /api/v1/price-lists/{id}/chains/{chainPublicId}` (`sales.pricelist`); `GET /api/v1/price-lists?includeSpecial=` (`sales.view`) |
| Listas dedicadas («precio especial») | `PriceList.SpecialClientId` / `SpecialChainId` (FK compuestas, `CK_PriceList_SpecialOwner`, `UX_PriceList_SpecialClient` / `UX_PriceList_SpecialChain`) | `PriceList.IsSpecial`, `PriceListRules.DedicatedLocked`, `PriceListService.SpecialPrices.cs` (clase parcial: no hay servicio nuevo en DI) | — (se ven con `includeSpecial`) |
| «Precios especiales» (ficha del cliente y de la cadena) | las mismas listas dedicadas | `SpecialPriceRules`, `SpecialPriceContracts`, `PriceListService.SpecialPrices.cs` (`Get…`, `Add…`, `Update…`, `Remove…` para cliente y cadena) | `GET/POST /api/v1/special-prices/clients/{id}` y `/chains/{id}`, `PATCH/DELETE …/{itemId}` (`SpecialPricesController`, nuevo; ver con `sales.view` o `sales.pricelist`, escribir con `sales.pricelist`) |
| Ofertas: público y cadenas | `Offer.AudienceMode` (`ALL`/`INCLUDE`/`EXCLUDE`, `CK_Offer_AudienceMode` lo ata a `AppliesToAll`), `OfferChain` (nueva), `UQ_Offer_IdTenant` | `OfferAudienceModes`, `Offer.SetAudienceMode`, `OfferRules.AppliesTo` / `AudiencesOverlap` / `Resolve`, `OfferService.AddChainsAsync` / `RemoveChainAsync` | `POST /api/v1/offers/{id}/chains`, `DELETE /api/v1/offers/{id}/chains/{chainPublicId}` (`sales.pricelist`); `audienceMode` en `POST`/`PATCH /api/v1/offers`; `GET /api/v1/offers?chainPublicId=` (`sales.view`) |
| Regla «gana el más bajo» | — | `PriceListRules.Resolve` y `OfferRules.Resolve` (único sitio de la regla), `PriceListQueries.InEffectAsync` / `OffersInEffectAsync` / `ResolveAsync` con `chainId`, `ClientChainQueries.ActiveChainOfClientAsync` / `ActiveClientChainMapAsync` (una cadena inactiva cuenta como «sin cadena») | catálogo, pedido guiado, orden y portal (todos pasan por la misma resolución) |
| Origen del precio guardado en la orden | `SalesOrderLine.PriceSource`, `PriceListId`, `OfferId`, `PriceChainId`, `ResolvedUnitPrice` (todas NULL en las líneas viejas; FK compuestas a lista, oferta y cadena) | `PriceOriginRules.Copy`, `SalesOrderService.SetPriceOrigin`, `PriceOriginNamesAsync`, `IsManualPrice` | `lines[]` de la orden (`priceSource`, `priceListName`, `offerName`, `priceChainName`, `resolvedUnitPrice`, `isManualPrice`); `linePrices[]` y catálogo con `chainPublicId` / `chainName` |
| Desactivar una cadena | — | `ClientChainService.DeactivationBlockersAsync`, `ClientChainRules.HasPricesOrOffers` | `POST /api/v1/client-chains/{id}/deactivate` (409 nuevo) |
| Fuente de datos | EntityType `SPECIAL_PRICE` (orden 199) | `SpecialPriceDataSource` (registrada en `DependencyInjection`; permiso de lectura `sales.view` en `DataSourceExtraReadPermission`) | Vistas, indicadores y gráficos |
| Web | — | `features/specialPrices` (`SpecialPricesPanel`, prefijo `spx-`), `features/chains/ChainOffersPanel`, `ChainPicker`; cambios en `ChainDetailPanel`, `ClientDetailPanel`, `OfferEditorModal`, `OffersPanel`, `PriceListEditorModal`, `PriceListsTab`, `priceSource.ts`, `PriceNotices`, `OrderModal`, `ProductsStep` | — |
| Migración de datos | — | Sección «2026-11-25 — Lote 247» al final de `Diseño/logistica-db-update.sql`: solo agrega, idempotente. Marca `L247_OFFER_AUDIENCE` (las ofertas con `AppliesToAll = 0` pasan a `INCLUDE`; las de `AppliesToAll = 1` se quedan en `ALL`) | — |
| CI | — | `scripts/smoke-precios-especiales.sh` (se niega a correr con correo real) y una línea en `.github/workflows/ci.yml` después de `smoke-cadenas.sh` | — |

`TenantId` sale de la sesión. Todo endpoint nuevo lleva `[RequireModule(ModuleKeys.SalesOrders)]` y `[RequirePermission]`. Un cliente, una cadena, una lista o una oferta de otra compañía responde 404, sin decir si existe. `estructura.sql` y `seed.sql` no se tocaron.

Reglas que se aplicaron:
- **Gana siempre el precio más bajo** entre el precio del cliente, el de su cadena (si está activa), la oferta más barata que le aplique y la lista general. Empate: cliente, cadena, oferta, lista general. El precio de venta del producto **no compite**: solo se usa si ninguno de los cuatro trae el producto.
- **Un precio especial más caro que la lista general no bloquea**: se cobra el de la lista general y sale el aviso `SPECIAL_ABOVE_GENERAL`.
- **Una lista dedicada por dueño y por vigencia.** La pantalla «Precios especiales» junta los renglones; cambiar las fechas de uno lo pasa a la lista de esas fechas; la lista que se queda sin renglones se desactiva sola.
- **Ninguna acción nueva cambia un estatus** (no hay `StatusService` ni `IStatusTransitionEffect`): las listas y las ofertas siguen con sus estados derivados (borrador, programada, vigente, vencida).
- **Contratos aditivos:** todo parámetro o campo nuevo de records y métodos va al final y con valor por omisión.

## Cómo se prueba

Resultados reportados (no corridos por quien escribe este documento):

| Qué | Resultado |
|---|---|
| `dotnet build Teikem.sln` y `dotnet test Teikem.sln` con SQL Server real | **6499 pruebas, 0 omitidas, todas en verde** (incluidas las que se omiten sin `TEIKEM_TEST_SQLSERVER`: la sección SQL corrida dos veces, las FK compuestas y la alta simultánea) |
| `npm run check` en `web-app` | Verde salvo **1 falla preexistente y ajena al lote: `DispatchMap.test.tsx`** |
| `npm run check` en `app-cliente` | **No se informó resultado** (su `schema.d.ts` solo se regenera y solo suma campos) |
| Playwright `web-app/e2e/lote247-precios-especiales.spec.ts`, API de pruebas **sin correo** (`Brevo__ApiKey` vacía, `Auth__Onboarding__Enabled=false`) | **4 corridas en verde: es/claro y en/oscuro a 1440 px; es/oscuro y en/claro a 390 px.** Comprueban filas de 56 px o menos, sin scroll horizontal de la página y paneles que no se desbordan. Capturas en `docs/manual/frontend/img/l247-*.png`, abiertas y vistas por quien escribe este documento |
| `scripts/smoke-precios-especiales.sh` | **No se corrió aquí.** Lo arbitra el CI (paso nuevo en `.github/workflows/ci.yml`, después de `smoke-cadenas.sh`) |
| `db-update` dos veces seguidas con el CLI | No se probó aquí. La prueba dotnet `ChainPricingSqlTests` corre la sección dos veces contra SQL Server real |
| Impuestos | `SpecialPriceTaxExemptionTests` fija **siete casos con cifras exactas** (ver «Pedido del dueño» abajo) |

Pruebas dotnet nuevas (en `tests/Teikem.Tests`): `ChainPricingRulesTests`, `ChainPricingPropertyTests` (equivalencia aleatoria con semilla 247 y fuerza bruta del cruce de públicos), `ChainPricingSqlTests`, `ChainPriceResolutionTests`, `SalesLinePriceOriginTests`, `SpecialPriceTaxExemptionTests`, `OfferAudienceServiceTests`, `SpecialPriceServiceTests`, `PriceListChainTests`, `SpecialPricesControllerSecurityTests`, `ClientChainDeactivationPricesTests`, `SpecialPriceDataSourceTests`, `SpecialPriceConcurrencySqlTests`. Cambiadas: `ClientChainQueriesTests`, `PriceListRulesTests`. Pruebas de web nuevas: `specialPriceRules.test.ts`, `SpecialPricesPanel.test.tsx`, `ChainOffersPanel.test.tsx`; ajustadas: `offerRules.test.ts`, `priceListRules.test.ts`, `OffersScreen.test.tsx`, `PriceListsTab.test.tsx`, `ProductsStep.offers.test.tsx`, `OrderModal.test.tsx`.

Para repetirlo a mano (API **sin** llave de Brevo: `scripts/api-para-pruebas.ps1`, con `Auth__Onboarding__Enabled=false`; nunca contra TEST ni producción):

1. `dotnet run --project src/Teikem.Api -- db-update` dos veces. La segunda no debe cambiar nada.
2. Listas → Cadenas → una cadena con dos clientes → panel **«Precios especiales»**: escoger un producto, precio 8, «Desde» hoy, sin «Hasta» → «Agregar precio especial». La fila sale «Vigente» y «Se cobra hoy» dice «Cadena {nombre}».
3. Una fila con un precio mayor que el de la lista general: sale el chip de aviso y el recuadro «Avisos de precios especiales»; «Se cobra hoy» dice «Lista general».
4. Ficha de un cliente de esa cadena: su panel «Precios especiales» muestra, debajo, **«De su cadena «…»»** en solo lectura. Agregarle un precio propio más bajo: «Se cobra hoy» dice «Precio especial».
5. Agregar un precio con «Hasta» de ayer: 400 «La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.».
6. Agregar el mismo producto con fechas que se cruzan con otro precio del mismo dueño: 409 «La lista se cruza con «…» en las mismas fechas y para los mismos clientes o cadenas en: …».
7. Contabilidad → Precios y ofertas: las listas «Precio especial — …» no salen; con **«Ver también los precios especiales»** salen con el chip «Precio especial de …». Abrir una: nombre y fechas bloqueados, nota con «Abrir la ficha»; por API, `PATCH` del nombre da 422 «Esta lista es el precio especial de «…»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha.».
8. Una oferta con las cajas **«Incluir»** y **«Excluir»** (clientes y cadenas): llenar una deshabilita la otra; quitar el último pide confirmar «La oferta quedará para todos los clientes.».
9. Crear un borrador de orden para el cliente de la cadena: cada línea muestra su origen («Cadena …», «Oferta «…»», sin chip si es lista general). Solicitarla y volver a abrirla: el origen se conserva aunque después cambie la lista.
10. Quitar todo precio especial y toda lista u oferta `INCLUDE` vigente de una cadena sin clientes y desactivarla: 200. Con alguno, 409 «La cadena tiene precios especiales u ofertas publicados y vigentes (N listas de precios y M ofertas): quítela de ellos antes de desactivarla.».
11. Con un usuario con `sales.view` y sin `sales.pricelist`: el panel se ve, no salen «Agregar», cambiar ni quitar, y `POST`/`PATCH`/`DELETE` dan 403.

### Pedido del dueño: siete casos de exención (`SpecialPriceTaxExemptionTests`)

Cliente de cadena con precio especial **8.00** (lista general 10.00); IVU estatal 10.5 % y municipal 1 %. En todos los casos la línea cobra 8.00 con origen `CHAIN_LIST`.

| Caso | Impuesto de la línea |
|---|---|
| Sin exención | 0.92 |
| Certificado municipal (no paga el municipal) | 0.84 |
| Certificado estatal (no paga el estatal) | 0.08 |
| Los dos certificados (en dos filas) | 0.00 (`EXEMPT_CLIENT`) |
| Exención total | 0.00 |
| Total más un certificado parcial | 0.00 (gana la total) |
| Total vencida más municipal vigente | 0.84, con el aviso de la exención vencida |

Otra prueba (`Price_resolution_never_reads_the_client_tax_exemption`) afirma que la resolución de precios no lee la exención del cliente.

## Decisiones a revisar

Cada una dice qué se hizo y la alternativa. Las marcadas «(código)» difieren del diseño aprobado o lo completan.

1. **Una lista dedicada por dueño y por vigencia.** «Precios especiales» crea por dentro una lista publicada por cliente o cadena y por par «desde / hasta»; el motor de precios sigue viendo solo listas. *Alternativa:* fechas por renglón (`PriceListItem.ValidFrom/ValidTo`) y una sola lista dedicada por dueño; cambia la resolución y los cruces de todas las listas.
2. **La dedicada se marca con `SpecialClientId` o `SpecialChainId`** (FK compuesta por compañía; `CK_PriceList_SpecialOwner` exige un solo dueño y `AppliesToAll = 0`) y lleva además su fila `PriceListClient` o `PriceListChain`, para que la resolución no conozca la marca. *Alternativa:* una columna `Kind`, o reconocerlas por el nombre (frágil).
3. **Las dedicadas se ocultan por omisión en «Precios y ofertas»** (interruptor «Ver también los precios especiales», se recuerda). Con unos 700 clientes con precio especial la pantalla se llenaría. Con el interruptor salen con el chip «Precio especial de X»: precios, renglones, publicar y desactivar se permiten; nombre, fechas y a quién aplica dan 422. *Alternativa:* mostrarlas siempre, o dejarlas editables del todo.
4. **Empate: cliente, cadena, oferta, lista general.** *Alternativa:* que la oferta gane el empate, o que la cadena gane al cliente.
5. **El «más bajo» incluye la lista general pero no el precio de venta del producto.** Sin lista general vigente, un precio especial más caro que el producto se cobra igual. *Alternativa:* tomar el precio de venta como «precio público».
6. **`SPECIAL_ABOVE_GENERAL` no bloquea.** Sale en el catálogo, el pedido guiado y el borrador, en la ficha de la lista, en «Precios especiales» y en la fuente `SPECIAL_PRICE` (`AboveGeneral`). *Alternativa:* rechazar al guardar (422).
7. **Incluir o excluir se decide en la cabecera de la oferta** (`Offer.AudienceMode`) para toda la oferta. Cambiar entre incluir y excluir con escogidos da 422. *Alternativa:* una columna `IsExcluded` por fila (permitiría mezclar).
8. **`AudienceMode` es `NVARCHAR(10)` con CHECK**, como `ClientTaxExemption.Scope`, y `AppliesToAll` se conserva sincronizado por `CK_Offer_AudienceMode`. *Alternativa:* un catálogo `LookupCode` «OfferAudience» con FK y retirar `AppliesToAll`.
9. **Sin cliente (catálogo genérico) aplican las ofertas `ALL` y `EXCLUDE`.** Una `EXCLUDE` sin escogidos vale para todos y se puede publicar; una `INCLUDE` sin escogidos no alcanza a nadie y no se publica (422). *Alternativa:* sin cliente, solo las `ALL`.
10. **Editor de ofertas con dos cajas, «Incluir» y «Excluir»** (clientes y cadenas); llenar una deshabilita la otra. **Quitar el último** pide confirmar y deja la oferta **para todos** (`ALL`): ojo, una oferta «solo para la cadena X» a la que se le quita la cadena pasa a valer para todos los clientes, no para nadie. *Alternativa:* un selector de tres opciones con una sola caja.
11. **Sin permiso nuevo.** Ver precios especiales: `sales.view` o `sales.pricelist`; escribir y poner cadenas en listas u ofertas: `sales.pricelist`; la fuente `SPECIAL_PRICE` se lee con `sales.view`. Nada que propagar a roles clonados. *Alternativa:* `sales.chain` para los de cadena, o un permiso `sales.specialprice`.
12. **El origen del precio se guarda en la línea** (`PriceSource`, `PriceListId`, `OfferId`, `PriceChainId`, `ResolvedUnitPrice`) cada vez que se escribe el precio (alta, cambio de cliente del borrador, cambio de precio de una orden solicitada). Se guardan ids; el nombre se lee al mostrar (si se renombra la lista, la orden muestra el nombre nuevo). Las líneas viejas quedan en NULL y no muestran chip. `IsManualPrice` es `PriceSource` guardado y `ResolvedUnitPrice` distinto de `UnitPrice`. *Alternativa:* calcularlo solo al mostrar (una orden ya hecha no diría el origen), o guardar los nombres congelados.
13. **Desactivar una cadena da 409** si tiene listas (incluidas sus dedicadas) activas, publicadas y no vencidas, u ofertas `INCLUDE` activas, publicadas y no vencidas con ella. Primero va el chequeo de clientes activos. Las ofertas `EXCLUDE` no bloquean. Es el contrato que dejó el Lote 245. *Alternativa:* no bloquear, o bloquear también por `EXCLUDE`.
14. **Dos listas chocan (409) solo si son del mismo tipo:** normal contra normal que comparten cliente o cadena, o dedicada contra dedicada del mismo dueño. Una dedicada y una normal conviven aunque alcancen al mismo cliente; una lista de cliente y una de cadena tampoco chocan. *Alternativa:* 409 siempre que compartan cliente o cadena.
15. **Dentro de un mismo nivel también gana el más bajo**; a igual precio, la que empezó más tarde y, a igual fecha, la más nueva. *Alternativa:* conservar la regla de antes dentro del nivel (gana la que empezó más tarde).
16. **La ficha del cliente muestra cuatro cosas:** sus precios (editables), los de su cadena (solo lectura, con enlace), los nombres de las listas normales que lo alcanzan y, por producto, «Se cobra hoy» con su origen. *Alternativa:* solo los suyos y una nota.
17. **Una dedicada sin renglones se desactiva sola.** Si después se agrega otro precio con esas fechas, se crea una lista nueva. *Alternativa:* dejarla vacía o reactivar la anterior.
18. **Fuente de datos `SPECIAL_PRICE`** (orden 199, el siguiente después de `CLIENT_CHAIN` 198), sin `DateField`, con `ValidTo`, `State`, `DaysToExpire`, `GeneralListPrice` y `AboveGeneral`. Sale una fila por renglón activo de lista dedicada activa, **con el estado de hoy** (incluidas las vencidas y las en borrador; el campo `State` lo dice) y con tope de filas. *Alternativa:* sin fuente nueva, o fuentes `PRICE_LIST` y `OFFER` con `DateField`.
19. **El aviso «precio especial por vencer» NO entra en este lote.** Va con el trabajo `DOCUMENT_EXPIRY_SCAN` del Lote D (§3.4 del plan), que leerá `SPECIAL_PRICE`. En pantalla, solo se resalta en ámbar «Vence en N d» cuando faltan 14 días o menos. *Alternativa:* construirlo aquí con un `IScheduledJob`.
20. **Impuestos (pedido del dueño):** precio antes del IVU y exenciones después, sin cambiar el cálculo. Los tipos de documento (estatal, municipal y total) y atar la exención a su documento siguen en el Lote D. *Alternativa:* otro trato de precio para clientes exentos (precios con IVU incluido), solo si el contable lo pide.
21. **«Ofertas» en la ficha de la cadena** muestra, en solo lectura, las ofertas activas y no vencidas que le aplican: para todos, incluyéndola, o excluyendo a otros sin excluirla a ella (`GET /offers?chainPublicId=`). *Alternativa:* solo las que nombran a la cadena.
22. **Tope de 500 cadenas por lista o por oferta.** Los clientes siguen en 2000. *Alternativa:* 2000.
23. **El lote es el 247** y el SQL se ancla por el texto «Lote 247: Precios especiales y ofertas por cliente y por cadena». *Alternativa:* numerarlo «Lote C» o agregarlo al documento maestro.
24. **`appliesToAll` y `audienceMode` juntos que no coinciden dan 400** «“Aplica a todos” no coincide con el público escogido: …» en `audienceMode`. Con solo `appliesToAll`: `true` es `ALL`, `false` es `INCLUDE`; con `false` sobre una oferta `EXCLUDE` se queda en `EXCLUDE`. *Alternativa:* que `audienceMode` mande en silencio.
25. **Al crear un precio especial, un «hasta» ya pasado da 400**; al editar se permite, para cerrar un precio que ya corre. *Alternativa:* permitirlo siempre.
26. **Cuando un cliente cambia de cadena** recibe los precios de la cadena nueva desde ese momento (la resolución lee `Client.ChainId` al cotizar). Los borradores conservan el precio de sus líneas (solo se recotizan al cambiar el cliente del borrador) y las órdenes solicitadas conservan su origen guardado. La vigencia se evalúa con la fecha del borrador o de la solicitud, no con la de entrega. *Alternativa:* recotizar los borradores abiertos.
27. **FK compuestas por compañía en todo lo nuevo, en SQL;** en EF se mapean FK simples (precedente del Lote 245). *Alternativa:* FK simples también en SQL.
28. **La web del personal muestra la cadena y el origen `CHAIN_LIST`; `app-cliente` no** (el Lote 245 decidió que la cadena no se ve en el portal; su `schema.d.ts` solo se regenera). *Alternativa:* mostrar «Precio de su cadena» en el portal.
29. **Capítulos del manual (código/entregable):** en vez de reescribir los capítulos 47 y 53, el cierre trae el **capítulo nuevo 55** y deja una nota al principio de la sección 2 del 47, otra en la sección 8 del 53 y otra en el espejo en inglés del 47. Falta fundir la regla nueva en las secciones 2, 3, 4 y 7 del 47 y en la 10 del 53 (ver «Lo que queda fuera»). *Alternativa:* reescribir el 47 y el 53 en este lote.
30. **(código) La web pide un precio mayor que cero; el API acepta 0.** El formulario de «Precios especiales» avisa «Escriba un precio mayor que cero (hasta 4 decimales).»; el servidor solo rechaza precios negativos, de más de 4 decimales o mayores de 9 999 999 999. Un precio 0 por API se guarda. *Alternativa:* exigir `> 0` también en el servidor (como el resto de renglones de lista, que hoy tampoco lo exigen).
31. **(código) Un precio especial agregado a una dedicada «en borrador» no se cobra.** `DedicatedListAsync` busca la dedicada **activa** con las mismas fechas sin mirar si está publicada; si alguien la volvió a borrador en «Precios y ofertas», el renglón nuevo cae en esa lista y la fila sale «Borrador» (no se cobra) hasta que se publique allá. *Alternativa:* publicar la dedicada al agregar, o crear una nueva.
32. **(código) Texto generado en el servidor en español aunque la interfaz esté en inglés.** El nombre autogenerado de la lista dedicada («Precio especial — {dueño} (del 26/10/2026 al 30/11/2026)»), el texto de los avisos `SPECIAL_ABOVE_GENERAL` (que cita ese nombre) y la vigencia de las ofertas («desde el …, sin fecha de fin», `Offers.cs`, `RangeText`) salen en español y con fechas `dd/MM/aaaa`, mientras la pantalla muestra las fechas con el formato regional (`MM/DD/AAAA` en las capturas). Se ve en `l247-orden-movil-en-light.png` (el aviso del producto SPX-B, en español dentro de una pantalla en inglés). **El texto de la vigencia es de un lote anterior (229); el nombre de la lista dedicada es de este lote.** *Alternativa:* el servidor devuelve los datos y la pantalla arma el texto con `t()`, o la lista dedicada guarda el nombre sin fechas.
33. **(código) Fuente `SPECIAL_PRICE`: `IsActive` es «dueño activo»** (no «renglón activo», que siempre es sí). El campo `OwnerKind` lleva `CLIENT` o `CHAIN` en inglés. *Alternativa:* renombrar los campos antes de que el Lote D los use.

### Contrato para el Lote D (avisos y certificados)

- La fuente **`SPECIAL_PRICE`** (orden 199) trae `ValidTo`, `State`, `DaysToExpire` (`null` = sin «hasta»; `0` = vence hoy; negativo = ya venció) y `AboveGeneral`. El aviso «precio especial por vencer» debe leerla; no hay trabajo programado en este lote.
- Los **certificados de exención** (estatal, municipal, total) siguen sin tipo de documento ni vínculo a su documento: es el Lote D. El Lote C no los lee.

### Contrato para el Lote E

- `SalesOrderLine.ResolvedUnitPrice` es el precio de lista que daba la resolución al escribir la línea; `UnitPrice` es el que se cobra. Si difieren, el precio es manual (`sales.price`): el DTO lo dice en `isManualPrice`. Las líneas anteriores al lote tienen todo NULL (sin origen, no manual).

## Lo que queda fuera de este lote (a propósito)

- **Correr `scripts/smoke-precios-especiales.sh`:** queda para el CI.
- **El aviso «precio especial por vencer»** y los tipos de documento de la exención (Lote D).
- **Mostrar la cadena o su precio en el portal y la app del cliente** (decisión 28).
- **Fundir la regla nueva en los capítulos [47](manual/47-precios-y-ofertas.md) (secciones 2, 3, 4 y 7) y [53](manual/53-cadenas.md) (sección 10)** y escribir `docs/manual/en/53-…` y los capítulos 52 a 54 en inglés (la edición en inglés llega al 51). Este cierre trae el capítulo [55](manual/55-precios-especiales-y-ofertas.md), su espejo en inglés, las pantallas ([L247](manual/frontend/l247-precios-especiales.md)) y la FAQ en los dos idiomas. La FAQ en inglés llegaba al Lote 241: solo se agregó la sección del 247 (faltan las de 242 a 246).
- **Capítulo de pantallas en inglés** (`docs/manual/en/frontend/`) y capturas dentro de ese árbol: las capturas en inglés (`l247-*-en-*.png`) están en `docs/manual/frontend/img/` y el capítulo de pantallas en español las cita.
- **Textos en inglés del servidor** (decisión 32).
- **Defecto visual en celular, corregido en este lote:** en el pedido guiado a 390 px el contenedor del precio (`.wz-price-wrap`) caía en la columna de 44 px y cortaba el chip de origen («ens SPX»; afectaba también al chip «Oferta» del Lote 229). Se agregó a la regla móvil de `wizard.css` (columna 2) y se regeneró la captura `l247-orden-movil-en-light.png`, ya con el chip completo bajo la cantidad. El punto del chip en la ficha de la cadena en inglés es un detalle menor de la captura, sin efecto en uso.
- **Probar con un usuario real de Solutions** la regla «gana el más bajo» con sus listas de grupo (decisión 14): la FAQ la explica, pero no se probó con sus datos.
- **Cherry-pick a `Depot-Implementation`** y cualquier commit: sin commitear; la rama la indica el dueño.
- **`web-app/src/features/notifications/helpIndex.json` y `app-cliente/src/features/kemi/helpIndex.json`:** regenerados con `npm run help:index` al cerrar. Al portal no llega nada: ni el capítulo 55 ni la FAQ del 247 llevan `<!-- kemi:portal -->`.
