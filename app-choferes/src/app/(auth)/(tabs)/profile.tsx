import { useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, Switch, View } from 'react-native';
import { Text } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import { authenticate, isBiometricAvailable } from '@services/auth/biometricAuth';
import LoggedHeader from '@components/LoggedHeader';
import { useMe, useSync } from '@hooks/queries';
import { useAuth } from '@hooks/AuthContext';
import { useIsOnline } from '@hooks/useIsOnline';
import { SYNC_INTERVAL_OPTIONS_MINUTES } from '@hooks/useSyncSettings';
import { useAppStore } from '@/stores';
import { countByStatus, drainOutbox } from '@/offline';
import { setLanguage, type AppLanguage } from '@/lang/i18n';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Profile tab: company/driver header, username, language switch (ES/EN),
// manual sync, logout. (The old app's profile was just a placeholder.)
export default function ProfileScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t, i18n } = useTranslation();
  const { data: me } = useMe();
  const { logout, user, companies, biometricEnabled, setBiometricEnabled } = useAuth();
  const { mode, setMode } = useTheme();
  const { sync, isSyncing } = useSync();
  const syncInterval = useAppStore((s) => s.syncIntervalMinutes);
  const setSyncInterval = useAppStore((s) => s.setSyncIntervalMinutes);
  const online = useIsOnline();
  const current = i18n.language as AppLanguage;

  // «Abrir con huella»: solo si el teléfono tiene lector y huellas registradas. Activarlo exige pasar la huella primero.
  const [bioAvailable, setBioAvailable] = useState(false);
  useEffect(() => {
    void isBiometricAvailable().then(setBioAvailable);
  }, []);
  const onToggleBiometrics = async (value: boolean) => {
    if (value && !(await authenticate(t('LOGIN.BIOMETRIC_ENABLE_PROMPT'), t('LOGIN.BIOMETRIC_CANCEL')))) return;
    await setBiometricEnabled(value);
  };

  const onSync = async () => {
    const n = await sync();
    Alert.alert(t('PROFILE.SYNC'), t('PROFILE.SYNCED', { count: n ?? 0 }));
  };

  // Logout wipes ALL local data (next driver must start clean), so anything
  // still queued would be lost. Try to send it now; if something remains
  // (offline), make the driver confirm the loss explicitly.
  const onLogout = async () => {
    try {
      await drainOutbox();
    } catch {
      /* still offline — the count below catches it */
    }
    const pending = await countByStatus('pending');
    if (pending > 0) {
      Alert.alert(
        t('PROFILE.LOGOUT_PENDING_TITLE'),
        t('PROFILE.LOGOUT_PENDING_BODY', { count: pending }),
        [
          { text: t('PROFILE.LOGOUT_CANCEL'), style: 'cancel' },
          { text: t('PROFILE.LOGOUT_ANYWAY'), style: 'destructive', onPress: () => void logout() },
        ],
      );
      return;
    }
    await logout();
  };

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <Text style={styles.title}>{t('PROFILE.PROFILE')}</Text>
      <LoggedHeader />

      <View style={styles.row}>
        <Text style={styles.label}>{t('PROFILE.USERNAME')}</Text>
        <Text style={styles.value}>{me?.username ?? ''}</Text>
      </View>

      <View style={styles.section}>
        <Text style={styles.label}>{t('PROFILE.LANGUAGE')}</Text>
        <View style={styles.langRow}>
          <Pressable
            style={[styles.lang, current === 'es' && styles.langActive]}
            onPress={() => void setLanguage('es')}
          >
            <Text style={[styles.langText, current === 'es' && styles.langTextActive]}>
              {t('PROFILE.SPANISH')}
            </Text>
          </Pressable>
          <Pressable
            style={[styles.lang, current === 'en' && styles.langActive]}
            onPress={() => void setLanguage('en')}
          >
            <Text style={[styles.langText, current === 'en' && styles.langTextActive]}>
              {t('PROFILE.ENGLISH')}
            </Text>
          </Pressable>
        </View>
      </View>

      <View style={styles.section}>
        <Text style={styles.label}>{t('PROFILE.THEME')}</Text>
        <View style={styles.langRow}>
          {(['auto', 'light', 'dark'] as const).map((m) => (
            <Pressable key={m} style={[styles.lang, mode === m && styles.langActive]} onPress={() => setMode(m)} accessibilityRole="button">
              <Text style={[styles.langText, mode === m && styles.langTextActive]}>{t(`PROFILE.THEME_${m.toUpperCase()}`)}</Text>
            </Pressable>
          ))}
        </View>
      </View>

      {companies.length > 0 && (
        <View style={styles.section}>
          <Text style={styles.label}>{t('PROFILE.COMPANIES')}</Text>
          {companies.map((c) => (
            <View key={c.tenantId} style={styles.company}>
              <Text style={styles.companyText}>{c.name}</Text>
              {c.blocked && <Text style={styles.companyTextActive}>{t('PROFILE.COMPANY_BLOCKED')}</Text>}
            </View>
          ))}
        </View>
      )}

      <View style={styles.section}>
        <Text style={styles.label}>{t('PROFILE.AUTO_SYNC')}</Text>
        <Text style={styles.autoSyncHint}>{t('PROFILE.AUTO_SYNC_HINT', { minutes: syncInterval })}</Text>
        <View style={styles.intervalRow}>
          {SYNC_INTERVAL_OPTIONS_MINUTES.map((m) => (
            <Pressable
              key={m}
              style={[styles.interval, m === syncInterval && styles.intervalActive]}
              onPress={() => setSyncInterval(m)}
            >
              <Text style={[styles.intervalText, m === syncInterval && styles.intervalTextActive]}>
                {t('PROFILE.AUTO_SYNC_MINUTES', { count: m })}
              </Text>
            </Pressable>
          ))}
        </View>
      </View>

      {/* Sin conexión no hay nada que sincronizar: deshabilitado y gris. */}
      <Pressable
        style={[styles.sync, (isSyncing || !online) && styles.disabled]}
        onPress={() => void onSync()}
        disabled={isSyncing || !online}
      >
        {isSyncing ? (
          <ActivityIndicator color={colors.primaryContrast} />
        ) : (
          <Text style={styles.syncText}>{t('PROFILE.SYNC')}</Text>
        )}
      </Pressable>

      {bioAvailable && (
        <View style={styles.bioRow}>
          <View style={styles.bioText}>
            <Text style={styles.label}>{t('PROFILE.BIOMETRIC_ENABLE')}</Text>
            <Text style={styles.autoSyncHint}>{t('PROFILE.BIOMETRIC_ENABLE_HINT')}</Text>
          </View>
          <Switch value={biometricEnabled} onValueChange={(v) => void onToggleBiometrics(v)} trackColor={{ true: colors.primary }} />
        </View>
      )}

      <Pressable style={styles.logout} onPress={() => void onLogout()}>
        <Text style={styles.logoutText}>{t('PROFILE.LOGOUT')}</Text>
      </Pressable>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, padding: 20, gap: 18 },
  title: { fontSize: 20, fontWeight: '700', color: colors.primary },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  section: { gap: 8 },
  label: { fontSize: 14, color: colors.muted },
  value: { fontSize: 15, color: colors.text },
  langRow: { flexDirection: 'row', gap: 10 },
  lang: { flex: 1, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingVertical: 12, alignItems: 'center' },
  langActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  langText: { color: colors.text, fontWeight: '600' },
  langTextActive: { color: colors.primaryContrast },
  autoSyncHint: { fontSize: 12, color: colors.muted },
  intervalRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  interval: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 10,
    paddingVertical: 8,
    paddingHorizontal: 14,
  },
  intervalActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  intervalText: { color: colors.text, fontWeight: '600', fontSize: 13 },
  intervalTextActive: { color: colors.primaryContrast },
  sync: { marginTop: 'auto', backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 14, alignItems: 'center' },
  syncText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 16 },
  disabled: { opacity: 0.6 },
  bioRow: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  bioText: { flex: 1, gap: 2 },
  company: { flexDirection: 'row', justifyContent: 'space-between', borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingVertical: 12, paddingHorizontal: 14 },
  companyActive: { backgroundColor: colors.primary, borderColor: colors.primary },
  companyText: { color: colors.text, fontWeight: '600' },
  companyTextActive: { color: colors.primaryContrast, fontWeight: '600' },
  logout: { borderWidth: 1, borderColor: colors.danger, borderRadius: 12, paddingVertical: 14, alignItems: 'center' },
  logoutText: { color: colors.danger, fontWeight: '700', fontSize: 16 },
});
