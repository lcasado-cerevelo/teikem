import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Image, KeyboardAvoidingView, Modal, Platform, Pressable, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import * as ImagePicker from 'expo-image-picker';
import { onlineManager, useQuery, useQueryClient } from '@tanstack/react-query';
import ScreenHeader from '@components/ScreenHeader';
import LinkText from '@components/LinkText';
import AuthPhoto from '@components/AuthPhoto';
import { useChatCompanies, useMarkRead, useThreadDetail } from '@hooks/messages';
import { useDriverWork } from '@hooks/queries';
import { createThread, photoSource, sendBody, type ChatMessage, type MessagePhoto, type MessageRefCard, type OutgoingRef } from '@services/messagesApi';
import { drainOutbox, enqueue, listPending } from '@/offline';
import { useAppStore } from '@/stores';
import { compressProofPhoto } from '@utils/photo';
import { canSend, findShipmentForCard, MAX_PHOTOS } from '@/utils/chatRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

interface PendingMessage {
  id: string;
  text: string;
  photos: number;
}

// Un mensaje escrito en el teléfono y todavía sin enviar (está en la cola sin conexión del chofer): se muestra con «Sin enviar» hasta que sale.
function usePendingMessages(threadId: string | undefined) {
  return useQuery({
    queryKey: ['pendingMessages', threadId ?? ''],
    enabled: !!threadId,
    refetchInterval: 3_000,
    queryFn: async (): Promise<PendingMessage[]> => {
      const items = await listPending(100);
      return items
        .filter((i) => i.kind === 'message')
        .flatMap((i) => {
          try {
            const p = JSON.parse(i.payload) as { threadId?: string; body?: { text?: string; attachments?: unknown[] } };
            return p.threadId === threadId ? [{ id: i.id, text: p.body?.text ?? '', photos: p.body?.attachments?.length ?? 0 }] : [];
          } catch {
            return [];
          }
        });
    },
  });
}

function errorText(e: unknown, fallback: string): string {
  const title = (e as { response?: { data?: { title?: string } } })?.response?.data?.title;
  return title && title.length < 200 ? title : fallback;
}

// La conversación: burbujas (las mías a la derecha), tarjeta de la orden o ruta de la que se habla, fotos y el campo para escribir con cámara y galería.
// Escribir en un hilo que ya existe funciona sin señal (queda en la cola y sale sola). Empezar una conversación nueva necesita conexión.
export default function ChatScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const router = useRouter();
  const qc = useQueryClient();
  const p = useLocalSearchParams<{ tenantId?: string; threadId?: string; newUserId?: string; newRoleId?: string; newName?: string; refEntity?: string; refId?: string; refTitle?: string }>();
  const tenantId = Number(p.tenantId);
  const threadId = p.threadId;
  const isDraft = !threadId;
  const company = useChatCompanies().find((c) => c.tenantId === tenantId);
  const { data: detail, isPending } = useThreadDetail(tenantId, threadId);
  const { data: pending } = usePendingMessages(threadId);
  const { data: work } = useDriverWork();
  const markRead = useMarkRead();
  const addShipment = useAppStore((s) => s.addShipment);

  const [text, setText] = useState('');
  const [photos, setPhotos] = useState<{ uri: string; base64: string }[]>([]);
  const [sending, setSending] = useState(false);
  const [refUsed, setRefUsed] = useState(false);
  const [viewer, setViewer] = useState<MessagePhoto | null>(null);

  const reversed = useMemo(() => [...(detail?.messages ?? [])].reverse(), [detail?.messages]);
  const unread = detail?.thread.unread ?? 0;
  useEffect(() => {
    if (threadId && unread > 0) void markRead(tenantId, threadId);
  }, [threadId, tenantId, unread, markRead]);

  const fmt = (iso: string) => new Date(iso).toLocaleString(i18n.language === 'en' ? 'en-US' : 'es-PR', { dateStyle: 'short', timeStyle: 'short' });
  const pendingRef: OutgoingRef | undefined = !refUsed && (p.refEntity === 'ORDER' || p.refEntity === 'TRIP') && p.refId ? { entity: p.refEntity, publicId: p.refId } : undefined;

  const pick = async (camera: boolean) => {
    if (photos.length >= MAX_PHOTOS) {
      Alert.alert(t('CHAT.MAX_PHOTOS'));
      return;
    }
    if (camera) {
      const perm = await ImagePicker.requestCameraPermissionsAsync();
      if (!perm.granted) {
        Alert.alert(t('DELIVERY.CAMERA_PERMISSION'));
        return;
      }
    }
    const opts = { mediaTypes: ['images' as const], quality: 0.8 };
    const result = camera ? await ImagePicker.launchCameraAsync(opts) : await ImagePicker.launchImageLibraryAsync(opts);
    const asset = result.canceled ? null : result.assets[0];
    if (asset) {
      const small = await compressProofPhoto(asset.uri, asset.width);
      setPhotos((prev) => [...prev, small].slice(0, MAX_PHOTOS));
    }
  };

  const send = async () => {
    if (!canSend(text, photos.length) || sending) return;
    setSending(true);
    try {
      const out = photos.map((ph, i) => ({ fileName: `foto-${i + 1}.jpg`, dataBase64: ph.base64 }));
      if (isDraft) {
        if (!company) return;
        const roleId = p.newRoleId ? Number(p.newRoleId) : undefined;
        const created = await createThread(company, {
          userIds: p.newUserId ? [Number(p.newUserId)] : undefined,
          roleId,
          followRole: roleId ? true : undefined,
          text: text.trim() || undefined,
          ref: pendingRef,
          photos: out,
        });
        void qc.invalidateQueries({ queryKey: ['threads'] });
        router.replace({ pathname: '/(auth)/Chat', params: { tenantId: String(tenantId), threadId: created.thread.publicId } });
        return;
      }
      const body = sendBody(text, out, pendingRef);
      await enqueue({ kind: 'message', endpoint: `messages/threads/${threadId}/messages`, method: 'POST', payload: { tenantId, threadId, body } });
      if (onlineManager.isOnline()) await drainOutbox();
      setText('');
      setPhotos([]);
      setRefUsed(true);
      void qc.invalidateQueries({ queryKey: ['thread', tenantId, threadId] });
      void qc.invalidateQueries({ queryKey: ['pendingMessages', threadId] });
      void qc.invalidateQueries({ queryKey: ['threads'] });
    } catch (e) {
      Alert.alert(errorText(e, isDraft ? t('CHAT.NEEDS_CONNECTION') : t('CHAT.SEND_ERROR')));
    } finally {
      setSending(false);
    }
  };

  const openCard = (card: MessageRefCard) => {
    if (card.entity === 'ORDER') {
      const s = findShipmentForCard(work?.shipments ?? [], tenantId, card.title);
      if (s) {
        addShipment(s);
        router.push('/(auth)/ShipmentDetails');
      } else Alert.alert(t('CHAT.ORDER_GONE'));
    } else router.push('/(auth)/(tabs)/manifests');
  };

  const renderMessage = ({ item }: { item: ChatMessage }) => {
    const mine = item.mine;
    const group = detail?.thread.kind === 'GROUP';
    return (
      <View style={[styles.bubbleRow, mine ? styles.rowMine : styles.rowOther]}>
        <View style={[styles.bubble, mine ? styles.bubbleMine : styles.bubbleOther]}>
          {!mine && group ? <Text style={styles.sender}>{item.senderName}</Text> : null}
          {item.ref ? (
            <Pressable style={[styles.card, mine && styles.cardMine]} onPress={() => openCard(item.ref as MessageRefCard)} accessibilityRole="button">
              <Text style={[styles.cardTitle, mine && styles.onMine]}>{`${item.ref.entity === 'ORDER' ? '📦' : '🚚'} ${item.ref.title}`}</Text>
              {item.ref.subtitle ? <Text style={[styles.cardSub, mine && styles.onMine]}>{item.ref.subtitle}</Text> : null}
              {item.ref.status ? <Text style={[styles.cardSub, mine && styles.onMine]}>{item.ref.status}</Text> : null}
            </Pressable>
          ) : null}
          {item.attachments.length > 0 && (
            <View style={styles.photos}>
              {item.attachments.map((a) => (
                <Pressable key={a.publicId} onPress={() => a.state === 'ACTIVE' && setViewer(a)} accessibilityRole="imagebutton">
                  <AuthPhoto tenantId={tenantId} photo={a} thumb style={styles.thumb} />
                </Pressable>
              ))}
            </View>
          )}
          {item.body ? <LinkText style={[styles.body, mine && styles.onMine]} linkStyle={mine ? styles.onMineLink : undefined}>{item.body}</LinkText> : null}
          <Text style={[styles.time, mine && styles.onMine]}>{fmt(item.createdAtUtc)}</Text>
        </View>
      </View>
    );
  };

  const title = detail?.thread.name ?? p.newName ?? t('CHAT.TITLE');

  return (
    <SafeAreaView style={styles.container} edges={['top', 'bottom']}>
      <ScreenHeader title={title} />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        {isPending && !isDraft ? (
          <ActivityIndicator color={colors.primary} />
        ) : (
          <FlatList
            style={styles.flex}
            inverted
            data={reversed}
            keyExtractor={(m) => String(m.id)}
            renderItem={renderMessage}
            ListHeaderComponent={
              (pending ?? []).length > 0 ? (
                <View>
                  {(pending ?? []).map((m) => (
                    <View key={m.id} style={[styles.bubbleRow, styles.rowMine]}>
                      <View style={[styles.bubble, styles.bubbleMine, styles.unsent]}>
                        {m.photos > 0 ? <Text style={styles.onMine}>{`📷 ${m.photos}`}</Text> : null}
                        {m.text ? <Text style={[styles.body, styles.onMine]}>{m.text}</Text> : null}
                        <Text style={[styles.time, styles.onMine]}>{t('CHAT.UNSENT')}</Text>
                      </View>
                    </View>
                  ))}
                </View>
              ) : null
            }
            ListEmptyComponent={isDraft ? <Text style={styles.empty}>{t('CHAT.DRAFT_HINT')}</Text> : null}
          />
        )}

        {pendingRef && (p.refTitle ?? '') !== '' && <Text style={styles.refHint}>{`${pendingRef.entity === 'ORDER' ? '📦' : '🚚'} ${p.refTitle}`}</Text>}
        {photos.length > 0 && (
          <View style={styles.previewRow}>
            {photos.map((ph, i) => (
              <Pressable key={ph.uri} onPress={() => setPhotos((prev) => prev.filter((_, j) => j !== i))} accessibilityLabel={t('CHAT.REMOVE_PHOTO')}>
                <Image source={{ uri: ph.uri }} style={styles.previewThumb} />
                <Ionicons name="close-circle" size={20} color={colors.danger} style={styles.previewX} />
              </Pressable>
            ))}
          </View>
        )}
        <View style={styles.composer}>
          <Pressable onPress={() => void pick(true)} accessibilityRole="button" accessibilityLabel={t('CHAT.CAMERA')}>
            <Ionicons name="camera-outline" size={26} color={colors.primary} />
          </Pressable>
          <Pressable onPress={() => void pick(false)} accessibilityRole="button" accessibilityLabel={t('CHAT.GALLERY')}>
            <Ionicons name="image-outline" size={26} color={colors.primary} />
          </Pressable>
          <TextInput style={styles.input} value={text} onChangeText={setText} placeholder={t('CHAT.TYPE')} multiline maxLength={2000} />
          <Pressable
            style={[styles.send, (!canSend(text, photos.length) || sending) && styles.sendOff]}
            onPress={() => void send()}
            disabled={!canSend(text, photos.length) || sending}
            accessibilityRole="button"
            accessibilityLabel={t('CHAT.SEND')}
          >
            {sending ? <ActivityIndicator color={colors.primaryContrast} /> : <Ionicons name="send" size={20} color={colors.primaryContrast} />}
          </Pressable>
        </View>
      </KeyboardAvoidingView>

      <Modal visible={viewer !== null} transparent animationType="fade" onRequestClose={() => setViewer(null)}>
        <View style={styles.viewer}>
          {viewer ? <Image source={photoSource(tenantId, viewer.publicId, false)} style={styles.viewerImg} resizeMode="contain" /> : null}
          <Pressable style={styles.viewerClose} onPress={() => setViewer(null)} accessibilityRole="button" accessibilityLabel={t('CHAT.CLOSE')}>
            <Ionicons name="close" size={30} color={colors.text} />
          </Pressable>
        </View>
      </Modal>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1 },
  empty: { textAlign: 'center', color: colors.muted, padding: 24, transform: [{ scaleY: -1 }] },
  bubbleRow: { flexDirection: 'row', paddingHorizontal: 12, marginVertical: 3 },
  rowMine: { justifyContent: 'flex-end' },
  rowOther: { justifyContent: 'flex-start' },
  bubble: { maxWidth: '82%', borderRadius: 14, paddingVertical: 8, paddingHorizontal: 12, gap: 4 },
  bubbleMine: { backgroundColor: colors.primary },
  bubbleOther: { backgroundColor: colors.backgroundSecondary, borderWidth: 1, borderColor: colors.gray },
  unsent: { opacity: 0.6 },
  onMine: { color: colors.primaryContrast },
  onMineLink: { color: colors.primaryContrast },
  sender: { fontSize: 12, fontWeight: '700', color: colors.primary },
  body: { fontSize: 15, color: colors.text },
  time: { fontSize: 11, color: colors.muted, alignSelf: 'flex-end' },
  card: { borderWidth: 1, borderColor: colors.primary, borderRadius: 10, padding: 8, gap: 2 },
  cardMine: { borderColor: colors.primaryContrast },
  cardTitle: { fontWeight: '700', fontSize: 14, color: colors.text },
  cardSub: { fontSize: 12, color: colors.muted },
  photos: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  thumb: { width: 96, height: 96, borderRadius: 10 },
  refHint: { marginHorizontal: 14, marginTop: 4, color: colors.primary, fontWeight: '700' },
  previewRow: { flexDirection: 'row', gap: 8, paddingHorizontal: 12, paddingTop: 6 },
  previewThumb: { width: 56, height: 56, borderRadius: 8 },
  previewX: { position: 'absolute', right: -6, top: -6 },
  composer: { flexDirection: 'row', alignItems: 'flex-end', gap: 10, paddingHorizontal: 12, paddingVertical: 8 },
  input: { flex: 1, maxHeight: 120, borderWidth: 1, borderColor: colors.gray, borderRadius: 18, paddingHorizontal: 12, paddingVertical: 8, fontSize: 15 },
  send: { width: 40, height: 40, borderRadius: 20, backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center' },
  sendOff: { opacity: 0.4 },
  viewer: { flex: 1, backgroundColor: colors.background, alignItems: 'center', justifyContent: 'center' },
  viewerImg: { width: '100%', height: '85%' },
  viewerClose: { position: 'absolute', top: 48, right: 20, padding: 8 },
});
