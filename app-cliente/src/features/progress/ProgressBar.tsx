// La barra de progreso de una orden: una barra dividida en tantas etapas como estatus tiene el camino normal de la compañía; las que ya pasaron en azul, la actual resaltada («va por aquí»), las que faltan apagadas.
// Si la orden está detenida (en espera, parcial, fallida…) la etapa actual sale en ámbar con el aviso; si fue cancelada, toda apagada; si se entregó, toda en verde. En la ficha y la página pública, con la lista de etapas.
import { Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { colors } from '../../kernel/ui/ui'
import { progressPercent, segmentKinds, stepKind, type ProgressState, type SegmentKind } from './progressRules'

const fill: Record<SegmentKind, string> = {
  done: colors.brand,
  current: colors.flow,
  alert: '#F59E0B',
  todo: '#DCE0E8',
  cancelled: '#C4C9D2',
}

export interface ProgressInfo {
  step: number
  total: number
  state: ProgressState
  statusLabel: string
}

/** La barra sola con su texto («Paso 3 de 8 · Recogido»); `compact` la hace más baja para las listas. */
export function ProgressBar({ progress, compact = false }: { progress: ProgressInfo; compact?: boolean }) {
  const { t } = useT()
  if (progress.total <= 0) return null
  const kinds = segmentKinds(progress.step, progress.total, progress.state)
  const done = progress.state === 'COMPLETED'
  const label =
    progress.state === 'CANCELLED'
      ? progress.statusLabel
      : progress.state === 'ALERT'
        ? t('progress.alert', { step: progress.step, total: progress.total, status: progress.statusLabel })
        : done
          ? progress.statusLabel
          : t('progress.step', { step: progress.step, total: progress.total, status: progress.statusLabel })
  return (
    <View
      accessible
      accessibilityRole="progressbar"
      accessibilityLabel={label}
      accessibilityValue={{ min: 0, max: 100, now: progressPercent(progress.step, progress.total, progress.state) }}
      style={{ gap: 4 }}
    >
      <View style={{ flexDirection: 'row', gap: 3 }}>
        {kinds.map((k, i) => (
          <View key={i} testID={`segment-${k}`} style={{ flex: 1, height: compact ? 6 : 10, borderRadius: 3, backgroundColor: done ? colors.ok : fill[k] }} />
        ))}
      </View>
      <Text style={{ fontSize: compact ? 12 : 13, color: progress.state === 'ALERT' ? colors.warn : colors.muted, fontWeight: progress.state === 'ALERT' ? '700' : '500' }}>{label}</Text>
    </View>
  )
}

/** La lista de etapas (una por estatus): hechas con ✓, la actual resaltada y las que faltan apagadas. */
export function ProgressSteps({ labels, progress }: { labels: readonly string[]; progress: ProgressInfo }) {
  return (
    <View style={{ gap: 2 }}>
      {labels.map((label, i) => {
        const kind = stepKind(i, progress.step, progress.state)
        const on = kind === 'done' || kind === 'current' || kind === 'alert'
        return (
          <View key={`${i}-${label}`} testID={`step-${kind}`} style={{ flexDirection: 'row', alignItems: 'center', gap: 10, paddingVertical: 3 }}>
            <View style={{ width: 18, height: 18, borderRadius: 9, backgroundColor: progress.state === 'COMPLETED' ? colors.ok : fill[kind], alignItems: 'center', justifyContent: 'center' }}>
              {(kind === 'done' || progress.state === 'COMPLETED') && <Text style={{ color: '#fff', fontSize: 11, fontWeight: '800' }}>✓</Text>}
            </View>
            <Text style={{ fontSize: 14, color: on ? colors.text : colors.muted, fontWeight: kind === 'current' || kind === 'alert' ? '800' : '500' }}>{label}</Text>
          </View>
        )
      })}
    </View>
  )
}
