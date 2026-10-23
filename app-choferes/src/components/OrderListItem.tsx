import { Pressable, StyleSheet, Text, View } from 'react-native';
import { Link } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '@/stores';
import { colors } from '@constants/theme';
import type { Shipment } from '@/types/models';

// Replicates the old OrderListItem. Tapping stores the shipment and opens
// ShipmentDetails.
export default function OrderListItem(props: Shipment) {
  const { t } = useTranslation();
  const addShipment = useAppStore((s) => s.addShipment);

  return (
    <Link href="/(auth)/ShipmentDetails" asChild>
      <Pressable style={styles.container} onPress={() => addShipment(props)}>
        <View>
          <Text style={styles.title}>{props.consigneeName}</Text>
          <Text style={styles.title}>
            {props.city ? `${props.city}, ${props.zip ?? ''}` : props.zip}
          </Text>
        </View>
        <View>
          <Text style={styles.bodyText}>
            {props.senderName} - {props.serviceTypeName}
          </Text>
          <Text style={styles.bodyText}>{props.addressLine1}</Text>
          <Text style={styles.bodyText}>{props.addressLine2}</Text>
          <Text style={styles.bodyText}>({props.referenceNo})</Text>
          <Text style={styles.piecesLabel}>{t('COMMON.PIECES')}: {props.qty}</Text>
        </View>
      </Pressable>
    </Link>
  );
}

const styles = StyleSheet.create({
  container: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 13,
    alignItems: 'flex-start',
    justifyContent: 'center',
    gap: 8,
    marginVertical: 10,
    marginHorizontal: 20,
    padding: 16,
  },
  title: { fontWeight: '600', fontSize: 16, color: colors.text },
  bodyText: { fontWeight: '600', fontSize: 14, color: colors.text },
  piecesLabel: { marginTop: 4, fontWeight: '400', fontSize: 12, color: colors.muted },
});
