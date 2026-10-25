import { useEffect, useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import OrderSearchBar from '@components/OrderSearchBar';
import CompanyChips from '@components/CompanyChips';
import { useAccess } from '@hooks/access';
import { useAppStore } from '@/stores';
import { searchOrders } from '@services/staffApi';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import type { Shipment } from '@/types/models';

// Lote 214 — Órdenes para el personal: buscar cualquier orden activa de la compañía (número, conduce, empaque, factura o escaneo de la etiqueta) y abrir su ficha,
// la misma del chofer (mapa, teléfono como enlace, «hablar sobre este envío») con las acciones de completar si tiene orders.deliver.
// La búsqueda es en vivo contra el servidor (sin señal no hay resultados: una orden cualquiera no se puede tener bajada de antemano).
export default function Orders() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const access = useAccess();
  const companies = useMemo(() => access.companiesWith('orders.view'), [access]);
  const [tenantId, setTenantId] = useState<number | null>(null);
  const company = companies.find((c) => c.tenantId === tenantId) ?? companies[0] ?? null;
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  const addShipment = useAppStore((s) => s.addShipment);

  // Se busca medio segundo después de dejar de escribir (o al instante al escanear).
  useEffect(() => {
    const id = setTimeout(() => setQuery(text.trim()), 500);
    return () => clearTimeout(id);
  }, [text]);

  const { data, isFetching, isError } = useQuery({
    queryKey: ['staff-orders', company?.tenantId ?? 0, query],
    queryFn: () => searchOrders({ tenantId: company!.tenantId, name: company!.name }, query),
    enabled: !!company && query.length > 0,
    staleTime: 30_000,
  });

  const open = (s: Shipment) => {
    addShipment(s);
    router.push('/(auth)/ShipmentDetails');
  };

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('STAFF_ORDERS.TITLE')} />
      {companies.length > 1 ? <CompanyChips companies={companies} activeId={company?.tenantId} onPick={setTenantId} /> : null}
      <OrderSearchBar value={text} onChange={setText} />
      <View style={styles.content}>
        {!query ? (
          <Text style={styles.empty}>{t('STAFF_ORDERS.HINT')}</Text>
        ) : isFetching && !data ? (
          <ActivityIndicator color={colors.primary} />
        ) : isError ? (
          <Text style={styles.empty}>{t('STAFF_ORDERS.ERROR')}</Text>
        ) : (data?.items.length ?? 0) === 0 ? (
          <Text style={styles.empty}>{t('ORDERS.NO_RESULTS')}</Text>
        ) : (
          <FlatList
            data={data?.items ?? []}
            keyExtractor={(item) => item.id}
            keyboardShouldPersistTaps="handled"
            ListHeaderComponent={data?.matchedBy ? <Text style={styles.matched}>{t('STAFF_ORDERS.EXACT_MATCH')}</Text> : null}
            renderItem={({ item }) => (
              <Pressable style={styles.row} onPress={() => open(item)} accessibilityRole="button">
                <View style={styles.rowTop}>
                  <Text style={styles.orderNo}>{item.orderNumber}</Text>
                  <Text style={styles.status}>{item.status}</Text>
                </View>
                <Text style={styles.name}>{item.consigneeName ?? ''}</Text>
                <Text style={styles.sub}>{[item.city, item.waybill ? `${t('SHIPMENT_DETAILS.WAYBILL')} ${item.waybill}` : null].filter(Boolean).join(' · ')}</Text>
                <Text style={styles.sub}>{`${t('COMMON.PIECES')}: ${item.pieces ?? ''}${item.codAmount ? ` · COD ${item.codAmount.toFixed(2)}` : ''}`}</Text>
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
  matched: { marginHorizontal: 20, marginTop: 6, color: colors.muted, fontSize: 12 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 6, marginHorizontal: 20, padding: 14, gap: 4 },
  rowTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  orderNo: { fontWeight: '700', fontSize: 16, color: colors.text },
  status: { fontWeight: '700', fontSize: 12, color: colors.primary },
  name: { fontWeight: '600', fontSize: 14, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
});
