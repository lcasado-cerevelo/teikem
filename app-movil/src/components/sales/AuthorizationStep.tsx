import { useRef, useState } from 'react';
import { Image, StyleSheet, View } from 'react-native';
import Signature, { type SignatureViewRef } from 'react-native-signature-canvas';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { getAccessToken } from '@config/axios';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { fileSource, type SalesOrder } from '@services/salesApi';
import { AUTHORIZED_BY_MAX, money, orderSummary, type WizardState } from '@utils/salesOrderRules';
import { Button, Field, Help, SectionTitle, SumRow } from './ui';

// Lote 226 — paso 4 del pedido en el teléfono: AUTORIZACIÓN. Nombre de quien autoriza el pedido en el negocio del cliente y su firma dibujada con el dedo
// (obligatorias solo si el cliente las exige) y el resumen final. La firma es el MISMO lienzo que usa el chofer al entregar (react-native-signature-canvas) y,
// como allí, va FUERA del ScrollView (`SignatureBlock`, en el pie): un ancestro desplazable le roba el trazo rápido.

export function AuthorizationStep({ state, onChange, requiresSignature, saved }: {
  state: WizardState;
  onChange: (next: (s: WizardState) => WizardState) => void;
  requiresSignature: boolean;
  saved: SalesOrder | null;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const summary = orderSummary(saved);
  return (
    <View style={styles.step}>
      <SectionTitle>{t('SALES_ORDER.AUTH.TITLE')}</SectionTitle>
      {requiresSignature ? <Text style={styles.note}>{t('SALES_ORDER.AUTH.REQUIRED')}</Text> : null}
      <Field
        label={t('SALES_ORDER.AUTH.NAME')}
        required={requiresSignature}
        value={state.authorizedByName}
        onChangeText={(v) => onChange((s) => ({ ...s, authorizedByName: v }))}
        maxLength={AUTHORIZED_BY_MAX}
        autoCorrect={false}
      />
      <View style={styles.sum}>
        <SumRow label={t('SALES_ORDER.AUTH.CLIENT')} value={saved?.clientName ?? state.clientLabel} />
        <SumRow label={t('SALES_ORDER.AUTH.LINES')} value={String(state.cart.length)} />
        <SumRow label={t('SALES_ORDER.SUMMARY.TOTAL')} value={money(summary.total)} strong />
      </View>
      <Help>{t('SALES_ORDER.AUTH.SEND_HELP')}</Help>
    </View>
  );
}

/** La firma con el dedo. `drawn` = la firma dibujada en esta sesión (data URL); `savedFilePublicId` = la que ya está en el servidor. */
export function SignatureBlock({ tenantId, drawn, onDrawn, savedFilePublicId, dirty, required }: {
  tenantId: number;
  drawn: string | null;
  onDrawn: (dataUrl: string | null) => void;
  savedFilePublicId: string | null;
  dirty: boolean;
  required: boolean;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const sigRef = useRef<SignatureViewRef>(null);
  const showSaved = !dirty && !!savedFilePublicId;
  // al volver a este paso, la firma ya dibujada (o la guardada) se muestra como imagen; «Firmar de nuevo» abre el lienzo vacío
  const [editing, setEditing] = useState(() => !drawn && !showSaved);
  const label = `${t('SALES_ORDER.AUTH.SIGNATURE')}${required ? ' *' : ''}`;

  if (!editing) {
    const source = drawn ? { uri: drawn } : savedFilePublicId ? fileSource(tenantId, savedFilePublicId, getAccessToken(tenantId)) : null;
    return (
      <View style={styles.sig}>
        <Text style={styles.label}>{label}</Text>
        {source ? <Image source={source} style={styles.preview} resizeMode="contain" accessibilityLabel={t('SALES_ORDER.AUTH.SIGNATURE_DONE')} /> : null}
        <Help>{drawn ? t('SALES_ORDER.AUTH.SIGNATURE_DONE') : t('SALES_ORDER.AUTH.SAVED_SIGNATURE')}</Help>
        <Button kind="outline" label={t('SALES_ORDER.AUTH.SIGN_AGAIN')} onPress={() => { onDrawn(null); setEditing(true); }} />
      </View>
    );
  }

  return (
    <View style={styles.sig}>
      <Text style={styles.label}>{label}</Text>
      <View style={styles.box} accessibilityLabel={t('SALES_ORDER.AUTH.SIGNATURE_PAD')}>
        <Signature
          ref={sigRef}
          // cada trazo deja la firma lista (no hace falta un «Aceptar» aparte)
          onEnd={() => sigRef.current?.readSignature()}
          onOK={(sig: string) => onDrawn(sig)}
          onEmpty={() => onDrawn(null)}
          onClear={() => onDrawn(null)}
          descriptionText=""
          webStyle=".m-signature-pad--footer{display:none}.m-signature-pad{box-shadow:none;border:none}"
          backgroundColor="#ffffff"
          penColor="#03204B"
          imageType="image/png"
        />
      </View>
      <Help>{required ? t('SALES_ORDER.AUTH.SIGNATURE_REQUIRED_HELP') : t('SALES_ORDER.AUTH.SIGNATURE_OPTIONAL')}</Help>
      <Button kind="outline" label={t('SALES_ORDER.AUTH.CLEAR')} onPress={() => sigRef.current?.clearSignature()} />
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  step: { gap: 12 },
  note: { fontSize: 14, color: colors.text, fontWeight: '600', borderLeftWidth: 3, borderLeftColor: colors.primary, paddingLeft: 8 },
  sum: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 12, backgroundColor: colors.backgroundSecondary },
  sig: { gap: 6 },
  label: { fontSize: 14, fontWeight: '600', color: colors.text },
  // el lienzo es blanco en los dos temas (la firma se guarda en negro sobre blanco, como la del chofer)
  box: { height: 160, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, overflow: 'hidden' },
  preview: { height: 110, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, backgroundColor: '#ffffff' },
});
