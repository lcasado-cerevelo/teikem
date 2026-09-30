// Lote 16 — opciones del selector "Modo de recepción" (ficha y alta del almacén, encabezado del recibo): los dos modos
// SIEMPRE (el valor del formulario debe tener su <option> aunque el catálogo aún no llegue), con la etiqueta del catálogo
// `ReceivingMode` del tenant y, sin catálogo, la de la interfaz (`warehouse.receivingModes.*`).
import { useMemo } from 'react'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { RECEIVING_MODE_DOMAIN, RECEIVING_MODES } from './receivingMode'

export function useReceivingModeOptions(): { value: string; label: string }[] {
  const t = useT()
  const { data: lookups = [] } = useLookups(RECEIVING_MODE_DOMAIN)
  return useMemo(
    () =>
      [RECEIVING_MODES.putaway, RECEIVING_MODES.direct].map((code) => ({
        value: code,
        label: lookups.find((o) => o.code.toUpperCase() === code)?.label ?? t(`warehouse.receivingModes.${code}`),
      })),
    [lookups, t],
  )
}
