import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import { useThreads, useChatCompanies } from '@hooks/messages';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import { previewOf } from '@/utils/chatRules';
import type { Colors } from '@constants/theme';

// Mensajes: las conversaciones del chofer con la oficina (y los grupos donde lo metan), de todas sus compañías. Tocar una la abre; «+» empieza una nueva.
export default function MessagesScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const router = useRouter();
  const companies = useChatCompanies();
  const { data, isPending, isRefetching, refetch, unread } = useThreads();
  const threads = data ?? [];
  const fmt = (iso: string) => new Date(iso).toLocaleString(i18n.language === 'en' ? 'en-US' : 'es-PR', { dateStyle: 'short', timeStyle: 'short' });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={`${t('CHAT.TITLE')}${unread > 0 ? ` (${unread})` : ''}`} />
      <Pressable style={styles.newBtn} onPress={() => router.push('/(auth)/NewChat')} accessibilityRole="button" accessibilityLabel={t('CHAT.NEW')}>
        <Ionicons name="create-outline" size={20} color={colors.primaryContrast} />
        <Text style={styles.newBtnText}>{t('CHAT.NEW')}</Text>
      </Pressable>
      {isPending ? (
        <ActivityIndicator color={colors.primary} />
      ) : threads.length === 0 ? (
        <Text style={styles.empty}>{t('CHAT.EMPTY')}</Text>
      ) : (
        <FlatList
          data={threads}
          keyExtractor={(x) => x.key}
          refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} />}
          renderItem={({ item }) => (
            <Pressable
              style={[styles.item, item.unread > 0 && styles.itemUnread]}
              onPress={() => router.push({ pathname: '/(auth)/Chat', params: { tenantId: String(item.tenantId), threadId: item.publicId } })}
              accessibilityRole="button"
            >
              <View style={styles.row}>
                <Text style={[styles.name, item.unread > 0 && styles.bold]} numberOfLines={1}>
                  {item.kind === 'GROUP' ? `👥 ${item.name}` : item.name}
                </Text>
                {item.unread > 0 && !item.isMuted && (
                  <View style={styles.badge}>
                    <Text style={styles.badgeText}>{item.unread}</Text>
                  </View>
                )}
              </View>
              <Text style={styles.preview} numberOfLines={2}>
                {item.kind === 'GROUP' && item.lastSenderName ? `${item.lastSenderName}: ` : ''}
                {previewOf(item.lastMessagePreview, t('CHAT.NO_MESSAGES'))}
              </Text>
              <Text style={styles.meta}>{`${companies.length > 1 ? `${item.companyName} · ` : ''}${fmt(item.lastMessageAtUtc)}`}</Text>
            </Pressable>
          )}
        />
      )}
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  newBtn: { flexDirection: 'row', alignItems: 'center', gap: 6, alignSelf: 'flex-end', marginHorizontal: 24, marginBottom: 8, backgroundColor: colors.primary, borderRadius: 20, paddingVertical: 8, paddingHorizontal: 14 },
  newBtnText: { color: colors.primaryContrast, fontWeight: '700' },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  item: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginHorizontal: 20, marginVertical: 6, padding: 14, gap: 4 },
  itemUnread: { borderColor: colors.primary },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 8 },
  name: { flex: 1, fontSize: 16, color: colors.text },
  bold: { fontWeight: '700' },
  badge: { minWidth: 22, height: 22, borderRadius: 11, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 6 },
  badgeText: { color: colors.primaryContrast, fontSize: 12, fontWeight: '700' },
  preview: { fontSize: 14, color: colors.text },
  meta: { fontSize: 12, color: colors.muted },
});
