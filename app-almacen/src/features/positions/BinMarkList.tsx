import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import { BigButton } from '../../kernel/ui/BigButton'
import { colors, fontSize, radius, spacing, touchTarget } from '../../kernel/ui/theme'
import { marksComplete, marksToRows, sortRecommendedFirst, sumMarks, type MarkOption, type Marks } from './binMarks'

export interface BinMarkListProps {
  options: readonly MarkOption[]
  marks: Marks
  total: number
  onToggle: (key: string) => void
  /** Marca de una vez las recomendadas. */
  onMarkRecommended: () => void
  onUse: () => void
  /** Etiqueta del botón que confirma las posiciones marcadas. */
  useLabel: string
  /** Aviso por posición marcada (p. ej. «no cabe»), opcional. */
  warnFor?: (key: string, qty: number) => string | null
  testID?: string
}

/** Listado de posiciones para repartir una cantidad entre varias: las recomendadas por el sistema primero y en otro azul; al tocar una se
 *  marca con lo que falte y el contador «Tomado X de Y» sube. Sirve igual para sacar (Despacho) que para recibir (Recibo directo). */
export function BinMarkList({ options, marks, total, onToggle, onMarkRecommended, onUse, useLabel, warnFor, testID }: BinMarkListProps) {
  const { t } = useT()
  const f = useFormat()
  const sorted = sortRecommendedFirst(options)
  const taken = sumMarks(marks)
  const complete = marksComplete(marks, total)
  const hasRecommended = options.some((o) => o.recommended)
  return (
    <View style={styles.box} testID={testID}>
      <Text style={styles.title}>{t('positions.listTitle')}</Text>
      <Text style={[styles.tally, complete && styles.tallyOk]} accessibilityLiveRegion="polite" testID={testID ? `${testID}-tally` : undefined}>
        {t('positions.tally', { taken: f.qty(taken), total: f.qty(total) })}
      </Text>
      {sorted.map((o) => {
        const qty = marks[o.key] ?? 0
        const on = qty > 0
        const warn = on ? (warnFor?.(o.key, qty) ?? null) : null
        return (
          <View key={o.key}>
            <Pressable
              accessibilityRole="checkbox"
              accessibilityState={{ checked: on }}
              accessibilityLabel={o.binCode}
              onPress={() => onToggle(o.key)}
              style={[styles.row, o.recommended && styles.rowRecommended, on && styles.rowOn]}
            >
              <Text style={styles.check}>{on ? '☑' : '☐'}</Text>
              <View style={styles.rowText}>
                <Text style={styles.bin}>
                  {o.binCode}
                  {o.recommended ? `  ·  ${t('positions.recommended')}` : ''}
                </Text>
                {o.detail ? <Text style={styles.detail}>{o.detail}</Text> : null}
              </View>
              {on ? <Text style={styles.qty}>{f.qty(qty)}</Text> : null}
            </Pressable>
            {warn ? <Text style={styles.warn}>{warn}</Text> : null}
          </View>
        )
      })}
      {hasRecommended ? <BigButton label={t('positions.markRecommended')} variant="secondary" onPress={onMarkRecommended} testID={testID ? `${testID}-recommended` : undefined} /> : null}
      <BigButton label={useLabel} onPress={onUse} disabled={!complete || marksToRows(options, marks).length === 0} testID={testID ? `${testID}-use` : undefined} />
    </View>
  )
}

const styles = StyleSheet.create({
  box: { gap: spacing.sm, padding: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panel },
  title: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  tally: { color: colors.warn, fontSize: fontSize.label, fontWeight: '700' },
  tallyOk: { color: colors.ok },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: touchTarget, paddingHorizontal: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, backgroundColor: colors.panelAlt },
  // las que el sistema usaría para completar la cantidad: otro azul, que se distinga de las demás
  rowRecommended: { borderColor: colors.brand, backgroundColor: colors.brandDark },
  rowOn: { borderColor: colors.ok, backgroundColor: colors.okStrong },
  check: { color: colors.text, fontSize: 24 },
  rowText: { flex: 1, minWidth: 0 },
  bin: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '700' },
  detail: { color: colors.text, fontSize: fontSize.listSubtitle, opacity: 0.85 },
  qty: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '800' },
  warn: { color: colors.warn, fontSize: fontSize.message },
})
