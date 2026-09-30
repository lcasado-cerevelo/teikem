// Lote 12 — botones "Reporte de inventario" y "Reporte de ajustes": generan el PDF (inventoryReports.ts) con los filtros que
// la pantalla les pasa (`ProductFilterState`). Resuelven solos los nombres de almacenes y categorías (mismas consultas en
// caché que los filtros), la compañía activa y el usuario. Mientras se genera, el botón queda ocupado ("Generando…"); un
// error se avisa con un toast. Hoy los usa 'Productos e inventario'; el de ajustes vuelve en la pantalla de Ajustes (Lote 4):
//   <AdjustmentsReportButton filters={{ ...EMPTY_PRODUCT_FILTERS, warehouses: [whId] }} />
import { useContext, useMemo, useState } from 'react'
import { SessionContext } from '../../app/session'
import { useLang, useT } from '../../kernel/i18n'
import { toast } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { useProductCategories, useWarehouses, warehouseLabel } from './api'
import { generateAdjustmentsReport, generateInventoryReport, type ProductReportContext } from './inventoryReports'
import type { ProductFilterNames, ProductFilterState } from './productFilters'

export interface ProductReportButtonProps {
  filters: ProductFilterState
  className?: string
}

/** Contexto del reporte (traducción, idioma, compañía, usuario y nombres de los filtros) para los filtros dados. */
function useReportContext(filters: ProductFilterState): ProductReportContext {
  const t = useT()
  const lang = useLang()
  const me = useContext(SessionContext)?.me
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const { data: categories = [] } = useProductCategories({}, { handleAccessDenied: false })
  const names = useMemo<ProductFilterNames>(
    () => ({
      warehouses: new Map(warehouses.map((w) => [w.publicId ?? '', warehouseLabel(w)])),
      categories: new Map(categories.map((c) => [String(c.id), c.path || c.name || String(c.id)])),
    }),
    [warehouses, categories],
  )
  return { t, lang, company: me?.tenantName, user: me?.fullName || me?.email, filters, names }
}

function ReportButton({ label, hint, className, run }: { label: string; hint: string; className?: string; run: () => Promise<void> }) {
  const t = useT()
  const [busy, setBusy] = useState(false)
  return (
    <button
      type="button"
      className={className ?? 'btn'}
      title={hint}
      aria-busy={busy || undefined}
      disabled={busy}
      onClick={async () => {
        setBusy(true)
        try {
          await run()
        } catch {
          toast.error(t('warehouse.products.reports.error'))
        } finally {
          setBusy(false)
        }
      }}
    >
      <IconDoc />
      {busy ? t('warehouse.products.reports.working') : label}
    </button>
  )
}

/** "Reporte de inventario": PDF con todos los productos que cumplen los filtros (inventario al momento). */
export function InventoryReportButton({ filters, className }: ProductReportButtonProps) {
  const t = useT()
  const ctx = useReportContext(filters)
  return (
    <ReportButton
      className={className}
      label={t('warehouse.products.invReport')}
      hint={t('warehouse.products.invReportHint')}
      run={() => generateInventoryReport(ctx)}
    />
  )
}

/** "Reporte de ajustes": PDF con los movimientos ADJUSTMENT de los productos que cumplen los filtros. */
export function AdjustmentsReportButton({ filters, className }: ProductReportButtonProps) {
  const t = useT()
  const ctx = useReportContext(filters)
  return (
    <ReportButton
      className={className}
      label={t('warehouse.products.adjReport')}
      hint={t('warehouse.products.adjReportHint')}
      run={() => generateAdjustmentsReport(ctx)}
    />
  )
}
