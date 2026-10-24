import { useMemo } from 'react';
import { ActivityIndicator, FlatList, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import OrderListItem from '@components/OrderListItem';
import OrderSearchBar from '@components/OrderSearchBar';
import { useDriverWork } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { filterShipments } from '@utils/shipmentSearch';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Los envíos que dejó elegidos la pantalla anterior (Entregas/Recogidas para hoy o un manifiesto), de cualquier compañía. El buscador también barre
// la compañía (nombre o número).
export default function OrdersList() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();

  const shipmentIds = useAppStore((s) => s.shipmentIds);
  const filters = useAppStore((s) => s.orderFilters);
  const setOrderFilters = useAppStore((s) => s.setOrderFilters);

  const { data, isPending } = useDriverWork();
  const idSet = useMemo(() => new Set(shipmentIds), [shipmentIds]);
  const cached = useMemo(
    () => [...(data?.shipments ?? [])].filter((s) => idSet.has(s.id)).sort((a, b) => a.companyName.localeCompare(b.companyName) || a.manifestCode.localeCompare(b.manifestCode) || a.sequence - b.sequence),
    [data, idSet],
  );
  const visible = useMemo(() => filterShipments(cached, filters.search), [cached, filters.search]);

  const title = `${t('ORDERS.ORDERS')} (${visible.length}/${shipmentIds.length})`;
  const emptyMessage = () => (filters.search.trim() ? t('ORDERS.NO_RESULTS') : t('ORDERS.EMPTY'));

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={title} />
      <OrderSearchBar value={filters.search} onChange={(text) => setOrderFilters({ search: text })} />
      <View style={styles.content}>
        {isPending ? (
          <ActivityIndicator color={colors.primary} />
        ) : visible.length === 0 ? (
          <Text style={styles.empty}>{emptyMessage()}</Text>
        ) : (
          <FlatList data={visible} keyExtractor={(item) => item.id} renderItem={({ item }) => <OrderListItem {...item} />} keyboardShouldPersistTaps="handled" />
        )}
      </View>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1, paddingHorizontal: 2 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
});
