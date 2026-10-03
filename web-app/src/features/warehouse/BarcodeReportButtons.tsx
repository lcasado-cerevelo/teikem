// Lote F14 — botones "Códigos de barras" (PDF con un código Code 128 por fila para imprimir y escanear el papel en el
// conteo): el de productos en 'Productos e inventario' (junto a los reportes de inventario y de ajustes) y el de posiciones
// en Ubicaciones y en la pestaña Posiciones de la ficha del almacén. Mismo comportamiento que los demás reportes
// (`ReportButton`: "Generando…" y deshabilitado mientras corre; error → toast) y mismo permiso que las pantallas que los
// muestran (`inventory.view`). Lo que sale es EXACTAMENTE lo filtrado: la pantalla pasa su estado de filtros o su
// consulta del listado; los "Filtros aplicados" de posiciones se leen de la barra de la pantalla (`useAppliedFilters`).
// Al lado, un selector pequeño "Automático / 2 columnas" (el kit no tiene un patrón de opciones para botones de reporte):
// automático = rejilla de 3 columnas si todos los códigos caben, si no 2 o 1; "2 columnas" = códigos más anchos.
//   <ProductBarcodeReportButton filters={filters} />
//   <BinBarcodeReportButton warehousePublicId={id} warehouse={{ code, name }} zones={zones} query={impossible ? null : query} />
import { useContext, useId, useState } from 'react'
import { SessionContext } from '../../app/session'
import { Can } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import { useAppliedFilters } from '../../kernel/ui'
import type { BarcodeColumnsOption } from '../../kernel/ui/barcodeReportPdf'
import type { ReportFilter } from '../../kernel/ui/reportPdf'
import type { WarehouseZoneDto } from './api'
import { generateBinBarcodeReport, generateProductBarcodeReport } from './barcodeReports'
import { ReportButton } from './InventoryReportButtons'
import type { BinListQuery } from './locations'
import type { ProductFilterState } from './productFilters'
import { useProductReportContext } from './useProductReportContext'

/** Selector de columnas junto al botón (etiqueta solo para lectores de pantalla; el título la muestra al pasar el mouse). */
function ColumnsSelect({ value, onChange }: { value: BarcodeColumnsOption; onChange: (v: BarcodeColumnsOption) => void }) {
  const t = useT()
  const id = useId()
  return (
    <>
      <label htmlFor={id} className="sr-only">
        {t('warehouse.barcodes.columns')}
      </label>
      <select id={id} className="bc-cols" title={t('warehouse.barcodes.columns')} value={String(value)} onChange={(e) => onChange(e.target.value === '2' ? 2 : 'auto')}>
        <option value="auto">{t('warehouse.barcodes.columnsAuto')}</option>
        <option value="2">{t('warehouse.barcodes.columnsTwo')}</option>
      </select>
    </>
  )
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
  const [columns, setColumns] = useState<BarcodeColumnsOption>('auto')
  return (
    <Can perm={BARCODE_REPORT_PERMISSION}>
      <span className="bc-report">
        <ReportButton
          className={className}
          label={t('warehouse.barcodes.button')}
          hint={t('warehouse.barcodes.productsHint')}
          run={() => generateProductBarcodeReport(ctx, columns)}
        />
        <ColumnsSelect value={columns} onChange={setColumns} />
      </span>
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
  const [columns, setColumns] = useState<BarcodeColumnsOption>('auto')
  return (
    <Can perm={BARCODE_REPORT_PERMISSION}>
      <span className="bc-report">
        <ReportButton
          className={className}
          label={t('warehouse.barcodes.button')}
          hint={t('warehouse.barcodes.binsHint')}
          run={() =>
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
            )
          }
        />
        <ColumnsSelect value={columns} onChange={setColumns} />
      </span>
    </Can>
  )
}
