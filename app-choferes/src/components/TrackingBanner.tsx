import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useDriverWork } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Aviso visible al chofer cuando tiene una ruta en curso y su ubicación NO se está enviando (la apagó en Perfil o le quitaron el permiso). La oficina lo ve igual en el monitor.
export default function TrackingBanner() {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const { data } = useDriverWork();
  const enabled = useAppStore((s) => s.trackingEnabled);
  const setEnabled = useAppStore((s) => s.setTrackingEnabled);
  const permissionLost = useAppStore((s) => s.trackingPermissionLost);
  const routeActive = Boolean(data?.shipments.some((s) => s.manifestAccepted));
  if (!routeActive || (enabled && !permissionLost)) return null;
  return (
    <View style={styles.box} accessibilityRole="alert">
      <Text style={styles.text}>{enabled ? t('TRACKING.BANNER_PERMISSION') : t('TRACKING.BANNER_OFF')}</Text>
      {!enabled && (
        <Pressable onPress={() => setEnabled(true)} accessibilityRole="button">
          <Text style={styles.action}>{t('TRACKING.BANNER_TURN_ON')}</Text>
        </Pressable>
      )}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  box: { borderWidth: 1, borderColor: colors.danger, borderRadius: 13, marginHorizontal: 20, marginTop: 10, padding: 12, gap: 6 },
  text: { color: colors.danger, fontWeight: '600', fontSize: 14 },
  action: { color: colors.primary, fontWeight: '700' },
});
