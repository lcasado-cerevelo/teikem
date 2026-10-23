import { ActivityIndicator, ScrollView, StyleSheet, Text, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import MapPreview from './MapPreview';
import { useCoordinatesFromAddress } from '@hooks/geocoding';
import { useConsignee } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { colors } from '@constants/theme';

// Details tab: map at top + consignee/service/COD info. The map uses the
// consignee's stored coordinates first, then geocodes line1+zip, then line1+city.
export default function ShipmentDetail() {
  const { t } = useTranslation();
  const shipment = useAppStore((s) => s.shipment);

  // Stored geo of the delivery address (ConsigneeNum == Consignee.ID).
  const consigneeId = shipment?.consigneeNum ? Number(shipment.consigneeNum) : undefined;
  const { data: consignee } = useConsignee(Number.isFinite(consigneeId) ? consigneeId : undefined);

  const { location, loading, located } = useCoordinatesFromAddress({
    storedLat: consignee?.latitude ? Number(consignee.latitude) : NaN,
    storedLng: consignee?.longitude ? Number(consignee.longitude) : NaN,
    addressLine1: shipment?.addressLine1,
    zip: shipment?.zip,
    city: shipment?.city,
  });

  const dueDate = shipment?.dueDate ? new Date(shipment.dueDate).toLocaleString() : '';
  const cityZip = [shipment?.city, shipment?.zip].filter(Boolean).join(', ');

  return (
    <View style={styles.container}>
      <View style={styles.mapContainer}>
        {loading ? (
          <View style={styles.mapMsg}>
            <ActivityIndicator color={colors.primary} />
          </View>
        ) : located ? (
          <MapPreview latitude={location.lat} longitude={location.lng} />
        ) : (
          <View style={styles.mapMsg}>
            <Text style={styles.mapMsgText}>{t('SHIPMENT_DETAILS.MAP_NOT_LOCATED')}</Text>
          </View>
        )}
      </View>

      <ScrollView>
        <View style={styles.infoContainer}>
          <View style={styles.section}>
            <Text style={styles.textHeader}>{shipment?.consigneeName ?? ''}</Text>
            <Text style={styles.textSubtitle}>{cityZip}</Text>
            <Text style={styles.textBody}>
              {shipment?.senderName ?? ''} - {shipment?.serviceTypeName ?? ''}
            </Text>
            <Text style={styles.textBody}>{shipment?.addressLine1 ?? ''}</Text>
            <Text style={styles.textBody}>{shipment?.addressLine2 ?? ''}</Text>
            <Text style={styles.textBody}>{shipment?.phoneNumber ?? ''}</Text>
          </View>

          <View style={styles.section}>
            <Text style={styles.textBody}>{dueDate}</Text>
          </View>

          <View style={styles.section}>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.STATUS')}: {shipment?.status ?? ''}</Text>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.WAYBILL')}: {shipment?.waybill ?? ''}</Text>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.INVOICE')}: {shipment?.waybill ?? ''}</Text>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.SERVICE_TYPE')}: {shipment?.serviceTypeName ?? ''}</Text>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.PIECES')}: {shipment?.qty ?? ''}</Text>
          </View>

          <View style={styles.section}>
            <Text style={styles.textBody}>
              {t('SHIPMENT_DETAILS.COD')}: {shipment?.codAmount && shipment.codAmount > 0 ? shipment.codAmount : ''}
            </Text>
          </View>
        </View>
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, gap: 24, alignContent: 'center' },
  infoContainer: { flex: 1, gap: 24, alignContent: 'center', marginBottom: 24 },
  mapContainer: { height: '45%', maxHeight: 500 },
  mapMsg: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.backgroundSecondary, paddingHorizontal: 24 },
  mapMsgText: { color: colors.muted, fontSize: 14, textAlign: 'center', fontWeight: '600' },
  section: { gap: 6, borderRadius: 13, marginHorizontal: 'auto', width: '70%' },
  textHeader: { fontWeight: '600', fontSize: 16, color: colors.text },
  textSubtitle: { fontWeight: '600', fontSize: 14, color: colors.text },
  textBody: { fontWeight: '600', fontSize: 12, color: colors.text },
});
