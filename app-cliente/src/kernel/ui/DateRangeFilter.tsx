// Filtro de fecha compacto con rangos predefinidos (2026-10-23, pedido de Luis para «Mis envíos»: «lo mismo que tenemos en la aplicación web del personal»): un desplegable
// (Todas las fechas, Hoy, Ayer, Últimos 7 días, Esta semana, Semana pasada, Últimos 30 días, Este mes, Mes pasado, Este año, Personalizado) y, solo en Personalizado, los
// campos Desde / Hasta. El valor que ve la pantalla es siempre { from, to } en AAAA-MM-DD; el nombre del rango se deduce de las fechas (docs/filtro-fecha-diseno.md).
import { useState } from 'react'
import { Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import { DateInput } from './DateInput'
import { type DayRange, presetOf, RANGE_PRESETS, type RangePreset, rangeOf } from './dateRanges'
import { Select } from './Select'
import { useColors } from './theme'

export interface DateRangeFilterProps {
  value: DayRange
  onChange: (value: DayRange) => void
  /** Rótulo encima (por omisión «Fecha»). */
  label?: string
  testID?: string
}

export function DateRangeFilter({ value, onChange, label, testID = 'date-range' }: DateRangeFilterProps) {
  const c = useColors()
  const { t } = useT()
  const [customChosen, setCustomChosen] = useState(false)   // Personalizado sin fechas todavía: se sigue mostrando como tal
  const preset: RangePreset = customChosen && !value.from && !value.to ? 'custom' : presetOf(value)
  const options = RANGE_PRESETS.map((p) => ({ value: p, label: t(`dateRange.${p}`) }))

  const pick = (p: string) => {
    const next = p as RangePreset
    setCustomChosen(next === 'custom')
    if (next === 'custom') return   // se conservan las fechas que haya (si vienen de un rango, siguen valiendo como personalizado)
    onChange(rangeOf(next))
  }

  const labelStyle = { fontSize: 13, fontWeight: '600' as const, color: c.text }
  return (
    <View style={{ flexDirection: 'row', gap: 8, alignItems: 'flex-end', flexWrap: 'wrap' }} testID={testID}>
      <View style={{ gap: 4, width: 190 }}>
        <Text style={labelStyle}>{label ?? t('dateRange.label')}</Text>
        <Select value={preset} options={options} onChange={pick} accessibilityLabel={label ?? t('dateRange.label')} testID={`${testID}-preset`} />
      </View>
      {preset === 'custom' && (
        <>
          <View style={{ gap: 4, width: 150 }}>
            <Text style={labelStyle}>{t('dateRange.from')}</Text>
            <DateInput value={value.from} max={value.to || undefined} onChange={(from) => onChange({ ...value, from })} accessibilityLabel={t('dateRange.from')} testID={`${testID}-from`} />
          </View>
          <View style={{ gap: 4, width: 150 }}>
            <Text style={labelStyle}>{t('dateRange.to')}</Text>
            <DateInput value={value.to} min={value.from || undefined} onChange={(to) => onChange({ ...value, to })} accessibilityLabel={t('dateRange.to')} testID={`${testID}-to`} />
          </View>
        </>
      )}
    </View>
  )
}
