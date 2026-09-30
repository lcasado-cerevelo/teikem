// Filtro del panel 'Almacén' de Pulso (Lote F7A): almacén ('Todos los almacenes' + activos) y categoría o producto
// (CategoryProductPicker del kit). La última selección se guarda en localStorage por compañía y usuario y se restaura al
// abrir Pulso; si el almacén, la categoría o el producto guardados ya no existen (o están dados de baja) se limpian sin error.
import { useId, type CSSProperties } from 'react'
import { useT } from '../../kernel/i18n/useT'
import { CategoryProductPicker } from '../../kernel/ui/CategoryProductPicker'
import { IconChevronDown } from '../../kernel/ui/icons'
import type { CategoryProductValue } from '../../kernel/ui/categoryTree'
import { useProductCategories, useWarehouses, warehouseLabel } from '../warehouse/api'
import type { WarehousePulseFilter } from './api'
import './pulse.css'

// Una fila que envuelve: en escritorio los dos controles lado a lado (con tope de ancho); a 360 px no caben juntos y cada
// uno pasa a su renglón ocupando todo el ancho (flex-grow), sin media queries.
const ROW: CSSProperties = { display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center', marginBottom: 14, minWidth: 0 }
const WH_BOX: CSSProperties = {
  flex: '1 1 220px',
  maxWidth: 320,
  minWidth: 0,
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  background: 'var(--bg-2)',
  border: '1px solid var(--line)',
  borderRadius: 9,
  padding: '0 4px 0 9px',
  fontSize: 12.5,
}
const WH_CAPTION: CSSProperties = { color: 'var(--faint)', fontSize: 11, flex: 'none' }
const WH_SELECT: CSSProperties = {
  flex: 1,
  minWidth: 0,
  background: 'transparent',
  border: 'none',
  color: 'var(--text)',
  fontSize: 12.5,
  fontWeight: 600,
  padding: '7px 2px',
  textOverflow: 'ellipsis',
}
// Base de 340 px = ancho del desplegable del picker (`.cpp .mp`): si los dos controles comparten renglón, este nunca queda
// más angosto que su desplegable, que se ancla a la izquierda y el `overflow: hidden` del panel recortaría (tablet vertical).
// Si no cabe, pasa a su propio renglón y ocupa todo el ancho.
const ITEM_BOX: CSSProperties = { flex: '1 1 340px', maxWidth: 440, minWidth: 0 }

export interface WarehouseFilterProps {
  filter: WarehousePulseFilter
  onChange: (filter: WarehousePulseFilter) => void
  warehouses: ReturnType<typeof useWarehouses>
  categories: ReturnType<typeof useProductCategories>
}

/** Fila de filtros del panel 'Almacén': envuelve; a 360 px cada control ocupa todo el ancho. */
export function WarehouseFilter({ filter, onChange, warehouses, categories }: WarehouseFilterProps) {
  const t = useT()
  const selectId = useId()
  const list = warehouses.data ?? []
  // Mientras llegan los almacenes, el guardado se conserva como opción para que el <select> no lo pierda.
  const keepSaved = Boolean(filter.warehousePublicId) && !list.some((w) => w.publicId === filter.warehousePublicId)

  return (
    <div role="group" aria-label={t('analytics.pulse.warehouse.filters')} style={ROW}>
      <label htmlFor={selectId} style={WH_BOX}>
        {/* flecha a la izquierda de la caja, como en el CategoryProductPicker vecino; el select no pinta la suya
            (su `background` en línea anula el chevron de fondo de base.css) */}
        <span className="wh-filter-chev" aria-hidden="true">
          <IconChevronDown />
        </span>
        <span style={WH_CAPTION}>{t('analytics.pulse.warehouse.warehouseFilter')}</span>
        <select
          id={selectId}
          style={WH_SELECT}
          value={filter.warehousePublicId ?? ''}
          onChange={(e) => onChange({ ...filter, warehousePublicId: e.target.value || null })}
        >
          <option value="">{t('analytics.pulse.warehouse.allWarehouses')}</option>
          {keepSaved && filter.warehousePublicId && <option value={filter.warehousePublicId}>{t('common.loading')}</option>}
          {list.map((w) => (
            <option key={w.publicId} value={w.publicId ?? ''}>
              {warehouseLabel(w)}
            </option>
          ))}
        </select>
      </label>
      <div style={ITEM_BOX}>
        <CategoryProductPicker
          value={filter.item}
          onChange={(item: CategoryProductValue) => onChange({ ...filter, item })}
          categories={categories.data ?? []}
          categoriesLoading={categories.isLoading}
        />
      </div>
    </div>
  )
}
