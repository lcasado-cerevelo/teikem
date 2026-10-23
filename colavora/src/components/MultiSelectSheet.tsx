import { useMemo, useState } from 'react';
import { FlatList, Modal, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { MaterialIcons } from '@expo/vector-icons';
import { colors } from '@constants/theme';

export interface MultiOption {
  id: number;
  label: string;
}

// Selector de múltiples opciones en modal, con buscador interno: un usuario
// puede tener ~90 senders, así que una lista sin filtro sería inservible.
export default function MultiSelectSheet({
  options,
  selected,
  onChange,
  placeholder,
  title,
}: {
  options: MultiOption[];
  selected: number[];
  onChange: (ids: number[]) => void;
  placeholder: string;
  title: string;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? options.filter((o) => o.label.toLowerCase().includes(q)) : options;
  }, [options, query]);

  const summary = useMemo(() => {
    if (selected.length === 0) return placeholder;
    if (selected.length === 1) {
      return options.find((o) => o.id === selected[0])?.label ?? placeholder;
    }
    return t('ORDERS.FILTER_SENDER_COUNT', { count: selected.length });
  }, [selected, options, placeholder, t]);

  const toggle = (id: number) =>
    onChange(selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]);

  return (
    <>
      <Pressable style={styles.field} onPress={() => setOpen(true)}>
        <Text style={[styles.fieldText, selected.length === 0 && styles.placeholder]} numberOfLines={1}>
          {summary}
        </Text>
        <MaterialIcons name="arrow-drop-down" size={22} color={colors.muted} />
      </Pressable>

      <Modal visible={open} animationType="slide" transparent onRequestClose={() => setOpen(false)}>
        <View style={styles.backdrop}>
          <View style={styles.sheet}>
            <View style={styles.sheetHeader}>
              <Text style={styles.sheetTitle}>{title}</Text>
              <Pressable onPress={() => setOpen(false)} hitSlop={8}>
                <MaterialIcons name="close" size={22} color={colors.text} />
              </Pressable>
            </View>

            <View style={styles.searchWrap}>
              <MaterialIcons name="search" size={18} color={colors.muted} />
              <TextInput
                style={styles.searchInput}
                value={query}
                onChangeText={setQuery}
                placeholder={t('ORDERS.FILTER_SENDER_SEARCH')}
                placeholderTextColor={colors.muted}
                autoCapitalize="none"
              />
            </View>

            <FlatList
              data={visible}
              keyExtractor={(o) => String(o.id)}
              keyboardShouldPersistTaps="handled"
              renderItem={({ item }) => {
                const on = selected.includes(item.id);
                return (
                  <Pressable style={styles.row} onPress={() => toggle(item.id)}>
                    <MaterialIcons
                      name={on ? 'check-box' : 'check-box-outline-blank'}
                      size={22}
                      color={on ? colors.primary : colors.muted}
                    />
                    <Text style={styles.rowText} numberOfLines={2}>{item.label}</Text>
                  </Pressable>
                );
              }}
            />

            <View style={styles.sheetActions}>
              <Pressable style={styles.clearBtn} onPress={() => onChange([])}>
                <Text style={styles.clearText}>{t('ORDERS.FILTER_CLEAR')}</Text>
              </Pressable>
              <Pressable style={styles.doneBtn} onPress={() => setOpen(false)}>
                <Text style={styles.doneText}>{t('ORDERS.FILTER_DONE')}</Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  field: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  fieldText: { flex: 1, color: colors.text },
  placeholder: { color: colors.muted },
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  sheet: {
    backgroundColor: colors.background,
    borderTopLeftRadius: 16,
    borderTopRightRadius: 16,
    paddingHorizontal: 20,
    paddingTop: 14,
    paddingBottom: 20,
    maxHeight: '80%',
  },
  sheetHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 },
  sheetTitle: { fontSize: 16, fontWeight: '700', color: colors.text },
  searchWrap: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 10,
    marginBottom: 8,
  },
  searchInput: { flex: 1, paddingVertical: 8, color: colors.text },
  row: { flexDirection: 'row', alignItems: 'center', gap: 10, paddingVertical: 10 },
  rowText: { flex: 1, color: colors.text },
  sheetActions: { flexDirection: 'row', gap: 10, marginTop: 10 },
  clearBtn: {
    flex: 1,
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
  },
  clearText: { color: colors.primary, fontWeight: '600' },
  doneBtn: {
    flex: 1,
    backgroundColor: colors.primary,
    borderRadius: 10,
    paddingVertical: 12,
    alignItems: 'center',
  },
  doneText: { color: colors.primaryContrast, fontWeight: '700' },
});
