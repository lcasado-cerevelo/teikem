import { useCallback, useMemo } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, Text } from 'react-native';
import { Link, type Href } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useMe, useDriverShipments } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { colors } from '@constants/theme';

interface HomeItem {
  key: string;
  label: string;
  counter: string;
  loading: boolean;
  href: Href | null;
  onPress?: () => void;
}

// A shipment is a PICKUP for this driver when it's still "Created" and the
// driver is the one assigned to pick it up (AssignPk). Deliveries are the rest
// of the driver's open shipments. The driver query already returns both
// (AssignPk OR AssignDL), so we split them client-side.
const isCreated = (s?: string | null) => (s ?? '').trim().toLowerCase() === 'created';

// Home: 4 cards driven by the driver's open shipments (single fetch).
//  Entregas para hoy      -> all open deliveries.
//  Manifesto de entrega   -> # distinct delivery manifests (manifestDL).
//  Recogidas para hoy     -> shipments Created + AssignPk = driver.
//  Manifesto de recogida  -> # distinct pickup manifests (manifestPk).
export default function Home() {
  const { t } = useTranslation();
  const { data: me } = useMe();
  const driverId = me?.userID;

  const { data: shipmentsData, isPending } = useDriverShipments(driverId);
  const shipments = shipmentsData ?? [];

  const addShipmentIds = useAppStore((s) => s.addShipmentIds);

  const deliveryIds = useMemo(() => shipments.map((s) => s.shipmentID), [shipments]);
  const totalDeliveries = shipments.length;
  const manifestCount = useMemo(
    () => new Set(shipments.map((s) => s.manifestDL ?? '—')).size,
    [shipments],
  );

  // Pickups = Created + assigned to this driver for pickup.
  const pickups = useMemo(
    () => shipments.filter((s) => isCreated(s.status) && s.assignPK === driverId),
    [shipments, driverId],
  );
  const pickupIds = useMemo(() => pickups.map((s) => s.shipmentID), [pickups]);
  const pickupManifestCount = useMemo(
    () => new Set(pickups.map((s) => s.manifestPk ?? '—')).size,
    [pickups],
  );

  const loading = Boolean(driverId) && isPending;

  const items: HomeItem[] = [
    {
      key: 'deliveries',
      label: t('HOME.DELIVERIES_FOR_TODAY'),
      counter: `${totalDeliveries}`,
      loading,
      href: '/(auth)/OrdersList',
      onPress: () => addShipmentIds(deliveryIds),
    },
    {
      key: 'manifest',
      label: t('HOME.DELIVERY_MANIFEST'),
      counter: `${manifestCount}`,
      loading,
      href: '/(auth)/(tabs)/manifests',
    },
    {
      key: 'pickups',
      label: t('HOME.PICKUPS_FOR_TODAY'),
      counter: `${pickups.length}`,
      loading,
      href: '/(auth)/OrdersList',
      onPress: () => addShipmentIds(pickupIds),
    },
    {
      key: 'pickup-manifest',
      label: t('HOME.PICKUP_MANIFEST'),
      counter: `${pickupManifestCount}`,
      loading,
      href: { pathname: '/(auth)/(tabs)/manifests', params: { kind: 'pickup' } },
    },
  ];

  const renderItem = useCallback(
    ({ item }: { item: HomeItem }) => {
      const content = (
        <Pressable
          style={StyleSheet.flatten([styles.item, !item.href && styles.itemDisabled])}
          disabled={!item.href}
          onPress={item.onPress}
        >
          <Text style={styles.description}>{item.label}</Text>
          {item.loading ? <ActivityIndicator /> : <Text style={styles.counter}>{item.counter}</Text>}
        </Pressable>
      );
      return item.href ? (
        <Link href={item.href} asChild>
          {content}
        </Link>
      ) : (
        content
      );
    },
    [],
  );

  return (
    <FlatList
      style={styles.container}
      data={items}
      keyExtractor={(item) => item.key}
      renderItem={renderItem}
    />
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  item: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginVertical: 10,
    marginHorizontal: 20,
    paddingVertical: 20,
    paddingHorizontal: 10,
    borderColor: colors.gray,
    borderWidth: 1,
    borderRadius: 13,
  },
  itemDisabled: { opacity: 0.4 },
  description: { fontSize: 16, color: colors.text },
  counter: { fontSize: 24, color: colors.text },
});
