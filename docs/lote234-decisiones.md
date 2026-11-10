# Lote 234 — Fotos que no cargaban, ofertas sin borrador y el kit de oferta en «Nueva orden» (2026-11-10)

Tres pedidos de Luis mientras probaba en su navegador. Toca lo construido en los lotes [229](lote229-decisiones.md) (ofertas y fotos) y [231](lote231-decisiones.md) (biblioteca de kits de oferta).

## 1. Las fotos no cargaban de forma confiable — causa raíz (dos fallas, las dos reproducidas)

| # | Falla | Cómo se reprodujo | Arreglo |
|---|---|---|---|
| A | **La pantalla soltaba la URL de la foto que otro componente seguía usando.** Las fotos se piden con la sesión y se muestran como URL de objeto (`useStoredFileUrl`, `web-app/src/features/orders/api.ts`); la URL queda en la caché de react-query y la comparten todos los que muestran ese archivo. El hook viejo la revocaba (`URL.revokeObjectURL`) cuando **cualquiera** de esos componentes se desmontaba. Al cambiar la foto de la oferta la tabla de renglones vuelve a montar sus celdas → sus fotos quedaban rotas; al cerrar el editor se rompía la de la tarjeta; al reabrir, la caché devolvía una URL ya muerta (hasta 5 minutos, o hasta recargar); en el pedido guiado, lo mismo al buscar o cambiar de marca. | Recorrido `web-app/e2e/lote234-fotos.spec.ts` con el hook de antes del commit 7faf2bb: falla en «después de cambiar la foto de la oferta: fotos de los renglones: rota(s) → Foto de KITF-…, Foto de FOTO-…». | El arreglo que ya estaba en 7faf2bb es **correcto y suficiente**: la URL vive lo que viva la consulta en la caché (se revoca en el evento `removed` del `QueryCache`; al volver a pedir el archivo la anterior se suelta 30 s después). No se cambió. Queda cubierto por el recorrido y por `storedFileUrl.test.tsx`. |
| B | **El servidor contestaba 500 a `GET /files/{id}` si el nombre del archivo tenía acentos o eñe** («Diseño sin título.png», «Promoción.jpg»). `StoredFileRules.SafeFileName` conserva las letras con acento y `FilesController` ponía el nombre tal cual en `Content-Disposition`; Kestrel rechaza cualquier carácter no ASCII en un encabezado (`InvalidOperationException: Invalid non-ASCII or control character in header: 0x00F3`). Esa foto no cargaba **nunca**, en ninguna pantalla; otra subida con nombre sin acentos sí: de ahí «a veces sale y a veces no». | Con el controlador de antes y las fotos del recorrido (subidas con esos nombres): `HTTP/1.1 500` en las cuatro; con el arreglo, `200`, `image/png` y `Content-Disposition: inline; filename="Promoci_n a_o nuevo.png"; filename*=UTF-8''Promoci%C3%B3n%20a%C3%B1o%20nuevo.png`. | `StoredFileRules.InlineDisposition` (ASCII + `filename*` RFC 5987), usado en `FilesController` y en `TenantLinksController` (mismo defecto). Los archivos ya guardados vuelven a verse sin tocarlos. |

Descartado: permisos (`sales.view` lee las fotos de oferta y de producto), tipo de contenido, cifrado y el redimensionado con SkiaSharp (las fotos del recorrido se guardan y se sirven bien; si Skia falla se guarda el original). No depende del sistema operativo: B es de Kestrel y A del navegador.

## 2. Las ofertas no tienen «borrador»

- `POST /offers` crea la oferta **publicada** (`OfferService.CreateAsync`). Vale según sus fechas; se apaga con «Desactivar» / «Reactivar».
- `POST …/publish` y `…/unpublish` siguen en el API por compatibilidad. **Decisión:** publicar una oferta ya publicada **no es un error** (antes 422 «La oferta ya está publicada.»): así quien todavía publica después de crear (scripts, pruebas) sigue funcionando.
- **Decisión a revisar:** sin borrador, una oferta «para clientes escogidos» puede quedarse sin clientes (ya no se rechaza desmarcar «Aplica a todos» ni quitar el último cliente): no aplica a nadie y la ventana lo dice («Esta oferta todavía no aplica a nadie: escoja al menos un cliente.»). Si no, no habría forma de crear una oferta de clientes escogidos.
- Web: sin «Volver a borrador»; «Publicar» solo aparece en una oferta que quedó en borrador de antes. El chip «Publicada (todavía no empieza)» pasa a «Programada (todavía no empieza)».
- SQL: sección «Lote 234» al final de `Diseño/logistica-db-update.sql`, una sola vez (marca `L234_OFFER_NO_DRAFT`): publica las ofertas activas en borrador. Las listas de precios no cambian.
- `scripts/solutions-pedido-vendedores/02-kit-oferta1.sql` ya creaba la oferta publicada; solo se corrigió el LEEME.

## 3. El kit de oferta sale igual en «Nueva orden» y en «Nuevo pedido»

«Nueva orden» (`OrderModal`) ya no pide `includeOfferKits` al buscador de productos: toma los kits de oferta de `GET /sales-orders/catalog?clientPublicId=` (el mismo catálogo del pedido guiado) y los pasa al selector (`ProductPicker.extraOptions`, `offerKitOptions.ts`), con el precio de la oferta. Sin cliente escogido no ofrece ninguno. No cambia el contrato del API (`includeOfferKits` sigue existiendo en `GET /products`); el servidor ya rechazaba al guardar un kit que no estuviera en venta.

## Cómo se probó

- Recorrido `web-app/e2e/lote234-fotos.spec.ts` contra el API real (base nueva `TeikemFotos`, sin Brevo, Development): **3 passed (4.5m)**. Comprueba `naturalWidth > 0` de cada foto en la tarjeta, el editor, tras cambiar la foto de la oferta y la del renglón, al reabrir 3 veces, tras esperar 66 s, al ir a otra pantalla y volver (enseguida y tras 66 s), al recargar, y en el pedido guiado (cuadrito, tarjetas y foto grande); que la oferta nace publicada por API y por pantalla sin «Publicar» ni «Volver a borrador»; y que un kit de biblioteca puesto en una oferta recién creada sale de inmediato en los dos sitios a su precio de oferta, mientras el de una oferta vencida no sale en ninguno.
- Compilación: `dotnet build` del API y de las pruebas (0 errores), `tsc -b` limpio.
- **Escritas y NO corridas** (orden del dueño de no correr pruebas): `tests/Teikem.Tests/Lote234Tests.cs`, los cambios de `tests/Teikem.Tests/OfferKitTests.cs`, `web-app/src/features/orders/storedFileUrl.test.tsx`, y los casos nuevos de `OffersScreen.test.tsx` y `OrderModal.test.tsx`.
- No se comprobó en Windows (el API del dueño) ni en el móvil; el recorrido es de escritorio. `app-cliente` y `app-movil` no se tocaron.
