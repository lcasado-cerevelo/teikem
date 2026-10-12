// Chip de vencimiento de un documento (licencia, certificación, documento de vehículo): lo comparten Choferes (F-D1) y la
// flota (F-D2). Recibe lo que ya calcula el servidor (`expiryState`, `daysToExpiry`, `isSuperseded`; ventana de 30 días) y no
// recalcula fechas. Un documento reemplazado por otro más nuevo del mismo tipo no cuenta como vencido: se pinta neutro.
import { useT } from '../i18n/useT'
import { Chip } from './Chip'
import { expiryChipInfo } from './expiry'

export interface ExpiryChipProps {
  state: string | null | undefined
  days?: number | null
  superseded?: boolean
}

/** `<ExpiryChip state={doc.expiryState} days={doc.daysToExpiry} superseded={doc.isSuperseded} />` */
export function ExpiryChip({ state, days, superseded }: ExpiryChipProps) {
  const t = useT()
  const info = expiryChipInfo(state, days, superseded)
  return (
    <Chip tone={info.tone} title={t(info.titleKey)}>
      {t(info.key, info.params)}
    </Chip>
  )
}
