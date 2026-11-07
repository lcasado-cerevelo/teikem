import { useState } from 'react';
import { Alert, Image, KeyboardAvoidingView, Modal, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import * as ImagePicker from 'expo-image-picker';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { compressProofPhoto } from '@utils/photo';
import { createBasicClient, uploadClientDocument, type ClientForOrder, type LookupOption } from '@services/salesApi';
import {
  CLIENT_DOC_TYPES, CLIENT_PAYMENT_TERMS, emptyNewClient, errorMessage, newClientIssues, toApiError, toBasicClientRequest,
  type ClientDocType, type NewClientValues,
} from '@utils/salesOrderRules';
import { Button, Choice, ErrorText, Field, Help, SectionTitle, SwitchRow } from './ui';

// Lote 226 — «Cliente nuevo» desde el pedido en el teléfono: el alta mínima (POST /clients/basic, clients.create_basic) con negocio, DBA, contacto, teléfonos, correo,
// SURI, dirección física, postal opcional y forma de pago; luego las fotos del registro de comerciante y del certificado de revendedor (cámara o galería).
// El cliente nace PROSPECTO: el pedido se solicita igual pero queda esperando su aprobación.
export default function NewClientModal({ visible, tenantId, terms, onClose, onCreated }: {
  visible: boolean;
  tenantId: number;
  terms: LookupOption[];
  onClose: () => void;
  onCreated: (client: ClientForOrder) => void;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const [v, setV] = useState<NewClientValues>(emptyNewClient);
  const [touched, setTouched] = useState(false);
  const [photos, setPhotos] = useState<Partial<Record<ClientDocType, string>>>({});
  const [busy, setBusy] = useState(false);
  const [serverErr, setServerErr] = useState<string | null>(null);
  const issues = touched ? newClientIssues(v) : {};
  const err = (k: keyof NewClientValues) => (issues[k] ? t(`SALES_ORDER.NEW_CLIENT.ERRORS.${issues[k]}`) : null);
  const set = (k: keyof NewClientValues) => (text: string) => setV((cur) => ({ ...cur, [k]: text }));
  // las formas de pago del formulario de los vendedores, en su orden, con la etiqueta del catálogo de la compañía (solo las que existen)
  const termOptions = CLIENT_PAYMENT_TERMS.map((code) => terms.find((o) => o.code.toUpperCase() === code)).filter((o): o is LookupOption => !!o);

  const close = () => {
    setV(emptyNewClient());
    setTouched(false);
    setPhotos({});
    setServerErr(null);
    onClose();
  };

  const pick = async (type: ClientDocType, camera: boolean) => {
    const perm = camera ? await ImagePicker.requestCameraPermissionsAsync() : await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!perm.granted) return Alert.alert(t(camera ? 'DELIVERY.CAMERA_PERMISSION' : 'SALES_ORDER.NEW_CLIENT.GALLERY_PERMISSION'));
    const result = camera
      ? await ImagePicker.launchCameraAsync({ mediaTypes: ['images'], quality: 0.8 })
      : await ImagePicker.launchImageLibraryAsync({ mediaTypes: ['images'], quality: 0.8 });
    const asset = result.canceled ? null : result.assets[0];
    if (!asset) return;
    const photo = await compressProofPhoto(asset.uri, asset.width);
    setPhotos((cur) => ({ ...cur, [type]: photo.uri }));
  };

  const save = async () => {
    setTouched(true);
    setServerErr(null);
    if (Object.keys(newClientIssues(v)).length > 0) return;
    setBusy(true);
    try {
      const created = await createBasicClient(tenantId, toBasicClientRequest(v));
      // las fotos van después (el cliente ya existe): si alguna falla, el cliente queda creado y se avisa
      let failed = false;
      for (const type of CLIENT_DOC_TYPES) {
        const uri = photos[type];
        if (!uri) continue;
        try {
          await uploadClientDocument(tenantId, created.publicId, type, uri);
        } catch {
          failed = true;
        }
      }
      if (failed) Alert.alert(t('SALES_ORDER.NEW_CLIENT.PHOTO_FAILED'));
      setV(emptyNewClient());
      setTouched(false);
      setPhotos({});
      onCreated(created);
    } catch (e) {
      const api = toApiError(e);
      setServerErr(api.network ? t('SALES_ORDER.NO_SIGNAL') : errorMessage(api) ?? t('SALES_ORDER.ERROR'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={close}>
      <SafeAreaView style={styles.screen}>
        <KeyboardAvoidingView style={styles.screen} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
          <ScrollView contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
            <Text style={styles.title} accessibilityRole="header">{t('SALES_ORDER.NEW_CLIENT.TITLE')}</Text>
            <Help>{t('SALES_ORDER.NEW_CLIENT.HELP')}</Help>
            <Field label={t('SALES_ORDER.NEW_CLIENT.NAME')} required value={v.name} onChangeText={set('name')} maxLength={200} error={err('name')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.DBA')} value={v.legalName} onChangeText={set('legalName')} maxLength={250} error={err('legalName')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.CONTACT')} required value={v.contactName} onChangeText={set('contactName')} maxLength={150} error={err('contactName')} />
            <View style={styles.row}>
              <View style={styles.grow}>
                <Field label={t('SALES_ORDER.NEW_CLIENT.PHONE')} required value={v.phone} onChangeText={set('phone')} keyboardType="phone-pad" error={err('phone')} help={t('SALES_ORDER.NEW_CLIENT.PHONE_HELP')} />
              </View>
              <View style={styles.ext}>
                <Field label={t('SALES_ORDER.NEW_CLIENT.EXT')} value={v.phoneExtension} onChangeText={set('phoneExtension')} keyboardType="number-pad" maxLength={6} error={err('phoneExtension')} />
              </View>
            </View>
            <Field label={t('SALES_ORDER.NEW_CLIENT.PHONE2')} value={v.phone2} onChangeText={set('phone2')} keyboardType="phone-pad" error={err('phone2')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.EMAIL')} value={v.email} onChangeText={set('email')} keyboardType="email-address" autoCapitalize="none" autoCorrect={false} error={err('email')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.SURI')} value={v.merchantRegistration} onChangeText={set('merchantRegistration')} maxLength={50} autoCapitalize="characters" error={err('merchantRegistration')} />

            <SectionTitle>{t('SALES_ORDER.NEW_CLIENT.PHYSICAL')}</SectionTitle>
            <Field label={t('SALES_ORDER.NEW_CLIENT.LINE1')} required value={v.physicalLine1} onChangeText={set('physicalLine1')} error={err('physicalLine1')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.LINE2')} value={v.physicalLine2} onChangeText={set('physicalLine2')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.CITY')} required value={v.physicalCity} onChangeText={set('physicalCity')} error={err('physicalCity')} />
            <Field label={t('SALES_ORDER.NEW_CLIENT.ZIP')} value={v.physicalPostalCode} onChangeText={set('physicalPostalCode')} keyboardType="number-pad" maxLength={10} />

            <SwitchRow label={t('SALES_ORDER.NEW_CLIENT.HAS_POSTAL')} value={v.hasPostal} onChange={(on) => setV((cur) => ({ ...cur, hasPostal: on }))} />
            {v.hasPostal ? (
              <>
                <Field label={t('SALES_ORDER.NEW_CLIENT.LINE1')} required value={v.postalLine1} onChangeText={set('postalLine1')} error={err('postalLine1')} />
                <Field label={t('SALES_ORDER.NEW_CLIENT.LINE2')} value={v.postalLine2} onChangeText={set('postalLine2')} />
                <Field label={t('SALES_ORDER.NEW_CLIENT.CITY')} required value={v.postalCity} onChangeText={set('postalCity')} error={err('postalCity')} />
                <Field label={t('SALES_ORDER.NEW_CLIENT.ZIP')} value={v.postalPostalCode} onChangeText={set('postalPostalCode')} keyboardType="number-pad" maxLength={10} />
              </>
            ) : null}

            {termOptions.length > 0 ? (
              <>
                <SectionTitle>{t('SALES_ORDER.NEW_CLIENT.PAYMENT')}</SectionTitle>
                {termOptions.map((o) => (
                  <Choice key={o.code} label={o.label} selected={v.paymentTerm === o.code} onPress={() => setV((cur) => ({ ...cur, paymentTerm: cur.paymentTerm === o.code ? '' : o.code }))} />
                ))}
              </>
            ) : null}

            <SectionTitle>{t('SALES_ORDER.NEW_CLIENT.PHOTOS')}</SectionTitle>
            {CLIENT_DOC_TYPES.map((type) => (
              <View key={type} style={styles.photoBlock}>
                <Text style={styles.photoLabel}>{t(`SALES_ORDER.NEW_CLIENT.DOC_${type}`)}</Text>
                {photos[type] ? <Image source={{ uri: photos[type] }} style={styles.photo} accessibilityLabel={t(`SALES_ORDER.NEW_CLIENT.DOC_${type}`)} /> : null}
                <View style={styles.row}>
                  <Button kind="outline" flex={1} label={t('SALES_ORDER.NEW_CLIENT.CAMERA')} onPress={() => void pick(type, true)} />
                  <Button kind="outline" flex={1} label={t('SALES_ORDER.NEW_CLIENT.GALLERY')} onPress={() => void pick(type, false)} />
                </View>
              </View>
            ))}

            {serverErr ? <ErrorText>{serverErr}</ErrorText> : null}
            {touched && Object.keys(issues).length > 0 ? <ErrorText>{t('SALES_ORDER.NEW_CLIENT.FIX')}</ErrorText> : null}
          </ScrollView>
          <View style={styles.foot}>
            <Button kind="outline" flex={1} label={t('COMMON.CANCEL')} onPress={close} disabled={busy} />
            <Button flex={1.4} label={t('SALES_ORDER.NEW_CLIENT.SAVE')} onPress={() => void save()} busy={busy} />
          </View>
        </KeyboardAvoidingView>
      </SafeAreaView>
    </Modal>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  body: { padding: 20, gap: 12 },
  title: { fontSize: 20, fontWeight: '700', color: colors.text },
  row: { flexDirection: 'row', gap: 10, flexWrap: 'wrap' },
  grow: { flex: 1, minWidth: 180 },
  ext: { width: 96 },
  photoBlock: { gap: 6 },
  photoLabel: { fontSize: 14, fontWeight: '600', color: colors.text },
  photo: { width: '100%', height: 140, borderRadius: 10, resizeMode: 'cover' },
  foot: { flexDirection: 'row', gap: 10, padding: 16, borderTopWidth: 1, borderTopColor: colors.gray },
});
