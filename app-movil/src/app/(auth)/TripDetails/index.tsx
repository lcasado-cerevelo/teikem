import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import { fetchTripDetail } from '@services/streetApi';
import { useAppStore } from '@/stores';
import { minutesSince, stopToShipment } from '@utils/streetRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 215 — una ruta de hoy: su chofer, la última ubicación que mandó y sus paradas en orden. Tocar una parada abre la ficha de la orden (de ahí se completa o se asigna).
export default function TripDetails() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const addShipment = useAppStore((s) => s.addShipment);
  const p = useLocalSearchParams<{ tenantId: string; tripId: string; companyName?: string }>();
  const company = { tenantId: Number(p.tenantId), name: p.companyName ?? '' };

  const { data, isPending, isError } = useQuery({
    queryKey: ['staff-trip', company.tenantId, p.tripId],
    queryFn: () => fetchTripDetail(company, p.tripId),
    enabled: !!p.tripId && Number.isFinite(company.tenantId),
    staleTime: 15_000,
  });
  const ago = minutesSince(data?.lastPing?.capturedAtUtc);

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={data?.code ?? t('TRIPS_TODAY.TITLE')} />
      {isPending ? (
        <ActivityIndicator color={colors.primary} />
      ) : isError || !data ? (
        <Text style={styles.empty}>{t('STAFF_ORDERS.ERROR')}</Text>
      ) : (
        <FlatList
          data={data.stops}
          keyExtractor={(s) => s.orderPublicId + s.sequence}
          ListHeaderComponent={
            <View style={styles.head}>
              <Text style={styles.title}>{data.driverName ?? t('TRIPS_TODAY.NO_DRIVER')}{data.vehicleCode ? ` · ${data.vehicleCode}` : ''}</Text>
              <Text style={styles.sub}>{`${data.status} · ${data.kind === 'PICKUP' ? t('TRIPS_TODAY.PICKUP') : t('TRIPS_TODAY.DELIVERY')} · ${t('TRIPS_TODAY.STOPS', { n: data.stopCount })}`}</Text>
              {ago != null ? <Text style={styles.sub}>{t('TRIPS_TODAY.LAST_PING', { m: ago })}</Text> : null}
            </View>
          }
          ListEmptyComponent={<Text style={styles.empty}>{t('TRIPS_TODAY.NO_STOPS')}</Text>}
          renderItem={({ item }) => (
            <Pressable
              style={styles.row}
              accessibilityRole="button"
              onPress={() => {
                addShipment(stopToShipment(company, data.kind, data.code, item));
                router.push('/(auth)/ShipmentDetails');
              }}
            >
              <Text style={styles.seq}>{item.sequence}</Text>
              <View style={styles.body}>
                <Text style={styles.name}>{item.consigneeName ?? item.orderNumber}</Text>
                <Text style={styles.sub}>{[item.line1, item.city].filter(Boolean).join(', ')}</Text>
                <Text style={styles.sub}>{`${item.orderNumber} · ${item.status}`}</Text>
              </View>
            </Pressable>
          )}
        />
      )}
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  head: { marginHorizontal: 20, marginBottom: 8, gap: 2 },
  title: { fontWeight: '700', fontSize: 16, color: colors.text },
  row: { flexDirection: 'row', gap: 12, borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 5, marginHorizontal: 20, padding: 12, alignItems: 'center' },
  seq: { fontWeight: '700', fontSize: 18, color: colors.primary, minWidth: 26, textAlign: 'center' },
  body: { flex: 1, gap: 2 },
  name: { fontWeight: '600', fontSize: 14, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
});
