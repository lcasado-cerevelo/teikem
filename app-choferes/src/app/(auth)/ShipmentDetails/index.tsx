import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import ShipmentDetailsTabs, { type ShipmentTab } from '@components/ShipmentDetailsTabs';
import ShipmentDetail from '@components/ShipmentDetail';
import ShipmentPieces from '@components/ShipmentPieces';
import ShipmentComments from '@components/ShipmentComments';
import ShipmentActions from '@components/ShipmentActions';
import { colors } from '@constants/theme';

// Header + the four tabs (Detalles / Piezas / Comentarios / Acciones), each with
// real content now.
export default function ShipmentDetails() {
  const { t } = useTranslation();
  const [selectedTab, setSelectedTab] = useState<ShipmentTab>('details');

  return (
    <View style={styles.container}>
      <ScreenHeader title={t('SHIPMENT_DETAILS.SHIPMENT_DETAILS')} />
      <ShipmentDetailsTabs style={styles.tabs} selectedTab={selectedTab} setSelectedTab={setSelectedTab} />

      {selectedTab === 'details' ? (
        <ShipmentDetail />
      ) : selectedTab === 'pieces' ? (
        <ShipmentPieces />
      ) : selectedTab === 'comments' ? (
        <ShipmentComments />
      ) : (
        <ShipmentActions setSelectedTab={setSelectedTab} />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, gap: 10, backgroundColor: colors.background },
  tabs: { marginHorizontal: 15 },
});
