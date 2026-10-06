// Tarea 26 — lista con buscador para los campos de producto y de posición (se abre desde ScanField con `pick`). Escribir cualquier cosa filtra la
// lista (SKU, nombre o código de barras; código, zona, pasillo, rack, nivel o posición); tocar una fila la entrega al campo como si se hubiera escaneado.
import { useMemo, useState } from 'react'
import { FlatList, Modal, Pressable, StyleSheet, Text, View } from 'react-native'

import { useT } from '../i18n/useT'
import { searchPicker, PICK_LIMIT, type PickItem, type PickKind } from '../warehouse/pickerSearch'
import { BigButton } from './BigButton'
import { KeyboardInput } from './KeyboardInput'
import { colors, fontSize, radius, spacing } from './theme'

export interface PickerModalProps {
  visible: boolean
  kind: PickKind
  warehousePublicId: string | null
  onSelect: (code: string) => void
  onClose: () => void
}

export function PickerModal({ visible, kind, warehousePublicId, onSelect, onClose }: PickerModalProps) {
  const { t } = useT()
  const [query, setQuery] = useState('')
  const items = useMemo<PickItem[]>(() => (visible ? searchPicker(kind, warehousePublicId, query) : []), [visible, kind, warehousePublicId, query])
  const title = t(kind === 'product' ? 'picker.titleProduct' : kind === 'bin' ? 'picker.titleBin' : 'picker.titleAny')

  function close() {
    setQuery('')
    onClose()
  }

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={close}>
      <View style={styles.screen} testID="picker-modal">
        <Text style={styles.title}>{title}</Text>
        <KeyboardInput
          value={query}
          onChangeText={setQuery}
          autoFocus
          autoCapitalize="none"
          autoCorrect={false}
          placeholder={t('picker.search')}
          placeholderTextColor={colors.muted}
          style={styles.input}
          accessibilityLabel={t('picker.search')}
        />
        <FlatList
          style={styles.list}
          data={items}
          keyExtractor={(it) => `${it.kind}:${it.code}`}
          keyboardShouldPersistTaps="handled"
          ListEmptyComponent={<Text style={styles.help}>{t('picker.empty')}</Text>}
          ListFooterComponent={items.length >= PICK_LIMIT ? <Text style={styles.help}>{t('picker.more', { max: PICK_LIMIT })}</Text> : null}
          renderItem={({ item }) => (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={item.code}
              onPress={() => {
                setQuery('')
                onSelect(item.code)
              }}
              style={styles.row}
            >
              <Text style={styles.rowTitle}>{item.title}</Text>
              {item.subtitle ? <Text style={styles.help}>{item.subtitle}</Text> : null}
            </Pressable>
          )}
        />
        <BigButton label={t('common.cancel')} variant="secondary" onPress={close} />
      </View>
    </Modal>
  )
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.bg, padding: spacing.lg, paddingTop: spacing.xl, paddingBottom: 48, gap: spacing.md },
  title: { color: colors.text, fontSize: fontSize.title, fontWeight: '700' },
  help: { color: colors.muted, fontSize: fontSize.message },
  list: { flex: 1 },
  row: { minHeight: 64, justifyContent: 'center', paddingHorizontal: spacing.md, paddingVertical: spacing.sm, backgroundColor: colors.panelAlt, borderRadius: radius.md, marginBottom: spacing.sm },
  rowTitle: { color: colors.text, fontSize: fontSize.listTitle, fontWeight: '600' },
  input: { minHeight: 56, borderWidth: 2, borderColor: colors.line, borderRadius: radius.md, paddingHorizontal: spacing.md, fontSize: 20, color: colors.text, backgroundColor: colors.panelAlt },
})
