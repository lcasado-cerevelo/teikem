// Filtro de fecha compacto con rangos predefinidos (2026-10-12; docs/filtro-fecha-diseno.md). Una celda de filtro de tamaño normal con un
// desplegable (Todas las fechas, Hoy, Este mes… Personalizado); solo con «Personalizado» aparecen, en la misma línea, los dos campos de fecha,
// también compactos (la celda se ensancha; así quedan alineados con los demás filtros). El valor sigue siendo `{ from, to }` ('YYYY-MM-DD' o ''): las pantallas no cambian su lógica ni su consulta; el desplegable
// DEDUCE el rango de las fechas (así funciona igual si la pantalla llega con fechas ya puestas) y escoger un rango escribe las fechas
// concretas. «Hoy» y el primer día de la semana son los de la compañía (zona horaria y ajuste de formato).
import { useId, useRef, useState } from 'react'
import { getFormatSettings, todayIso } from '../format'
import { useLang, useT } from '../i18n/useT'
import { DATE_PRESETS, matchPreset, presetRange, type DatePresetContext, type DatePresetId, type DatePresetMode } from './datePresets'
import { EMPTY_RANGE, type DateRange } from './dateRange'
import { dateRangeFilterText } from './filterRegistry'
import { useRegisterFilter } from './filterScopeContext'
import './dateRangeFilter.css'

export interface DateRangeFilterProps {
  label: string
  value: DateRange
  onChange: (value: DateRange) => void
  /** Juego de rangos: 'past' (por omisión: movimientos, documentos creados), 'future' (fechas esperadas) o 'both' (pasado reciente y futuro). */
  mode?: DatePresetMode
  /** Arranca con «Personalizado» abierto (los dos campos a la vista): para pantallas que ya eligieron «fechas» en otro control. */
  startCustom?: boolean
}

const sameRange = (a: DateRange | null, b: DateRange): boolean => a !== null && a.from === b.from && a.to === b.to

export function DateRangeFilter({ label, value, onChange, mode = 'past', startCustom = false }: DateRangeFilterProps) {
  const t = useT()
  const lang = useLang()
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  const ctx: DatePresetContext = { today: todayIso(), weekStartDay: getFormatSettings().weekStartDay }

  // «Personalizado» se recuerda con las fechas que tenía al escogerlo o al editarlas: mientras el valor siga siendo ese se muestran los
  // dos campos (aunque las fechas coincidan con un rango: no se cambia el desplegable mientras se escribe); si el valor cambia desde
  // afuera («Limpiar», un enlace) vuelve a deducirse.
  const [customFor, setCustomFor] = useState<DateRange | null>(startCustom ? value : null)
  const selected: DatePresetId = sameRange(customFor, value) ? 'custom' : matchPreset(value, ctx, mode)

  const choose = (next: DatePresetId) => {
    if (next === 'custom') {
      setCustomFor(value)
      return
    }
    setCustomFor(null)
    onChange(presetRange(next, ctx) ?? EMPTY_RANGE)
  }
  const edit = (patch: Partial<DateRange>) => {
    const next = { ...value, ...patch }
    setCustomFor(next)
    onChange(next)
  }

  // en el ámbito (línea de filtros de las exportaciones): siempre las FECHAS concretas, "del 01/10/2026 al 31/10/2026", nunca el nombre del
  // rango («Este mes» no dice de qué días es un reporte guardado)
  useRegisterFilter(label, dateRangeFilterText(value, lang, t), ref)

  const custom = selected === 'custom'
  return (
    <div className={`f drf${custom ? ' drf-open' : ''}`} role="group" aria-labelledby={`${id}-l`} ref={ref}>
      <div className="drf-row">
        <div className="drf-col">
          <label id={`${id}-l`} htmlFor={`${id}-preset`}>
            {label}
          </label>
          <select id={`${id}-preset`} value={selected} onChange={(e) => choose(e.target.value as DatePresetId)}>
            {DATE_PRESETS[mode].map((p) => (
              <option key={p} value={p}>
                {t(`ui.filters.range.${p}`)}
              </option>
            ))}
          </select>
        </div>
        {custom && (
          <>
            <div className="drf-col">
              <label htmlFor={`${id}-from`}>{t('ui.filters.from')}</label>
              <input id={`${id}-from`} type="date" value={value.from} max={value.to || undefined} onChange={(e) => edit({ from: e.target.value })} />
            </div>
            <div className="drf-col">
              <label htmlFor={`${id}-to`}>{t('ui.filters.to')}</label>
              <input id={`${id}-to`} type="date" value={value.to} min={value.from || undefined} onChange={(e) => edit({ to: e.target.value })} />
            </div>
          </>
        )}
      </div>
    </div>
  )
}
