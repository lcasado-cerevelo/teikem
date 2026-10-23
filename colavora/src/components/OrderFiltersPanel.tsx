import { useMemo } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { MaterialIcons } from '@expo/vector-icons';
import MultiSelectSheet, { type MultiOption } from './MultiSelectSheet';
import type { UserSender } from '@services/custom-api/custom-api';
import type { OrderFilters } from '@/stores';
import { colors } from '@constants/theme';

// yyyy-MM-dd: se valida en vez de usar un date-picker nativo para no meter otra
// dependencia. Vacío = sin límite por ese lado.
const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;
export const isValidDate = (s: string) => s === '' || (DATE_RE.test(s) && !Number.isNaN(Date.parse(s)));

// Panel avanzado del listado de órdenes. SOLO se monta para usuarios que están
// en UsersSender (los choferes no lo ven). El filtro de sender es OBLIGATORIO:
// sin él el alcance serían decenas de miles de órdenes y la consulta al servidor
// no acotaría nada.
export default function OrderFiltersPanel({
  senders,
  filters,
  onChange,
}: {
  senders: UserSender[];
  filters: OrderFilters;
  onChange: (patch: Partial<OrderFilters>) => void;
}) {
  const { t } = useTranslation();

  const options: MultiOption[] = useMemo(
    () =>
      senders.map((s) => ({
        id: s.senderID,
        label: s.senderName?.trim() || s.sender || String(s.senderID),
      })),
    [senders],
  );

  const fromBad = !isValidDate(filters.dateFrom);
  const toBad = !isValidDate(filters.dateTo);

  // Resumen visible cuando el panel está colapsado: si hay algo aplicado, el
  // usuario debe poder verlo sin tener que abrir el panel para acordarse.
  const summary = useMemo(() => {
    const parts: string[] = [];
    if (filters.senderIds.length > 0) {
      parts.push(t('ORDERS.FILTERS_SUMMARY_SENDERS', { count: filters.senderIds.length }));
    }
    if (filters.dateFrom) parts.push(t('ORDERS.FILTERS_SUMMARY_FROM', { date: filters.dateFrom }));
    if (filters.dateTo) parts.push(t('ORDERS.FILTERS_SUMMARY_TO', { date: filters.dateTo }));
    if (filters.city.trim()) parts.push(t('ORDERS.FILTERS_SUMMARY_CITY', { city: filters.city.trim() }));
    return parts.join(' · ');
  }, [filters.senderIds, filters.dateFrom, filters.dateTo, filters.city, t]);

  return (
    <View style={styles.wrap}>
      <Pressable style={styles.toggle} onPress={() => onChange({ panelOpen: !filters.panelOpen })}>
        <MaterialIcons name="tune" size={18} color={colors.primary} />
        <View style={styles.flex}>
          <Text style={styles.toggleText}>{t('ORDERS.FILTERS')}</Text>
          {!filters.panelOpen && summary.length > 0 && (
            <Text style={styles.summary} numberOfLines={1}>
              {summary}
            </Text>
          )}
        </View>
        <MaterialIcons
          name={filters.panelOpen ? 'expand-less' : 'expand-more'}
          size={22}
          color={colors.primary}
        />
      </Pressable>

      {filters.panelOpen && (
        <View style={styles.body}>
          <Text style={styles.label}>
            {t('ORDERS.FILTER_SENDER')} <Text style={styles.required}>*</Text>
          </Text>
          <MultiSelectSheet
            options={options}
            selected={filters.senderIds}
            onChange={(ids) => onChange({ senderIds: ids })}
            placeholder={t('ORDERS.FILTER_SENDER_PLACEHOLDER')}
            title={t('ORDERS.FILTER_SENDER')}
          />
          {filters.senderIds.length === 0 && (
            <Text style={styles.hint}>{t('ORDERS.FILTER_SENDER_REQUIRED')}</Text>
          )}

          <Text style={styles.label}>{t('ORDERS.FILTER_DATES')}</Text>
          <View style={styles.row}>
            <TextInput
              style={[styles.input, styles.flex, fromBad && styles.inputBad]}
              value={filters.dateFrom}
              onChangeText={(v) => onChange({ dateFrom: v })}
              placeholder="yyyy-mm-dd"
              placeholderTextColor={colors.muted}
              autoCapitalize="none"
              keyboardType="numbers-and-punctuation"
            />
            <TextInput
              style={[styles.input, styles.flex, toBad && styles.inputBad]}
              value={filters.dateTo}
              onChangeText={(v) => onChange({ dateTo: v })}
              placeholder="yyyy-mm-dd"
              placeholderTextColor={colors.muted}
              autoCapitalize="none"
              keyboardType="numbers-and-punctuation"
            />
          </View>
          {(fromBad || toBad) && <Text style={styles.hint}>{t('ORDERS.FILTER_DATE_INVALID')}</Text>}

          <Text style={styles.label}>{t('ORDERS.FILTER_CITY')}</Text>
          <TextInput
            style={styles.input}
            value={filters.city}
            onChangeText={(v) => onChange({ city: v })}
            placeholder={t('ORDERS.FILTER_CITY_PLACEHOLDER')}
            placeholderTextColor={colors.muted}
          />

          <Pressable
            style={styles.clear}
            onPress={() => onChange({ senderIds: [], dateFrom: '', dateTo: '', city: '' })}
          >
            <Text style={styles.clearText}>{t('ORDERS.FILTER_CLEAR')}</Text>
          </Pressable>
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { marginHorizontal: 20, marginBottom: 6 },
  toggle: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingVertical: 8 },
  toggleText: { color: colors.primary, fontWeight: '600' },
  summary: { color: colors.muted, fontSize: 12 },
  body: { gap: 6, paddingBottom: 8 },
  label: { fontSize: 13, fontWeight: '600', color: colors.text, marginTop: 4 },
  required: { color: colors.danger },
  hint: { fontSize: 12, color: colors.muted },
  row: { flexDirection: 'row', gap: 10 },
  flex: { flex: 1 },
  input: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    color: colors.text,
  },
  inputBad: { borderColor: colors.danger },
  clear: {
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: 10,
    paddingVertical: 10,
    alignItems: 'center',
    marginTop: 8,
  },
  clearText: { color: colors.primary, fontWeight: '600' },
});
