// Lote 12 — botones "Reporte de inventario" y "Reporte de ajustes": generan el PDF (inventoryReports.ts) con los filtros que
// la pantalla les pasa (`ProductFilterState`). Resuelven solos los nombres de almacenes y categorías (mismas consultas en
// caché que los filtros), la compañía activa y el usuario. Mientras se genera, el botón queda ocupado ("Generando…"); un
// error se avisa con un toast. Los usa 'Productos e inventario'; el de ajustes también 'Transferencias y ajustes' (Lote 14,
// hallazgo 22), con sus propios filtros ya armados (fechas, dueño, dirección, motivo…):
//   <AdjustmentsReportButton filters={{ ...EMPTY_PRODUCT_FILTERS, warehouses: [whId] }} />
//   <AdjustmentsReportButton query={{ kardexQuery, filterLabels }} />
import { useState } from 'react'
import { useT } from '../../kernel/i18n'
import { toast } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { generateAdjustmentsReport, generateInventoryReport, type AdjustmentsReportQuery, type ProductReportContext } from './inventoryReports'
import { EMPTY_PRODUCT_FILTERS, type ProductFilterState } from './productFilters'
import { useProductReportContext } from './useProductReportContext'

export interface ProductReportButtonProps {
  filters: ProductFilterState
  className?: string
}

/** Botón de reporte: "Generando…" y deshabilitado mientras corre `run`; si falla, toast de error. Lo comparten los reportes
 *  de Productos e inventario y los de códigos de barras (Lote F14). */
export function ReportButton({ label, hint, className, run }: { label: string; hint: string; className?: string; run: () => Promise<void> }) {
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
  const ctx = useProductReportContext(filters)
  return (
    <ReportButton
      className={className}
      label={t('warehouse.products.invReport')}
      hint={t('warehouse.products.invReportHint')}
      run={() => generateInventoryReport(ctx)}
    />
  )
}

export interface AdjustmentsReportButtonProps {
  /** Filtros de 'Productos e inventario' (se trasladan al Kárdex). */
  filters?: ProductFilterState
  /** Lote 14: consulta del Kárdex ya armada y sus "Filtros aplicados" (Transferencias y ajustes); manda sobre `filters`. */
  query?: AdjustmentsReportQuery
  className?: string
}

/** "Reporte de ajustes": PDF con los movimientos ADJUSTMENT que cumplen los filtros (de Productos o de la consulta dada). */
export function AdjustmentsReportButton({ filters, query, className }: AdjustmentsReportButtonProps) {
  const t = useT()
  const base = useProductReportContext(filters ?? EMPTY_PRODUCT_FILTERS)
  const ctx: ProductReportContext = query ? { ...base, adjustments: query } : base
  return (
    <ReportButton
      className={className}
      label={t('warehouse.products.adjReport')}
      hint={t('warehouse.products.adjReportHint')}
      run={() => generateAdjustmentsReport(ctx)}
    />
  )
}
