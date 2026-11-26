// Los componentes de la tasa por omisión vigente (las casillas de los formularios de exención y los nombres de las listas). Compartido por Exenciones y la captura única.
import { useMemo } from 'react'
import { componentOptions, type ComponentOption } from './taxRules'
import { useTaxRates, useTaxSettings } from './taxApi'

const NO_CODES: string[] = []

export function useComponentOptions(selected: readonly string[] = NO_CODES): ComponentOption[] {
  const rates = useTaxRates()
  const settings = useTaxSettings()
  return useMemo(() => componentOptions(rates.data, settings.data?.defaultRateCode, selected), [rates.data, settings.data?.defaultRateCode, selected])
}
