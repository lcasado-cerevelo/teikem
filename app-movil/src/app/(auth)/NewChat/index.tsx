import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import ScreenHeader from '@components/ScreenHeader';
import { useChatCompanies } from '@hooks/messages';
import { fetchPeople, fetchSuggested, type MessagePerson, type Suggestion } from '@services/messagesApi';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Nueva conversación: se escoge la compañía (si trabaja en varias), luego con quién hablar. Si viene de «Hablar sobre este envío» arriba salen los SUGERIDOS
// (responsable de la ruta, de la zona, despachadores, conversaciones recientes); debajo, las personas con las que puede escribir (las que la compañía le permite ver).
export default function NewChatScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const p = useLocalSearchParams<{ tenantId?: string; refEntity?: string; refId?: string; refTitle?: string }>();
  const companies = useChatCompanies();
  // la compañía sale del envío (si vino de uno), de la única compañía del chofer, o de lo que escoja
  const [picked, setPicked] = useState<number | null>(null);
  const tenantId: number | null = picked ?? (p.tenantId ? Number(p.tenantId) : companies.length === 1 ? companies[0].tenantId : null);
  const setTenantId = setPicked;
  const [q, setQ] = useState('');
  const [debounced, setDebounced] = useState('');
  const hasRef = (p.refEntity === 'ORDER' || p.refEntity === 'TRIP') && !!p.refId;

  useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 300);
    return () => clearTimeout(id);
  }, [q]);

  const people = useQuery({ queryKey: ['people', tenantId ?? 0, debounced], queryFn: () => fetchPeople(tenantId as number, debounced), enabled: tenantId !== null, staleTime: 30_000 });
  const suggested = useQuery({
    queryKey: ['suggested', tenantId ?? 0, p.refEntity ?? '', p.refId ?? ''],
    queryFn: () => fetchSuggested(tenantId as number, p.refEntity as 'ORDER' | 'TRIP', p.refId as string),
    enabled: tenantId !== null && hasRef,
    staleTime: 60_000,
  });

  const refParams = useMemo(() => (hasRef ? { refEntity: p.refEntity as string, refId: p.refId as string, refTitle: p.refTitle ?? '' } : {}), [hasRef, p.refEntity, p.refId, p.refTitle]);

  const open = (extra: Record<string, string>) => router.replace({ pathname: '/(auth)/Chat', params: { tenantId: String(tenantId), ...refParams, ...extra } });
  const pickPerson = (x: MessagePerson) => open({ newUserId: String(x.id), newName: x.name });
  const pickSuggestion = (s: Suggestion) => {
    if (s.userId !== null) open({ newUserId: String(s.userId), newName: s.name });
    else if (s.roleId !== null) open({ newRoleId: String(s.roleId), newName: s.name });
  };
  const sourceLabel = (s: Suggestion) => (s.source === 'TRIP' ? t('CHAT.SRC_TRIP') : s.source === 'ROLE' ? t('CHAT.SRC_ROLE') : s.source === 'RECENT' ? t('CHAT.SRC_RECENT') : s.sourceLabel);

  if (tenantId === null) {
    return (
      <SafeAreaView style={styles.container} edges={['top']}>
        <ScreenHeader title={t('CHAT.NEW')} />
        <Text style={styles.section}>{t('CHAT.PICK_COMPANY')}</Text>
        {companies.map((c) => (
          <Pressable key={c.tenantId} style={styles.item} onPress={() => setTenantId(c.tenantId)} accessibilityRole="button">
            <Text style={styles.name}>{c.name}</Text>
          </Pressable>
        ))}
      </SafeAreaView>
    );
  }

  const list = people.data ?? [];
  const sug = suggested.data ?? [];
  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('CHAT.NEW')} />
      {hasRef && p.refTitle ? <Text style={styles.refHint}>{`${p.refEntity === 'ORDER' ? '📦' : '🚚'} ${p.refTitle}`}</Text> : null}
      <FlatList
        data={list}
        keyExtractor={(x) => String(x.id)}
        keyboardShouldPersistTaps="handled"
        ListHeaderComponent={
          <View>
            {hasRef && sug.length > 0 && (
              <View>
                <Text style={styles.section}>{t('CHAT.SUGGESTED')}</Text>
                {sug.map((s) => (
                  <Pressable key={`${s.source}-${s.userId ?? 'r'}-${s.roleId ?? 'u'}`} style={styles.item} onPress={() => pickSuggestion(s)} accessibilityRole="button">
                    <Text style={styles.name}>{s.roleId !== null ? `👥 ${s.name}` : s.name}</Text>
                    <Text style={styles.sub}>{sourceLabel(s)}</Text>
                  </Pressable>
                ))}
              </View>
            )}
            <Text style={styles.section}>{t('CHAT.PEOPLE')}</Text>
            <TextInput style={styles.search} value={q} onChangeText={setQ} placeholder={t('CHAT.SEARCH_PEOPLE')} autoCapitalize="none" />
            {people.isPending ? <ActivityIndicator color={colors.primary} /> : null}
          </View>
        }
        ListEmptyComponent={people.isPending ? null : <Text style={styles.empty}>{t('CHAT.NO_PEOPLE')}</Text>}
        renderItem={({ item }) => (
          <Pressable style={styles.item} onPress={() => pickPerson(item)} accessibilityRole="button">
            <Text style={styles.name}>{item.name}</Text>
            {item.online ? <Text style={styles.sub}>{t('CHAT.ONLINE')}</Text> : null}
          </Pressable>
        )}
      />
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  section: { marginHorizontal: 24, marginTop: 10, marginBottom: 4, fontWeight: '700', color: colors.muted },
  refHint: { marginHorizontal: 24, color: colors.primary, fontWeight: '700' },
  search: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, marginHorizontal: 20, marginVertical: 6, paddingHorizontal: 12, paddingVertical: 8, fontSize: 15 },
  item: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginHorizontal: 20, marginVertical: 5, padding: 14, gap: 2 },
  name: { fontSize: 16, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
  empty: { textAlign: 'center', color: colors.muted, marginTop: 16 },
});
