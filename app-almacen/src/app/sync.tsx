import { useMemo, useState } from 'react'
import { ActivityIndicator, ScrollView, StyleSheet, Text } from 'react-native'

import { useT } from '../kernel/i18n/useT'
import { runSync, useLastSync, usePendingCount } from '../kernel/sync/engine'
import { discardRow, listOutbox, retryRow } from '../kernel/sync/outbox'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { colors, spacing } from '../kernel/ui/theme'

/** Pantalla 8 (docs/mobile/app-almacen-plan.md §2): lo que está en la cola de salida (kernel/sync/outbox.ts) y el
 *  resultado de la última pasada. Pendientes se mandan solas en la próxima pasada; con error, cada fila se puede
 *  reintentar o descartar tras revisarla. */
export default function SyncScreen() {
  const { t } = useT()
  const pending = usePendingCount()
  const lastSync = useLastSync()
  const [tick, setTick] = useState(0)
  const [busy, setBusy] = useState(false)
  const refresh = () => setTick((n) => n + 1)

  // tick fuerza releer la cola tras reintentar/descartar/sincronizar; usePendingCount ya se actualiza solo (cuenta
  // viva, y ya haría re-renderizar), pero las filas mismas (kind, error) solo se releen aquí.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const rows = useMemo(() => listOutbox(), [tick])
  const pendingRows = rows.filter((r) => r.status === 'pending')
  const rejectedRows = rows.filter((r) => r.status === 'rejected')

  async function syncNow() {
    setBusy(true)
    try {
      await runSync()
    } finally {
      setBusy(false)
      refresh()
    }
  }

  const lastSyncLabel = lastSync ? new Date(lastSync.ranAtUtc).toLocaleTimeString() : t('sync.never')

  return (
    <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>{t('sync.title')}</Text>
      <Text style={styles.help}>{t('sync.lastSync', { when: lastSyncLabel })}</Text>
      {lastSync?.error ? <Text style={styles.error}>{lastSync.error}</Text> : null}

      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      <BigButton label={t('home.syncNow')} onPress={syncNow} disabled={busy} />

      <Text style={styles.label}>{t('sync.pendingWithCount', { count: pending })}</Text>
      <LineList
        items={pendingRows.map((r) => ({ id: r.id, title: r.kind, subtitle: r.path }))}
        removeLabel={t('common.remove')}
        emptyLabel={t('sync.empty')}
      />

      <Text style={styles.label}>{t('sync.rejectedWithCount', { count: rejectedRows.length })}</Text>
      <LineList
        items={rejectedRows.map((r) => ({ id: r.id, title: r.kind, subtitle: r.last_error ?? undefined }))}
        removeLabel={t('sync.discard')}
        emptyLabel={t('sync.empty')}
        onRemove={(id) => {
          discardRow(Number(id))
          refresh()
        }}
      />
      {rejectedRows.map((r) => (
        <BigButton
          key={r.id}
          label={t('sync.retryRow', { kind: r.kind })}
          variant="secondary"
          onPress={() => {
            retryRow(r.id)
            refresh()
          }}
        />
      ))}
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: 20, fontWeight: '700' },
  label: { color: colors.text, fontSize: 16, fontWeight: '600' },
  help: { color: colors.muted, fontSize: 13 },
  error: { color: colors.error, fontSize: 13 },
})
