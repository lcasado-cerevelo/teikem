import { useMemo, useState } from 'react';
import { Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { MaterialIcons } from '@expo/vector-icons';
import { Text } from '@components/Text';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import { fold } from '@utils/scanRules';
import type { Colors } from '@constants/theme';

export interface MultiOption { id: string; label: string }

const MAX_SHOWN = 60;

// Lote 216 (ajuste) — lista de selección MÚLTIPLE con búsqueda por escrito (sin tildes ni mayúsculas): rutas, ciudades o choferes para el escaneo. Muestra las primeras 60 coincidencias; escribir acota.
export default function SearchMultiSelect({
  options,
  selected,
  onToggle,
  onClear,
  placeholder,
}: {
  options: MultiOption[];
  selected: Set<string>;
  onToggle: (id: string) => void;
  onClear: () => void;
  placeholder: string;
}) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const q = fold(query);
  const filtered = useMemo(() => (q ? options.filter((o) => fold(o.label).includes(q)) : options), [options, q]);
  const shown = filtered.slice(0, MAX_SHOWN);

  return (
    <View style={styles.box}>
      <View style={styles.searchRow}>
        <MaterialIcons name="search" size={18} color={colors.muted} />
        <TextInput style={styles.search} value={query} onChangeText={setQuery} placeholder={placeholder} placeholderTextColor={colors.muted} autoCapitalize="none" autoCorrect={false} accessibilityLabel={placeholder} />
        {selected.size > 0 ? (
          <Pressable onPress={onClear} accessibilityRole="button" hitSlop={8}><Text style={styles.clear}>{t('SCAN.SEL_CLEAR', { n: selected.size })}</Text></Pressable>
        ) : null}
      </View>
      {options.length === 0 ? <Text style={styles.empty}>{t('SCAN.SEL_NONE')}</Text> : null}
      {shown.map((o) => {
        const on = selected.has(o.id);
        return (
          <Pressable key={o.id} style={styles.row} onPress={() => onToggle(o.id)} accessibilityRole="checkbox" accessibilityState={{ checked: on }}>
            <MaterialIcons name={on ? 'check-box' : 'check-box-outline-blank'} size={22} color={on ? colors.primary : colors.muted} />
            <Text style={styles.label}>{o.label}</Text>
          </Pressable>
        );
      })}
      {filtered.length > shown.length ? <Text style={styles.empty}>{t('SCAN.SEL_MORE', { n: filtered.length - shown.length })}</Text> : null}
      {options.length > 0 && filtered.length === 0 ? <Text style={styles.empty}>{t('SCAN.SEL_NO_MATCH')}</Text> : null}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  box: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 8, gap: 2 },
  searchRow: { flexDirection: 'row', alignItems: 'center', gap: 6, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.gray, paddingBottom: 6, marginBottom: 4 },
  search: { flex: 1, fontSize: 15, paddingVertical: 4, color: colors.text },
  clear: { color: colors.primary, fontWeight: '700', fontSize: 12 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingVertical: 8 },
  label: { flex: 1, color: colors.text, fontSize: 14 },
  empty: { color: colors.muted, fontSize: 12, paddingVertical: 6, textAlign: 'center' },
});
