// Cantidad con calculadora (pedido del dueño 2026-10-05): el campo de cantidad trae al lado un botón de calculadora; al tocarlo el campo único se CAMBIA
// por «filas × columnas + sueltas» (con «+ otro bloque» para estibas de varias capas) y debajo se ve la cuenta y el total. El total va llenando la
// cantidad a medida que se escribe; al volver a «Cantidad directa» queda ese número. Solo la cantidad se guarda (no la fórmula). Lógica en quantityCalc.ts.
import { useState } from 'react'
import { Pressable, StyleSheet, Text, View, type TextInputProps } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { KeyboardInput, KeyboardToggleButton } from '../../kernel/ui/KeyboardInput'
import { useSoftKeyboard } from '../../kernel/ui/useSoftKeyboard'
import { colors, fontSize, radius, spacing, touchTarget } from '../../kernel/ui/theme'
import { addBlock, calcFromText, calcTotal, removeBlock, setBlock, totalToText, type CalcState } from './quantityCalc'

export interface QuantityFieldProps {
  value: string
  onChangeText: (text: string) => void
  accessibilityLabel: string
  testID?: string
  autoFocus?: boolean
  style?: TextInputProps['style']
  selectTextOnFocus?: boolean
}

/** Cantidad directa con el botón de la calculadora al lado; el modo calculadora reemplaza al campo en el mismo lugar. */
export function QuantityField({ value, onChangeText, accessibilityLabel, testID, autoFocus, style, selectTextOnFocus }: QuantityFieldProps) {
  const { t } = useT()
  const [calc, setCalc] = useState<CalcState | null>(null)

  if (calc) {
    return (
      <QuantityCalculator
        state={calc}
        onChange={(next) => {
          setCalc(next)
          onChangeText(totalToText(calcTotal(next).total))
        }}
        onClose={() => setCalc(null)}
      />
    )
  }
  return (
    <View style={styles.row}>
      <View style={styles.grow}>
        <KeyboardInput
          value={value}
          onChangeText={onChangeText}
          keyboardType="decimal-pad"
          style={style}
          accessibilityLabel={accessibilityLabel}
          testID={testID}
          autoFocus={autoFocus}
          selectTextOnFocus={selectTextOnFocus}
        />
      </View>
      <Pressable accessibilityRole="button" accessibilityLabel={t('calc.open')} onPress={() => setCalc(calcFromText(value))} style={styles.calcBtn}>
        <Text style={styles.calcIcon}>🧮</Text>
      </Pressable>
    </View>
  )
}

export interface QuantityCalculatorProps {
  state: CalcState
  onChange: (next: CalcState) => void
  /** Vuelve a la cantidad directa (con el total ya puesto). */
  onClose: () => void
}

/** El cuerpo de la calculadora: bloques filas × columnas, sueltas y el total. Controlado por quien lo usa (campo en línea o ventana de una fila). */
export function QuantityCalculator({ state, onChange, onClose }: QuantityCalculatorProps) {
  const { t } = useT()
  const kb = useSoftKeyboard()
  const result = calcTotal(state)
  const issueText =
    result.issue === 'incompleteBlock'
      ? t('calc.incompleteBlock')
      : result.issue === 'invalid'
        ? t('calc.invalid')
        : result.issue === 'tooLarge'
          ? t('calc.tooLarge')
          : null

  return (
    <View style={styles.panel} testID="quantity-calculator">
      <View style={styles.head}>
        <Text style={styles.title}>{t('calc.title')}</Text>
        <KeyboardToggleButton on={kb.show} onPress={kb.toggle} />
        <Pressable accessibilityRole="button" accessibilityLabel={t('calc.direct')} onPress={onClose} style={styles.directBtn}>
          <Text style={styles.directLabel}>{t('calc.direct')}</Text>
        </Pressable>
      </View>

      {state.blocks.map((b, i) => (
        <View key={i} style={styles.blockRow}>
          <View style={styles.cell}>
            <Text style={styles.cellLabel}>{t('calc.rows')}</Text>
            <KeyboardInput
              toggle={false}
              softKeyboard={kb.show}
              value={b.rows}
              onChangeText={(v) => onChange(setBlock(state, i, { rows: v }))}
              keyboardType="number-pad"
              style={styles.input}
              accessibilityLabel={t('calc.rowsAt', { n: i + 1 })}
              autoFocus={i === 0}
              selectTextOnFocus
            />
          </View>
          <Text style={styles.times}>×</Text>
          <View style={styles.cell}>
            <Text style={styles.cellLabel}>{t('calc.cols')}</Text>
            <KeyboardInput
              toggle={false}
              softKeyboard={kb.show}
              value={b.cols}
              onChangeText={(v) => onChange(setBlock(state, i, { cols: v }))}
              keyboardType="number-pad"
              style={styles.input}
              accessibilityLabel={t('calc.colsAt', { n: i + 1 })}
              selectTextOnFocus
            />
          </View>
          {state.blocks.length > 1 ? (
            <Pressable accessibilityRole="button" accessibilityLabel={t('calc.removeBlock', { n: i + 1 })} onPress={() => onChange(removeBlock(state, i))} style={styles.removeBtn}>
              <Text style={styles.removeLabel}>✕</Text>
            </Pressable>
          ) : null}
        </View>
      ))}

      <Pressable accessibilityRole="button" accessibilityLabel={t('calc.addBlock')} onPress={() => onChange(addBlock(state))} style={styles.link}>
        <Text style={styles.linkLabel}>{t('calc.addBlock')}</Text>
      </Pressable>

      <View style={styles.cellFull}>
        <Text style={styles.cellLabel}>{t('calc.extra')}</Text>
        <KeyboardInput
          toggle={false}
          softKeyboard={kb.show}
          value={state.extra}
          onChangeText={(v) => onChange({ ...state, extra: v })}
          keyboardType="decimal-pad"
          style={styles.input}
          accessibilityLabel={t('calc.extra')}
          selectTextOnFocus
        />
      </View>

      <View style={styles.sum} accessibilityLiveRegion="polite">
        {result.expression ? <Text style={styles.expression}>{result.expression}</Text> : null}
        {issueText ? (
          <Text style={styles.issue}>{issueText}</Text>
        ) : result.total !== null ? (
          <Text style={styles.total} testID="quantity-calculator-total">
            {t('calc.total', { total: totalToText(result.total) })}
          </Text>
        ) : (
          <Text style={styles.help}>{t('calc.hint')}</Text>
        )}
      </View>
    </View>
  )
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'stretch', gap: spacing.sm },
  grow: { flex: 1, minWidth: 0 },
  calcBtn: { minHeight: touchTarget, width: 52, borderRadius: radius.md, borderWidth: 2, borderColor: colors.brand, alignItems: 'center', justifyContent: 'center' },
  calcIcon: { fontSize: 24 },
  panel: { gap: spacing.sm, padding: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.brand, backgroundColor: colors.panelAlt },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  title: { flex: 1, color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  directBtn: { minHeight: 44, justifyContent: 'center', paddingHorizontal: spacing.sm },
  directLabel: { color: colors.brand, fontSize: fontSize.message, fontWeight: '700', textDecorationLine: 'underline' },
  blockRow: { flexDirection: 'row', alignItems: 'flex-end', gap: spacing.sm },
  cell: { flex: 1, minWidth: 0, gap: 2 },
  cellFull: { gap: 2 },
  cellLabel: { color: colors.muted, fontSize: fontSize.message },
  times: { color: colors.text, fontSize: 24, fontWeight: '700', paddingBottom: 10 },
  input: {
    minHeight: touchTarget,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.bg,
  },
  removeBtn: { minWidth: 44, minHeight: 44, alignItems: 'center', justifyContent: 'center', borderWidth: 1, borderColor: colors.line, borderRadius: radius.sm },
  removeLabel: { color: colors.error, fontSize: 22, fontWeight: '700' },
  link: { minHeight: 44, justifyContent: 'center', alignSelf: 'flex-start', paddingHorizontal: spacing.xs },
  linkLabel: { color: colors.brand, fontSize: fontSize.label, fontWeight: '700', textDecorationLine: 'underline' },
  sum: { gap: 2, paddingTop: spacing.xs },
  expression: { color: colors.muted, fontSize: fontSize.message },
  total: { color: colors.text, fontSize: 28, fontWeight: '800' },
  issue: { color: colors.error, fontSize: fontSize.message, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
})
