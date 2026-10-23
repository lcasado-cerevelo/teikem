import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { useComments } from '@hooks/queries';
import { enqueueComment } from '@/offline/mutations';
import { drainOutbox } from '@/offline';
import { useAppStore } from '@/stores';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Comments tab: lists the shipment's comments and lets the driver add one
// (queued offline). Was "En construcción" in the old app — built here.
export default function ShipmentComments() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const shipment = useAppStore((s) => s.shipment);
  const shipmentId = shipment?.shipmentID;

  const { data: comments, isLoading } = useComments(shipmentId);
  const qc = useQueryClient();
  const [text, setText] = useState('');
  const [sending, setSending] = useState(false);

  // Descending order: the SP returns the order's "Order Notes" first (oldest),
  // then user comments newest-first. We keep the user comments on top and push
  // the order notes to the bottom, so the newest is always first.
  const ordered = useMemo(() => {
    const list = comments ?? [];
    const notes = list.filter((c) => c.includes('Order Notes:'));
    const userComments = list.filter((c) => !c.includes('Order Notes:'));
    return [...userComments, ...notes];
  }, [comments]);

  const send = async () => {
    const value = text.trim();
    if (!shipmentId || !value) return;
    setSending(true);
    try {
      await enqueueComment(shipmentId, value);
      void drainOutbox();
      // Optimista: lo muestro de una arriba (más reciente primero).
      qc.setQueryData(['comments', shipmentId], (old: string[] | undefined) => [value, ...(old ?? [])]);
      setText('');
    } finally {
      setSending(false);
    }
  };

  return (
    <View style={styles.container}>
      <View style={styles.inputRow}>
        <TextInput
          style={styles.input}
          value={text}
          onChangeText={setText}
          placeholder={t('SHIPMENT_DETAILS.ADD_COMMENT')}
          placeholderTextColor={colors.muted}
        />
        <Pressable
          style={[styles.send, (!text.trim() || sending) && styles.disabled]}
          onPress={send}
          disabled={!text.trim() || sending}
        >
          {sending ? (
            <ActivityIndicator color={colors.primaryContrast} />
          ) : (
            <Text style={styles.sendText}>{t('COMMON.SEND')}</Text>
          )}
        </Pressable>
      </View>

      {isLoading ? (
        <View style={styles.center}>
          <ActivityIndicator color={colors.primary} />
        </View>
      ) : ordered.length === 0 ? (
        <View style={styles.center}>
          <Text style={styles.muted}>{t('SHIPMENT_DETAILS.NO_COMMENTS')}</Text>
        </View>
      ) : (
        <FlatList
          contentContainerStyle={styles.list}
          data={ordered}
          keyExtractor={(_, i) => String(i)}
          renderItem={({ item }) => (
            <View style={styles.card}>
              <Text style={styles.body}>{item}</Text>
            </View>
          )}
        />
      )}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1 },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  muted: { color: colors.muted, fontSize: 14 },
  list: { padding: 16, gap: 12 },
  card: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, padding: 14 },
  body: { fontSize: 13, color: colors.text },
  inputRow: { flexDirection: 'row', gap: 8, padding: 12, alignItems: 'center' },
  input: {
    flex: 1,
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    color: colors.text,
  },
  send: { backgroundColor: colors.primary, borderRadius: 10, paddingHorizontal: 16, paddingVertical: 10, alignItems: 'center', justifyContent: 'center' },
  sendText: { color: colors.primaryContrast, fontWeight: '600' },
  disabled: { opacity: 0.5 },
});
