import { useState } from 'react';
import { KeyboardAvoidingView, Modal, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import PasswordInput from '@components/PasswordInput';
import { useAccess } from '@hooks/access';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import type { SalesAvailability } from '@services/salesApi';
import { Button, ErrorText, Field, Help, SwitchRow } from './ui';

// Lote 226 — las dos hojas de «Enviar pedido»: la CONTRASEÑA (cuando el teléfono no tiene huella, la huella falló o la verificación de la sesión venció: 403
// aal2_required) con el código MFA si la cuenta lo tiene, y el FALTANTE de inventario o el crédito excedido (como el diálogo de la web), que repite el envío.

export function PasswordSheet({ visible, reason, mfa, busy, error, field, onCancel, onConfirm }: {
  visible: boolean;
  /** Por qué se pide: no hay huella / la huella no se confirmó / la verificación venció. */
  reason: 'NO_BIOMETRIC' | 'BIOMETRIC_FAILED' | 'EXPIRED';
  mfa: boolean;
  busy: boolean;
  error: string | null;
  field: 'password' | 'mfaCode' | null;
  onCancel: () => void;
  onConfirm: (password: string, mfaCode: string) => void;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [missing, setMissing] = useState<'password' | 'mfaCode' | null>(null);
  const showMfa = mfa || field === 'mfaCode';
  const confirm = () => {
    if (password === '') return setMissing('password');
    if (showMfa && code.trim() === '') return setMissing('mfaCode');
    setMissing(null);
    onConfirm(password, code.trim());
  };
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onCancel}>
      <KeyboardAvoidingView style={styles.backdrop} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <View style={styles.card}>
          <Text style={styles.title} accessibilityRole="header">{t('SALES_ORDER.SEND.PASSWORD_TITLE')}</Text>
          <Help>{t(`SALES_ORDER.SEND.WHY_${reason}`)}</Help>
          <View style={styles.field}>
            <Text style={styles.label}>{t('SALES_ORDER.SEND.PASSWORD')}</Text>
            <PasswordInput
              style={[styles.input, (missing === 'password' || field === 'password') && styles.inputError]}
              value={password}
              onChangeText={setPassword}
              accessibilityLabel={t('SALES_ORDER.SEND.PASSWORD')}
              textContentType="password"
              autoComplete="password"
              onSubmitEditing={confirm}
            />
          </View>
          {showMfa ? (
            <Field label={t('SALES_ORDER.SEND.MFA')} value={code} onChangeText={setCode} keyboardType="number-pad" maxLength={10} textContentType="oneTimeCode" autoComplete="one-time-code" error={missing === 'mfaCode' ? t('SALES_ORDER.SEND.MFA_MISSING') : null} />
          ) : null}
          {missing === 'password' ? <ErrorText>{t('SALES_ORDER.SEND.PASSWORD_MISSING')}</ErrorText> : null}
          {error ? <ErrorText>{error}</ErrorText> : null}
          <View style={styles.actions}>
            <Button kind="outline" flex={1} label={t('COMMON.CANCEL')} onPress={onCancel} disabled={busy} />
            <Button flex={1.4} label={t('SALES_ORDER.SEND.CONFIRM')} onPress={confirm} busy={busy} />
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

export function ShortageSheet({ visible, number, check, busy, error, onCancel, onSend }: {
  visible: boolean;
  number: string;
  check: SalesAvailability | null;
  busy: boolean;
  error: string | null;
  onCancel: () => void;
  onSend: (allowBackorder: boolean, overrideCredit: boolean) => void;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const access = useAccess();
  const canOverride = access.can('orders.credit_override');
  const [override, setOverride] = useState(false);
  const exceeded = !!check?.credit?.exceeded;
  const shortLines = (check?.lines ?? []).filter((l) => (l.shortage ?? 0) > 0);
  const blockedByCredit = exceeded && !(override && canOverride);
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onCancel}>
      <View style={styles.backdrop}>
        <View style={[styles.card, styles.tall]}>
          <Text style={styles.title} accessibilityRole="header">{t('SALES_ORDER.CHECK.TITLE', { number })}</Text>
          <ScrollView contentContainerStyle={styles.list}>
            {shortLines.length > 0 ? <Text style={styles.body}>{t('SALES_ORDER.CHECK.SHORTAGE')}</Text> : null}
            {shortLines.map((l) => (
              <View key={l.lineNo} style={styles.line}>
                <Text style={styles.sku}>{`${l.sku} · ${l.productName}`}</Text>
                <Text style={styles.body}>{t('SALES_ORDER.CHECK.LINE', { ordered: l.ordered, available: l.available, missing: l.shortage })}</Text>
              </View>
            ))}
            {exceeded ? (
              <View style={styles.line}>
                <Text style={styles.sku}>{t('SALES_ORDER.CHECK.CREDIT')}</Text>
                {check?.credit?.message ? <Text style={styles.body}>{check.credit.message}</Text> : null}
                {canOverride ? (
                  <SwitchRow label={t('SALES_ORDER.CHECK.OVERRIDE')} value={override} onChange={setOverride} />
                ) : (
                  <Help>{t('SALES_ORDER.CHECK.NO_OVERRIDE')}</Help>
                )}
              </View>
            ) : null}
          </ScrollView>
          {error ? <ErrorText>{error}</ErrorText> : null}
          <View style={styles.actions}>
            <Button kind="outline" flex={1} label={t('SALES_ORDER.CHECK.BACK')} onPress={onCancel} disabled={busy} />
            <Button
              flex={1.4}
              label={shortLines.length > 0 ? t('SALES_ORDER.CHECK.BACKORDER') : t('SALES_ORDER.CHECK.SEND')}
              onPress={() => onSend(shortLines.length > 0, exceeded && override)}
              busy={busy}
              disabled={blockedByCredit}
            />
          </View>
        </View>
      </View>
    </Modal>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: colors.backdrop, justifyContent: 'center', padding: 16 },
  card: { backgroundColor: colors.background, borderRadius: 16, padding: 18, gap: 12, maxWidth: 520, width: '100%', alignSelf: 'center' },
  tall: { maxHeight: '85%' },
  title: { fontSize: 18, fontWeight: '700', color: colors.text },
  field: { gap: 4 },
  label: { fontSize: 14, fontWeight: '600', color: colors.text },
  input: { borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, paddingVertical: 10, fontSize: 16, color: colors.text },
  inputError: { borderColor: colors.danger },
  actions: { flexDirection: 'row', gap: 10 },
  list: { gap: 8 },
  line: { borderWidth: 1, borderColor: colors.gray, borderRadius: 10, padding: 10, gap: 4 },
  sku: { fontSize: 14, fontWeight: '700', color: colors.text },
  body: { fontSize: 14, color: colors.text },
});
