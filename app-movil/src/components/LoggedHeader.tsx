import { StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@hooks/AuthContext';
import { useIsOnline } from '@hooks/useIsOnline';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import InitialsAvatar from '@components/InitialsAvatar';

// Encabezado del inicio: iniciales del chofer, su nombre, sus compañías y el estado de la conexión (verde «En línea» / gris «Sin conexión»).
export default function LoggedHeader() {
  const { t } = useTranslation();
  const { user, companies } = useAuth();
  const online = useIsOnline();
  const styles = useStyles(createStyles);

  return (
    <View style={styles.container}>
      <InitialsAvatar name={user?.fullName ?? user?.email ?? undefined} size={50} />
      <View style={styles.info}>
        <Text style={styles.company} numberOfLines={1}>{companies.map((c) => c.name).join(' · ') || '...'}</Text>
        <Text style={styles.user}>{user?.fullName ?? user?.email ?? '...'}</Text>
        <Text style={[styles.status, online ? styles.online : styles.offline]}>{online ? t('ONLINE') : t('OFFLINE')}</Text>
      </View>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flexDirection: 'row', alignItems: 'center', gap: 10, margin: 10 },
  info: { gap: 1, maxWidth: 230 },
  company: { fontSize: 14 },
  user: { fontWeight: '700', fontSize: 17 },
  status: { width: 100, textAlign: 'center', borderRadius: 10, color: '#ffffff' },
  online: { backgroundColor: colors.online },
  offline: { backgroundColor: colors.offline },
});
