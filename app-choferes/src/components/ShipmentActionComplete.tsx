import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, Image, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as ImagePicker from 'expo-image-picker';
import * as Location from 'expo-location';
import Signature, { type SignatureViewRef } from 'react-native-signature-canvas';
import { onlineManager } from '@tanstack/react-query';
import { useAppStore } from '@/stores';
import { useRemoveShipmentFromLists } from '@hooks/queries';
import { enqueueDeliver } from '@/offline/mutations';
import { drainOutbox, getItem } from '@/offline';
import { compressProofPhoto } from '@utils/photo';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Entregar: quién recibió (obligatorio), nota, foto y firma de quien recibió (opcionales) y la ubicación del teléfono (el GPS manda sobre la dirección).
// Se guarda en la cola y sale cuando hay señal: entregar sin cobertura no se pierde ni se duplica.
export default function ShipmentActionComplete({ onBack }: { onBack: () => void }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const shipment = useAppStore((s) => s.shipment);
  const resetShipment = useAppStore((s) => s.resetShipment);
  const removeShipmentFromLists = useRemoveShipmentFromLists();
  const sigRef = useRef<SignatureViewRef>(null);

  const [podName, setPodName] = useState('');
  const [comment, setComment] = useState('');
  const [photo, setPhoto] = useState<{ uri: string; base64: string } | null>(null);
  const [coords, setCoords] = useState<{ lat: number; lng: number } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const hasCod = (shipment?.codAmount ?? 0) > 0;

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const perm = await Location.requestForegroundPermissionsAsync();
        if (!perm.granted) return;
        const pos = await Location.getCurrentPositionAsync({});
        if (!cancelled) setCoords({ lat: pos.coords.latitude, lng: pos.coords.longitude });
      } catch {
        /* sin ubicación: la entrega se registra igual */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

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

  const clearSignature = () => sigRef.current?.clearSignature();

  const finishDeliver = async (signatureBase64: string) => {
    if (!shipment) {
      setSubmitting(false);
      return;
    }
    try {
      const id = await enqueueDeliver({
        tenantId: shipment.tenantId,
        shipmentId: shipment.id,
        recipientName: podName.trim(),
        notes: comment.trim() || undefined,
        deliveredAtUtc: new Date().toISOString(),
        latitude: coords?.lat,
        longitude: coords?.lng,
        signatureBase64,
        photosBase64: photo ? [photo.base64] : undefined,
      });
      if (onlineManager.isOnline()) await drainOutbox();
      const item = await getItem(id);
      const sent = item?.status === 'done';
      removeShipmentFromLists(shipment.id);
      resetShipment();
      const body = sent ? t('DELIVERY.DELIVERED_SENT') : t('DELIVERY.DELIVERED_QUEUED');
      Alert.alert(t('DELIVERY.DELIVERED_TITLE'), body, [{ text: 'OK', onPress: () => router.back() }]);
    } catch {
      Alert.alert(t('DELIVERY.ERROR'));
    } finally {
      setSubmitting(false);
    }
  };

  const onSignatureOK = (sig: string) => void finishDeliver(sig.replace(/^data:image\/\w+;base64,/, ''));
  const onSignatureEmpty = () => void finishDeliver('');

  const submit = () => {
    if (!shipment) return;
    if (!podName.trim()) {
      Alert.alert(t('DELIVERY.MISSING_NAME'));
      return;
    }
    setSubmitting(true);
    sigRef.current?.readSignature();
  };

  return (
    <View style={styles.screen}>
      {/* El WebView de la firma necesita el toque sin competencia: cualquier ancestro desplazable le roba el trazo rápido (Android lo decide en nativo, antes de cualquier estado de JS).
          Por eso la firma y sus botones van FUERA del ScrollView, en un pie fijo. */}
      <ScrollView style={styles.scroll} contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled">
        <Text style={styles.title}>{t('ACTIONS.COMPLETE_TITLE')}</Text>

        <Text style={styles.label}>{t('DELIVERY.RECEIVED_BY')}</Text>
        <TextInput style={styles.input} value={podName} onChangeText={setPodName} placeholder={t('DELIVERY.RECEIVER_NAME')} maxLength={150} />

        <Text style={styles.label}>{t('DELIVERY.COMMENT_OPTIONAL')}</Text>
        <TextInput style={styles.input} value={comment} onChangeText={setComment} placeholder={t('DELIVERY.DELIVERY_NOTES')} maxLength={500} />

        {hasCod && <Text style={styles.cod}>{`${t('ACTIONS.COD_SECTION')}: ${(shipment?.codAmount ?? 0).toFixed(2)} — ${t('ACTIONS.COD_PENDING_NOTE')}`}</Text>}

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
      </ScrollView>

      <View style={[styles.footer, { paddingBottom: 12 + insets.bottom }]}>
        <Text style={styles.label}>{t('DELIVERY.SIGNATURE')}</Text>
        <View style={styles.signatureBox}>
          <Signature
            ref={sigRef}
            onOK={onSignatureOK}
            onEmpty={onSignatureEmpty}
            descriptionText=""
            webStyle=".m-signature-pad--footer{display:none}.m-signature-pad{box-shadow:none;border:none}"
            backgroundColor="#ffffff"
            penColor="#03204B"
          />
        </View>
        <Pressable style={styles.buttonOutline} onPress={clearSignature}>
          <Text style={styles.buttonOutlineText}>{t('DELIVERY.CLEAR')}</Text>
        </Pressable>

        <Text style={styles.gps}>
          {coords ? `${t('DELIVERY.GPS')}: ${coords.lat.toFixed(5)}, ${coords.lng.toFixed(5)}` : `${t('DELIVERY.GPS')}: ${t('DELIVERY.NO_LOCATION')}`}
        </Text>

        <View style={styles.actions}>
          <Pressable style={styles.buttonOutlineFlex} onPress={onBack} disabled={submitting}>
            <Text style={styles.buttonOutlineText}>{t('ACTIONS.BACK')}</Text>
          </Pressable>
          <Pressable style={[styles.submit, submitting && styles.disabled]} onPress={submit} disabled={submitting}>
            {submitting ? <ActivityIndicator color={colors.primaryContrast} /> : <Text style={styles.submitText}>{t('DELIVERY.MARK_DELIVERED')}</Text>}
          </Pressable>
        </View>
      </View>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  screen: { flex: 1 },
  // flexShrink (not flex:1): take only the space the content needs, and only
  // shrink/scroll when it would overflow — flex:1 was forcing this region to
  // always fill the remaining screen, leaving a dead gap before Firma whenever
  // the form was short (e.g. no COD).
  scroll: { flexShrink: 1 },
  container: { paddingHorizontal: 20, paddingTop: 4, paddingBottom: 12, gap: 6 },
  footer: { paddingHorizontal: 20, paddingTop: 6, gap: 6 },
  title: { fontSize: 17, fontWeight: '700', color: colors.text, marginBottom: 2 },
  label: { fontSize: 14, fontWeight: '600', color: colors.text, marginTop: 2 },
  muted: { color: colors.muted, fontSize: 14 },
  cod: { color: colors.text, fontSize: 14, fontWeight: '600' },
  input: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    color: colors.text,
  },
  button: { backgroundColor: colors.primary, borderRadius: 10, paddingVertical: 12, alignItems: 'center' },
  buttonText: { color: colors.primaryContrast, fontWeight: '600' },
  buttonOutline: { borderWidth: 1, borderColor: colors.primary, borderRadius: 10, paddingVertical: 12, alignItems: 'center' },
  buttonOutlineFlex: {
    flex: 1,
    borderWidth: 1,
    borderColor: colors.primary,
    borderRadius: 12,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  buttonOutlineText: { color: colors.primary, fontWeight: '600' },
  photo: { width: '100%', height: 150, borderRadius: 10, resizeMode: 'cover' },
  link: { color: colors.primary, textAlign: 'center', marginTop: 4, fontWeight: '600' },
  signatureBox: { height: 170, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, overflow: 'hidden' },
  gps: { color: colors.muted, fontSize: 12, textAlign: 'center', marginTop: 2 },
  actions: { flexDirection: 'row', gap: 10, marginTop: 6 },
  submit: {
    flex: 1.5,
    backgroundColor: colors.primary,
    borderRadius: 12,
    paddingVertical: 12,
    alignItems: 'center',
    justifyContent: 'center',
  },
  submitText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 15, textAlign: 'center' },
  disabled: { opacity: 0.6 },
});
