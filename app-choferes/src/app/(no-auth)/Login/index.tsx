import { Alert, Image, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View, ActivityIndicator } from 'react-native';
import { useRouter } from 'expo-router';
import { useForm, FormProvider, type SubmitHandler } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { useBiometricOffer } from '@hooks/useBiometricOffer';
import FormInput from '@components/FormInput';
import { colors } from '@constants/theme';
import { BRAND } from '@constants/brand';

// Mismo patrón de correo que usaba la app anterior para validar el usuario.
const EmailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

interface LoginForm {
  userName: string;
  password: string;
}

// Inicio de sesión: logo de la marca arriba, dos campos con sombra, botón azul marino (#03204B) y «Powered by».
// Paso 1: correo y contraseña. Si la compañía (o la persona) exige el segundo factor, sigue la pantalla del código (Mfa).
// «Abrir con huella» NO está aquí: la huella solo abre una sesión ya guardada (ver AuthContext y la pantalla de bloqueo).
export default function Login() {
  const { t } = useTranslation();
  const router = useRouter();
  const { login, loading, browserConfirmation, cancelMfa } = useAuth();
  const offerBiometrics = useBiometricOffer();
  const methods = useForm<LoginForm>({ defaultValues: { userName: '', password: '' } });

  const onSubmit: SubmitHandler<LoginForm> = async (data) => {
    cancelMfa();
    const result = await login({ userName: data.userName.trim(), password: data.password });
    if (result === 'mfa') router.push('/(no-auth)/Mfa');
    else if (result === 'browser') Alert.alert(t('LOGIN.BROWSER_TITLE'), t('LOGIN.BROWSER_BODY', { email: browserConfirmation?.email ?? '' }));
    else if (result === 'onboarding') Alert.alert(t('LOGIN.ONBOARDING_TITLE'), t('LOGIN.ONBOARDING_BODY'));
    else if (result === 'ok') await offerBiometrics();
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
  poweredBy: { color: colors.text },
});
