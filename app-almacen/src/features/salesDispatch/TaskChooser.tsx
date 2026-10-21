// Lote 192 — la lista de productos por sacar de un documento (orden de venta o aviso de salida) para ELEGIR cuál se saca ahora, en vez de seguir solo el orden de la ruta.
// Muestra cada tarea pendiente (producto, posición y cantidad); tocar una la pone como la actual. Las omitidas siguen ahí con su marca para poder volver a ellas.
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import type { SalesPick, SalesTask } from './salesDispatchLogic'

export function TaskChooser({
  tasks,
  picks,
  skipped,
  currentKey,
  onChoose,
  testID = 'task-chooser',
}: {
  tasks: readonly SalesTask[]
  picks: readonly SalesPick[]
  skipped: ReadonlySet<string>
  currentKey: string | null
  onChoose: (task: SalesTask) => void
  testID?: string
}) {
  const { t } = useT()
  const f = useFormat()
  const done = new Set(picks.map((p) => p.taskKey))
  const pending = tasks.filter((x) => !done.has(x.key))
  if (pending.length < 2) return null
  return (
    <View style={styles.box} testID={testID}>
      <Text style={styles.label}>{t('taskChooser.title')}</Text>
      <Text style={styles.help}>{t('taskChooser.help')}</Text>
      {pending.map((x) => (
        <Pressable
          key={x.key}
          accessibilityRole="button"
          accessibilityLabel={t('taskChooser.pick', { sku: x.sku, bin: x.binCode })}
          onPress={() => onChoose(x)}
          style={({ pressed }) => [styles.row, x.key === currentKey && styles.rowOn, pressed && styles.rowPressed]}
          testID={`${testID}-${x.key}`}
        >
          <Text style={styles.label}>
            {x.key === currentKey ? '▶ ' : ''}
            {x.sku} · {x.productName}
          </Text>
          <Text style={styles.help}>
            {t('taskChooser.line', { bin: x.binCode, qty: f.qty(x.qty) })}
            {x.lotNumber ? ' · ' + x.lotNumber : ''}
            {skipped.has(x.key) ? ' · ' + t('taskChooser.skipped') : ''}
          </Text>
        </Pressable>
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  box: { gap: spacing.sm },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  help: { color: colors.muted, fontSize: fontSize.message },
  row: { gap: 2, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.panelAlt },
  rowOn: { borderColor: colors.brand, borderWidth: 2 },
  rowPressed: { backgroundColor: colors.panel },
})
