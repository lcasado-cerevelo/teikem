import { useEffect, useState } from 'react';
import { ActivityIndicator, Image, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { Redirect, useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { colors } from '@constants/theme';
import { BRAND } from '@constants/brand';

// Primer ingreso del chofer, dentro de la app: (1) código de 6 dígitos que llega a su correo personal, (2) contraseña propia, (3) verificación en dos pasos
// (esa última es la pantalla del MFA). Después vuelve a iniciar sesión con su contraseña nueva y su código.
export default function Onboarding() {
  const { t } = useTranslation();
  const router = useRouter();
  const { onboarding, sendEmailCode, verifyEmailCode, setPassword, continueToMfa, cancelOnboarding, mfaChallenge, loading } = useAuth();
  const [code, setCode] = useState('');
  const [devCode, setDevCode] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [pwd, setPwd] = useState('');
  const [pwd2, setPwd2] = useState('');
  const [error, setError] = useState<string | null>(null);

  const emailStep = onboarding ? !onboarding.emailVerified : false;
  const passwordStep = onboarding ? onboarding.emailVerified && onboarding.passwordChangeRequired : false;

  // Con correo y contraseña listos: sigue el MFA.
  useEffect(() => {
    if (mfaChallenge) router.replace('/(no-auth)/Mfa');
  }, [mfaChallenge, router]);

  if (!onboarding) return <Redirect href="/(no-auth)/Login" />;

  const back = () => {
    cancelOnboarding();
    router.replace('/(no-auth)/Login');
  };

  const onSend = async () => {
    const r = await sendEmailCode();
    if (r?.sent) {
      setSent(true);
      setDevCode(r.devCode ?? null);
    }
  };

  const onSetPassword = async () => {
    if (pwd !== pwd2) {
      setError(t('ONBOARDING.PASSWORD_MISMATCH'));
      return;
    }
    setError(null);
    await setPassword(pwd);
  };

  const button = (label: string, onPress: () => void, disabled = false) => (
    <View style={styles.buttonOuter}>
      {loading ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        <Pressable style={[styles.button, disabled && styles.disabled]} onPress={onPress} disabled={disabled} accessibilityRole="button">
          <Text style={styles.buttonLabel}>{label}</Text>
        </Pressable>
      )}
    </View>
  );

  let body;
  if (emailStep) {
    body = (
      <>
        <Text style={styles.title}>{t('ONBOARDING.EMAIL_TITLE')}</Text>
        <Text style={styles.subtitle}>{t('ONBOARDING.EMAIL_BODY', { email: onboarding.email })}</Text>
        {!sent ? (
          button(t('ONBOARDING.SEND_CODE'), () => void onSend())
        ) : (
          <>
            <View style={styles.field}>
              <Text style={styles.label}>{t('ONBOARDING.CODE')}</Text>
              <TextInput style={styles.input} value={code} onChangeText={setCode} inputMode="numeric" autoComplete="one-time-code" maxLength={6} />
              {devCode ? (
                <Text style={styles.dev}>
                  {t('MFA.DEV_CODE', { code: devCode })}{' '}
                  <Text style={styles.link} onPress={() => setCode(devCode)}>
                    {t('MFA.DEV_USE')}
                  </Text>
                </Text>
              ) : null}
            </View>
            {button(t('ONBOARDING.VERIFY'), () => void verifyEmailCode(code), code.trim().length < 6)}
            <Text style={styles.link} onPress={() => void onSend()}>
              {t('ONBOARDING.RESEND')}
            </Text>
          </>
        )}
      </>
    );
  } else if (passwordStep) {
    body = (
      <>
        <Text style={styles.title}>{t('ONBOARDING.PASSWORD_TITLE')}</Text>
        <Text style={styles.subtitle}>{t('ONBOARDING.PASSWORD_BODY')}</Text>
        <View style={styles.field}>
          <Text style={styles.label}>{t('ONBOARDING.NEW_PASSWORD')}</Text>
          <TextInput style={styles.input} value={pwd} onChangeText={setPwd} secureTextEntry autoCapitalize="none" autoComplete="new-password" />
          <Text style={styles.label}>{t('ONBOARDING.REPEAT_PASSWORD')}</Text>
          <TextInput style={styles.input} value={pwd2} onChangeText={setPwd2} secureTextEntry autoCapitalize="none" autoComplete="new-password" />
          {error ? <Text style={styles.error}>{error}</Text> : null}
        </View>
        {button(t('ONBOARDING.SAVE_PASSWORD'), () => void onSetPassword(), pwd.length === 0)}
      </>
    );
  } else {
    body = (
      <>
        <Text style={styles.title}>{t('ONBOARDING.MFA_TITLE')}</Text>
        <Text style={styles.subtitle}>{t('ONBOARDING.MFA_BODY')}</Text>
        {button(t('ONBOARDING.CONTINUE'), continueToMfa)}
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
  input: { height: 40, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, fontSize: 18, color: colors.text },
  dev: { fontSize: 13, color: colors.muted },
  error: { fontSize: 14, color: '#B00020' },
  link: { color: colors.primary, fontWeight: '600' },
  buttonOuter: { borderRadius: 8, width: '60%', maxWidth: 240 },
  button: { backgroundColor: colors.primary, borderRadius: 8, paddingVertical: 10, paddingHorizontal: 24, alignItems: 'center', justifyContent: 'center' },
  disabled: { opacity: 0.5 },
  buttonLabel: { color: colors.primaryContrast, fontWeight: '600', fontSize: 18 },
  poweredBy: { color: colors.text },
});
