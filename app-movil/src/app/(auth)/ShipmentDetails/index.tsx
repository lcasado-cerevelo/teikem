import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import ShipmentDetailsTabs, { type ShipmentTab } from '@components/ShipmentDetailsTabs';
import ShipmentDetail from '@components/ShipmentDetail';
import ShipmentActions from '@components/ShipmentActions';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Header + the four tabs (Detalles / Piezas / Comentarios / Acciones), each with
// real content now.
export default function ShipmentDetails() {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [selectedTab, setSelectedTab] = useState<ShipmentTab>('details');

  return (
    <View style={styles.container}>
      <ScreenHeader title={t('SHIPMENT_DETAILS.SHIPMENT_DETAILS')} />
      <ShipmentDetailsTabs style={styles.tabs} selectedTab={selectedTab} setSelectedTab={setSelectedTab} />

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
});
