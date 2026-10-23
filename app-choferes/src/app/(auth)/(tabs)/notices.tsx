import { useMemo } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import LinkText from '@components/LinkText';
import { useNotices } from '@hooks/queries';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Bandeja de avisos de la oficina (de todas las compañías del chofer). Tocar un aviso lo marca leído; «Marcar todo» los marca todos.
export default function NoticesScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const { data, isPending, unread, markRead } = useNotices();
  const notices = useMemo(() => data ?? [], [data]);

  const fmt = (iso: string) => new Date(iso).toLocaleString(i18n.language === 'en' ? 'en-US' : 'es-PR', { dateStyle: 'short', timeStyle: 'short' });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={`${t('NOTICES.TITLE')}${unread > 0 ? ` (${unread})` : ''}`} />
      {unread > 0 && (
        <Pressable onPress={() => void markRead(notices)} accessibilityRole="button">
          <Text style={styles.markAll}>{t('NOTICES.MARK_ALL')}</Text>
        </Pressable>
      )}
      {isPending ? (
        <ActivityIndicator color={colors.primary} />
      ) : notices.length === 0 ? (
        <Text style={styles.empty}>{t('NOTICES.EMPTY')}</Text>
      ) : (
        <FlatList
          data={notices}
          keyExtractor={(n) => `${n.tenantId}-${n.id}`}
          renderItem={({ item }) => (
            <Pressable style={[styles.item, !item.read && styles.itemUnread]} onPress={() => void markRead([item])} accessibilityRole="button">
              <View style={styles.row}>
                <Text style={[styles.title, !item.read && styles.bold]}>{item.title}</Text>
                {!item.read && <View style={styles.dot} />}
              </View>
              {item.body ? <LinkText style={styles.body}>{item.body}</LinkText> : null}
              <Text style={styles.meta}>{`${item.companyName} · ${fmt(item.createdAtUtc)}`}</Text>
            </Pressable>
          )}
        />
      )}
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  markAll: { color: colors.primary, fontWeight: '700', textAlign: 'right', marginHorizontal: 24, marginBottom: 6 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  item: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginHorizontal: 20, marginVertical: 8, padding: 14, gap: 6 },
  itemUnread: { borderColor: colors.primary },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  title: { flex: 1, fontSize: 16, color: colors.text },
  bold: { fontWeight: '700' },
  dot: { width: 10, height: 10, borderRadius: 5, backgroundColor: colors.primary },
  body: { fontSize: 14, color: colors.text },
  meta: { fontSize: 12, color: colors.muted },
});
