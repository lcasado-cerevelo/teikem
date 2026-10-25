import { useCallback, useMemo } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet } from 'react-native';
import { Text } from '@components/Text';
import { Link, type Href } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useDriverWork } from '@hooks/queries';
import { useAccess } from '@hooks/access';
import AttentionCard from '@components/home/AttentionCard';
import PulseCard from '@components/home/PulseCard';
import TrackingBanner from '@components/TrackingBanner';
import { useAppStore } from '@/stores';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

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
// Home: 4 cards driven by the driver's open shipments (single fetch).
//  Entregas para hoy      -> all open deliveries.
//  Manifesto de entrega   -> # distinct delivery manifests (manifestDL).
//  Recogidas para hoy     -> shipments Created + AssignPk = driver.
//  Manifesto de recogida  -> # distinct pickup manifests (manifestPk).
export default function Home() {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const access = useAccess();
  // Lote 213: el inicio del personal (atención y pulso) va arriba; lo del chofer (sus tarjetas) debajo si además es chofer. El usuario «solo chofer» ve solo lo suyo.
  const { data, isPending } = useDriverWork();
  const shipments = data?.shipments ?? [];
  const manifests = data?.manifests ?? [];

  const addShipmentIds = useAppStore((s) => s.addShipmentIds);

  // Recogidas primero, luego entregas; un botón sin contenido no se muestra. Todo junto, de todas las compañías del chofer.
  const pickups = useMemo(() => shipments.filter((s) => s.kind === 'PICKUP'), [shipments]);
  const deliveries = useMemo(() => shipments.filter((s) => s.kind === 'DELIVERY'), [shipments]);
  const pickupManifests = useMemo(() => manifests.filter((m) => m.kind === 'PICKUP').length, [manifests]);
  const deliveryManifests = useMemo(() => manifests.filter((m) => m.kind === 'DELIVERY').length, [manifests]);

  const loading = isPending;

  const allItems: HomeItem[] = [
    {
      key: 'pickups',
      label: t('HOME.PICKUPS_FOR_TODAY'),
      counter: `${pickups.length}`,
      loading,
      href: '/(auth)/OrdersList',
      onPress: () => addShipmentIds(pickups.map((s) => s.id)),
    },
    {
      key: 'pickup-manifest',
      label: t('HOME.PICKUP_MANIFEST'),
      counter: `${pickupManifests}`,
      loading,
      href: { pathname: '/(auth)/(tabs)/manifests', params: { kind: 'pickup' } },
    },
    {
      key: 'deliveries',
      label: t('HOME.DELIVERIES_FOR_TODAY'),
      counter: `${deliveries.length}`,
      loading,
      href: '/(auth)/OrdersList',
      onPress: () => addShipmentIds(deliveries.map((s) => s.id)),
    },
    {
      key: 'manifest',
      label: t('HOME.DELIVERY_MANIFEST'),
      counter: `${deliveryManifests}`,
      loading,
      href: '/(auth)/(tabs)/manifests',
    },
  ];
  const shown = allItems.filter((it) => loading || it.counter !== '0');
  const driverItems = access.isDriver || access.driverOnly || !access.staff ? (shown.length > 0 ? shown : allItems) : [];
  // Lote 214: lo del personal son accesos (sin contador): Órdenes con orders.view. Después, su trabajo de chofer si también lo es.
  const staffItems: HomeItem[] = [
    ...(access.staff && access.can('orders.view') ? [{ key: 'staff-orders', label: t('STAFF_ORDERS.TITLE'), counter: '›', loading: false, href: '/(auth)/Orders' as Href }] : []),
    ...(access.staff && access.can('trips.scan') ? [{ key: 'staff-scan', label: t('SCAN.TITLE'), counter: '›', loading: false, href: '/(auth)/Scan' as Href }] : []),
    ...(access.staff && access.can('trips.view') ? [{ key: 'staff-trips', label: t('TRIPS_TODAY.TITLE'), counter: '›', loading: false, href: '/(auth)/Trips' as Href }] : []),
    ...(access.staff && access.can('clients.read') ? [{ key: 'staff-clients', label: t('LOOKUP.CLIENTS'), counter: '›', loading: false, href: '/(auth)/Clients' as Href }] : []),
    ...(access.staff && access.can('locations.read') ? [{ key: 'staff-consignees', label: t('LOOKUP.CONSIGNEES'), counter: '›', loading: false, href: '/(auth)/Consignees' as Href }] : []),
    ...(access.staff && access.can('inventory.view') ? [{ key: 'staff-stock', label: t('LOOKUP.STOCK'), counter: '›', loading: false, href: '/(auth)/Stock' as Href }] : []),
  ];
  const items = [...staffItems, ...driverItems];
  const header = (
    <>
      <TrackingBanner />
      {access.staff && access.can('pulse.attention') ? <AttentionCard multi={access.companies.length > 1} /> : null}
      {access.staff && access.can('analytics.view') ? <PulseCard multi={access.companies.length > 1} /> : null}
    </>
  );

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
      const linked = item.href ? (
        <Link href={item.href} asChild>
          {content}
        </Link>
      ) : (
        content
      );
      // El rótulo «Mi trabajo de chofer» va antes de la primera tarjeta del chofer cuando también hay accesos del personal.
      const firstDriver = staffItems.length > 0 && driverItems[0]?.key === item.key;
      return firstDriver ? (
        <>
          <Text style={styles.section}>{t('HOME.MY_DRIVER_WORK')}</Text>
          {linked}
        </>
      ) : (
        linked
      );
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [staffItems.length, driverItems[0]?.key, styles, t],
  );

  return (
    <FlatList
      ListHeaderComponent={header}
      style={styles.container}
      data={items}
      keyExtractor={(item) => item.key}
      renderItem={renderItem}
      extraData={styles}
    />
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
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
  section: { marginHorizontal: 20, marginTop: 12, fontWeight: '700', color: colors.muted },
  description: { fontSize: 16, color: colors.text },
  counter: { fontSize: 24, color: colors.text },
});
