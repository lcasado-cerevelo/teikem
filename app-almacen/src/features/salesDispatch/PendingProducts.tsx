// Lote 201 — lo que falta por sacar de los documentos que se despachan, como lista de productos (uno por fila, con lo que falta): tocar uno equivale a escanearlo. Reemplaza a «Buscar en lista»,
// porque contra un documento solo se despachan sus productos y están todos aquí.
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import { colors, fontSize, radius, spacing } from '../../kernel/ui/theme'
import type { SalesTask } from './salesDispatchLogic'

export interface PendingProduct {
  productPublicId: string
  sku: string
  productName: string
  qty: number
}

/** Las tareas que faltan, juntas por producto (en el orden en que aparece cada uno). */
export function groupPendingProducts(tasks: readonly SalesTask[]): PendingProduct[] {
  const by = new Map<string, PendingProduct>()
  for (const t of tasks) {
    const cur = by.get(t.productPublicId)
    if (cur) cur.qty += t.qty
    else by.set(t.productPublicId, { productPublicId: t.productPublicId, sku: t.sku, productName: t.productName, qty: t.qty })
  }
  return [...by.values()]
}

export function PendingProducts({ tasks, onChoose, testID = 'pending-products' }: { tasks: readonly SalesTask[]; onChoose: (sku: string) => void; testID?: string }) {
  const { t } = useT()
  const f = useFormat()
  const items = groupPendingProducts(tasks)
  if (items.length === 0) return null
  return (
    <View style={styles.wrap} testID={testID}>
      <Text style={styles.title}>{t('dispatch.pendingProducts')}</Text>
      <Text style={styles.help}>{t('dispatch.pendingProductsHelp')}</Text>
      {items.map((p) => (
        <Pressable key={p.productPublicId} accessibilityRole="button" accessibilityLabel={t('dispatch.pickProduct', { sku: p.sku })} onPress={() => onChoose(p.sku)} style={({ pressed }) => [styles.row, pressed && styles.pressed]} testID={`pending-${p.sku}`}>
          <View style={styles.texts}>
            <Text style={styles.sku}>{p.sku}</Text>
            <Text style={styles.help}>{p.productName}</Text>
          </View>
          <Text style={styles.qty}>{t('dispatch.toPick', { qty: f.qty(p.qty) })}</Text>
        </Pressable>
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.sm },
  title: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.md, padding: spacing.md, borderRadius: radius.md, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.panelAlt },
  pressed: { backgroundColor: colors.panel },
  texts: { flex: 1, gap: 2 },
  sku: { color: colors.text, fontSize: fontSize.label, fontWeight: '700' },
  qty: { color: colors.brand, fontSize: fontSize.message, fontWeight: '700' },
})
