// Segundo bloque de decisiones del dueño (2026-10-03) — aviso de Sincronización para un lote de conteo PARCIAL: el servidor guardó las
// líneas libres y omitió las que el supervisor ya había corregido (countSkipped.ts). Mismo estilo que la tarjeta de rechazo del
// Lote A6 (letras ≥ 16, contraste alto) pero con tono de advertencia, no de error: lo demás sí se guardó. Botones: "Actualizar el
// conteo" (GET, solo al tocarlo; nunca muestra la cantidad esperada) y "Descartar este aviso".
import { useState } from 'react'
import { StyleSheet, Text, View } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import { skippedLineText, type SkippedNotice } from './countSkipped'
import { RefreshResult } from './RejectedCountBatch'
import { fetchCountForRejection, type CountRefreshResult } from './countRejectionApi'

export interface SkippedLinesNoticeProps {
  notice: SkippedNotice
  onDismiss: () => void
  /** Para pruebas: sustituye la consulta al servidor. */
  fetchCount?: (countId: number) => Promise<CountRefreshResult>
}

export function SkippedLinesNotice({ notice, onDismiss, fetchCount = fetchCountForRejection }: SkippedLinesNoticeProps) {
  const { t } = useT()
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<CountRefreshResult | null>(null)

  async function refreshCount() {
    if (notice.countId === null) return
    setBusy(true)
    try {
      setResult(await fetchCount(notice.countId))
    } catch {
      setResult({ kind: 'error', message: t('errors.generic') })
    } finally {
      setBusy(false)
    }
  }

  return (
    <View style={styles.card} testID={`skipped-notice-${notice.id}`}>
      <Text style={styles.headline}>{t('countSkipped.headline')}</Text>
      <Text style={styles.strong}>{t('countSkipped.intro')}</Text>
      <View style={styles.block}>
        {notice.lines.map((l, i) => (
          <Text key={`${l.lineId ?? 'x'}-${i}`} style={styles.text}>
            {skippedLineText(l, t)}
          </Text>
        ))}
      </View>
      <Text style={styles.text}>{t('countSkipped.hint')}</Text>

      {notice.countId !== null ? (
        <BigButton label={t('countSkipped.refresh')} variant="secondary" onPress={() => void refreshCount()} loading={busy} />
      ) : null}
      {result ? <RefreshResult result={result} /> : null}
      <BigButton label={t('countSkipped.dismiss')} variant="danger" onPress={onDismiss} disabled={busy} />
    </View>
  )
}

const styles = StyleSheet.create({
  card: {
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.warn,
    backgroundColor: colors.panel,
  },
  block: { gap: spacing.xs },
  headline: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  strong: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  text: { color: colors.text, fontSize: fontSize.message },
})
