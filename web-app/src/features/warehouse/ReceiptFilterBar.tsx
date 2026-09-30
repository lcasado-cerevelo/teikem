// Lote 13 — filtros de la lista de recibos (maestro-detalle de Recibo), compartidos por las pestañas Recibos y 'Acomodo
// pendiente'. El archivo no se llama ReceiptFilters.tsx: en Windows './ReceiptFilters' se resolvería a receiptFilters.ts
// (la lógica pura), que solo difiere en mayúsculas. Almacén (buscador de un almacén: el API filtra por uno), Estatus y Tipo (`SearchSelect`), Creado
// (`DateRangeFilter`), Producto (`ProductMultiFilter` con inactivos: es historial) y Diferencia (`SearchSelect`
// Faltante/Sobrante/Sin diferencia → `variance[]`). Todos van al API (`receiptListQuery` de receiptFilters.ts). En 'Acomodo
// pendiente' el Estatus se limita a Completado y Completado con diferencia (`statusCodes`).
import { useId, useMemo } from 'react'
import { useLookups, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateRangeFilter, Filters, SearchSelect } from '../../kernel/ui'
import { ProductMultiFilter, WarehousePicker } from './pickers'
import { EMPTY_RECEIPT_FILTERS, RECEIPT_STATUS_DOMAIN, VARIANCE_CODES, type ReceiptFilterState } from './receiptFilters'

const TYPE_DOMAIN = 'ReceiptType'

export interface ReceiptFilterBarProps {
  value: ReceiptFilterState
  onChange: (next: ReceiptFilterState) => void
  /** Estatus que se ofrecen (por defecto todos los del catálogo). */
  statusCodes?: readonly string[]
}

export function ReceiptFilterBar({ value, onChange, statusCodes }: ReceiptFilterBarProps) {
  const t = useT()
  const whId = useId()
  const { data: statuses = [] } = useStatuses(RECEIPT_STATUS_DOMAIN)
  const { data: types = [] } = useLookups(TYPE_DOMAIN)
  const statusOptions = useMemo(
    () => statuses.filter((s) => !statusCodes || statusCodes.includes(s.code)).map((s) => ({ value: s.code, label: s.label })),
    [statuses, statusCodes],
  )
  const typeOptions = useMemo(() => types.map((s) => ({ value: s.code, label: s.label })), [types])
  const varianceOptions = useMemo(
    () => VARIANCE_CODES.map((c) => ({ value: c, label: t(`warehouse.receipts.filters.varianceOptions.${c}`) })),
    [t],
  )
  const set = <K extends keyof ReceiptFilterState>(key: K, v: ReceiptFilterState[K]) => onChange({ ...value, [key]: v })

  return (
    <Filters onClear={() => onChange(EMPTY_RECEIPT_FILTERS)} label={t('warehouse.receipts.list.aria')}>
      <div className="f">
        <label htmlFor={whId}>{t('warehouse.receipts.filters.warehouse')}</label>
        <WarehousePicker
          id={whId}
          value={value.warehousePublicId}
          onChange={(v) => set('warehousePublicId', v)}
          placeholder={t('warehouse.receipts.filters.anyWarehouse')}
        />
      </div>
      <SearchSelect label={t('warehouse.receipts.filters.status')} options={statusOptions} value={value.status} onChange={(v) => set('status', v)} />
      <SearchSelect label={t('warehouse.receipts.filters.types')} options={typeOptions} value={value.types} onChange={(v) => set('types', v)} />
      <DateRangeFilter label={t('warehouse.receipts.filters.created')} value={value.created} onChange={(v) => set('created', v)} />
      <ProductMultiFilter label={t('warehouse.receipts.filters.product')} value={value.products} onChange={(v) => set('products', v)} includeInactive />
      <SearchSelect label={t('warehouse.receipts.filters.variance')} options={varianceOptions} value={value.variance} onChange={(v) => set('variance', v)} />
    </Filters>
  )
}
