import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { Link } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '@/stores';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import type { Shipment } from '@/types/models';

export default function OrderListItem(props: Shipment) {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const addShipment = useAppStore((s) => s.addShipment);

  return (
    <Link href="/(auth)/ShipmentDetails" asChild>
      <Pressable style={styles.container} onPress={() => addShipment(props)}>
        <View>
          <Text style={styles.title}>{props.consigneeName}</Text>
          <Text style={styles.title}>{[props.city, props.postalCode].filter(Boolean).join(', ')}</Text>
        </View>
        <View>
          <Text style={styles.company}>{props.companyName}</Text>
          <Text style={styles.bodyText}>{props.address}</Text>
          <Text style={styles.bodyText}>({props.orderNumber})</Text>
          <Text style={styles.piecesLabel}>{t('COMMON.PIECES')}: {props.pieces ?? ''}</Text>
        </View>
      </Pressable>
    </Link>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, alignItems: 'flex-start', justifyContent: 'center', gap: 8, marginVertical: 10, marginHorizontal: 20, padding: 16 },
  title: { fontWeight: '600', fontSize: 16, color: colors.text },
  company: { fontWeight: '700', fontSize: 13, color: colors.primary },
  bodyText: { fontWeight: '600', fontSize: 14, color: colors.text },
  piecesLabel: { marginTop: 4, fontWeight: '400', fontSize: 12, color: colors.muted },
});
