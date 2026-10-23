import { useState } from 'react';
import { FlatList, Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { colors } from '@constants/theme';

export interface Option {
  id: number;
  label: string;
}

// Lightweight dropdown replacement (no native Picker dependency): a pressable
// field that opens a modal list. Used for the exception reason and the COD type.
export default function OptionSelect({
  options,
  value,
  placeholder,
  onSelect,
  onClear,
  disabled,
}: {
  options: Option[];
  value: number | null;
  placeholder: string;
  onSelect: (id: number) => void;
  // When provided, a ✕ appears next to the selected value to undo an
  // accidental pick (the field goes back to the placeholder).
  onClear?: () => void;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const selected = options.find((o) => o.id === value);

  return (
    <>
      <Pressable
        style={[styles.field, disabled && styles.disabled]}
        onPress={() => !disabled && setOpen(true)}
      >
        <Text style={selected ? styles.value : styles.placeholder} numberOfLines={1}>
          {selected ? selected.label : placeholder}
        </Text>
        {selected && onClear && !disabled ? (
          <Pressable onPress={onClear} hitSlop={10}>
            <Text style={styles.clear}>✕</Text>
          </Pressable>
        ) : (
          <Text style={styles.caret}>▾</Text>
        )}
      </Pressable>

      <Modal visible={open} transparent animationType="fade" onRequestClose={() => setOpen(false)}>
        <Pressable style={styles.backdrop} onPress={() => setOpen(false)}>
          <View style={styles.sheet}>
            <FlatList
              data={options}
              keyExtractor={(o) => String(o.id)}
              renderItem={({ item }) => (
                <Pressable
                  style={styles.row}
                  onPress={() => {
                    onSelect(item.id);
                    setOpen(false);
                  }}
                >
                  <Text style={[styles.rowText, item.id === value && styles.rowTextActive]}>{item.label}</Text>
                </Pressable>
              )}
            />
          </View>
        </Pressable>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 12,
  },
  disabled: { opacity: 0.5 },
  value: { color: colors.text, flex: 1 },
  placeholder: { color: colors.muted, flex: 1 },
  caret: { color: colors.muted, marginLeft: 8 },
  clear: { color: colors.danger, marginLeft: 8, fontSize: 16, fontWeight: '600' },
  backdrop: { flex: 1, backgroundColor: '#00000066', justifyContent: 'center', padding: 24 },
  sheet: { backgroundColor: colors.background, borderRadius: 14, maxHeight: '70%', overflow: 'hidden' },
  row: { paddingHorizontal: 16, paddingVertical: 14, borderBottomWidth: 1, borderBottomColor: colors.gray },
  rowText: { fontSize: 15, color: colors.text },
  rowTextActive: { color: colors.primary, fontWeight: '700' },
});
