import { useState } from 'react';
import { ActivityIndicator, Alert, Image, KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import type { AxiosError } from 'axios';
import { forgotPassword } from '@services/auth/teikemAuth';
import { KEY_BY_KIND, loginErrorKind } from '@utils/loginErrors';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { BRAND } from '@constants/brand';

// «¿Olvidaste tu contraseña?» (Lote 219): se escribe el correo y el servidor manda un enlace de un solo uso que abre la web para elegir la contraseña nueva. La respuesta es la misma exista o no el correo.
const EmailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function ForgotPassword() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const router = useRouter();
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState<string | null>(null);
  const valid = EmailRegex.test(email.trim());

  const submit = async () => {
    if (!valid || busy) return;
    setBusy(true);
    try {
      setSent((await forgotPassword(email, i18n.language)) || t('FORGOT_PASSWORD.SENT'));
    } catch (err) {
      const kind = loginErrorKind((err as AxiosError).response?.status);
      if (kind === 'other') Alert.alert(t('FORGOT_PASSWORD.ERROR_TITLE'), t('LOGIN.SERVER_ERROR'));
      else Alert.alert(t(KEY_BY_KIND[kind].title), t(KEY_BY_KIND[kind].body));
    } finally {
      setBusy(false);
    }
  };

  return (
    <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <ScrollView contentContainerStyle={styles.container} keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
        <Image source={BRAND.logo} style={styles.logo} resizeMode="contain" />
        <Text style={styles.title}>{t('FORGOT_PASSWORD.TITLE')}</Text>
        {sent ? (
          <View style={styles.form}>
            <Text style={styles.ok} accessibilityRole="alert" testID="forgot-sent">{sent}</Text>
            <Text style={styles.help}>{t('FORGOT_PASSWORD.SENT_HELP')}</Text>
          </View>
        ) : (
          <View style={styles.form}>
            <Text style={styles.help}>{t('FORGOT_PASSWORD.SUBTITLE')}</Text>
            <TextInput
              style={styles.input}
              value={email}
              onChangeText={setEmail}
              placeholder={t('LOGIN.USERNAME')}
              placeholderTextColor={colors.muted}
              accessibilityLabel={t('LOGIN.USERNAME')}
              inputMode="email"
              autoCapitalize="none"
              autoCorrect={false}
              returnKeyType="send"
              onSubmitEditing={() => void submit()}
              testID="forgot-email"
            />
            {busy ? (
              <ActivityIndicator color={colors.primary} />
            ) : (
              <Pressable style={[styles.button, !valid && styles.off]} disabled={!valid} onPress={() => void submit()} accessibilityRole="button" testID="forgot-submit">
                <Text style={styles.buttonLabel}>{t('FORGOT_PASSWORD.SUBMIT')}</Text>
              </Pressable>
            )}
          </View>
        )}
        <Pressable onPress={() => router.replace('/(no-auth)/Login')} accessibilityRole="button" testID="forgot-back">
          <Text style={styles.link}>{t('FORGOT_PASSWORD.BACK')}</Text>
        </Pressable>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
  container: { flexGrow: 1, alignItems: 'center', paddingHorizontal: 26, paddingTop: 40, paddingBottom: 26, gap: 20, backgroundColor: colors.background },
  logo: { width: 120, height: 120, maxWidth: 120, maxHeight: 120 },
  title: { fontSize: 22, fontWeight: '700', color: colors.text },
  form: { width: '100%', gap: 14 },
  help: { color: colors.muted, fontSize: 14, lineHeight: 20 },
  ok: { color: colors.text, fontWeight: '600', fontSize: 15 },
  input: { borderWidth: 0.5, borderColor: colors.text, borderRadius: 10, height: 44, paddingHorizontal: 12, color: colors.text, fontSize: 16 },
  button: { backgroundColor: colors.primary, borderRadius: 10, height: 46, alignItems: 'center', justifyContent: 'center' },
  off: { opacity: 0.5 },
  buttonLabel: { color: colors.primaryContrast, fontWeight: '700', fontSize: 16 },
  link: { color: colors.primary, fontWeight: '600', fontSize: 15, paddingVertical: 8 },
});
