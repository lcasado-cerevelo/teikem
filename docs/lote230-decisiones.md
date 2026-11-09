# Lote 230 — Dimensiones y peso del producto con unidad, y sustitución de piezas de un kit (backend, 2026-11-09)

Parte de [ofertas-kits-fotos-diseno.md](ofertas-kits-fotos-diseno.md) §6, §6b, §8 y §9 (acordado con Luis). Solo backend: la pantalla (editor del producto con medidas y unidad,
«Cambiar» en las piezas del kit en el pedido) va en su parte de frontend; el manual y la FAQ, con ella. En paralelo, el lote 229 hizo ofertas, precios y foto del producto.
«Ajustable al pedir» se queda tal cual (§9): la sustitución es aparte.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Datos (`logistica-db-update.sql`, sección «2026-11-09 — Lote 230») | `Product.Length`, `Width`, `Height` (DECIMAL(12,4) ≥ 0, NULL), `DimensionUnit` (VARCHAR(2): `IN`, `CM`, `FT`, `M`) y `WeightUnit` (`LB`, `KG`), con CHECK; `ProductKitComponent.NotSubstitutable` (BIT, 0 = sustituible); `SalesOrderLineComponent.SubstitutedProductId` (FK a Product, NULL). Solo agrega columnas; `WeightKg` y `VolumeM3` no cambian. Idempotente (`COL_LENGTH`, `OBJECT_ID`). |
| Reglas puras | `Domain/Wms/ProductDimensionRules.cs` (conversión, cálculo del volumen, sincronización con el volumen manual, peso lb/kg, sugerencia por región, validación, volumen para mostrar) y `Domain/Wms/KitSubstitutionRules.cs` (regla de la sustitución y sus mensajes). |
| Producto | `ProductCreateRequest` / `ProductPatchRequest` + `length`, `width`, `height`, `dimensionUnit`, `weight`, `weightUnit`. `ProductDetailDto` + `measures` (`ProductMeasuresDto`: medidas, unidad, volumen en la unidad cúbica, si es calculado, peso en su unidad), `categoryPath` («Guantes / Nitrilo»), `categoryParentId`, `categoryParentName`. `GET /api/v1/products/units-suggestion` (inventory.view) → `ProductUnitsSuggestionDto`. |
| Importación / exportación de productos | Columnas nuevas opcionales `length`, `width`, `height`, `dimensionUnit`, `weight`, `weightUnit` con la misma regla del alta (crean y actualizan; celda vacía = sin cambio). La salida de productos (`EntityOutput`) las trae también (el peso en su unidad). |
| Kit | `KitComponentRequest` + `notSubstitutable` (editor del kit y, para el lote 229, el kit de una oferta: es el request de piezas compartido) y `substitutesProductPublicId` (solo en las piezas de una línea de orden de venta; el editor del kit lo ignora). `KitComponentDto` + `notSubstitutable`. `KitService` guarda la marca (null = la que ya tenía; pieza nueva = sustituible). |
| Sustitución en la orden | `SalesOrderService.Substitution.cs`: `GET /api/v1/sales-orders/kits/{kitPublicId}/substitutes` (sales.create, módulo SALES_ORDERS) → `KitSubstitutesDto` (cada pieza del catálogo del kit con su categoría, precio, «no sustituible» y sus sustitutos válidos); sirve antes de guardar la línea. En `PATCH/POST /sales-orders` la línea manda todas las piezas y la que cambia lleva `substitutesProductPublicId` = la pieza original. La foto de la línea guarda la pieza REAL en `ComponentProductId` y la original en `SubstitutedProductId`; `SalesOrderLineComponentDto` + `substitutesProductPublicId`, `substitutesSku`, `substitutesName`. Al volver a guardar el borrador sin piezas, la sustitución se conserva (como la foto del lote 69). |

## La regla (lo que valida el servidor)
**Dimensiones** (`ProductDimensionRules`):
- Largo, ancho y alto se guardan como se escribieron, con su unidad, y son de la **unidad base** del producto.
- Con las **tres** medidas, `VolumeM3` se **calcula** (convertido a m³, 4 decimales) y un `volumeM3` mandado a mano **se ignora** (gana el cálculo).
- Si falta cualquiera, el `volumeM3` manual se acepta; si no viene, se **conserva** el que había: un volumen escrito antes no se pierde por poner (o quitar) una medida suelta.
- **0 en una medida = quitarla** (en el PATCH, `null` = sin cambio).
- Sin `dimensionUnit` se usa la que ya tenía el producto o, si no tiene, la sugerida por la región (pulgadas o centímetros).
- Cambiar solo la unidad **no convierte** los números: dice en qué unidad están escritos (el volumen se recalcula si están las tres).

**Peso**: `weight` (en `weightUnit`) se guarda en kg (`WeightKg`, 3 decimales) y se recuerda la unidad; manda sobre `weightKg` si vienen los dos. `weightKg` (la forma de siempre) conserva la unidad que tenía el producto (KG si no tenía). Sin `weightUnit`: la del producto o la sugerida. Se muestra en libras con 2 decimales (la vuelta kg → lb recupera exactamente lo escrito con hasta 2 decimales) y en kilos con 3.

**Unidad sugerida por región** (`Tenant.RegionCode`; hoy solo hay PR y US): Puerto Rico y Estados Unidos = sistema inglés (`IN` lo chico, `FT` lo grande, `LB`); cualquier otra región = métrico (`CM`, `M`, `KG`).

**Sustitución** (`KitSubstitutionRules`): la pieza no está marcada «no sustituible»; el sustituto es activo, no es kit, es del mismo dueño de inventario que el kit (en ventas: propio) y no es ya otra pieza del kit; es de la **misma categoría exacta** (mismo `ProductCategoryId`; si la pieza no tiene categoría, no se sustituye); tiene **exactamente el mismo** `SalePrice` (si la pieza no tiene precio de venta, no se sustituye); en un kit fijo va en la **misma cantidad** y, deshaciendo las sustituciones, las demás piezas deben ser las del catálogo. Sin permiso aparte (basta `sales.create`). Vale para kits fijos y ajustables, regulares y de oferta. El precio de la línea no cambia; la reserva, el despacho, el costo, la factura/IIF y el aparato (sync) ya usaban la foto de la línea, así que usan la pieza real sin cambios en ese código.

## Mensajes (para el manual y la FAQ)
| Caso | HTTP | Mensaje |
|---|---|---|
| Medida negativa | 400 | «El largo no puede ser negativo.» (igual con «ancho» y «alto») |
| Medida con más de 4 decimales o demasiado grande | 400 | «El largo debe ser un número de hasta 4 decimales.» |
| Unidad de medidas inválida | 400 | «La unidad de las medidas debe ser IN (pulgadas), CM (centímetros), FT (pies) o M (metros).» |
| Unidad de peso inválida | 400 | «La unidad del peso debe ser LB (libras) o KG (kilos).» |
| Peso negativo / enorme | 400 | «El peso no puede ser negativo.» · «El peso es demasiado grande.» |
| Volumen calculado enorme | 400 | «El volumen calculado con el largo, el ancho y el alto es demasiado grande.» |
| Pieza marcada no sustituible | 400 | «La pieza GM del kit no es sustituible.» |
| La «original» no es pieza del kit | 400 | «GV no es una pieza del kit KIT-1: solo se sustituyen sus piezas.» |
| Pieza sin categoría | 400 | «La pieza CAJA no tiene categoría: solo se sustituye por un producto de la misma categoría.» |
| Otra categoría | 400 | «GV no es de la misma categoría que GM: solo se sustituye por un producto de la misma categoría.» |
| Pieza sin precio de venta | 400 | «La pieza GM no tiene precio de venta: solo se sustituye por un producto del mismo precio.» |
| Otro precio | 400 | «GXL no tiene el mismo precio de venta que GM: solo se sustituye por un producto del mismo precio.» |
| Otra cantidad (kit fijo) | 400 | «El sustituto de GM va en la misma cantidad que la pieza: 2.» |
| Sustituto que ya es otra pieza | 400 | «CAJA ya es otra pieza del kit: no puede sustituir una pieza.» |
| La misma pieza dos veces | 400 | «La pieza GM se sustituyó más de una vez.» |
| Sustituto inactivo / kit / otro dueño | 400 | los del lote 69: «El componente GS está inactivo.» · «K2 es un kit: un kit no puede llevar otro kit como componente.» · «El componente GS es de otro dueño de inventario que el kit.» (en la lista de sustitutos simplemente no salen) |
| Kit fijo con otro cambio además de la sustitución | 400 | «El kit KIT-1 no se puede ajustar al pedir.» (el de siempre) |
| Sustitutos de algo que no es kit | 422 | «El producto GM no es un kit.» |
| Kit o pieza original que no existe | 404 | «Producto no encontrado.» · «Componente no encontrado.» |

## Quién usa el volumen y el peso (revisado antes de cambiar nada)
Siguen leyendo `Product.WeightKg` (kg) y `Product.VolumeM3` (m³), que no cambiaron de significado ni de columna:
- `PutawaySuggester` (capacidad de peso de la posición, `MaxWeightKg`): solo `WeightKg`.
- `ProductService` (detalle `weightKg`/`volumeM3`), `PortalCreateService` (alta y edición desde el portal, por `weightKg`/`volumeM3`), `ProductImportRows` (columnas `weightKg`/`volumeM3`), `EntityOutput` (salida de productos).
- Nadie más: tarifas, viajes, flotilla y órdenes de transporte usan sus propios `WeightKg`/`VolumeM3` (de `CargoLine`, `TransportOrder`, `Vehicle`), no los del producto; reportes/fuentes de datos, sync del aparato y QuickBooks no leen el peso ni el volumen del producto.

## Decisiones tomadas en este lote (a revisar con Luis)
1. **Volumen manual con las tres medidas: se ignora** (no 400). Así una pantalla o una importación que reenvía el volumen viejo junto con una medida nueva no falla; el detalle devuelve el calculado y `volumeCalculated = true`.
2. **0 en una medida la quita**, como en los mínimos del producto.
3. **Unidades como texto con CHECK** (`IN/CM/FT/M`, `LB/KG`, como `RotationMethod`), no como `LookupCode`: son fijas, con factores de conversión en el código, y no se configuran por compañía.
4. **`VolumeM3` sigue con 4 decimales** (columna sin tocar): para algo muy chico (p. ej. 1 × 1 × 1 pulgadas ≈ 0.000016 m³) el m³ guardado redondea a 0.0000; el volumen exacto se muestra en la unidad cúbica de las medidas (`measures.volume` en IN3), que sale de las tres medidas. Si hace falta más precisión interna, se amplía la columna en otra sección.
5. **Sin unidad, la sugerida por la región** (pulgadas / libras en PR y US) al escribir medidas o peso por primera vez; cambiar solo la unidad no convierte los números.
6. **Sustitución en el request de líneas**: se mandan todas las piezas y la sustituta lleva `substitutesProductPublicId`; el endpoint de sustitutos es por kit (`/sales-orders/kits/{kitPublicId}/substitutes`) para que sirva antes de guardar la línea. Se valida contra el catálogo **actual** del kit: si el catálogo cambió después de guardar el borrador, al mandar piezas de nuevo se valida contra el nuevo (para tomar el catálogo nuevo, quitar y volver a agregar la línea, como en el lote 69).
7. **Kit ajustable + sustitución**: la fila sustituta se valida (pieza del catálogo, categoría, precio, «no sustituible») pero su cantidad es libre (el kit ya permite ajustar cantidades).
8. **Un sustituto no puede ser otra pieza del mismo kit** (evita piezas repetidas en la foto) ni la pieza misma (`substitutesProductPublicId` igual al producto = no es sustitución).
9. Precio comparado: `Product.SalePrice` exacto (§8); una pieza sin precio de venta o sin categoría no tiene sustitutos.

## Pruebas
- `ProductDimensionRulesTests` (xunit, 28 casos): volumen por unidad (IN, CM, FT, M), sin las tres no hay cálculo, manual ignorado con las tres, volumen escrito que no se pierde con una medida suelta y se reemplaza con la tercera, 0 = quitar, unidad actual o sugerida, validación y normalización, peso lb/kg y su vuelta con 2 decimales, `weight` sobre `weightKg`, sugerencia por región (PR, US, MX, sin región), volumen para mostrar; alta con las tres medidas, sugerencia por región (PR y métrico), edición que conserva el volumen, 400 con los mensajes exactos, categoría con su padre en el detalle, importación con las columnas nuevas.
- `KitSubstitutionTests` (xunit, 8): sustitutos de misma categoría y precio (sin otro precio, otra categoría, inactivos ni la pieza misma; sin categoría = ninguno; «no sustituible» = ninguno y la marca se conserva en el editor; 422 si no es kit); sustituir con el precio de la línea sin cambio y la original a la vista (también al volver a guardar el borrador); la reserva y el despacho con la pieza real (la original no se toca); rechazos con código y mensaje exactos (otro precio, otra categoría, otra cantidad, sin categoría, no es pieza, ya es otra pieza, inactivo, kit fijo con otro cambio, no sustituible); reglas puras (dueño, kit, sin precio); kit ajustable (sustituir y ajustar; ajustar sin sustituir como siempre); kit de una oferta (lote 229); el aparato (sync) recibe la pieza real.
- Ajustadas: `WmsContractsTests` (firmas de `ProductCreateRequest`, `ProductPatchRequest`, `ProductDetailDto`) y `WmsControllerSecurityTests` (mapa con `UnitsSuggestion`).
- `db-init` desde cero en una base aparte (`Teikem_L230`) y la sección del lote corrida dos veces más con `sqlcmd -b` (sin errores; columnas y CHECK presentes); `db-update` después: «base de datos al día».
- Contrato: `web-app/openapi.json` solo con lo nuevo (2 rutas, 5 esquemas y las propiedades nuevas de 6 esquemas), tomado del swagger del API levantado en Development **sin llave de Brevo** sobre la base aparte; `schema.d.ts` regenerado en web-app y copiado igual a app-cliente y app-almacen (solo adiciones); `tsc -b` de web-app sin errores.
- Suite completa contra SQL Server real (`TEIKEM_TEST_SQLSERVER`): **5807 pasaron, 0 fallaron, 0 omitidas**.

## Frontend (web-app, 2026-11-09)
| Pieza | Qué es |
|---|---|
| Editor de producto | `ProductEditorModal`: ventana `xl` con bloques con título (Identificación, Inventario, Precios, Medidas y peso; campos personalizados al final) en una rejilla `.pe-grid` de 4 columnas (3 bajo 1100 px, 2 bajo 720 px, 1 bajo 480 px; sin scroll horizontal a 360 px). Desaparece «Más datos del producto»: código de barras y mínimo/máximo de picking pasan a Inventario; peso y volumen a Medidas y peso. Categoría como ruta «Guantes → Nitrilo». Ningún campo ni validación se perdió. Hueco natural para el bloque «Foto» (lote 229) junto a Identificación. |
| Ayuda plegada | Nuevo en el kit: `HelpTip` («?» con recuadro, Escape no cierra el modal) y `Field tip`. La ayuda larga de Rotación y de Unidades por empaque va ahí; el aviso de «ya tiene movimientos» queda como una línea corta arriba de Inventario. |
| Medidas y peso | Unidad de las medidas obligatoria (IN/CM/FT/M; la del producto o la de `GET /products/units-suggestion`; si no llega, IN/LB), largo, ancho, alto; volumen calculado y no editable con las tres (in³/cm³/ft³/m³ con su equivalente en m³), manual sin ellas; peso + unidad (LB/KG). Reglas puras en `features/warehouse/productMeasures.ts` con los vectores de `ProductDimensionRulesTests`. |
| Kit | `KitEditorModal`: casilla «No sustituible» por pieza (desmarcada por omisión) con su ayuda; chip en la tabla del kit. «Ajustable al pedir» igual. |
| Sustitución | `KitLineBlock` (borrador de la orden y carrito del pedido guiado): «Cambiar» → solo los sustitutos válidos (SKU · nombre · marca) → «en lugar de {SKU}» + «Deshacer»; la línea manda todas las piezas con `substitutesProductPublicId`. `KitComponentsSection` (orden guardada/solicitada) muestra la pieza real «en lugar de …». Reglas puras en `features/sales/kitSubstitution.ts`; hook `useKitSubstitutes`. |
| Manual y FAQ | `docs/manual/06` §2.4 y §8b.3b, `docs/manual/18` §17.1, `faq.md` (sección «Lote 230») e índice. Falta la traducción en `docs/manual/en` y las capturas. |

### Decisiones del frontend (a revisar con Luis)
1. **Volumen manual en la unidad cúbica de las medidas** (in³, cm³, ft³, m³), no en m³, para que se vea igual que el calculado; viaja convertido a m³ con 4 decimales y debajo del campo se dice «Se guarda como … m³». Para algo muy chico en pulgadas el m³ redondea (decisión 4 del backend): conviene escribir las tres medidas.
2. **Lo que se ve es lo que se guarda**: cambiar la unidad de las medidas o del peso no convierte los números escritos (como el servidor con las medidas). Por eso, al cambiar solo la unidad del peso, la pantalla manda el peso Y la unidad (el servidor, con solo la unidad, habría convertido el número).
3. **PATCH solo con lo tocado** de medidas y peso (null = sin cambio); una medida vaciada va 0 (= quitarla); un peso vaciado va 0; `weightKg` ya no se manda desde el editor. El volumen manual solo viaja si se tocó (o cambió su unidad) y nunca con las tres medidas.
4. **Unidad de las medidas siempre visible y obligatoria** (con la sugerida puesta): «la pantalla pregunta la unidad» sin una pregunta aparte; solo viaja si hay alguna medida o volumen (o si se cambió).
5. **Piezas del kit desde el endpoint de sustitutos** cuando el usuario tiene `sales.create`: así un vendedor sin `inventory.view` también ve las piezas (antes no las veía). Sin `sales.create` se usa el kit del almacén como antes.
6. **La lista de «Cambiar» excluye** el producto que ya está puesto y los que ya son otra pieza de la línea (dos piezas no quedan con el mismo producto).
7. En el modo «Ver datos del producto» (sin `inventory.manage`) el formulario está deshabilitado, y con él los «?» de ayuda (quedan en el `fieldset` deshabilitado).
8. El bloque «Campos personalizados» se oculta por CSS (`:has`) si la compañía no tiene campos para productos.

### Pruebas del frontend
- `productMeasures.test.ts` (vectores del servidor: volumen por unidad, peso lb/kg y su vuelta, redondeo; valores iniciales; cuerpo del alta y del PATCH; topes).
- `ProductEditorModal.test.tsx` (bloques y orden de campos, ventana `xl`, ayuda plegada, categoría como ruta, unidad sugerida y la del producto, volumen calculado ↔ manual, POST/PATCH con las medidas, mensaje exacto de medida negativa) más las existentes.
- `HelpTip.test.tsx`, `kitRules.test.ts` y `KitTab.test.tsx` («No sustituible»), `kitSubstitution.test.ts`, `KitLineBlock.test.tsx` (Cambiar/Deshacer, borrador guardado, «en lugar de» en la orden) y `ProductsStep.test.tsx` (sustitución en el carrito del pedido guiado y el cuerpo de la línea).
