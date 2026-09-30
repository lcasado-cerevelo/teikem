// Lote 14 (P7, hallazgo 6) — barra de filtros COMPARTIDA por las tres pestañas de 'Kárdex de movimientos' (Kárdex, Saldos y
// Conciliación). El estado (`InventoryFilterState`, `kardexView.ts`) vive en la pantalla: cambiar de pestaña no descarta
// nada. Filtros: Fecha, Tipo, Almacén, Posición (`BinMultiFilter`, entre almacenes), Producto (con dados de baja), Categoría
// (con subcategorías), Dueño (`OwnerFilter`), Motivo, Dirección (entradas/salidas), Lote, Serie y "Solo manuales"; además
// "Incluir en cero" y "Solo con disponible" en Saldos y Estatus en Conciliación. Un filtro que la pestaña no aplica
// (`TAB_FILTERS`) se atenúa y, si tiene valor, se nombra en la ayuda bajo la barra. El documento de origen que llega en la
// URL (`refEntity`/`refId`) se muestra como píldora que se puede quitar.
import { useId, useMemo, type ReactNode } from 'react'
import { useLookups, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, DateRangeFilter, Filters, SearchSelect, SelectFilter } from '../../kernel/ui'
import { IconClose } from '../../kernel/ui/icons'
import { useProductCategories, useWarehouses, warehouseLabel } from './api'
import { ToggleFilter } from './filterControls'
import {
  DEFAULT_DISCREPANCY_STATUS,
  EMPTY_INVENTORY_FILTERS,
  filterApplies,
  inactiveFilters,
  type InventoryFilterKey,
  type InventoryFilterState,
  type InventoryTab,
  type KardexDirection,
} from './kardexView'
import { BinMultiFilter, OwnerFilter, ProductMultiFilter, type ProductFilterItem } from './pickers'
import './warehouse.css'

export interface InventoryFilterBarProps {
  tab: InventoryTab
  value: InventoryFilterState
  /** Cambio parcial (la pantalla vuelve a la página 1). */
  onChange: (patch: Partial<InventoryFilterState>) => void
  /** Productos para pintar (con su SKU ya resuelto cuando llegaron por URL). */
  shownProducts: ProductFilterItem[]
}

const F = 'warehouse.inventory.filters'

export function InventoryFilterBar({ tab, value, onChange, shownProducts }: InventoryFilterBarProps) {
  const t = useT()
  const lotId = useId()
  const serialId = useId()
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const { data: categories = [] } = useProductCategories({}, { handleAccessDenied: false })
  const categoryOptions = useMemo(() => categories.map((c) => ({ value: String(c.id), label: c.path || c.name || String(c.id) })), [categories])
  const { data: txnTypes = [] } = useLookups('InventoryTxnType')
  const typeOptions = useMemo(() => txnTypes.map((o) => ({ value: o.code, label: o.label })), [txnTypes])
  const { data: reasons = [] } = useLookups('AdjustmentReason', { includeDisabled: true })
  const reasonOptions = useMemo(() => reasons.map((o) => ({ value: o.code, label: o.label })), [reasons])
  const { data: statuses = [] } = useStatuses('InventoryDiscrepancyStatus')
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])

  const unused = inactiveFilters(tab, value)
  /** Envuelve un filtro que la pestaña no aplica (atenuado; sigue editable porque el estado es compartido). Sin valor, en el
   *  celular se oculta (`.f-na.empty`) para no alargar la barra. */
  const wrap = (key: InventoryFilterKey, node: ReactNode) =>
    filterApplies(tab, key) ? (
      node
    ) : (
      <div className={unused.includes(key) ? 'f-na' : 'f-na empty'} key={key}>
        {node}
      </div>
    )
  const filterName = (key: InventoryFilterKey) => t(`${F}.names.${key}`)

  return (
    <>
      <Filters
        label={t(`${F}.label`)}
        onClear={() => onChange({ ...EMPTY_INVENTORY_FILTERS, discrepancyStatus: [...DEFAULT_DISCREPANCY_STATUS] })}
      >
        {wrap('range', <DateRangeFilter label={t(`${F}.range`)} value={value.range} onChange={(range) => onChange({ range })} />)}
        {wrap('types', <SearchSelect label={t(`${F}.type`)} options={typeOptions} value={value.types} onChange={(types) => onChange({ types })} />)}
        {wrap(
          'warehouses',
          <SearchSelect
            label={t(`${F}.warehouse`)}
            options={warehouseOptions}
            value={value.warehousePublicIds}
            onChange={(warehousePublicIds) => onChange({ warehousePublicIds })}
          />,
        )}
        {wrap(
          'bins',
          <BinMultiFilter
            label={t(`${F}.bin`)}
            value={value.bins}
            onChange={(bins) => onChange({ bins })}
            warehousePublicIds={value.warehousePublicIds}
            includeInactive
          />,
        )}
        {wrap('products', <ProductMultiFilter label={t(`${F}.product`)} value={shownProducts} onChange={(products) => onChange({ products })} includeInactive />)}
        {wrap(
          'categories',
          <SearchSelect label={t(`${F}.category`)} options={categoryOptions} value={value.categoryIds} onChange={(categoryIds) => onChange({ categoryIds })} />,
        )}
        {wrap('owners', <OwnerFilter label={t(`${F}.owner`)} value={value.owners} onChange={(owners) => onChange({ owners })} />)}
        {wrap('reasons', <SearchSelect label={t(`${F}.reason`)} options={reasonOptions} value={value.reasons} onChange={(reasons) => onChange({ reasons })} />)}
        {wrap(
          'direction',
          <SelectFilter
            label={t(`${F}.direction`)}
            value={value.direction}
            onChange={(v) => onChange({ direction: v as KardexDirection })}
            options={[
              { value: 'IN', label: t(`${F}.directionIn`) },
              { value: 'OUT', label: t(`${F}.directionOut`) },
            ]}
          />,
        )}
        {wrap(
          'lot',
          <div className="f">
            <label htmlFor={lotId}>{t(`${F}.lot`)}</label>
            <input id={lotId} type="text" value={value.lotNumber} maxLength={60} onChange={(e) => onChange({ lotNumber: e.target.value })} />
          </div>,
        )}
        {wrap(
          'serial',
          <div className="f">
            <label htmlFor={serialId}>{t(`${F}.serial`)}</label>
            <input id={serialId} type="text" value={value.serialNumber} maxLength={80} onChange={(e) => onChange({ serialNumber: e.target.value })} />
          </div>,
        )}
        {wrap('manualOnly', <ToggleFilter label={t(`${F}.manualOnly`)} checked={value.manualOnly} onChange={(manualOnly) => onChange({ manualOnly })} />)}
        {tab === 'balances' && (
          <>
            <ToggleFilter label={t(`${F}.includeZero`)} checked={value.includeZero} onChange={(includeZero) => onChange({ includeZero })} />
            <ToggleFilter label={t(`${F}.onlyAvailable`)} checked={value.onlyAvailable} onChange={(onlyAvailable) => onChange({ onlyAvailable })} />
          </>
        )}
        {tab === 'reconciliation' && (
          <SearchSelect
            label={t(`${F}.status`)}
            options={statusOptions}
            value={value.discrepancyStatus}
            onChange={(discrepancyStatus) => onChange({ discrepancyStatus })}
          />
        )}
      </Filters>
      {value.refEntity && value.refId != null && (
        <div className="pfilter-chips filters-note">
          <Chip tone="wh" title={t(`${F}.refTitle`)}>
            <span className="pfilter-text">{t(`${F}.ref`, { entity: value.refEntity, id: value.refId })}</span>
            <button type="button" className="iconbtn" aria-label={t(`${F}.refRemove`)} onClick={() => onChange({ refEntity: '', refId: null })}>
              <IconClose />
            </button>
          </Chip>
        </div>
      )}
      {unused.length > 0 && <p className="filters-note">{t(`${F}.notApplied.${tab}`, { filters: unused.map(filterName).join(', ') })}</p>}
    </>
  )
}
