import { StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useMe, useCompany } from '@hooks/queries';
import { useIsOnline } from '@hooks/useIsOnline';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import InitialsAvatar from '@components/InitialsAvatar';

// Replicates the old LoggedHeader: driver-initials avatar, company name,
// driver name, and a connectivity badge (green "En línea" / gray "Sin conexión").
export default function LoggedHeader() {
  const { t } = useTranslation();
  const { data: me } = useMe();
  const { data: company } = useCompany(me?.companyID);
  const online = useIsOnline();
  const styles = useStyles(createStyles);

  return (
    <View style={styles.container}>
      <InitialsAvatar name={me?.driverName} size={50} />
      <View style={styles.info}>
        <Text style={styles.company}>{company?.companyName ?? '...'}</Text>
        <Text style={styles.user}>{me?.driverName ?? '...'}</Text>
        <Text style={[styles.status, online ? styles.online : styles.offline]}>
          {online ? t('ONLINE') : t('OFFLINE')}
        </Text>
      </View>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flexDirection: 'row', alignItems: 'center', gap: 10, margin: 10 },
  info: { gap: 1 },
  company: { fontSize: 14 },
  user: { fontWeight: '700', fontSize: 17 },
  status: { width: 100, textAlign: 'center', borderRadius: 10, color: '#ffffff' },
  online: { backgroundColor: colors.online },
  offline: { backgroundColor: colors.offline },
});
