import { useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import OptionSelect from '@components/OptionSelect';
import { fetchStreetTargets, streetAssign, streetQuickRoute, streetUnassign, type StreetResult, type StreetTrip } from '@services/streetApi';
import { buttonFor, distanceLabel, minutesSince } from '@utils/streetRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

function errorText(e: unknown, fallback: string): string {
  const title = (e as { response?: { data?: { title?: string } } })?.response?.data?.title;
  return title && title.length < 300 ? title : fallback;
}

// Lote 215 — Asignar en la calle (trips.streetassign): a qué ruta mandar la orden. Las rutas abiertas de hoy del mismo tipo, las más cercanas al domicilio primero (con la distancia y la última ubicación del chofer), «sacar de la ruta»
// y la ruta rápida (chofer + vehículo). Al chofer se le avisa. Necesita señal: lo que se manda se aplica al instante en el servidor.
export default function StreetAssign() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const qc = useQueryClient();
  const p = useLocalSearchParams<{ tenantId: string; orderId: string }>();
  const tenantId = Number(p.tenantId);
  const [busy, setBusy] = useState(false);
  const [driverIdx, setDriverIdx] = useState<number | null>(null);
  const [vehicleIdx, setVehicleIdx] = useState<number | null>(null);

  const { data, isPending, isError, error } = useQuery({
    queryKey: ['street-targets', tenantId, p.orderId],
    queryFn: () => fetchStreetTargets(tenantId, p.orderId),
    enabled: !!p.orderId && Number.isFinite(tenantId),
    staleTime: 0,
  });

  const done = (r: StreetResult) => {
    void qc.invalidateQueries({ queryKey: ['staff-trips'] });
    void qc.invalidateQueries({ queryKey: ['staff-trip'] });
    void qc.invalidateQueries({ queryKey: ['shipment'] });
    void qc.invalidateQueries({ queryKey: ['street-targets'] });
    const key = r.action === 'MOVED' ? 'MOVED' : r.action === 'UNASSIGNED' ? 'UNASSIGNED' : r.action === 'QUICK_ROUTE' ? 'QUICK' : 'ASSIGNED';
    Alert.alert(t('STREET.DONE_TITLE'), `${t(`STREET.DONE_${key}`, { order: r.orderNumber ?? '', trip: r.tripCode ?? '' })}${r.driverNotified ? `\n${t('STREET.DRIVER_NOTIFIED')}` : ''}`, [{ text: 'OK', onPress: () => router.back() }]);
  };

  const run = async (fn: () => Promise<StreetResult>) => {
    setBusy(true);
    try {
      done(await fn());
    } catch (e) {
      Alert.alert(t('STREET.ERROR_TITLE'), errorText(e, t('STREET.ERROR')));
    } finally {
      setBusy(false);
    }
  };

  const confirm = (message: string, action: () => Promise<StreetResult>) =>
    Alert.alert(t('STREET.CONFIRM_TITLE'), message, [{ text: t('COMMON.CANCEL'), style: 'cancel' }, { text: t('STREET.CONFIRM_OK'), onPress: () => void run(action) }]);

  const hasTrip = !!data?.currentTripPublicId;

  const renderTrip = ({ item }: { item: StreetTrip }) => {
    const btn = buttonFor(item.isCurrent, hasTrip);
    const dist = distanceLabel(item.distanceKm);
    const ago = minutesSince(item.driverPingAtUtc);
    return (
      <View style={styles.row}>
        <View style={styles.top}>
          <Text style={styles.code}>{item.code}</Text>
          <Text style={styles.status}>{item.status}</Text>
        </View>
        <Text style={styles.name}>{item.driverName ?? t('TRIPS_TODAY.NO_DRIVER')}{item.vehicleCode ? ` · ${item.vehicleCode}` : ''}</Text>
        <Text style={styles.sub}>
          {[t('TRIPS_TODAY.STOPS', { n: item.stopCount }), dist ? t('STREET.DISTANCE', { d: dist }) : null, ago != null ? t('TRIPS_TODAY.LAST_PING', { m: ago }) : null].filter(Boolean).join(' · ')}
        </Text>
        {btn === 'current' ? (
          <Text style={styles.here}>{t('STREET.IS_HERE')}</Text>
        ) : (
          <Pressable
            style={[styles.btn, (busy || !data?.movable) && styles.disabled]}
            disabled={busy || !data?.movable}
            accessibilityRole="button"
            onPress={() => confirm(t(btn === 'move' ? 'STREET.CONFIRM_MOVE' : 'STREET.CONFIRM_ASSIGN', { order: data!.orderNumber, trip: item.code }), () => streetAssign(tenantId, data!.orderPublicId, item.publicId))}
          >
            <Text style={styles.btnText}>{t(btn === 'move' ? 'STREET.MOVE_HERE' : 'STREET.SEND_HERE')}</Text>
          </Pressable>
        )}
      </View>
    );
  };

  const footer = data ? (
    <View style={styles.footer}>
      {hasTrip ? (
        <Pressable
          style={[styles.btnOutline, (busy || !data.movable) && styles.disabled]}
          disabled={busy || !data.movable}
          accessibilityRole="button"
          onPress={() => confirm(t('STREET.CONFIRM_UNASSIGN', { order: data.orderNumber, trip: data.currentTripCode ?? '' }), () => streetUnassign(tenantId, data.orderPublicId))}
        >
          <Text style={styles.btnOutlineText}>{t('STREET.UNASSIGN')}</Text>
        </Pressable>
      ) : (
        <View style={styles.quick}>
          <Text style={styles.sectionTitle}>{t('STREET.QUICK_TITLE')}</Text>
          <Text style={styles.sub}>{t('STREET.QUICK_HINT')}</Text>
          <OptionSelect options={data.drivers.map((d, i) => ({ id: i + 1, label: d.hasOpenTrip ? `${d.name} · ${t('STREET.HAS_ROUTE')}` : d.name }))} value={driverIdx} placeholder={t('STREET.PICK_DRIVER')} onSelect={setDriverIdx} onClear={() => setDriverIdx(null)} />
          <OptionSelect options={data.vehicles.map((v, i) => ({ id: i + 1, label: v.plate ? `${v.code} · ${v.plate}` : v.code }))} value={vehicleIdx} placeholder={t('STREET.PICK_VEHICLE')} onSelect={setVehicleIdx} onClear={() => setVehicleIdx(null)} />
          <Pressable
            style={[styles.btn, (busy || !data.movable || driverIdx == null || vehicleIdx == null) && styles.disabled]}
            disabled={busy || !data.movable || driverIdx == null || vehicleIdx == null}
            accessibilityRole="button"
            onPress={() => {
              const d = data.drivers[(driverIdx as number) - 1];
              const v = data.vehicles[(vehicleIdx as number) - 1];
              confirm(t('STREET.CONFIRM_QUICK', { order: data.orderNumber, driver: d.name }), () => streetQuickRoute(tenantId, [data.orderPublicId], d.publicId, v.publicId));
            }}
          >
            <Text style={styles.btnText}>{t('STREET.QUICK_GO')}</Text>
          </Pressable>
        </View>
      )}
    </View>
  ) : null;

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('STREET.TITLE')} />
      {isPending ? (
        <ActivityIndicator color={colors.primary} />
      ) : isError || !data ? (
        <Text style={styles.empty}>{errorText(error, t('STAFF_ORDERS.ERROR'))}</Text>
      ) : (
        <FlatList
          data={data.trips}
          keyExtractor={(r) => r.publicId}
          renderItem={renderTrip}
          ListHeaderComponent={
            <View style={styles.head}>
              <Text style={styles.title}>{data.orderNumber}</Text>
              <Text style={styles.sub}>{`${data.kind === 'PICKUP' ? t('TRIPS_TODAY.PICKUP') : t('TRIPS_TODAY.DELIVERY')} · ${data.status}`}</Text>
              <Text style={styles.sub}>{data.currentTripCode ? t('STREET.IN_ROUTE', { trip: data.currentTripCode }) : t('STREET.NO_ROUTE')}</Text>
              {!data.movable && data.reason ? <Text style={styles.warn}>{data.reason}</Text> : null}
              <Text style={styles.sectionTitle}>{t('STREET.ROUTES_TITLE')}</Text>
            </View>
          }
          ListEmptyComponent={<Text style={styles.empty}>{t('STREET.NO_ROUTES')}</Text>}
          ListFooterComponent={footer}
        />
      )}
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  head: { marginHorizontal: 20, marginBottom: 6, gap: 2 },
  title: { fontWeight: '700', fontSize: 18, color: colors.text },
  sectionTitle: { fontWeight: '700', fontSize: 14, color: colors.primary, marginTop: 10 },
  warn: { color: colors.text, fontWeight: '600', marginTop: 6 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 6, marginHorizontal: 20, padding: 14, gap: 4 },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  code: { fontWeight: '700', fontSize: 16, color: colors.text },
  status: { fontWeight: '700', fontSize: 12, color: colors.primary },
  name: { fontWeight: '600', fontSize: 14, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
  here: { marginTop: 6, fontWeight: '700', color: colors.primary },
  btn: { marginTop: 8, backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 12, alignItems: 'center' },
  btnText: { color: colors.primaryContrast, fontWeight: '700' },
  btnOutline: { borderWidth: 1, borderColor: colors.primary, borderRadius: 12, paddingVertical: 12, alignItems: 'center' },
  btnOutlineText: { color: colors.primary, fontWeight: '700' },
  disabled: { opacity: 0.4 },
  footer: { marginHorizontal: 20, marginVertical: 14, gap: 8 },
  quick: { gap: 8 },
});
