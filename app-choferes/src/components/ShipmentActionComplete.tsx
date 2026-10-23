import { useEffect, useMemo, useRef, useState } from 'react';
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
import { useCodTypes, useConsignee, usePieceIds, usePiecesByIds, useRemoveShipmentFromLists } from '@hooks/queries';
import { enqueueCod, enqueueDispatch } from '@/offline/mutations';
import { drainOutbox, getItem } from '@/offline';
import { compressProofPhoto } from '@utils/photo';
import OptionSelect from './OptionSelect';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// How far (meters) the driver can be from the stored address before we treat the
// stored coordinates as wrong and re-ask.
const LOCATION_THRESHOLD_M = 200;

// Great-circle distance in meters between two GPS points.
function distanceMeters(a: { lat: number; lng: number }, b: { lat: number; lng: number }): number {
  const R = 6371000;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(b.lat - a.lat);
  const dLng = toRad(b.lng - a.lng);
  const s =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(s));
}

interface CodDraft {
  key: string;
  codTypeId: number;
  codTypeLabel: string;
  codAmount: number;
  codCheck: string;
}

// Complete/delivery flow of the "Acciones" tab. Captures who received, an optional
// comment, COD collections, a proof photo, a signature and the GPS fix, then sends
// a "Delivered" dispatch (+ one COD event each). Photo and signature are mandatory
// only when the customer requires them (Shipment.photoOnDelivery /
// signatureOnDelivery). On delivery it also backfills/corrects the consignee's
// stored coordinates when the driver confirms being at the address.
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

  const photoRequired = Boolean(shipment?.photoOnDelivery);
  const signatureRequired = Boolean(shipment?.signatureOnDelivery);
  // COD collection only applies when the order actually carries a COD amount.
  const hasCod = (shipment?.codAmount ?? 0) > 0;

  // Real piece barcodes for the dispatch. Shipments.Barcode is a comma-joined
  // list (not a scannable barcode), so the source of truth is Shipments_Pieces;
  // splitting the joined list is only a fallback when pieces aren't cached.
  const { data: pieceIds } = usePieceIds(shipment?.shipmentID);
  const { data: pieces } = usePiecesByIds(pieceIds ?? []);
  const barcodes = useMemo(() => {
    const fromPieces = (pieces ?? []).map((p) => p.barcode).filter(Boolean);
    if (fromPieces.length > 0) return fromPieces;
    return (shipment?.barcode ?? '')
      .split(',')
      .map((b) => b.trim())
      .filter(Boolean);
  }, [pieces, shipment?.barcode]);

  const [podName, setPodName] = useState('');
  const [comment, setComment] = useState('');
  const [photo, setPhoto] = useState<{ uri: string; base64: string } | null>(null);
  const [coords, setCoords] = useState<{ lat: number; lng: number } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // --- COD -------------------------------------------------------------------
  const { data: codTypes, isLoading: codTypesLoading } = useCodTypes();
  const [cods, setCods] = useState<CodDraft[]>([]);
  const [codTypeId, setCodTypeId] = useState<number | null>(null);
  const [codAmount, setCodAmount] = useState('');
  const [codCheck, setCodCheck] = useState('');
  const codOptions = useMemo(
    () => (codTypes ?? []).map((c) => ({ id: c.codTypeID, label: c.codType ?? String(c.codTypeID) })),
    [codTypes],
  );

  const addCod = () => {
    if (codTypeId == null) {
      Alert.alert(t('ACTIONS.COD_TYPE_REQUIRED'));
      return;
    }
    const amount = Number(codAmount.replace(',', '.'));
    if (!Number.isFinite(amount) || amount <= 0) {
      Alert.alert(t('ACTIONS.COD_AMOUNT_INVALID'));
      return;
    }
    const label = codOptions.find((o) => o.id === codTypeId)?.label ?? String(codTypeId);
    setCods((prev) => [
      ...prev,
      { key: `${codTypeId}-${amount}-${prev.length}`, codTypeId, codTypeLabel: label, codAmount: amount, codCheck: codCheck.trim() },
    ]);
    setCodTypeId(null);
    setCodAmount('');
    setCodCheck('');
  };

  const removeCod = (key: string) => setCods((prev) => prev.filter((c) => c.key !== key));
  // --- END: COD --------------------------------------------------------------

  // Stored coordinates of the delivery address (ConsigneeNum == Consignee.ID).
  const consigneeId = shipment?.consigneeNum ? Number(shipment.consigneeNum) : undefined;
  const { data: consignee } = useConsignee(Number.isFinite(consigneeId) ? consigneeId : undefined);
  const storedLat = Number(consignee?.latitude);
  const storedLng = Number(consignee?.longitude);
  const hasStored =
    !!consignee?.latitude &&
    !!consignee?.longitude &&
    Number.isFinite(storedLat) &&
    Number.isFinite(storedLng) &&
    !(storedLat === 0 && storedLng === 0);

  // Grab a GPS fix on mount (best-effort; delivery still works without it).
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const perm = await Location.requestForegroundPermissionsAsync();
        if (!perm.granted) return;
        const pos = await Location.getCurrentPositionAsync({});
        if (!cancelled) setCoords({ lat: pos.coords.latitude, lng: pos.coords.longitude });
      } catch {
        /* keep coords null */
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
    if (asset) {
      // Downscale before storing: a full-res base64 photo makes the upload so
      // heavy it can time out and read as "offline" (see utils/photo.ts).
      setPhoto(await compressProofPhoto(asset.uri, asset.width));
    }
  };

  const clearSignature = () => sigRef.current?.clearSignature();

  // Send the delivery. Enqueues each COD first (so it posts before the order is
  // closed), then the dispatch. updateLocation=true backfills/corrects the
  // consignee's stored coordinates.
  const doDispatch = async (signatureBase64: string, updateLocation: boolean) => {
    if (!shipment) {
      setSubmitting(false);
      return;
    }
    try {
      for (const cod of cods) {
        await enqueueCod({
          shipmentId: shipment.shipmentID,
          codTypeId: cod.codTypeId,
          codAmount: cod.codAmount,
          codCheck: cod.codCheck || undefined,
        });
      }
      const id = await enqueueDispatch({
        shipmentId: shipment.shipmentID,
        barcodes,
        podName: podName.trim(),
        comment: comment.trim() || undefined,
        latitude: coords?.lat,
        longitude: coords?.lng,
        updateLocation: updateLocation && Boolean(coords),
        photoBase64: photo?.base64 ?? '',
        signatureBase64,
      });
      if (onlineManager.isOnline()) await drainOutbox();
      const item = await getItem(id);
      const sent = item?.status === 'done';
      // La orden quedó cerrada (enviada o en cola): fuera de los listados.
      removeShipmentFromLists(shipment.shipmentID);
      resetShipment();
      // "Queued" already tells the driver everything they need — the raw
      // transport error (e.g. "Network Error") is a debugging detail, not a
      // user-facing one; it stays in item.lastError for support/logs.
      const body = sent ? t('DELIVERY.DELIVERED_SENT') : t('DELIVERY.DELIVERED_QUEUED');
      Alert.alert(t('DELIVERY.DELIVERED_TITLE'), body, [{ text: 'OK', onPress: () => router.back() }]);
    } catch {
      Alert.alert(t('DELIVERY.ERROR'));
    } finally {
      setSubmitting(false);
    }
  };

  // Decide whether to ask the driver to capture the address location: when the
  // address has no stored coords, OR it has them but the driver is far from them.
  const finishDispatch = (signatureBase64: string) => {
    if (!shipment) {
      setSubmitting(false);
      return;
    }
    const farFromStored =
      hasStored && !!coords && distanceMeters(coords, { lat: storedLat, lng: storedLng }) > LOCATION_THRESHOLD_M;
    const shouldAsk = !!coords && (!hasStored || farFromStored);

    if (shouldAsk) {
      Alert.alert(
        t('DELIVERY.LOCATION_TITLE'),
        t('DELIVERY.LOCATION_BODY'),
        [
          { text: t('DELIVERY.LOCATION_HERE'), onPress: () => void doDispatch(signatureBase64, true) },
          { text: t('DELIVERY.LOCATION_ELSEWHERE'), onPress: () => void doDispatch(signatureBase64, false) },
        ],
        { cancelable: false },
      );
    } else {
      void doDispatch(signatureBase64, false);
    }
  };

  // The signature pad returns a data URL; strip the prefix and finish the delivery.
  const onSignatureOK = (sig: string) => finishDispatch(sig.replace(/^data:image\/\w+;base64,/, ''));

  // Fired when readSignature() found an empty pad. Required -> warn; optional ->
  // proceed with no signature.
  const onSignatureEmpty = () => {
    if (signatureRequired) {
      setSubmitting(false);
      Alert.alert(t('DELIVERY.MISSING_SIGNATURE'));
    } else {
      finishDispatch('');
    }
  };

  // "Marcar como entregado": validate, then read the signature. onSignatureOK
  // continues with the dispatch; onSignatureEmpty handles the no-drawing case.
  const submit = () => {
    if (!shipment) return;
    if (barcodes.length === 0) {
      // Without at least one piece barcode the SP records no events.
      Alert.alert(t('ACTIONS.NO_BARCODES'));
      return;
    }
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
      {/* The signature WebView needs uncontested touch: any scrollable ancestor
          races its own gesture recognizer against a fast signing stroke and
          intermittently steals the drag (Android decides in native code, ahead
          of any JS state toggle). So the pad + its buttons sit OUTSIDE the
          ScrollView, in a fixed footer — no ancestor gesture can compete. */}
      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.container}
        keyboardShouldPersistTaps="handled"
      >
      <Text style={styles.title}>{t('ACTIONS.COMPLETE_TITLE')}</Text>

      <Text style={styles.label}>{t('DELIVERY.RECEIVED_BY')}</Text>
      <TextInput
        style={styles.input}
        value={podName}
        onChangeText={setPodName}
        placeholder={t('DELIVERY.RECEIVER_NAME')}
        placeholderTextColor={colors.muted}
      />

      <Text style={styles.label}>{t('DELIVERY.COMMENT_OPTIONAL')}</Text>
      <TextInput
        style={styles.input}
        value={comment}
        onChangeText={setComment}
        placeholder={t('DELIVERY.DELIVERY_NOTES')}
        placeholderTextColor={colors.muted}
      />

      {/* COD collection — only when the order carries a COD amount */}
      {hasCod && (
        <>
          <Text style={styles.label}>
            {t('ACTIONS.COD_SECTION')} · {shipment?.codType ? `${shipment.codType} · ` : ''}
            {(shipment?.codAmount ?? 0).toFixed(2)}
          </Text>
          {codTypesLoading ? (
            <ActivityIndicator color={colors.primary} />
          ) : codOptions.length > 0 ? (
            <View style={styles.codBox}>
              <OptionSelect
                options={codOptions}
                value={codTypeId}
                placeholder={t('ACTIONS.SELECT_COD_TYPE')}
                onSelect={setCodTypeId}
                onClear={() => setCodTypeId(null)}
              />
              <View style={styles.row}>
                <TextInput
                  style={[styles.input, styles.flex]}
                  value={codAmount}
                  onChangeText={setCodAmount}
                  placeholder={t('ACTIONS.COD_AMOUNT')}
                  placeholderTextColor={colors.muted}
                  keyboardType="decimal-pad"
                />
                <TextInput
                  style={[styles.input, styles.flex]}
                  value={codCheck}
                  onChangeText={setCodCheck}
                  placeholder={t('ACTIONS.COD_DOCUMENT')}
                  placeholderTextColor={colors.muted}
                />
              </View>
              <Pressable style={styles.buttonOutline} onPress={addCod}>
                <Text style={styles.buttonOutlineText}>{t('ACTIONS.ADD_COD')}</Text>
              </Pressable>
            </View>
          ) : (
            <Text style={styles.muted}>{t('ACTIONS.NO_COD')}</Text>
          )}
          {cods.map((cod) => (
            <View key={cod.key} style={styles.codItem}>
              <Text style={styles.codItemText}>
                {cod.codTypeLabel}: {cod.codAmount.toFixed(2)}
                {cod.codCheck ? ` · ${cod.codCheck}` : ''}
              </Text>
              <Pressable onPress={() => removeCod(cod.key)}>
                <Text style={styles.remove}>{t('ACTIONS.REMOVE')}</Text>
              </Pressable>
            </View>
          ))}
        </>
      )}

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
      </ScrollView>

      <View style={[styles.footer, { paddingBottom: 12 + insets.bottom }]}>
        <Text style={styles.label}>
          {t('DELIVERY.SIGNATURE')}
          {signatureRequired ? ' *' : ''}
        </Text>
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
          {coords
            ? `${t('DELIVERY.GPS')}: ${coords.lat.toFixed(5)}, ${coords.lng.toFixed(5)}`
            : `${t('DELIVERY.GPS')}: ${t('DELIVERY.NO_LOCATION')}`}
        </Text>

        <View style={styles.actions}>
          <Pressable style={styles.buttonOutlineFlex} onPress={onBack} disabled={submitting}>
            <Text style={styles.buttonOutlineText}>{t('ACTIONS.BACK')}</Text>
          </Pressable>
          <Pressable style={[styles.submit, submitting && styles.disabled]} onPress={submit} disabled={submitting}>
            {submitting ? (
              <ActivityIndicator color={colors.primaryContrast} />
            ) : (
              <Text style={styles.submitText}>{t('DELIVERY.MARK_DELIVERED')}</Text>
            )}
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
  codBox: { gap: 8 },
  codItem: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  codItemText: { color: colors.text, flex: 1 },
  remove: { color: colors.danger, fontWeight: '600', marginLeft: 8 },
  row: { flexDirection: 'row', gap: 10 },
  flex: { flex: 1 },
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
