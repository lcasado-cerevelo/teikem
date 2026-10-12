// Contexto de los reportes PDF de 'Productos e inventario' (Lote 12; separado en el Lote F14 para compartirlo con el reporte
// de códigos de barras): traducción, idioma, compañía activa, usuario y los nombres de almacenes y categorías de los
// filtros (mismas consultas en caché que la barra de filtros; un 403 no saca de la pantalla).
import { useContext, useMemo } from 'react'
import { SessionContext } from '../../app/session'
import { useLang, useT } from '../../kernel/i18n'
import { useInventoryOwners, useProductCategories, useWarehouses, warehouseLabel } from './api'
import type { ProductReportContext } from './inventoryReports'
import { OWN_OWNER } from './kardexView'
import type { ProductFilterNames, ProductFilterState } from './productFilters'

/** Contexto del reporte para los filtros dados. */
export function useProductReportContext(filters: ProductFilterState): ProductReportContext {
  const t = useT()
  const lang = useLang()
  const me = useContext(SessionContext)?.me
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const { data: categories = [] } = useProductCategories({}, { handleAccessDenied: false })
  const { data: owners = [] } = useInventoryOwners({ handleAccessDenied: false })
  const names = useMemo<ProductFilterNames>(
    () => ({
      warehouses: new Map(warehouses.map((w) => [w.publicId ?? '', warehouseLabel(w)])),
      categories: new Map(categories.map((c) => [String(c.id), c.path || c.name || String(c.id)])),
      owners: new Map(owners.map((o) => [o.isOwn ? OWN_OWNER : (o.clientPublicId ?? ''), o.name ?? ''])),
    }),
    [warehouses, categories, owners],
  )
  return { t, lang, company: me?.tenantName, user: me?.fullName || me?.email, filters, names }
}
