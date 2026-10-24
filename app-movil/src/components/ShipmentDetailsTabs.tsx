import { StyleSheet, View, type ViewStyle } from 'react-native';
import { useTranslation } from 'react-i18next';
import Chip from './Chip';

// The four tabs of ShipmentDetails (Detalles / Piezas / Comentarios / Acciones).
export type ShipmentTab = 'details' | 'actions';

const TABS: Array<{ key: ShipmentTab; i18n: string }> = [
  { key: 'details', i18n: 'SHIPMENT_DETAILS.DETAILS' },
  { key: 'actions', i18n: 'SHIPMENT_DETAILS.ACTIONS' },
];

export default function ShipmentDetailsTabs({
  selectedTab,
  setSelectedTab,
  style,
}: {
  selectedTab: ShipmentTab;
  setSelectedTab: (tab: ShipmentTab) => void;
  style?: ViewStyle;
}) {
  const { t } = useTranslation();
  return (
    <View style={[styles.container, style]}>
      {TABS.map((tab) => (
        <Chip
          key={tab.key}
          label={t(tab.i18n)}
          active={selectedTab === tab.key}
          onPress={() => setSelectedTab(tab.key)}
        />
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-around',
    gap: 10,
  },
});
