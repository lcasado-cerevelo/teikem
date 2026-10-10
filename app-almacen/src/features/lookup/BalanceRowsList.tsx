// 2026-10-10 — filas de saldo de Consultar (por producto o búsqueda libre), cada una con su botón «Mover» cuando quien consulta puede transferir
// (permiso warehouse.transfer) y la fila tiene posición y algo disponible. Sin permiso, la lista es la de siempre (solo se lee).
import { Pressable, StyleSheet, Text, View } from 'react-native'

import { useFormat } from '../../kernel/format/useFormat'
import { useT } from '../../kernel/i18n/useT'
import { colors, fontSize, radius, spacing, touchTarget } from '../../kernel/ui/theme'
import { isBlockedZone } from '../transfer/transferLogic'
import type { BalanceRow } from './lookupLogic'

export interface BalanceRowsListProps {
  rows: BalanceRow[]
  /** Con función, cada fila movible muestra «Mover». */
  onMove?: (row: BalanceRow) => void
  /** Con función (permiso warehouse.adjust), cada fila con posición muestra «Ajustar» (cambia solo la cantidad). */
  onAdjust?: (row: BalanceRow) => void
}

export function canMoveRow(row: BalanceRow): boolean {
  return row.binId != null && Boolean(row.binCode) && row.qtyAvailable > 0 && !isBlockedZone(row.zoneTypeCode)
}

/** «Ajustar»: la fila tiene posición y no es de cuarentena, en renta ni cross-dock (aunque no quede nada disponible: se puede subir). */
export function canAdjustRow(row: BalanceRow): boolean {
  return row.binId != null && Boolean(row.binCode) && !isBlockedZone(row.zoneTypeCode)
}

export function BalanceRowsList({ rows, onMove, onAdjust }: BalanceRowsListProps) {
  const { t } = useT()
  const f = useFormat()
  if (rows.length === 0) return <Text style={styles.empty}>{t('lookup.empty')}</Text>
  return (
    <View>
      {rows.map((r) => (
        <View key={r.id} style={styles.row} testID={`balance-row-${r.id}`}>
          <View style={styles.text}>
            <Text style={styles.title}>{`${r.sku} · ${r.productName}`}</Text>
            <Text style={styles.sub}>
              {[r.binCode, r.lotNumber, `${t('lookup.onHand')}: ${f.qty(r.qtyOnHand)}`, `${t('lookup.available')}: ${f.qty(r.qtyAvailable)}`].filter(Boolean).join(' · ')}
            </Text>
          </View>
          {onAdjust && canAdjustRow(r) ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('lookup.adjustLabel', { sku: r.sku })}
              onPress={() => onAdjust(r)}
              style={({ pressed }) => [styles.btn, styles.btnAlt, pressed && styles.pressed]}
              testID={`adjust-${r.id}`}
            >
              <Text style={styles.btnLabel}>{t('lookup.adjust')}</Text>
            </Pressable>
          ) : null}
          {onMove && canMoveRow(r) ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('lookup.moveLabel', { sku: r.sku, bin: r.binCode ?? '' })}
              onPress={() => onMove(r)}
              style={({ pressed }) => [styles.btn, pressed && styles.pressed]}
              testID={`move-${r.id}`}
            >
              <Text style={styles.btnLabel}>{t('lookup.move')}</Text>
            </Pressable>
          ) : null}
        </View>
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.md, backgroundColor: colors.panelAlt, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.sm },
  text: { flex: 1, gap: 2 },
  title: { color: colors.text, fontSize: fontSize.message, fontWeight: '700' },
  sub: { color: colors.muted, fontSize: fontSize.message },
  btn: { minHeight: touchTarget, minWidth: 88, alignItems: 'center', justifyContent: 'center', borderRadius: radius.md, backgroundColor: colors.brand, paddingHorizontal: spacing.md },
  btnLabel: { color: colors.onStrong, fontSize: fontSize.label, fontWeight: '700' },
  btnAlt: { backgroundColor: colors.panel, borderWidth: 2, borderColor: colors.brand },
  pressed: { opacity: 0.7 },
  empty: { color: colors.muted, fontSize: fontSize.message, textAlign: 'center', paddingVertical: spacing.lg },
})
