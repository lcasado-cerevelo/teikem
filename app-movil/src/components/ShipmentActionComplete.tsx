import { useEffect, useMemo, useRef, useState } from 'react';
import * as Crypto from 'expo-crypto';
import { ActivityIndicator, Alert, Image, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import * as ImagePicker from 'expo-image-picker';
import { getCurrentFix } from '@utils/location';
import Signature, { type SignatureViewRef } from 'react-native-signature-canvas';
import { onlineManager } from '@tanstack/react-query';
import { useAppStore } from '@/stores';
import { useRemoveShipmentFromLists, useShipmentDetail } from '@hooks/queries';
import OptionSelect from './OptionSelect';
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
  const { data: detail } = useShipmentDetail(shipment);
  const signatureRequired = Boolean(detail?.signatureRequired);
  const photoRequired = Boolean(detail?.photoRequired);
  const codPending = detail?.codPending ?? (shipment?.codAmount ?? null);
  const hasCod = (codPending ?? 0) > 0;

  // Cobro contra entrega: el chofer anota cuánto cobró y cómo (puede ser parcial o en varios pagos).
  const METHODS = ['CASH', 'CHECK', 'COMPANY_CHECK', 'ATH_MOVIL', 'CARD'] as const;
  const methodOptions = useMemo(() => METHODS.map((m, i) => ({ id: i + 1, label: t(`ACTIONS.COD_METHOD_${m}`) })), [t]);
  const [cods, setCods] = useState<Array<{ clientId: string; method: string; amount: number; checkNumber?: string }>>([]);
  const [codMethod, setCodMethod] = useState<number | null>(null);
  const [codAmount, setCodAmount] = useState('');
  const [codCheck, setCodCheck] = useState('');
  const collectedNow = cods.reduce((s, c) => s + c.amount, 0);
  const addCod = () => {
    const method = codMethod == null ? null : METHODS[codMethod - 1];
    const amount = Number(codAmount.replace(',', '.'));
    if (!method) return Alert.alert(t('ACTIONS.COD_TYPE_REQUIRED'));
    if (!Number.isFinite(amount) || amount <= 0) return Alert.alert(t('ACTIONS.COD_AMOUNT_INVALID'));
    if (amount + collectedNow > (codPending ?? 0) + 0.005) return Alert.alert(t('ACTIONS.COD_EXCEEDS', { pending: (codPending ?? 0).toFixed(2) }));
    if ((method === 'CHECK' || method === 'COMPANY_CHECK') && !codCheck.trim()) return Alert.alert(t('ACTIONS.COD_CHECK_REQUIRED'));
    setCods((prev) => [...prev, { clientId: Crypto.randomUUID(), method, amount: Math.round(amount * 100) / 100, checkNumber: codCheck.trim() || undefined }]);
    setCodMethod(null);
    setCodAmount('');
    setCodCheck('');
  };

  // Se muestra la ubicación al abrir, pero al entregar se toma una NUEVA (la de ese momento).
  useEffect(() => {
    let cancelled = false;
    void getCurrentFix().then((f) => {
      if (!cancelled && f) setCoords(f);
    });
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
      const fix = (await getCurrentFix(8000)) ?? coords;
      const id = await enqueueDeliver({
        tenantId: shipment.tenantId,
        shipmentId: shipment.id,
        recipientName: podName.trim(),
        notes: comment.trim() || undefined,
        deliveredAtUtc: new Date().toISOString(),
        latitude: fix?.lat,
        longitude: fix?.lng,
        signatureBase64,
        photosBase64: photo ? [photo.base64] : undefined,
        cods,
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
  const onSignatureEmpty = () => {
    if (signatureRequired) {
      setSubmitting(false);
      Alert.alert(t('DELIVERY.MISSING_SIGNATURE'));
    } else void finishDeliver('');
  };

  const submit = () => {
    if (!shipment) return;
    if (!podName.trim()) {
      Alert.alert(t('DELIVERY.MISSING_NAME'));
      return;
    }
    if (photoRequired && !photo) {
      Alert.alert(t('DELIVERY.MISSING_PHOTO'));
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

        {hasCod && (
          <View style={styles.codBox}>
            <Text style={styles.cod}>{`${t('ACTIONS.COD_SECTION')}: ${t('ACTIONS.COD_PENDING', { amount: ((codPending ?? 0) - collectedNow).toFixed(2) })}`}</Text>
            <OptionSelect options={methodOptions} value={codMethod} placeholder={t('ACTIONS.SELECT_COD_TYPE')} onSelect={setCodMethod} onClear={() => setCodMethod(null)} />
            <TextInput style={styles.input} value={codAmount} onChangeText={setCodAmount} placeholder={t('ACTIONS.COD_AMOUNT')} keyboardType="decimal-pad" />
            {(codMethod === 2 || codMethod === 3) && <TextInput style={styles.input} value={codCheck} onChangeText={setCodCheck} placeholder={t('ACTIONS.COD_DOCUMENT')} />}
            <Pressable style={styles.buttonOutline} onPress={addCod}>
              <Text style={styles.buttonOutlineText}>{t('ACTIONS.ADD_COD')}</Text>
            </Pressable>
            {cods.map((c) => (
              <View key={c.clientId} style={styles.codItem}>
                <Text style={styles.codItemText}>{`${t(`ACTIONS.COD_METHOD_${c.method}`)}: ${c.amount.toFixed(2)}${c.checkNumber ? ` · ${c.checkNumber}` : ''}`}</Text>
                <Pressable onPress={() => setCods((prev) => prev.filter((x) => x.clientId !== c.clientId))}>
                  <Text style={styles.remove}>{t('ACTIONS.REMOVE')}</Text>
                </Pressable>
              </View>
            ))}
          </View>
        )}

        <Text style={styles.label}>{t('DELIVERY.PROOF_PHOTO')}{photoRequired ? ' *' : ''}</Text>
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
        <Text style={styles.label}>{t('DELIVERY.SIGNATURE')}{signatureRequired ? ' *' : ''}</Text>
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
  codBox: { gap: 8 },
  codItem: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, paddingVertical: 10 },
  codItemText: { color: colors.text, flex: 1 },
  remove: { color: colors.danger, fontWeight: '600', marginLeft: 8 },
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
