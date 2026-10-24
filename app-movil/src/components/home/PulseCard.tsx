import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { usePulse } from '@hooks/staff';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Pulso del día (Lote 213): los indicadores que la persona puede ver en la web, en tarjetas pequeñas, por compañía.
export default function PulseCard({ multi }: { multi: boolean }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const { data, isPending } = usePulse();
  const locale = i18n.language === 'en' ? 'en-US' : 'es-PR';
  const fmt = (v: number | null, money: boolean) => (v == null ? '—' : new Intl.NumberFormat(locale, money ? { style: 'currency', currency: 'USD', maximumFractionDigits: 0 } : { maximumFractionDigits: 1 }).format(v));
  const list = (data ?? []).filter((c) => c.indicators.length > 0);
  if (!isPending && list.length === 0) return null;
  return (
    <View style={styles.card}>
      <Text style={styles.title}>{t('PULSE.TITLE')}</Text>
      {isPending ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        list.map((c) => (
          <View key={c.tenantId} style={styles.company}>
            {multi ? <Text style={styles.companyName}>{c.companyName}</Text> : null}
            <View style={styles.grid}>
              {c.indicators.map((i) => (
                <View key={i.id} style={styles.tile}>
                  <Text style={styles.value}>{fmt(i.value, i.isMoney)}</Text>
                  <Text style={styles.name} numberOfLines={2}>{i.name}</Text>
                </View>
              ))}
            </View>
          </View>
        ))
      )}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  card: { marginHorizontal: 20, marginVertical: 8, padding: 14, borderWidth: 1, borderColor: colors.gray, borderRadius: 13, gap: 8 },
  title: { fontSize: 16, fontWeight: '700', color: colors.text },
  company: { gap: 6 },
  companyName: { fontWeight: '700', color: colors.muted, fontSize: 13 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  tile: { width: '47%', backgroundColor: colors.backgroundSecondary, borderRadius: 10, padding: 10, gap: 2 },
  value: { fontSize: 20, fontWeight: '700', color: colors.text },
  name: { fontSize: 12, color: colors.muted },
});
