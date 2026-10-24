import { useState } from 'react';
import { ActivityIndicator, Alert, Image, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as ImagePicker from 'expo-image-picker';
import { onlineManager } from '@tanstack/react-query';
import { useAppStore } from '@/stores';
import { useRemoveShipmentFromLists } from '@hooks/queries';
import { enqueuePickup } from '@/offline/mutations';
import { drainOutbox, getItem } from '@/offline';
import { compressProofPhoto } from '@utils/photo';
import { getCurrentFix } from '@utils/location';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Recoger: quién entregó el paquete al chofer (opcional), nota, foto y la ubicación del teléfono.
// Se guarda en la cola y sale cuando hay señal, igual que una entrega.
export default function ShipmentActionPickup({ onBack }: { onBack: () => void }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const shipment = useAppStore((s) => s.shipment);
  const resetShipment = useAppStore((s) => s.resetShipment);
  const removeShipmentFromLists = useRemoveShipmentFromLists();

  const [giver, setGiver] = useState('');
  const [comment, setComment] = useState('');
  const [photo, setPhoto] = useState<{ uri: string; base64: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const takePhoto = async () => {
    const perm = await ImagePicker.requestCameraPermissionsAsync();
    if (!perm.granted) {
      Alert.alert(t('DELIVERY.CAMERA_PERMISSION'));
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 0.8 });
    const asset = result.canceled ? null : result.assets[0];
    if (asset) setPhoto(await compressProofPhoto(asset.uri, asset.width));
  };

  const submit = async () => {
    if (!shipment) return;
    setSubmitting(true);
    try {
      const fix = await getCurrentFix(8000);
      const id = await enqueuePickup({
        tenantId: shipment.tenantId,
        shipmentId: shipment.id,
        giverName: giver.trim() || undefined,
        notes: comment.trim() || undefined,
        pickedAtUtc: new Date().toISOString(),
        latitude: fix?.lat,
        longitude: fix?.lng,
        photosBase64: photo ? [photo.base64] : undefined,
      });
      if (onlineManager.isOnline()) await drainOutbox();
      const item = await getItem(id);
      const sent = item?.status === 'done';
      removeShipmentFromLists(shipment.id);
      resetShipment();
      Alert.alert(t('PICKUP.PICKED_TITLE'), sent ? t('PICKUP.PICKED_SENT') : t('PICKUP.PICKED_QUEUED'), [{ text: 'OK', onPress: () => router.back() }]);
    } catch {
      Alert.alert(t('PICKUP.ERROR'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ScrollView contentContainerStyle={[styles.container, { paddingBottom: 20 + insets.bottom }]} keyboardShouldPersistTaps="handled">
      <Text style={styles.title}>{t('PICKUP.TITLE')}</Text>

      <Text style={styles.label}>{t('PICKUP.GIVER')}</Text>
      <TextInput style={styles.input} value={giver} onChangeText={setGiver} placeholder={t('PICKUP.GIVER_PLACEHOLDER')} placeholderTextColor={colors.muted} />

      <Text style={styles.label}>{t('ACTIONS.COMMENT')}</Text>
      <TextInput style={[styles.input, styles.multiline]} value={comment} onChangeText={setComment} placeholderTextColor={colors.muted} multiline />

      <Text style={styles.label}>{t('DELIVERY.PROOF_PHOTO')}</Text>
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
        <Pressable style={[styles.submit, submitting && styles.disabled]} onPress={() => void submit()} disabled={submitting}>
          {submitting ? <ActivityIndicator color={colors.primaryContrast} /> : <Text style={styles.submitText}>{t('PICKUP.CONFIRM')}</Text>}
        </Pressable>
      </View>
    </ScrollView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { paddingHorizontal: 20, paddingTop: 6, paddingBottom: 20, gap: 8 },
  title: { fontSize: 17, fontWeight: '700', color: colors.text, marginBottom: 4 },
  label: { fontSize: 14, fontWeight: '600', color: colors.text, marginTop: 4 },
  input: { borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, paddingVertical: 10, color: colors.text },
  multiline: { height: 90, textAlignVertical: 'top' },
  button: { backgroundColor: colors.primary, borderRadius: 10, paddingVertical: 12, alignItems: 'center' },
  buttonText: { color: colors.primaryContrast, fontWeight: '600' },
  photo: { width: '100%', height: 150, borderRadius: 10, resizeMode: 'cover' },
  link: { color: colors.primary, textAlign: 'center', marginTop: 4, fontWeight: '600' },
  actions: { flexDirection: 'row', gap: 10, marginTop: 12 },
  buttonOutline: { flex: 1, borderWidth: 1, borderColor: colors.primary, borderRadius: 12, paddingVertical: 12, alignItems: 'center', justifyContent: 'center' },
  buttonOutlineText: { color: colors.primary, fontWeight: '600' },
  submit: { flex: 1, backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 12, alignItems: 'center', justifyContent: 'center' },
  submitText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 15, textAlign: 'center' },
  disabled: { opacity: 0.6 },
});
