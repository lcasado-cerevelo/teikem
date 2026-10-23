import { useCallback, useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { MaterialIcons } from '@expo/vector-icons';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { authenticate, MAX_BIOMETRIC_FAILURES } from '@services/auth/biometricAuth';
import { colors } from '@constants/theme';

// Pantalla de bloqueo: «Abrir con huella». Al entrar pide la huella (o el PIN del teléfono) sola; si falla se puede reintentar. Tras 5 fallos seguidos, o con
// «Usar mi contraseña», se cierra la sesión del teléfono y se pide todo otra vez (contraseña y, si aplica, el código).
export default function LockScreen() {
  const { t } = useTranslation();
  const { unlock, logout } = useAuth();
  const [failures, setFailures] = useState(0);
  const [busy, setBusy] = useState(false);
  const asked = useRef(false);
  const failuresRef = useRef(0);

  const tryBiometric = useCallback(async () => {
    setBusy(true);
    const ok = await authenticate(t('LOCK.PROMPT'), t('LOCK.CANCEL'));
    setBusy(false);
    if (ok) {
      unlock();
      return;
    }
    failuresRef.current += 1;
    setFailures(failuresRef.current);
    if (failuresRef.current >= MAX_BIOMETRIC_FAILURES) void logout();
  }, [t, unlock, logout]);

  // al llegar se pide la huella sin tocar nada
  useEffect(() => {
    if (asked.current) return;
    asked.current = true;
    void tryBiometric();
  }, [tryBiometric]);

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.circle}>
        <MaterialIcons name="fingerprint" size={64} color={colors.primary} />
      </View>
      <Text style={styles.title}>{t('LOCK.TITLE')}</Text>
      <Text style={styles.help}>{t('LOCK.HELP')}</Text>
      {failures > 0 && <Text style={styles.failed}>{t('LOCK.FAILED', { left: MAX_BIOMETRIC_FAILURES - failures })}</Text>}
      <Pressable style={styles.button} onPress={() => void tryBiometric()} disabled={busy} accessibilityRole="button">
        {busy ? <ActivityIndicator color={colors.primaryContrast} /> : <Text style={styles.buttonText}>{t('LOCK.UNLOCK')}</Text>}
      </Pressable>
      <Pressable style={styles.secondary} onPress={() => void logout()} accessibilityRole="button">
        <Text style={styles.secondaryText}>{t('LOCK.PASSWORD')}</Text>
      </Pressable>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, alignItems: 'center', justifyContent: 'center', padding: 24, gap: 14 },
  circle: { width: 104, height: 104, borderRadius: 52, borderWidth: 2, borderColor: colors.primary, alignItems: 'center', justifyContent: 'center' },
  title: { fontSize: 20, fontWeight: '700', color: colors.primary },
  help: { fontSize: 14, color: colors.muted, textAlign: 'center' },
  failed: { color: colors.danger },
  button: { backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 14, paddingHorizontal: 32, alignItems: 'center' },
  buttonText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 16 },
  secondary: { paddingVertical: 12 },
  secondaryText: { color: colors.primary, fontWeight: '600' },
});
