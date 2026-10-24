import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useAttention } from '@hooks/staff';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { attentionTitle } from './attentionText';

// «Necesita tu atención» (Lote 213): lo que la web muestra en el Pulso, por compañía, con los 5 pendientes más antiguos de cada una. Solo lectura en esta versión.
export default function AttentionCard({ multi }: { multi: boolean }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const { data, isPending } = useAttention();
  const money = (n: number) => new Intl.NumberFormat(i18n.language === 'en' ? 'en-US' : 'es-PR', { style: 'currency', currency: 'USD' }).format(n);
  const list = data ?? [];
  const total = list.reduce((n, c) => n + c.total, 0);
  return (
    <View style={styles.card}>
      <View style={styles.head}>
        <Text style={styles.title}>{t('ATTENTION.TITLE')}</Text>
        {isPending ? <ActivityIndicator color={colors.primary} /> : <Text style={[styles.badge, total === 0 && styles.badgeOk]}>{total}</Text>}
      </View>
      {!isPending && total === 0 ? (
        <Text style={styles.muted}>{t('ATTENTION.ALL_CLEAR')}</Text>
      ) : (
        list.map((c) => (
          <View key={c.tenantId} style={styles.company}>
            {multi ? <Text style={styles.companyName}>{`${c.companyName} · ${c.total}`}</Text> : null}
            {c.items.map((it, i) => (
              <View key={`${it.code}-${i}`} style={[styles.row, it.tone === 'danger' && styles.rowDanger]}>
                <Text style={styles.rowText}>{attentionTitle(it, t, money)}</Text>
              </View>
            ))}
          </View>
        ))
      )}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  card: { marginHorizontal: 20, marginVertical: 8, padding: 14, borderWidth: 1, borderColor: colors.gray, borderRadius: 13, gap: 8 },
  head: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  title: { fontSize: 16, fontWeight: '700', color: colors.text },
  badge: { minWidth: 28, textAlign: 'center', borderRadius: 14, paddingHorizontal: 8, paddingVertical: 2, backgroundColor: colors.danger, color: colors.primaryContrast, fontWeight: '700' },
  badgeOk: { backgroundColor: colors.online },
  muted: { color: colors.muted },
  company: { gap: 6 },
  companyName: { fontWeight: '700', color: colors.muted, fontSize: 13 },
  row: { borderLeftWidth: 3, borderLeftColor: colors.primary, paddingLeft: 8, paddingVertical: 4 },
  rowDanger: { borderLeftColor: colors.danger },
  rowText: { color: colors.text, fontSize: 14 },
});
