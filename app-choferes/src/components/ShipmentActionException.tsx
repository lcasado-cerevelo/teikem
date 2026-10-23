import { useMemo, useState } from 'react';
import { ActivityIndicator, Alert, Image, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as ImagePicker from 'expo-image-picker';
import { onlineManager } from '@tanstack/react-query';
import { useAppStore } from '@/stores';
import { useFailureReasons, useRemoveShipmentFromLists } from '@hooks/queries';
import { enqueueFail } from '@/offline/mutations';
import { drainOutbox, getItem } from '@/offline';
import { compressProofPhoto } from '@utils/photo';
import OptionSelect from './OptionSelect';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Exception flow of the "Acciones" tab: a non-delivery event. Reason (from the
// server catalog) + required comment + a proof photo that is mandatory only when
// the customer requires it (Shipment.photoOnEvent). On success it jumps to the
// Comments tab, mirroring the old app.
export default function ShipmentActionException({ onBack }: { onBack: () => void }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const shipment = useAppStore((s) => s.shipment);
  const removeShipmentFromLists = useRemoveShipmentFromLists();
  const { data: reasons, isLoading: reasonsLoading } = useFailureReasons(shipment?.tenantId);

  const [reasonId, setReasonId] = useState<number | null>(null);
  const [comment, setComment] = useState('');
  const [photo, setPhoto] = useState<{ uri: string; base64: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const photoRequired = false;

  const options = useMemo(
    () => (reasons ?? []).map((r, i) => ({ id: i + 1, label: r.label })),
    [reasons],
  );

  const takePhoto = async () => {
    const perm = await ImagePicker.requestCameraPermissionsAsync();
    if (!perm.granted) {
      Alert.alert(t('DELIVERY.CAMERA_PERMISSION'));
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 0.8 });
    const asset = result.canceled ? null : result.assets[0];
    if (asset) {
      // Downscale before storing: a full-res base64 photo makes the upload so
      // heavy it can time out and read as "offline" (see utils/photo.ts).
      setPhoto(await compressProofPhoto(asset.uri, asset.width));
    }
  };

  const submit = async () => {
    if (!shipment) return;
    if (reasonId == null) {
      Alert.alert(t('ACTIONS.REASON_REQUIRED'));
      return;
    }
    if (!comment.trim()) {
      Alert.alert(t('ACTIONS.COMMENT_REQUIRED'));
      return;
    }
    if (photoRequired && !photo) {
      Alert.alert(t('DELIVERY.MISSING_PHOTO'));
      return;
    }

    setSubmitting(true);
    try {
      const id = await enqueueFail({
        tenantId: shipment.tenantId,
        shipmentId: shipment.id,
        reasonCode: (reasons ?? [])[reasonId - 1].code,
        notes: comment.trim(),
        attemptedAtUtc: new Date().toISOString(),
        photoBase64: photo?.base64,
      });
      if (onlineManager.isOnline()) await drainOutbox();
      const item = await getItem(id);
      const sent = item?.status === 'done';
      // La excepción también cierra la orden (evento 'Completed' en el server):
      // sácala de los listados igual que una entrega.
      removeShipmentFromLists(shipment.id);
      // "Queued" already tells the driver everything they need — the raw
      // transport error (e.g. "Network Error") is a debugging detail, not a
      // user-facing one; it stays in item.lastError for support/logs.
      const body = sent ? t('ACTIONS.EXCEPTION_SENT') : t('ACTIONS.EXCEPTION_QUEUED');
      Alert.alert(t('ACTIONS.EXCEPTION_SAVED_TITLE'), body, [
        { text: 'OK', onPress: () => router.back() },
      ]);
    } catch {
      Alert.alert(t('ACTIONS.EXCEPTION_ERROR'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ScrollView
      contentContainerStyle={[styles.container, { paddingBottom: 20 + insets.bottom }]}
      keyboardShouldPersistTaps="handled"
    >
      <Text style={styles.title}>{t('ACTIONS.EXCEPTION_TITLE')}</Text>

      <Text style={styles.label}>{t('ACTIONS.REASON')}</Text>
      {reasonsLoading ? (
        <ActivityIndicator color={colors.primary} />
      ) : options.length === 0 ? (
        <Text style={styles.muted}>{t('ACTIONS.NO_REASONS')}</Text>
      ) : (
        <OptionSelect
          options={options}
          value={reasonId}
          placeholder={t('ACTIONS.SELECT_REASON')}
          onSelect={setReasonId}
        />
      )}

      <Text style={styles.label}>{t('ACTIONS.COMMENT')}</Text>
      <TextInput
        style={[styles.input, styles.multiline]}
        value={comment}
        onChangeText={setComment}
        placeholder={t('DELIVERY.DELIVERY_NOTES')}
        placeholderTextColor={colors.muted}
        multiline
      />

      <Text style={styles.label}>
        {t('DELIVERY.PROOF_PHOTO')}
        {photoRequired ? ' *' : ''}
      </Text>
      {photo ? (
        <Pressable onPress={takePhoto}>
          <Image source={{ uri: photo.uri }} style={styles.photo} />
          <Text style={styles.link}>{t('DELIVERY.RETAKE')}</Text>
        </Pressable>
      ) : (
        <Pressable style={styles.button} onPress={takePhoto}>
          <Text style={styles.buttonText}>{t('DELIVERY.TAKE_PHOTO')}</Text>
        </Pressable>
      )}

      <View style={styles.actions}>
        <Pressable style={styles.buttonOutline} onPress={onBack} disabled={submitting}>
          <Text style={styles.buttonOutlineText}>{t('ACTIONS.BACK')}</Text>
        </Pressable>
        <Pressable style={[styles.submit, submitting && styles.disabled]} onPress={submit} disabled={submitting}>
          {submitting ? (
            <ActivityIndicator color={colors.primaryContrast} />
          ) : (
            <Text style={styles.submitText}>{t('COMMON.SAVE')}</Text>
          )}
        </Pressable>
      </View>
    </ScrollView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { paddingHorizontal: 20, paddingTop: 6, paddingBottom: 20, gap: 8 },
  title: { fontSize: 17, fontWeight: '700', color: colors.text, marginBottom: 4 },
  label: { fontSize: 14, fontWeight: '600', color: colors.text, marginTop: 4 },
  muted: { color: colors.muted, fontSize: 14 },
  input: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    color: colors.text,
  },
  multiline: { height: 90, textAlignVertical: 'top' },
  button: { backgroundColor: colors.primary, borderRadius: 10, paddingVertical: 12, alignItems: 'center' },
  buttonText: { color: colors.primaryContrast, fontWeight: '600' },
  photo: { width: '100%', height: 150, borderRadius: 10, resizeMode: 'cover' },
  link: { color: colors.primary, textAlign: 'center', marginTop: 4, fontWeight: '600' },
  actions: { flexDirection: 'row', gap: 10, marginTop: 12 },
  buttonOutline: {
    flex: 1,
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: 12,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  buttonOutlineText: { color: colors.primary, fontWeight: '600' },
  submit: {
    flex: 1,
    backgroundColor: colors.primary,
    borderRadius: 12,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  submitText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 15, textAlign: 'center' },
  disabled: { opacity: 0.6 },
});
