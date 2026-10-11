// Lote A6 — tarjeta de Sincronización para una captura de conteo rechazada porque el supervisor ya corrigió una línea (409,
// countRejection.ts). Explica en grande (kit de mensajes del Lote A3: ScanMessage, letras ≥ 16, contraste alto) qué pasó, que no se
// guardó nada de ese envío y qué hacer; ofrece "Actualizar el conteo" (GET /cycle-counts/{id}, solo al tocarlo) y "Descartar",
// igual que el resto de los rechazos. Nunca muestra cantidades esperadas: solo lo vigente y quién corrigió.
import { useState } from 'react'
import { StyleSheet, Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { outboxKindLabel } from '../../kernel/sync/outboxKind'
import { BigButton } from '../../kernel/ui/BigButton'
import { ScanMessage } from '../../kernel/ui/ScanMessage'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { refreshStateKey, summarizeRefreshedLines, type CorrectedLineRejection, type RefreshedLine } from './countRejection'
import { fetchCountForRejection, type CountRefreshResult } from './countRejectionApi'

/** Líneas que se pintan tras actualizar (un conteo admite hasta 1000; en la pantalla de 4" se resume el resto). */
export const MAX_REFRESHED_LINES_SHOWN = 30

export interface RejectedCountBatchProps {
  rowId: number
  rejection: CorrectedLineRejection
  /** true si el cierre del mismo conteo (countFinish) también quedó con error en la cola. */
  finishAlsoRejected: boolean
  onDiscard: () => void
  onRetry: () => void
  /** Para pruebas: sustituye la consulta al servidor. */
  fetchCount?: (countId: number) => Promise<CountRefreshResult>
}

export function RejectedCountBatch({ rowId, rejection, finishAlsoRejected, onDiscard, onRetry, fetchCount = fetchCountForRejection }: RejectedCountBatchProps) {
  const { t } = useT()
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<CountRefreshResult | null>(null)

  async function refreshCount() {
    if (rejection.countId === null) return
    setBusy(true)
    try {
      setResult(await fetchCount(rejection.countId))
    } catch {
      setResult({ kind: 'error', message: t('errors.generic') })
    } finally {
      setBusy(false)
    }
  }

  return (
    <View style={styles.card} testID={`rejected-count-batch-${rowId}`}>
      <ScanMessage tone="error" message={t('countRejection.headline')} />
      <Text style={styles.strong}>{t('countRejection.nothingSaved')}</Text>

      {rejection.rows ? (
        <View style={styles.block}>
          <Text style={styles.label}>{t('countRejection.rowsTitle')}</Text>
          {rejection.rows.map((r) => (
            <Text key={`${r.row}-${r.sku}`} style={styles.text}>
              {r.capturedQty === null
                ? t('countRejection.row', { number: r.row, sku: r.sku })
                : t('countRejection.rowWithQty', { number: r.row, sku: r.sku, qty: r.capturedQty })}
            </Text>
          ))}
        </View>
      ) : (
        <View style={styles.block}>
          <Text style={styles.label}>{t('countRejection.serverSaid')}</Text>
          <Text style={styles.text}>{rejection.message}</Text>
        </View>
      )}

      <View style={styles.block}>
        <Text style={styles.label}>{t('countRejection.whatToDoTitle')}</Text>
        <Text style={styles.text}>{t('countRejection.whatToDo')}</Text>
        <Text style={styles.text}>{t('countRejection.noReopenInApp')}</Text>
      </View>
      {finishAlsoRejected ? <Text style={styles.text}>{t('countRejection.finishAlsoRejected')}</Text> : null}

      {rejection.countId !== null ? (
        <BigButton label={t('countRejection.refresh')} variant="secondary" onPress={() => void refreshCount()} loading={busy} />
      ) : (
        <Text style={styles.text}>{t('countRejection.noCountId')}</Text>
      )}
      {result ? <RefreshResult result={result} /> : null}

      <BigButton label={t('sync.retryRow', { kind: outboxKindLabel('countBatch', t) })} variant="secondary" onPress={onRetry} disabled={busy} />
      <BigButton label={t('countRejection.discard')} variant="danger" onPress={onDiscard} disabled={busy} />
    </View>
  )
}

export function RefreshResult({ result }: { result: CountRefreshResult }) {
  const { t } = useT()
  if (result.kind === 'offline') return <ScanMessage tone="error" message={t('countRejection.offline')} />
  if (result.kind === 'error') return <ScanMessage tone="error" message={result.message} />
  if (result.kind === 'notFound') return <ScanMessage tone="error" message={t(refreshStateKey('notFound'))} />

  const summary = summarizeRefreshedLines(result.lines)
  const shown = result.state === 'closed' ? [] : result.lines.slice(0, MAX_REFRESHED_LINES_SHOWN)
  return (
    <View style={styles.block} accessibilityLiveRegion="polite" testID="count-refresh-result">
      {/* reconciliado: aviso rojo (ya no hay nada que capturar); abierto o Contado: texto fuerte, no es un error */}
      {result.state === 'closed' ? (
        <ScanMessage tone="error" message={t(refreshStateKey(result.state), { number: result.number })} />
      ) : (
        <Text style={styles.strong}>{t(refreshStateKey(result.state), { number: result.number })}</Text>
      )}
      {result.state !== 'closed' ? (
        <Text style={styles.strong}>
          {t('countRejection.summary', { total: summary.total, corrected: summary.corrected, uncounted: summary.uncounted })}
        </Text>
      ) : null}
      {shown.map((l) => (
        <View key={l.lineId} style={styles.line}>
          <Text style={styles.lineTitle}>{lineTitle(l)}</Text>
          <Text style={[styles.text, l.wasCorrected && styles.corrected]}>{lineStatus(l, t)}</Text>
        </View>
      ))}
      {result.lines.length > shown.length && shown.length > 0 ? (
        <Text style={styles.text}>{t('countRejection.moreLines', { count: result.lines.length - shown.length })}</Text>
      ) : null}
    </View>
  )
}

function lineTitle(l: RefreshedLine): string {
  const parts = [l.sku, l.binCode].filter((p) => p !== '')
  if (l.lotNumber) parts.push(l.lotNumber)
  return parts.join(' · ')
}

function lineStatus(l: RefreshedLine, t: (key: string, params?: Record<string, string | number>) => string): string {
  const qty = l.countedQty === null ? t('countRejection.lineUncounted') : t('countRejection.lineCounted', { qty: l.countedQty })
  if (!l.wasCorrected) return qty
  const name = l.correctedByName?.trim()
  const by = name ? t('countRejection.lineCorrected', { name }) : t('countRejection.lineCorrectedAnon')
  return `${by} · ${qty}`
}

const styles = StyleSheet.create({
  card: {
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.errorStrong,
    backgroundColor: colors.panel,
  },
  block: { gap: spacing.xs },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  strong: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  text: { color: colors.text, fontSize: fontSize.message },
  corrected: { color: colors.warn, fontWeight: '700' },
  line: { paddingVertical: spacing.sm, paddingHorizontal: spacing.md, backgroundColor: colors.panelAlt, borderRadius: radius.sm, gap: 2 },
  lineTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
})
