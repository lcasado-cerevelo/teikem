// Fila de filtro «campo · operador · valor» (los 16 operadores del DSL). La comparten el editor de Indicadores/Gráficos
// (`DefinitionEditor`) y el constructor de Vistas (`ViewBuilderModal`, F-F2); ambos las unen con Y (`serializeFilterRows`).
import { useId } from 'react'
import { useT } from '../../kernel/i18n/useT'
import './filterRow.css'
import { FILTER_OPS, LIST_OPS, NO_VALUE_OPS, RANGE_OPS, type FilterOp, type FilterRow } from './definitions'

export interface FilterRowEditorProps {
  row: FilterRow
  fields: { key?: string | null; label?: string | null }[]
  onChange: (patch: Partial<FilterRow>) => void
  onRemove: () => void
}

export function FilterRowEditor({ row, fields, onChange, onRemove }: FilterRowEditorProps) {
  const t = useT()
  const id = useId()
  const showValue = !NO_VALUE_OPS.has(row.op)
  const showSecondValue = RANGE_OPS.has(row.op)
  return (
    <div className="fr-row">
      <div className="f">
        <label htmlFor={`${id}-field`}>{t('analytics.editor.filterField')}</label>
        <select id={`${id}-field`} value={row.field} onChange={(e) => onChange({ field: e.target.value })}>
          {fields.map((f) => (
            <option key={f.key} value={f.key ?? ''}>
              {f.label ?? f.key}
            </option>
          ))}
        </select>
      </div>
      <div className="f">
        <label htmlFor={`${id}-op`}>{t('analytics.editor.filterOp')}</label>
        <select id={`${id}-op`} value={row.op} onChange={(e) => onChange({ op: e.target.value as FilterOp })}>
          {FILTER_OPS.map((op) => (
            <option key={op} value={op}>
              {t(`analytics.editor.filterOps.${op}`)}
            </option>
          ))}
        </select>
      </div>
      {showValue && (
        <div className="f">
          <label htmlFor={`${id}-value`}>{t('analytics.editor.filterValue')}</label>
          <div style={{ display: 'flex', gap: 6 }}>
            <input
              id={`${id}-value`}
              style={{ minWidth: 0, flex: 1 }}
              value={row.value}
              onChange={(e) => onChange({ value: e.target.value })}
              placeholder={LIST_OPS.has(row.op) ? t('analytics.editor.filterValueListPh') : undefined}
            />
            {showSecondValue && (
              <input aria-label={t('analytics.editor.filterValue2')} style={{ minWidth: 0, flex: 1 }} value={row.value2} onChange={(e) => onChange({ value2: e.target.value })} />
            )}
          </div>
        </div>
      )}
      <button type="button" className="btn sm" onClick={onRemove} aria-label={t('analytics.editor.removeFilter')}>
        ✕
      </button>
    </div>
  )
}
