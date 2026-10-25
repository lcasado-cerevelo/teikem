import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import ShipmentDetailsTabs, { type ShipmentTab } from '@components/ShipmentDetailsTabs';
import ShipmentDetail from '@components/ShipmentDetail';
import ShipmentActions from '@components/ShipmentActions';
import { useAppStore } from '@/stores';
import { useShipmentDetail } from '@hooks/queries';
import { useAccess } from '@hooks/access';
import { staffActionsOf } from '@utils/staffOrders';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Header + the four tabs (Detalles / Piezas / Comentarios / Acciones), each with
// real content now.
export default function ShipmentDetails() {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [selectedTab, setSelectedTab] = useState<ShipmentTab>('details');
  // Personal (no chofer): un botón grande y a la vista para completar/recoger la orden, sin tener que buscar la pestaña «Acciones».
  const shipment = useAppStore((s) => s.shipment);
  const { data: detail } = useShipmentDetail(shipment);
  const access = useAccess();
  const canDeliver = shipment?.source === 'staff' && access.companiesWith('orders.deliver').some((c) => c.tenantId === shipment.tenantId);
  const staffActions = canDeliver && detail?.staff ? staffActionsOf(detail.staff) : null;
  const quick = staffActions?.complete ? 'CLOSE_ORDER' : staffActions?.pickup ? 'PICKUP_ORDER' : null;

  return (
    <View style={styles.container}>
      <ScreenHeader title={t('SHIPMENT_DETAILS.SHIPMENT_DETAILS')} />
      <ShipmentDetailsTabs style={styles.tabs} selectedTab={selectedTab} setSelectedTab={setSelectedTab} />

      {quick && selectedTab === 'details' ? (
        <Pressable style={styles.quick} onPress={() => setSelectedTab('actions')} accessibilityRole="button">
          <Text style={styles.quickText}>{t(`STAFF_ORDERS.${quick}`)}</Text>
        </Pressable>
      ) : null}

      {selectedTab === 'details' ? (
        <ShipmentDetail />
      ) : (
        <ShipmentActions />
      )}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, gap: 10, backgroundColor: colors.background },
  tabs: { marginHorizontal: 15 },
  quick: { marginHorizontal: 15, minHeight: 48, borderRadius: 12, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.primary },
  quickText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 16 },
});
