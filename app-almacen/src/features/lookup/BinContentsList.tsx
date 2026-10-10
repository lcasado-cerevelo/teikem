// Lote A8 — lista de lo que el sistema dice que hay en una posición (Consultar, al escanear la posición). Complementa la hoja
// impresa de la posición (Lote 23/F15): el operario ve lo vigente aunque la hoja esté vieja. Una fila por producto (los lotes
// juntos), SKU grande, nombre y lotes (solo se muestran); las cantidades del sistema solo si `showQty` (permiso
// warehouse.count, kernel/auth/permissions.ts). Las filas no se tocan: Consultar no tenía ninguna acción sobre una fila.
import { useState } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import { colors, fontSize, radius, spacing, touchTarget } from '../../kernel/ui/theme'
import { showProductSearch } from '../count/countLogic'
import { filterBinContents, lotsToShow, type BinContentItem } from './lookupLogic'
import { KeyboardInput } from '../../kernel/ui/KeyboardInput'

export interface BinContentsListProps {
  items: BinContentItem[]
  /** Mostrar "En mano" y "Disponible" (solo con warehouse.count). */
  showQty: boolean
  /** Con función (permiso warehouse.transfer), cada producto con algo disponible muestra «Mover». */
  onMove?: (item: BinContentItem) => void
  /** Con función (permiso warehouse.adjust), cada producto muestra «Ajustar» (cambia solo la cantidad). */
  onAdjust?: (item: BinContentItem) => void
}

export function BinContentsList({ items, showQty, onMove, onAdjust }: BinContentsListProps) {
  const { t } = useT()
  const f = useFormat()
  const [query, setQuery] = useState('')

  if (items.length === 0) return <Text style={styles.empty}>{t('lookup.binEmpty')}</Text>

  // mismo umbral que la lista del conteo por producto (decisión del dueño: buscador solo con MÁS de 6)
  const searchable = showProductSearch(items.length)
  const visible = searchable ? filterBinContents(items, query) : items

  return (
    <View style={styles.wrap}>
      <Text style={styles.help}>{items.length === 1 ? t('lookup.binCountOne') : t('lookup.binCountMany', { count: items.length })}</Text>
      {searchable ? (
        <View style={styles.field}>
          <Text style={styles.label}>{t('lookup.binSearchLabel')}</Text>
          <KeyboardInput
            value={query}
            onChangeText={setQuery}
            style={styles.search}
            accessibilityLabel={t('lookup.binSearchLabel')}
            autoCapitalize="characters"
            autoCorrect={false}
          />
        </View>
      ) : null}
      {searchable && visible.length === 0 ? <Text style={styles.help}>{t('lookup.binSearchEmpty', { query: query.trim() })}</Text> : null}
      <View>
        {visible.map((item) => {
          const { shown, more } = lotsToShow(item.lots)
          const lotText =
            shown.length === 0 ? null : more > 0 ? t('lookup.lotsMore', { lots: shown.join(', '), count: more }) : t('lookup.lots', { lots: shown.join(', ') })
          return (
            <View key={item.productPublicId || item.sku} style={styles.row} testID={`bin-content-${item.sku}`}>
              <Text style={styles.sku}>{item.sku}</Text>
              {item.productName ? <Text style={styles.name}>{item.productName}</Text> : null}
              {lotText ? <Text style={styles.detail}>{lotText}</Text> : null}
              {showQty ? (
                <Text style={styles.qty}>
                  {`${t('lookup.onHand')}: ${f.qty(item.qtyOnHand)} · ${t('lookup.available')}: ${f.qty(item.qtyAvailable)}`}
                </Text>
              ) : null}
              {onAdjust ? (
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={t('lookup.adjustLabel', { sku: item.sku })}
                  onPress={() => onAdjust(item)}
                  style={({ pressed }) => [styles.moveBtn, styles.adjustBtn, pressed && styles.pressed]}
                  testID={`adjust-${item.sku}`}
                >
                  <Text style={styles.moveLabel}>{t('lookup.adjust')}</Text>
                </Pressable>
              ) : null}
              {onMove && item.qtyAvailable > 0 ? (
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={t('lookup.moveLabel', { sku: item.sku, bin: '' }).trim()}
                  onPress={() => onMove(item)}
                  style={({ pressed }) => [styles.moveBtn, pressed && styles.pressed]}
                  testID={`move-${item.sku}`}
                >
                  <Text style={styles.moveLabel}>{t('lookup.move')}</Text>
                </Pressable>
              ) : null}
            </View>
          )
        })}
      </View>
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.md },
  help: { color: colors.muted, fontSize: fontSize.message },
  label: { color: colors.text, fontSize: fontSize.label, fontWeight: '600' },
  field: { gap: spacing.xs },
  search: {
    minHeight: touchTarget,
    borderWidth: 2,
    borderColor: colors.line,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    fontSize: 20,
    color: colors.text,
    backgroundColor: colors.panelAlt,
  },
  // una fila por producto: el texto se envuelve (nunca empuja la pantalla a los lados en 360 px)
  row: {
    gap: 2,
    paddingVertical: spacing.md,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.panelAlt,
    borderRadius: radius.md,
    marginBottom: spacing.sm,
  },
  sku: { color: colors.text, fontSize: fontSize.title, fontWeight: '800' },
  name: { color: colors.text, fontSize: fontSize.message },
  detail: { color: colors.muted, fontSize: fontSize.message },
  qty: { color: colors.text, fontSize: fontSize.message, fontWeight: '700' },
  moveBtn: { marginTop: spacing.sm, minHeight: touchTarget, alignItems: 'center', justifyContent: 'center', borderRadius: radius.md, backgroundColor: colors.brand },
  moveLabel: { color: colors.onStrong, fontSize: fontSize.label, fontWeight: '700' },
  adjustBtn: { backgroundColor: colors.panel, borderWidth: 2, borderColor: colors.brand },
  pressed: { opacity: 0.7 },
  empty: { color: colors.muted, fontSize: fontSize.message, textAlign: 'center', paddingVertical: spacing.lg },
})
