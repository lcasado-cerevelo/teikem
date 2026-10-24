import { useMemo } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import { useAccess } from '@hooks/access';
import { fetchTodayTrips, type TripRow } from '@services/streetApi';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 215 — Rutas de hoy para el personal (trips.view): las rutas del día de cada compañía, con su chofer, vehículo, estatus y paradas. En vivo (sin señal no hay lista).
interface Row extends TripRow { tenantId: number; companyName: string }

export default function Trips() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const access = useAccess();
  const companies = useMemo(() => access.companiesWith('trips.view'), [access]);

  const { data, isPending, isError, refetch, isRefetching } = useQuery({
    queryKey: ['staff-trips', companies.map((c) => c.tenantId).join(',')],
    queryFn: async (): Promise<Row[]> => {
      const results = await Promise.all(companies.map(async (c) => (await fetchTodayTrips({ tenantId: c.tenantId, name: c.name })).map((r) => ({ ...r, tenantId: c.tenantId, companyName: c.name }))));
      return results.flat().sort((a, b) => a.companyName.localeCompare(b.companyName) || a.code.localeCompare(b.code));
    },
    enabled: companies.length > 0,
    staleTime: 30_000,
  });

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('TRIPS_TODAY.TITLE')} />
      <View style={styles.content}>
        {isPending ? (
          <ActivityIndicator color={colors.primary} />
        ) : isError ? (
          <Text style={styles.empty}>{t('STAFF_ORDERS.ERROR')}</Text>
        ) : (data?.length ?? 0) === 0 ? (
          <Text style={styles.empty}>{t('TRIPS_TODAY.EMPTY')}</Text>
        ) : (
          <FlatList
            data={data}
            keyExtractor={(r) => `${r.tenantId}-${r.publicId}`}
            refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={() => void refetch()} />}
            renderItem={({ item }) => (
              <Pressable
                style={styles.row}
                accessibilityRole="button"
                onPress={() => router.push({ pathname: '/(auth)/TripDetails', params: { tenantId: String(item.tenantId), tripId: item.publicId, companyName: item.companyName } })}
              >
                <View style={styles.top}>
                  <Text style={styles.code}>{item.code}</Text>
                  <Text style={styles.status}>{item.status}</Text>
                </View>
                <Text style={styles.name}>{item.driverName ?? t('TRIPS_TODAY.NO_DRIVER')}{item.vehicleCode ? ` · ${item.vehicleCode}` : ''}</Text>
                <Text style={styles.sub}>
                  {[companies.length > 1 ? item.companyName : null, item.kind === 'PICKUP' ? t('TRIPS_TODAY.PICKUP') : t('TRIPS_TODAY.DELIVERY'), t('TRIPS_TODAY.STOPS', { n: item.stopCount })].filter(Boolean).join(' · ')}
                </Text>
              </Pressable>
            )}
          />
        )}
      </View>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 6, marginHorizontal: 20, padding: 14, gap: 4 },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  code: { fontWeight: '700', fontSize: 16, color: colors.text },
  status: { fontWeight: '700', fontSize: 12, color: colors.primary },
  name: { fontWeight: '600', fontSize: 14, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
});
