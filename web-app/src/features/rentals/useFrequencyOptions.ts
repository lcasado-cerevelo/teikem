// Lote F17 (Rentas F-R1) — frecuencias de cobro de una renta (catálogo `RentalBillingFrequency`: Diaria, Semanal, Mensual y
// "Fija" = ONE_TIME), con la etiqueta del catálogo (traducida por el API) o, si falta, la de `rentals.frequencies.*`.
import { useCallback, useMemo } from 'react'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { RATE_FREQUENCIES } from './rentalRules'

/** Opciones `{ value, label }` en el orden del catálogo (o DAILY, WEEKLY, MONTHLY, ONE_TIME si no carga). */
export function useFrequencyOptions() {
  const t = useT()
  const { data = [] } = useLookups('RentalBillingFrequency')
  return useMemo(() => {
    const known: readonly string[] = RATE_FREQUENCIES
    const fromCatalog = data.filter((o) => known.includes(o.code.toUpperCase())).map((o) => o.code.toUpperCase())
    const codes = fromCatalog.length > 0 ? fromCatalog : [...known]
    return codes.map((code) => ({ value: code, label: data.find((o) => o.code.toUpperCase() === code)?.label || t(`rentals.frequencies.${code}`) }))
  }, [data, t])
}

/** Etiqueta de una frecuencia por su código (la del DTO si viene; si no, la del catálogo o la propia). */
export function useFrequencyLabel() {
  const options = useFrequencyOptions()
  return useCallback(
    (code: string | null | undefined, label?: string | null) => label || options.find((o) => o.value === (code ?? '').toUpperCase())?.label || code || '',
    [options],
  )
}
