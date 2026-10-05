// Lote F14 — botones "Códigos de barras" (PDF con un código Code 128 por fila para imprimir y escanear el papel en el
// conteo): el de productos en 'Productos e inventario' (junto a los reportes de inventario y de ajustes) y el de posiciones
// en Ubicaciones y en la pestaña Posiciones de la ficha del almacén. Mismo comportamiento que los demás reportes
// (`ReportButton`: "Generando…" y deshabilitado mientras corre; error → toast) y mismo permiso que las pantallas que los
// muestran (`inventory.view`). Lo que sale es EXACTAMENTE lo filtrado: la pantalla pasa su estado de filtros o su
// consulta del listado; los "Filtros aplicados" de posiciones se leen de la barra de la pantalla (`useAppliedFilters`).
// 2026-10-05: el botón es un MENÚ (`ReportMenuButton`) con "Automático" y "2 columnas"; al elegir una opción se genera el PDF (antes había
// un selector aparte al lado). Automático = rejilla de 3 columnas si todos los códigos caben, si no 2 o 1; "2 columnas" = códigos más anchos.
//   <ProductBarcodeReportButton filters={filters} />
//   <BinBarcodeReportButton warehousePublicId={id} warehouse={{ code, name }} zones={zones} query={impossible ? null : query} />
import { useContext } from 'react'
import { SessionContext } from '../../app/session'
import { Can } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import { IconDoc, ReportMenuButton, useAppliedFilters, type ReportMenuItem } from '../../kernel/ui'
import type { BarcodeColumnsOption } from '../../kernel/ui/barcodeReportPdf'
import type { ReportFilter } from '../../kernel/ui/reportPdf'
import type { WarehouseZoneDto } from './api'
import { generateBinBarcodeReport, generateProductBarcodeReport } from './barcodeReports'
import type { BinListQuery } from './locations'
import type { ProductFilterState } from './productFilters'
import { useProductReportContext } from './useProductReportContext'

/** Opciones del menú del botón (2026-10-05, Luis): "Códigos de barras" es un menú; al elegir una opción se genera el PDF. Reemplaza el
 *  selector "Automático / 2 columnas" que iba al lado. */
function useColumnItems(run: (columns: BarcodeColumnsOption) => Promise<void>): ReportMenuItem[] {
  const t = useT()
  return [
    { key: 'auto', label: t('warehouse.barcodes.columnsAuto'), hint: t('warehouse.barcodes.columnsAutoHint'), run: () => run('auto') },
    { key: '2', label: t('warehouse.barcodes.columnsTwo'), hint: t('warehouse.barcodes.columnsTwoHint'), run: () => run(2) },
  ]
}

/** Permiso de los reportes de códigos: el de las pantallas donde están (y el de sus otros reportes y Exportar). */
export const BARCODE_REPORT_PERMISSION = 'inventory.view'

export interface ProductBarcodeReportButtonProps {
  filters: ProductFilterState
  className?: string
}

/** "Códigos de barras" de Productos e inventario: un código por SKU de lo filtrado. */
export function ProductBarcodeReportButton({ filters, className }: ProductBarcodeReportButtonProps) {
  const t = useT()
  const ctx = useProductReportContext(filters)
  const items = useColumnItems((columns) => generateProductBarcodeReport(ctx, columns))
  return (
    <Can perm={BARCODE_REPORT_PERMISSION}>
      <ReportMenuButton
        className={className}
        label={t('warehouse.barcodes.button')}
        hint={t('warehouse.barcodes.productsHint')}
        icon={<IconDoc />}
        items={items}
        errorMessage={t('warehouse.products.reports.error')}
        workingLabel={t('warehouse.products.reports.working')}
        menuLabel={t('warehouse.barcodes.columns')}
      />
    </Can>
  )
}

export interface BinBarcodeReportButtonProps {
  warehousePublicId: string
  /** Almacén de las posiciones (código y nombre: la línea gris de cada fila). */
  warehouse: { code?: string | null; name?: string | null }
  zones: readonly WarehouseZoneDto[]
  /** Consulta del listado de la tabla sin `skip`/`take`; null = los filtros no dejan ninguna zona (tabla vacía). */
  query: BinListQuery | null
  /** Filtros que no están en la barra de la pantalla y van primero en "Filtros aplicados" (p. ej. el almacén de la ficha). */
  extraFilters?: readonly ReportFilter[]
  className?: string
}

/** "Códigos de barras" de posiciones: un código por posición de lo filtrado. */
export function BinBarcodeReportButton({ warehousePublicId, warehouse, zones, query, extraFilters, className }: BinBarcodeReportButtonProps) {
  const t = useT()
  const lang = useLang()
  const me = useContext(SessionContext)?.me
  const appliedFilters = useAppliedFilters()
  const items = useColumnItems((columns) =>
    generateBinBarcodeReport(
      warehousePublicId,
      query,
      {
        t,
        lang,
        company: me?.tenantName,
        user: me?.fullName || me?.email,
        // la barra de la pantalla en el momento del clic (mismo texto que la línea de filtros de Exportar)
        filters: [...(extraFilters ?? []), ...appliedFilters()],
        warehouse,
        zones,
      },
      columns,
    ),
  )
  return (
    <Can perm={BARCODE_REPORT_PERMISSION}>
      <ReportMenuButton
        className={className}
        label={t('warehouse.barcodes.button')}
        hint={t('warehouse.barcodes.binsHint')}
        icon={<IconDoc />}
        items={items}
        errorMessage={t('warehouse.products.reports.error')}
        workingLabel={t('warehouse.products.reports.working')}
        menuLabel={t('warehouse.barcodes.columns')}
      />
    </Can>
  )
}
