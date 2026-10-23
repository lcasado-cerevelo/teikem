import { useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Linking, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { Redirect, useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { useBiometricOffer } from '@hooks/useBiometricOffer';
import type { MfaEnrollResultDto } from '@services/auth/teikemAuth';
import { colors } from '@constants/theme';
import { BRAND } from '@constants/brand';
import { Image } from 'react-native';

// Paso 2 del inicio de sesión: el código de la app de autenticación (o uno de recuperación). Si la compañía exige el segundo factor y la persona todavía no lo
// tiene, primero lo configura (clave → código → códigos de recuperación) y vuelve a iniciar sesión, igual que la web.
export default function Mfa() {
  const { t } = useTranslation();
  const router = useRouter();
  const { mfaChallenge, verifyMfa, enrollMfa, confirmMfa, finishEnrollment, cancelMfa, loading } = useAuth();
  const offerBiometrics = useBiometricOffer();
  const [code, setCode] = useState('');
  const [enroll, setEnroll] = useState<MfaEnrollResultDto | null>(null);
  const [recovery, setRecovery] = useState<string[] | null>(null);

  // sin paso pendiente (y sin códigos por mostrar) no hay nada que hacer aquí
  if (!mfaChallenge && !recovery) return <Redirect href="/(no-auth)/Login" />;

  const back = () => {
    cancelMfa();
    router.replace('/(no-auth)/Login');
  };

  const onVerify = async () => {
    if (await verifyMfa(code)) await offerBiometrics();
  };

  const onStartEnroll = async () => setEnroll(await enrollMfa());

  const onConfirm = async () => {
    const codes = await confirmMfa(code);
    if (codes) {
      setRecovery(codes);
      setCode('');
    }
  };

  const devCode = enroll ? enroll.devMfaCode : mfaChallenge?.devCode;
  const field = (
    <View style={styles.field}>
      <Text style={styles.label}>{t('MFA.CODE')}</Text>
      <TextInput
        style={styles.input}
        value={code}
        onChangeText={setCode}
        inputMode="numeric"
        autoComplete="one-time-code"
        autoFocus
        maxLength={32}
        placeholderTextColor={colors.muted}
      />
      {devCode ? (
        <Text style={styles.dev} testID="mfa-dev-code">
          {t('MFA.DEV_CODE', { code: devCode })}{' '}
          <Text style={styles.link} onPress={() => setCode(devCode)}>
            {t('MFA.DEV_USE')}
          </Text>
        </Text>
      ) : null}
    </View>
  );

  const button = (label: string, onPress: () => void) => (
    <View style={styles.buttonOuter}>
      {loading ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        <Pressable style={styles.button} onPress={onPress} accessibilityRole="button">
          <Text style={styles.buttonLabel}>{label}</Text>
        </Pressable>
      )}
    </View>
  );

  let body;
  if (recovery) {
    body = (
      <>
        <Text style={styles.title}>{t('MFA.RECOVERY_TITLE')}</Text>
        <Text style={styles.subtitle}>{t('MFA.RECOVERY_BODY')}</Text>
        <View style={styles.codes}>
          {recovery.map((c) => (
            <Text key={c} style={styles.recovery} selectable>
              {c}
            </Text>
          ))}
        </View>
        {button(t('MFA.RECOVERY_CONTINUE'), () => {
          finishEnrollment();
          setRecovery(null);
          router.replace('/(no-auth)/Login');
        })}
      </>
    );
  } else if (mfaChallenge?.enrollmentRequired && !enroll) {
    body = (
      <>
        <Text style={styles.title}>{t('MFA.ENROLL_TITLE')}</Text>
        <Text style={styles.subtitle}>{t('MFA.ENROLL_SUBTITLE')}</Text>
        {button(t('MFA.ENROLL_START'), () => void onStartEnroll())}
      </>
    );
  } else if (enroll) {
    body = (
      <>
        <Text style={styles.title}>{t('MFA.ENROLL_TITLE')}</Text>
        <Text style={styles.subtitle}>{t('MFA.SECRET')}</Text>
        <Text style={styles.secret} selectable testID="mfa-secret">
          {enroll.secret}
        </Text>
        <Text style={styles.link} onPress={() => void Linking.openURL(enroll.otpAuthUri)}>
          {t('MFA.OPEN_IN_APP')}
        </Text>
        {field}
        {button(t('MFA.CONFIRM'), () => void onConfirm())}
      </>
    );
  } else {
    body = (
      <>
        <Text style={styles.title}>{t('MFA.TITLE')}</Text>
        <Text style={styles.subtitle}>{t('MFA.SUBTITLE')}</Text>
        {field}
        {button(t('MFA.VERIFY'), () => void onVerify())}
      </>
    );
  }

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
        <Image source={BRAND.logo} style={styles.logo} resizeMode="contain" />
        {body}
        <Text style={styles.link} onPress={back}>
          {t('MFA.BACK')}
        </Text>
        <Text style={styles.poweredBy}>{BRAND.poweredBy}</Text>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
  container: { flexGrow: 1, alignItems: 'center', paddingHorizontal: 26, paddingTop: 40, paddingBottom: 26, gap: 16, backgroundColor: colors.background },
  logo: { width: 120, height: 120 },
  title: { fontSize: 20, fontWeight: '700', color: colors.primary, textAlign: 'center' },
  subtitle: { fontSize: 14, color: colors.muted, textAlign: 'center' },
  field: { width: '100%', gap: 6 },
  label: { fontSize: 16, color: colors.text },
  input: { height: 40, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, fontSize: 18, color: colors.text, letterSpacing: 2 },
  dev: { fontSize: 13, color: colors.muted },
  link: { color: colors.primary, fontWeight: '600' },
  secret: { fontSize: 16, fontWeight: '700', color: colors.text, letterSpacing: 1, textAlign: 'center' },
  codes: { width: '100%', gap: 4, alignItems: 'center' },
  recovery: { fontSize: 16, color: colors.text, fontWeight: '600' },
  buttonOuter: { borderRadius: 8, width: '60%', maxWidth: 240 },
  button: { backgroundColor: colors.primary, borderRadius: 8, paddingVertical: 10, paddingHorizontal: 24, alignItems: 'center', justifyContent: 'center' },
  buttonLabel: { color: colors.primaryContrast, fontWeight: '600', fontSize: 18 },
  poweredBy: { color: colors.text },
});
