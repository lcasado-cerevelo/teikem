# Lote 233 — Parámetro de la compañía «La compañía trabaja con kits armados» (2026-11-10)

Pedido de Luis: un sí/no por compañía, **NO por omisión**. Con NO, «Nuevo producto» no muestra el recuadro «¿Lo que va a crear es un kit?»; con SÍ, sí. Sigue al [Lote 232](lote232-decisiones.md).

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Base (`logistica-db-update.sql`, sección «2026-11-10 — Lote 233», al final) | `Tenant.UsesAssembledKits BIT NOT NULL DEFAULT 0` (`DF_Tenant_UsesAssembledKits`). Solo en la corrida que crea la columna, las compañías que ya tienen algún producto `ASSEMBLED_KIT` quedan en 1. Idempotente, solo agrega. |
| Dominio | `Tenant.UsesAssembledKits`; `AssembledKitRules.CompanyDoesNotUseAssembledKits`. |
| API | Viaja en los ajustes de la compañía que ya usa la pestaña Operación: `GET /api/v1/tenant/settings` (solo sesión) devuelve `usesAssembledKits`; `PUT /api/v1/tenant/settings` (`admin.tenant`) lo cambia (`null` = sin cambio). Sin endpoint ni permiso nuevos. |
| Regla del servidor | `AssembledKitService.CreateAsync` (`POST /products` con `isAssembledKit: true`): con el ajuste apagado → **422** «La compañía no trabaja con kits armados: actívelo en Ajustes → Operación.» y no se crea nada. Leer, cambiar la composición, armar, desarmar y vender un kit que ya existe **no** miran el ajuste. Los kits de oferta no se tocan. |
| Web | Sistema → Ajustes → Operación → panel **Kits** (`KitsPanel` en `OperationsTab.tsx`; módulo WMS): interruptor que guarda al instante, con dos líneas de ayuda. `ProductEditorModal`: `KitQuestion` solo se pinta en el alta si `useTenantSettings().data.usesAssembledKits === true` (mientras carga, no). Editar un kit armado existente no depende del ajuste. Textos es/en (`system.settings.ops.kits*`). |
| Contrato | `web-app/openapi.json`: solo `usesAssembledKits` en `TenantSettingsDto` y `TenantSettingsUpdateRequest` (fusión desde el swagger del API en Development sin llave de Brevo); `schema.d.ts` regenerado en web-app y copiado igual a app-almacen y app-cliente. |
| Manual y FAQ (es y en) | 06 §2, §8b.1a (nuevo), §8b.2, §8b.3 y §8b.4; F9 §6; README; FAQ «Lote 233». |

## Cómo se probó
- xunit `AssembledKitTests` (3 pruebas nuevas): por omisión NO y el alta da 422 con el mensaje exacto; encenderlo permite el alta; un cambio parcial sin el campo no lo toca; apagarlo después deja armar y desarmar los kits que existen pero no crear otros; la sección SQL. Las pruebas del Lote 232 (`AssembledKitTests`, `AssembledKitSqlTests`) encienden el ajuste en su siembra.
- vitest: `AssembledKitEditor.test.tsx` (apagado: sin recuadro y se guarda un producto normal; cargando: sin recuadro; encendido: con recuadro; apagado: editar un kit existente muestra su composición) y `TenantSettingsPage.test.tsx` (apagado por omisión, el interruptor guarda `usesAssembledKits`, solo lectura sin `admin.tenant`).
- `db-init` desde cero en `TeikemL233`, la sección «Lote 233» dos veces con `sqlcmd -I` (sin errores) y `db-update` («base de datos al día»). Recorrido por el API real (sin correo): 422 apagado → encender → 200 → apagar → el kit existente se lee → 422 otro kit.

## Decisiones a revisar
1. **Vive en los ajustes generales de la compañía** (`/tenant/settings`), no en un endpoint propio: es donde ya está la pestaña Operación (categoría por defecto, rotación) y su lectura solo pide sesión, así que quien crea productos lo lee sin permiso extra.
2. **En los contratos va como propiedad** (`{ get; init; }`), no como parámetro del constructor de `TenantSettingsDto` / `TenantSettingsUpdateRequest`: hay pruebas que fijan el orden de esos parámetros contando desde el final. En JSON es un campo más.
3. **Compañías que ya tenían kits armados quedan en SÍ** al crearse la columna (el Lote 232 salió el mismo día, así que en la práctica ninguna; es por seguridad). Las demás, NO. Solutions queda en NO.
4. **Solo el alta mira el ajuste.** Cambiar la composición de un kit existente sin historia sigue permitido con el ajuste apagado.
5. **Sin configuración EF explícita**: un `bool` no nulo se mapea solo (igual que `SalesShippingTaxable`).
6. **Se guarda al tocar el interruptor** (como «Imprimir los labels automáticamente»), sin botón Guardar.
7. **Otros usuarios lo ven al recargar**: los ajustes se guardan en caché 10 minutos en la web; quien lo cambia lo ve al instante.

## Pendiente
- Captura del panel «Kits» en F9 y del editor sin el recuadro (Playwright; no se corrió).
- El recorrido Playwright del Lote 232 que crea un kit armado, si existe, tiene que encender el ajuste antes.
