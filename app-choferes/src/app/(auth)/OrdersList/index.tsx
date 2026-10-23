import { useMemo } from 'react';
import { ActivityIndicator, FlatList, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import ScreenHeader from '@components/ScreenHeader';
import OrderListItem from '@components/OrderListItem';
import OrderSearchBar from '@components/OrderSearchBar';
import OrderFiltersPanel, { isValidDate } from '@components/OrderFiltersPanel';
import { useMe, useMySenders, useDriverShipments } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { filterShipments } from '@utils/shipmentSearch';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Una sola fuente de datos para TODOS: el fetch bulk (useDriverShipments) ya
// bajó, en una llamada, todas las órdenes abiertas del usuario — sean las
// suyas (chofer) o las de todos sus senders + las suyas (oficina, UsersSender).
// El buscador y el panel de sender/fecha/ciudad filtran ESE array EN MEMORIA;
// no hay ninguna razón para volver a pedirle nada al servidor — la lista ya
// está descargada y offline.
export default function OrdersList() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();

  const shipmentIds = useAppStore((s) => s.shipmentIds);
  const filters = useAppStore((s) => s.orderFilters);
  const setOrderFilters = useAppStore((s) => s.setOrderFilters);

  const { data: senders } = useMySenders();
  const canFilter = (senders?.length ?? 0) > 0;

  const { data: me } = useMe();
  const { data: allShipments, isPending } = useDriverShipments(me?.userID);
  const idSet = useMemo(() => new Set(shipmentIds), [shipmentIds]);
  const cached = useMemo(
    () => (allShipments ?? []).filter((s) => idSet.has(s.shipmentID)),
    [allShipments, idSet],
  );

  // Sender: el picker trabaja con SenderID (Customers.SenderID); el shipment
  // solo trae el código de sender (Shipments.Sender). Se traduce el conjunto
  // elegido a códigos antes de filtrar.
  const senderCodes = useMemo(() => {
    if (filters.senderIds.length === 0) return null;
    return new Set(
      (senders ?? [])
        .filter((s) => filters.senderIds.includes(s.senderID))
        .map((s) => s.sender)
        .filter((code): code is string => Boolean(code)),
    );
  }, [filters.senderIds, senders]);

  const dateFromTs = useMemo(() => {
    if (!filters.dateFrom || !isValidDate(filters.dateFrom)) return null;
    const t = new Date(`${filters.dateFrom}T00:00:00`).getTime();
    return Number.isNaN(t) ? null : t;
  }, [filters.dateFrom]);
  const dateToTs = useMemo(() => {
    if (!filters.dateTo || !isValidDate(filters.dateTo)) return null;
    const t = new Date(`${filters.dateTo}T23:59:59.999`).getTime();
    return Number.isNaN(t) ? null : t;
  }, [filters.dateTo]);
  const cityFilter = filters.city.trim().toLowerCase();

  const hasActiveFilter = Boolean(senderCodes || dateFromTs !== null || dateToTs !== null || cityFilter);

  const filtered = useMemo(() => {
    let list = cached;
    if (senderCodes) list = list.filter((s) => s.sender && senderCodes.has(s.sender));
    if (dateFromTs !== null || dateToTs !== null) {
      list = list.filter((s) => {
        if (!s.readyDate) return false;
        const t = new Date(s.readyDate).getTime();
        if (Number.isNaN(t)) return false;
        if (dateFromTs !== null && t < dateFromTs) return false;
        if (dateToTs !== null && t > dateToTs) return false;
        return true;
      });
    }
    if (cityFilter) list = list.filter((s) => (s.city ?? '').trim().toLowerCase() === cityFilter);
    return list;
  }, [cached, senderCodes, dateFromTs, dateToTs, cityFilter]);

  const visible = useMemo(() => filterShipments(filtered, filters.search), [filtered, filters.search]);

  const title = `${t('ORDERS.ORDERS')} (${visible.length}/${shipmentIds.length})`;

  const emptyMessage = () =>
    filters.search.trim() || hasActiveFilter ? t('ORDERS.NO_RESULTS') : t('ORDERS.EMPTY');

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={title} />
      <OrderSearchBar value={filters.search} onChange={(text) => setOrderFilters({ search: text })} />
      {canFilter && (
        <OrderFiltersPanel senders={senders ?? []} filters={filters} onChange={setOrderFilters} />
      )}
      <View style={styles.content}>
        {isPending ? (
          <ActivityIndicator color={colors.primary} />
        ) : visible.length === 0 ? (
          <Text style={styles.empty}>{emptyMessage()}</Text>
        ) : (
          <FlatList
            data={visible}
            keyExtractor={(item) => String(item.shipmentID)}
            renderItem={({ item }) => <OrderListItem {...item} />}
            keyboardShouldPersistTaps="handled"
          />
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
