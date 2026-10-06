// Lote F17 (Rentas F-R1) — insignia de vencimiento de una renta abierta (Programada o En renta): "Vencida hace N días"
// (rojo), "Se recoge hoy" / "Vence en N días" (ámbar, hasta 7 días) o "En N días" (neutro). Es un dato calculado por el
// servidor (`daysToPickup`, `isOverdue`, con el día de la compañía), nunca un estatus. Sin renta abierta: "—".
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { dueLabel, dueState, type RentalListItemDto } from './rentalRules'

export function DueChip({ rental }: { rental: Pick<RentalListItemDto, 'statusCode' | 'daysToPickup' | 'isOverdue'> }) {
  const t = useT()
  const state = dueState(rental)
  if (!state) return <span aria-label={t('rentals.due.none')}>—</span>
  const text = dueLabel(t, state, rental.daysToPickup)
  const tone = state === 'overdue' ? 'fail' : state === 'later' ? 'neutral' : 'warn'
  return <Chip tone={tone}>{text}</Chip>
}
