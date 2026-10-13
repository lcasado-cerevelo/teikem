// Textos fijos del label de envío (De:, A:, Waybill, Factura, COD, «Paquete i de N») en el idioma del usuario.
import { useMemo } from 'react'
import { useT } from '../../kernel/i18n'
import type { LabelTexts } from '../../kernel/ui/orderLabelLayout'

export function useLabelTexts(): LabelTexts {
  const t = useT()
  return useMemo(
    () => ({
      from: t('printing.label.from'),
      to: t('printing.label.to'),
      waybill: t('printing.label.waybill'),
      invoice: t('printing.label.invoice'),
      cod: t('printing.label.cod'),
      packageOf: t('printing.label.packageOf'),
    }),
    [t],
  )
}
