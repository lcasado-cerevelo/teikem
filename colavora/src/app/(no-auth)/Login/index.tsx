import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  Image,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useForm, FormProvider, type SubmitHandler } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { MaterialIcons } from '@expo/vector-icons';
import { useAuth } from '@hooks/AuthContext';
import {
  authenticate,
  clearCredentials,
  getStoredCredentials,
  isBiometricAvailable,
  saveCredentials,
} from '@services/auth/biometricAuth';
import FormInput from '@components/FormInput';
import { colors } from '@constants/theme';
import { BRAND } from '@constants/brand';

// Same email pattern the old app used to validate the username field.
const EmailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface LoginForm {
  userName: string;
  password: string;
}

// Embedded username/password login: brand logo on top, two shadowed inputs,
// navy button (#03204B), "Powered by <brand>" — logo/text swap per BRAND.
// With biometric hardware + a prior opt-in, a fingerprint/face button signs
// back in without typing (see services/auth/biometricAuth.ts).
export default function Login() {
  const { t } = useTranslation();
  const { login, loading } = useAuth();
  const methods = useForm<LoginForm>({ defaultValues: { userName: '', password: '' } });

  // Username of the stored biometric credentials (null = no button). Shown on
  // the button so it's obvious WHOSE session a fingerprint restores.
  const [bioUser, setBioUser] = useState<string | null>(null);

  const refreshBioUser = useCallback(async () => {
    if (!(await isBiometricAvailable())) {
      setBioUser(null);
      return;
    }
    const creds = await getStoredCredentials();
    setBioUser(creds?.userName ?? null);
  }, []);

  useEffect(() => {
    void refreshBioUser();
  }, [refreshBioUser]);

  // After a successful password login, offer to enable biometric sign-in
  // (only when it isn't already stored for this same user). Accepting FIRST
  // requires passing the device's biometric prompt — proves the person
  // enabling it can actually unlock it, before anything is stored.
  const maybeOfferBiometrics = async (creds: LoginForm) => {
    if (!(await isBiometricAvailable())) return;
    const stored = await getStoredCredentials();
    if (stored?.userName === creds.userName && stored.password === creds.password) return;
    // Credentials stored for ANOTHER user are stale on this phone now.
    if (stored && stored.userName !== creds.userName) await clearCredentials();
    Alert.alert(t('LOGIN.BIOMETRIC_OFFER_TITLE'), t('LOGIN.BIOMETRIC_OFFER_BODY'), [
      { text: t('LOGIN.BIOMETRIC_NOT_NOW'), style: 'cancel' },
      {
        text: t('LOGIN.BIOMETRIC_ENABLE'),
        onPress: () => {
          void (async () => {
            const passed = await authenticate(
              t('LOGIN.BIOMETRIC_ENABLE_PROMPT'),
              t('LOGIN.BIOMETRIC_CANCEL'),
            );
            if (passed) await saveCredentials(creds);
          })();
        },
      },
    ]);
  };

  const onSubmit: SubmitHandler<LoginForm> = async (data) => {
    const creds = { userName: data.userName.trim(), password: data.password };
    const ok = await login(creds);
    if (ok) await maybeOfferBiometrics(creds);
  };

  const onBiometricLogin = async () => {
    const passed = await authenticate(t('LOGIN.BIOMETRIC_PROMPT'), t('LOGIN.BIOMETRIC_CANCEL'));
    if (!passed) return;
    const creds = await getStoredCredentials();
    if (!creds) {
      setBioUser(null);
      return;
    }
    const ok = await login(creds);
    // Auth0 rejected the saved password (changed/expired): drop it so the
    // driver isn't offered a button that can never work.
    if (!ok) {
      await clearCredentials();
      setBioUser(null);
    }
  };

  return (
    <KeyboardAvoidingView
      style={styles.flex}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        contentContainerStyle={styles.container}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
      >
        <Image source={BRAND.logo} style={styles.logo} resizeMode="contain" />

        <View style={styles.formContainer}>
        <FormProvider {...methods}>
          <FormInput
            name="userName"
            label={t('LOGIN.USERNAME')}
            inputMode="email"
            autoCapitalize="none"
            autoCorrect={false}
            rules={{
              required: t('VALIDATIONS.REQUIRED'),
              pattern: { value: EmailRegex, message: t('VALIDATIONS.EMAIL') },
            }}
          />
          <FormInput
            name="password"
            label={t('LOGIN.PASSWORD')}
            secureTextEntry
            rules={{ required: t('VALIDATIONS.REQUIRED') }}
          />
        </FormProvider>
      </View>

      {loading ? (
        <ActivityIndicator color={colors.primary} />
      ) : (
        <>
          <View style={styles.buttonOuter}>
            <Pressable
              style={styles.button}
              onPress={methods.handleSubmit(onSubmit)}
              accessibilityRole="button"
            >
              <Text style={styles.buttonLabel}>{t('LOGIN.LOGIN')}</Text>
            </Pressable>
          </View>
          {bioUser && (
            <Pressable
              style={styles.bioButton}
              onPress={() => void onBiometricLogin()}
              accessibilityRole="button"
              accessibilityLabel={t('LOGIN.BIOMETRIC_BUTTON')}
            >
              <View style={styles.bioIconCircle}>
                <MaterialIcons name="fingerprint" size={44} color={colors.primary} />
              </View>
              <Text style={styles.bioButtonUser}>{bioUser}</Text>
            </Pressable>
          )}
        </>
      )}

        <Text style={styles.poweredBy}>{BRAND.poweredBy}</Text>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  // ported from Login.styles.ts
  flex: { flex: 1, backgroundColor: colors.background },
  container: {
    flexGrow: 1,
    justifyContent: 'flex-start',
    alignItems: 'center',
    paddingHorizontal: 26,
    paddingTop: 40,
    paddingBottom: 26,
    gap: 20,
    backgroundColor: colors.background,
  },
  logo: { width: 120, height: 120, maxWidth: 120, maxHeight: 120 },
  formContainer: { width: '100%', justifyContent: 'center', alignItems: 'center', gap: 16 },
  buttonOuter: { borderRadius: 8, width: '50%', maxWidth: 200 },
  button: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: 10,
    paddingHorizontal: 24,
    alignItems: 'center',
    justifyContent: 'center',
    ...Platform.select({
      ios: { shadowOffset: { width: 0, height: 4 }, shadowColor: '#000', shadowOpacity: 0.25 },
      android: { elevation: 3 },
      default: {},
    }),
  },
  buttonLabel: { color: colors.primaryContrast, fontWeight: '600', fontSize: 18 },
  bioButton: { alignItems: 'center', gap: 4, paddingVertical: 6 },
  bioIconCircle: {
    width: 64,
    height: 64,
    borderRadius: 32,
    borderWidth: 1.5,
    borderColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bioButtonUser: { color: colors.muted, fontSize: 12 },
  poweredBy: { color: colors.text },
});
