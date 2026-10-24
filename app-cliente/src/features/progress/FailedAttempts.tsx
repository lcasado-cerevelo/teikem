// Píldora de «intentos fallidos» (2026-10-24, pedido de Luis): una orden que sale como «Asignada» o «Sin asignar» pero ya salió a la calle y volvió no está retrasada, está reprogramada; la píldora
// dice cuántos intentos fallidos lleva («1 intento fallido», «2 intentos fallidos») para que se vea de un vistazo. Sin intentos no se pinta nada.
import { View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/ui'
import { statusTone } from '../format'

export function FailedAttemptsChip({ count }: { count?: number | null }) {
  const { t } = useT()
  const n = count ?? 0
  if (n <= 0) return null
  return <Chip label={t(n === 1 ? 'orders.attempts.one' : 'orders.attempts.many', { n })} tone="warn" />
}

/** El estatus (chip de color) y, debajo, la píldora de intentos fallidos si los hay. */
export function StatusWithAttempts({ status, label, attempts }: { status?: string | null; label?: string | null; attempts?: number | null }) {
  return (
    <View style={{ gap: 4, alignItems: 'flex-start' }}>
      <Chip label={label ?? status ?? ''} tone={statusTone(status)} />
      <FailedAttemptsChip count={attempts} />
    </View>
  )
}
