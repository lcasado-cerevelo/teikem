import { useMemo, useState } from 'react'
import { ActivityIndicator, ScrollView, StyleSheet, Text } from 'react-native'

import { useT } from '../kernel/i18n/useT'
import { runSync, useLastSync, usePendingCount } from '../kernel/sync/engine'
import { discardRow, listOutbox, retryRow, type OutboxRow } from '../kernel/sync/outbox'
import { outboxKindLabel } from '../kernel/sync/outboxKind'
import { classifyCountBatchRejection, countIdFromFinishPath, type CorrectedLineRejection } from '../features/count/countRejection'
import { RejectedCountBatch } from '../features/count/RejectedCountBatch'
import { dismissSkippedNotice, listSkippedNotices } from '../features/count/countSkipped'
import { SkippedLinesNotice } from '../features/count/SkippedLinesNotice'
import { BigButton } from '../kernel/ui/BigButton'
import { LineList } from '../kernel/ui/LineList'
import { colors, fontSize, spacing } from '../kernel/ui/theme'
import { useFormat } from '../kernel/format/useFormat'
import { scannerStatusKey, useScannerStatus } from '../kernel/scanner/useScanner'

/** Pantalla 8 (docs/mobile/app-almacen-plan.md §2): lo que está en la cola de salida (kernel/sync/outbox.ts) y el
 *  resultado de la última pasada. Pendientes se mandan solas en la próxima pasada; con error, cada fila se puede
 *  reintentar o descartar tras revisarla. Lote A6: la captura de conteo rechazada porque el supervisor ya corrigió una línea
 *  (409) se muestra aparte con su explicación y "Actualizar el conteo" (features/count/RejectedCountBatch.tsx). */
export default function SyncScreen() {
  const { t } = useT()
  const f = useFormat()
  const scanner = useScannerStatus()
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
  // Lote A7: avisos de lotes de conteo parciales (el servidor guardó las libres y omitió las corregidas); persisten hasta descartarlos.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const skippedNotices = useMemo(() => listSkippedNotices(), [tick, pending, lastSync])
  // Lote A6: la captura de conteo rechazada porque el supervisor ya corrigió una línea (409) se explica aparte, en grande; el
  // resto de los rechazos sigue en la lista de siempre.
  const correctedRejections = rejectedRows
    .map((r) => ({ row: r, rejection: classifyCountBatchRejection(r) }))
    .filter((x): x is { row: OutboxRow; rejection: CorrectedLineRejection } => x.rejection !== null)
  const correctedIds = new Set(correctedRejections.map((x) => x.row.id))
  const otherRejectedRows = rejectedRows.filter((r) => !correctedIds.has(r.id))
  const rejectedFinishCountIds = new Set(
    rejectedRows.filter((r) => r.kind === 'countFinish').map((r) => countIdFromFinishPath(r.path)).filter((id): id is number => id !== null),
  )

  async function syncNow() {
    setBusy(true)
    try {
      await runSync()
    } finally {
      setBusy(false)
      refresh()
    }
  }

  // 2026-10-11: el tipo de cada operación con su nombre traducido (el `kind` técnico solo si llega uno desconocido)
  const kindLabel = (kind: string) => outboxKindLabel(kind, t)

  // hora en la zona y con el formato (12/24 h) de la compañía; con la fecha si no fue hoy
  const lastSyncLabel = lastSync ? f.when(lastSync.ranAtUtc) : t('sync.never')

  return (
    <ScrollView contentContainerStyle={styles.fill} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>{t('sync.title')}</Text>
      <Text style={styles.help}>{t('sync.lastSync', { when: lastSyncLabel })}</Text>
      {lastSync?.error ? <Text style={styles.error}>{lastSync.error}</Text> : null}

      {/* docs/mobile/mejoras-ux-zebra.md §2.3: si el perfil del lector quedó aplicado (lo que contesta DataWedge) */}
      <Text style={[styles.label, scanner.status.state === 'ready' ? styles.ok : scanner.status.state === 'noProfile' ? styles.errorLabel : null]}>
        {t(scannerStatusKey(scanner.status.state))}
      </Text>
      {scanner.status.detail && scanner.status.state !== 'ready' ? <Text style={styles.help}>{t('scanner.detail', { detail: scanner.status.detail })}</Text> : null}
      {scanner.status.state === 'noProfile' || scanner.status.state === 'unconfirmed' ? (
        <>
          <Text style={styles.help}>{t('scanner.help')}</Text>
          <BigButton label={t('scanner.recheck')} variant="secondary" onPress={scanner.recheck} />
        </>
      ) : null}

      {busy ? <ActivityIndicator color={colors.brand} /> : null}
      <BigButton label={t('home.syncNow')} onPress={syncNow} disabled={busy} />

      {skippedNotices.map((n) => (
        <SkippedLinesNotice
          key={n.id}
          notice={n}
          onDismiss={() => {
            dismissSkippedNotice(n.id)
            refresh()
          }}
        />
      ))}

      <Text style={styles.label}>{t('sync.pendingWithCount', { count: pending })}</Text>
      <LineList
        items={pendingRows.map((r) => ({ id: r.id, title: kindLabel(r.kind), subtitle: r.path }))}
        removeLabel={t('common.remove')}
        emptyLabel={t('sync.empty')}
      />

      <Text style={styles.label}>{t('sync.rejectedWithCount', { count: rejectedRows.length })}</Text>
      {correctedRejections.map(({ row, rejection }) => (
        <RejectedCountBatch
          key={row.id}
          rowId={row.id}
          rejection={rejection}
          finishAlsoRejected={rejection.countId !== null && rejectedFinishCountIds.has(rejection.countId)}
          onDiscard={() => {
            discardRow(row.id)
            refresh()
          }}
          onRetry={() => {
            retryRow(row.id)
            refresh()
          }}
        />
      ))}
      <LineList
        items={otherRejectedRows.map((r) => ({ id: r.id, title: kindLabel(r.kind), subtitle: r.last_error ?? undefined }))}
        removeLabel={t('sync.discard')}
        emptyLabel={correctedRejections.length > 0 ? undefined : t('sync.empty')}
        onRemove={(id) => {
          discardRow(Number(id))
          refresh()
        }}
      />
      {otherRejectedRows.map((r) => (
        <BigButton
          key={r.id}
          label={t('sync.retryRow', { kind: kindLabel(r.kind) })}
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
  fill: { flexGrow: 1, backgroundColor: colors.bg, padding: spacing.lg, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  error: { color: colors.error, fontSize: fontSize.message },
  ok: { color: colors.ok },
  errorLabel: { color: colors.error },
})
