// Cantidad con calculadora (pedido del dueño 2026-10-05): el campo de cantidad trae al lado un botón de calculadora; al tocarlo el campo único se CAMBIA
// por «filas × columnas + sueltas» (con «+ otro bloque» para estibas de varias capas) y debajo se ve la cuenta y el total. El total va llenando la
// cantidad a medida que se escribe; al volver a «Cantidad directa» queda ese número. Solo la cantidad se guarda (no la fórmula). Lógica en quantityCalc.ts.
// Lote A9 (pruebas en el Zebra, 2026-10-06): el botón trae un icono claro dibujado (CalculatorIcon) sobre fondo azul oscuro (el emoji 🧮 no se
// veía sobre el fondo oscuro); en la cabecera, «Calculadora» va en una sola línea y el enlace de texto «Cantidad directa» pasó a ser un botón
// con flecha (←) cuyo nombre accesible sigue siendo «Cantidad directa».
import { useState } from 'react'
import { Pressable, StyleSheet, Text, View, type TextInputProps } from 'react-native'

import { useT } from '../../kernel/i18n/useT'
import { CalculatorIcon } from '../../kernel/ui/CalculatorIcon'
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
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('calc.open')}
        onPress={() => setCalc(calcFromText(value))}
        style={({ pressed }) => [styles.calcBtn, pressed && styles.pressed]}
        testID="quantity-calc-open"
      >
        <CalculatorIcon />
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
      {/* volver (flecha) · título en UNA línea (se achica antes que partirse) · teclado en pantalla */}
      <View style={styles.head}>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={t('calc.direct')}
          onPress={onClose}
          hitSlop={4}
          style={({ pressed }) => [styles.backBtn, pressed && styles.pressed]}
          testID="quantity-calculator-back"
        >
          <Text style={styles.backIcon}>←</Text>
        </Pressable>
        <View style={styles.titleBox}>
          <Text style={styles.title} numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.7} testID="quantity-calculator-title">
            {t('calc.title')}
          </Text>
        </View>
        <KeyboardToggleButton on={kb.show} onPress={kb.toggle} />
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
  // fondo azul oscuro con icono blanco (contraste ~8:1) y borde azul: se distingue del campo y del fondo de la app; 56 × 56 dp
  calcBtn: {
    minHeight: touchTarget,
    width: touchTarget,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.brand,
    backgroundColor: colors.brandDark,
    alignItems: 'center',
    justifyContent: 'center',
  },
  pressed: { opacity: 0.8 },
  panel: { gap: spacing.sm, padding: spacing.md, borderRadius: radius.md, borderWidth: 2, borderColor: colors.brand, backgroundColor: colors.panelAlt },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  // el título toma el espacio que sobra y se encoge (nunca empuja a los botones ni se parte en dos líneas)
  titleBox: { flex: 1, flexShrink: 1, minWidth: 0 },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  backBtn: {
    width: touchTarget,
    minHeight: touchTarget,
    borderRadius: radius.md,
    borderWidth: 2,
    borderColor: colors.brand,
    backgroundColor: colors.brandDark,
    alignItems: 'center',
    justifyContent: 'center',
  },
  backIcon: { color: colors.onStrong, fontSize: 28, fontWeight: '800', lineHeight: 32 },
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
  removeBtn: { minWidth: 48, minHeight: 48, alignItems: 'center', justifyContent: 'center', borderWidth: 1, borderColor: colors.line, borderRadius: radius.sm },
  removeLabel: { color: colors.error, fontSize: 22, fontWeight: '700' },
  link: { minHeight: 48, justifyContent: 'center', alignSelf: 'flex-start', paddingHorizontal: spacing.xs },
  linkLabel: { color: colors.brand, fontSize: fontSize.label, fontWeight: '700', textDecorationLine: 'underline' },
  sum: { gap: 2, paddingTop: spacing.xs },
  expression: { color: colors.muted, fontSize: fontSize.message },
  total: { color: colors.text, fontSize: 28, fontWeight: '800' },
  issue: { color: colors.error, fontSize: fontSize.message, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
})
