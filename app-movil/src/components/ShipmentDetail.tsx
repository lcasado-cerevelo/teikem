import { ActivityIndicator, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'expo-router';
import LinkText from './LinkText';
import MapPreview from './MapPreview';
import StaffOrderExtras from './StaffOrderExtras';
import { useCoordinatesFromAddress } from '@hooks/geocoding';
import { useShipmentDetail } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Details tab: map at top + consignee/service/COD info. The map uses the
// consignee's stored coordinates first, then geocodes line1+zip, then line1+city.
export default function ShipmentDetail() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const base = useAppStore((s) => s.shipment);
  const { data: detail } = useShipmentDetail(base);
  const shipment = detail ?? base;

  // El punto del mapa sale de la dirección (la ubicación exacta de la entrega la pone el GPS del teléfono al entregar).
  const geo = useCoordinatesFromAddress({
    addressLine1: shipment?.address,
    zip: shipment?.postalCode,
    city: shipment?.city,
  });
  // Una orden del personal trae el punto de la parada del servidor (si la dirección ya tiene coordenadas); si no, se geocodifica como la del chofer.
  const staff = detail?.staff;
  const { location, loading, located } =
    staff?.latitude != null && staff.longitude != null ? { location: { lat: staff.latitude, lng: staff.longitude }, loading: false, located: true } : geo;

  const cityZip = [shipment?.city, shipment?.postalCode].filter(Boolean).join(', ');

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
            <Text style={styles.textBody}>{shipment?.companyName ?? ''}{detail?.clientName ? ` - ${detail.clientName}` : ''}</Text>
            <Text style={styles.textBody}>{shipment?.address ?? ''}</Text>
            {detail?.contactPerson ? <Text style={styles.textBody}>{detail.contactPerson}</Text> : null}
            {detail?.phone ? <LinkText style={styles.textBody}>{detail.phone}</LinkText> : null}
            {shipment ? (
              <Pressable
                style={styles.talkBtn}
                onPress={() => router.push({ pathname: '/(auth)/NewChat', params: { tenantId: String(shipment.tenantId), refEntity: 'ORDER', refId: shipment.id, refTitle: shipment.orderNumber } })}
                accessibilityRole="button"
              >
                <Text style={styles.talkBtnText}>{`💬 ${t('CHAT.ABOUT_SHIPMENT')}`}</Text>
              </Pressable>
            ) : null}
          </View>

          <View style={styles.section}>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.STATUS')}: {shipment?.status ?? ''}</Text>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.ORDER')}: {shipment?.orderNumber ?? ''}</Text>
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.WAYBILL')}: {shipment?.waybill ?? ''}</Text>
            {shipment?.source === 'staff' ? null : <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.MANIFEST')}: {shipment?.manifestCode ?? ''}</Text>}
            <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.PIECES')}: {shipment?.pieces ?? ''}</Text>
          </View>

          {shipment?.codAmount && shipment.codAmount > 0 ? (
            <View style={styles.section}>
              <Text style={styles.textBody}>{t('SHIPMENT_DETAILS.COD')}: {shipment.codAmount.toFixed(2)}</Text>
            </View>
          ) : null}

          {shipment?.notes ? (
            <View style={styles.section}>
              <Text style={styles.textBody}>{shipment.notes}</Text>
            </View>
          ) : null}

          {staff ? <StaffOrderExtras x={staff} /> : null}
        </View>
      </ScrollView>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, gap: 24, alignContent: 'center' },
  talkBtn: { alignSelf: 'flex-start', marginTop: 8, borderWidth: 1, borderColor: colors.primary, borderRadius: 18, paddingVertical: 8, paddingHorizontal: 14 },
  talkBtnText: { color: colors.primary, fontWeight: '700' },
  infoContainer: { flex: 1, gap: 24, alignContent: 'center', marginBottom: 24 },
  mapContainer: { height: '45%', maxHeight: 500 },
  mapMsg: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.backgroundSecondary, paddingHorizontal: 24 },
  mapMsgText: { color: colors.muted, fontSize: 14, textAlign: 'center', fontWeight: '600' },
  section: { gap: 6, borderRadius: 13, marginHorizontal: 'auto', width: '70%' },
  textHeader: { fontWeight: '600', fontSize: 16, color: colors.text },
  textSubtitle: { fontWeight: '600', fontSize: 14, color: colors.text },
  textBody: { fontWeight: '600', fontSize: 12, color: colors.text },
});
