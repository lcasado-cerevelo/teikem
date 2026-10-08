// 2026-10-07 — lista de almacenes de la compañía para elegir con cuál trabaja el aparato (se abre desde Inicio, «Cambiar»).
import { FlatList, Modal, Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import type { WarehouseOption } from '../warehouse/activeWarehouse'
import { BigButton } from './BigButton'
import { colors, fontSize, radius, spacing } from './theme'

export interface WarehousePickerModalProps {
  visible: boolean
  options: WarehouseOption[]
  currentPublicId: string | null
  defaultPublicId: string | null
  onSelect: (option: WarehouseOption) => void
  onClose: () => void
}

export function WarehousePickerModal({ visible, options, currentPublicId, defaultPublicId, onSelect, onClose }: WarehousePickerModalProps) {
  const { t } = useT()
  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={styles.screen} testID="warehouse-picker">
        <Text style={styles.title}>{t('warehousePick.title')}</Text>
        <FlatList
          style={styles.list}
          data={options}
          keyExtractor={(o) => o.publicId}
          ListEmptyComponent={<Text style={styles.help}>{t('warehousePick.empty')}</Text>}
          renderItem={({ item }) => {
            const current = item.publicId === currentPublicId
            return (
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={item.name}
                accessibilityState={{ selected: current }}
                onPress={() => onSelect(item)}
                style={[styles.row, current && styles.rowCurrent]}
              >
                <Text style={styles.rowTitle}>{item.name}</Text>
                <Text style={styles.help}>
                  {[item.code, current ? t('warehousePick.current') : null, item.publicId === defaultPublicId ? t('warehousePick.isDefault') : null]
                    .filter(Boolean)
                    .join(' · ')}
                </Text>
              </Pressable>
            )
          }}
        />
        <BigButton label={t('common.cancel')} variant="secondary" onPress={onClose} />
      </View>
    </Modal>
  )
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.bg, padding: spacing.lg, paddingTop: spacing.xl, paddingBottom: 48, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  list: { flex: 1 },
  row: { minHeight: 64, justifyContent: 'center', paddingHorizontal: spacing.md, paddingVertical: spacing.sm, backgroundColor: colors.panelAlt, borderRadius: radius.md, marginBottom: spacing.sm, borderWidth: 2, borderColor: 'transparent' },
  rowCurrent: { borderColor: colors.brand },
  rowTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
})
