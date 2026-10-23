import { useCallback, useMemo } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet } from 'react-native';
import { Text } from '@components/Text';
import { Link, type Href } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { useDriverWork } from '@hooks/queries';
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
  const items = shown.length > 0 ? shown : allItems;

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
  description: { fontSize: 16, color: colors.text },
  counter: { fontSize: 24, color: colors.text },
});
