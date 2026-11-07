import { useCallback, useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Modal, Pressable, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useFocusEffect, useLocalSearchParams, useRouter } from 'expo-router';
import { useQuery } from '@tanstack/react-query';
import { MaterialIcons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { Text, TextInput } from '@components/Text';
import ScreenHeader from '@components/ScreenHeader';
import CompanyChips from '@components/CompanyChips';
import { Badge, Button, Help, SumRow } from '@components/sales/ui';
import { useAccess } from '@hooks/access';
import { useAuth } from '@hooks/AuthContext';
import { useDebounced } from '@hooks/useDebounced';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { fetchSalesOrder, listSalesOrders, type SalesOrderRow } from '@services/salesApi';
import { MY_ORDER_STATUSES, money, myOrders, opensWizard, orderBadges, orderSummary, type MyOrderStatus } from '@utils/salesOrderRules';

// Lote 226 (P4 del pedido de vendedores) — PEDIDOS en la app del personal (Home → Pedidos, con sales.create): «Mis pedidos» (los que capturé yo, por estatus y
// con buscador por número, cliente o producto) y «Nuevo pedido». Un borrador se abre en el pedido donde se quedó; lo demás se mira en una hoja (líneas y totales).
// La lista es en vivo (sin señal no hay resultados). Ver la lista exige además sales.view (lo trae el rol Ventas).
export default function SalesOrders() {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const router = useRouter();
  const access = useAccess();
  const { user } = useAuth();
  const params = useLocalSearchParams<{ tenantId?: string }>();
  const companies = useMemo(() => access.companiesWith('sales.create'), [access]);
  const [tenantId, setTenantId] = useState<number | null>(params.tenantId ? Number(params.tenantId) : null);
  const company = companies.find((c) => c.tenantId === tenantId) ?? companies[0] ?? null;
  const canList = !!company && (company.permissions ?? []).includes('sales.view');
  const [status, setStatus] = useState<MyOrderStatus>('');
  const [text, setText] = useState('');
  const q = useDebounced(text.trim(), 500);
  const [openId, setOpenId] = useState<string | null>(null);

  const list = useQuery({
    queryKey: ['my-sales-orders', company?.tenantId ?? 0, status, q],
    queryFn: () => listSalesOrders(company!.tenantId, status, q),
    enabled: canList,
    staleTime: 15_000,
  });
  // al volver del pedido, la lista se actualiza
  useFocusEffect(useCallback(() => { if (canList) void list.refetch(); }, [canList])); // eslint-disable-line react-hooks/exhaustive-deps
  const mine = useMemo(() => myOrders(list.data ?? [], user), [list.data, user]);

  const open = (row: SalesOrderRow) => {
    if (opensWizard(row)) router.push({ pathname: '/(auth)/NewSalesOrder', params: { tenantId: String(company!.tenantId), order: row.publicId } });
    else setOpenId(row.publicId);
  };

  const header = (
    <View style={styles.head}>
      <Button label={t('SALES_ORDER.NEW')} onPress={() => router.push({ pathname: '/(auth)/NewSalesOrder', params: { tenantId: String(company?.tenantId ?? '') } })} disabled={!company} testID="sales-new" />
      {canList ? (
        <>
          <Text style={styles.title} accessibilityRole="header">{t('SALES_ORDER.MINE')}</Text>
          <View style={styles.statuses} accessibilityRole="tablist">
            {MY_ORDER_STATUSES.map((s) => {
              const on = s === status;
              return (
                <Pressable
                  key={s || 'ALL'}
                  onPress={() => setStatus(s)}
                  style={[styles.status, on ? { backgroundColor: colors.primary, borderColor: colors.primary } : null]}
                  accessibilityRole="tab"
                  accessibilityState={{ selected: on }}
                >
                  <Text style={[styles.statusText, { color: on ? colors.primaryContrast : colors.text }]}>{t(`SALES_ORDER.STATUS.${s || 'ALL'}`)}</Text>
                </Pressable>
              );
            })}
          </View>
          <View style={styles.searchWrap}>
            <MaterialIcons name="search" size={20} color={colors.muted} />
            <TextInput style={styles.search} value={text} onChangeText={setText} placeholder={t('SALES_ORDER.SEARCH')} accessibilityLabel={t('SALES_ORDER.SEARCH')} autoCapitalize="none" autoCorrect={false} returnKeyType="search" />
          </View>
          <Help>{t('SALES_ORDER.MINE_HELP')}</Help>
        </>
      ) : (
        <Help>{t('SALES_ORDER.NO_LIST')}</Help>
      )}
    </View>
  );

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={t('SALES_ORDER.TITLE')} />
      {companies.length > 1 ? <CompanyChips companies={companies} activeId={company?.tenantId} onPick={setTenantId} /> : null}
      <FlatList
        data={canList ? mine : []}
        keyExtractor={(r) => r.publicId}
        ListHeaderComponent={header}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.listBody}
        refreshControl={canList ? <RefreshControl refreshing={list.isRefetching} onRefresh={() => void list.refetch()} /> : undefined}
        ListEmptyComponent={
          !canList ? null : list.isPending ? (
            <ActivityIndicator color={colors.primary} />
          ) : list.isError ? (
            <Text style={styles.empty}>{t('SALES_ORDER.NO_SIGNAL')}</Text>
          ) : (
            <Text style={styles.empty}>{t('SALES_ORDER.NONE')}</Text>
          )
        }
        renderItem={({ item }) => (
          <Pressable style={styles.row} onPress={() => open(item)} accessibilityRole="button" accessibilityLabel={`${item.number}, ${item.clientName ?? ''}, ${item.statusLabel ?? item.status}, ${money(item.total)}`}>
            <View style={styles.rowTop}>
              <Text style={styles.number}>{item.number}</Text>
              <Text style={styles.statusLabel}>{item.statusLabel ?? item.status}</Text>
            </View>
            <Text style={styles.client}>{item.clientName ?? ''}</Text>
            <Text style={styles.sub}>{[item.requestedDate ?? '', t('SALES_ORDER.LINES', { n: item.lineCount ?? 0 }), money(item.total)].filter(Boolean).join(' · ')}</Text>
            {orderBadges(item).length > 0 ? (
              <View style={styles.badges}>
                {orderBadges(item).map((b) => <Badge key={b} tone={b === 'URGENT' || b === 'AWAITING' ? 'warn' : 'info'} label={t(`SALES_ORDER.BADGE.${b}`)} />)}
              </View>
            ) : null}
            {opensWizard(item) ? <Text style={styles.continue}>{t('SALES_ORDER.CONTINUE')}</Text> : null}
          </Pressable>
        )}
      />
      {company && openId ? <OrderSheet tenantId={company.tenantId} publicId={openId} onClose={() => setOpenId(null)} /> : null}
    </SafeAreaView>
  );
}

/** Una orden ya enviada, para mirarla: estatus, distintivos, líneas y totales (cambiarla se hace en la web). */
function OrderSheet({ tenantId, publicId, onClose }: { tenantId: number; publicId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const order = useQuery({ queryKey: ['sales-order', tenantId, publicId], queryFn: () => fetchSalesOrder(tenantId, publicId) });
  const o = order.data;
  const sum = orderSummary(o);
  return (
    <Modal visible animationType="slide" onRequestClose={onClose}>
      <SafeAreaView style={styles.container}>
        <ScrollView contentContainerStyle={styles.sheetBody}>
          {order.isPending ? <ActivityIndicator color={colors.primary} /> : order.isError || !o ? <Text style={styles.empty}>{t('SALES_ORDER.NO_SIGNAL')}</Text> : (
            <>
              <Text style={styles.sheetTitle} accessibilityRole="header">{o.number}</Text>
              <Text style={styles.client}>{o.clientName}</Text>
              <Text style={styles.statusLabel}>{o.statusLabel ?? o.status}</Text>
              <View style={styles.badges}>
                {o.terms?.isUrgent ? <Badge tone="warn" label={t('SALES_ORDER.BADGE.URGENT')} /> : null}
                {o.terms?.awaitingClientApproval ? <Badge tone="warn" label={t('SALES_ORDER.BADGE.AWAITING')} /> : null}
                {o.terms?.isConsignment ? <Badge label={t('SALES_ORDER.BADGE.CONSIGNMENT')} /> : null}
                {(o.terms?.deliveryMode ?? '').toUpperCase() === 'PICKUP' ? <Badge label={t('SALES_ORDER.BADGE.PICKUP')} /> : null}
              </View>
              {o.terms?.paymentMethodLabel ? <SumRow label={t('SALES_ORDER.CLIENT.PAYMENT')} value={[o.terms.paymentMethodLabel, o.terms.paymentTermLabel].filter(Boolean).join(' · ')} /> : null}
              {o.consigneeName ? <SumRow label={t('SALES_ORDER.DELIVERY.WHERE')} value={o.consigneeName} /> : null}
              {(o.lines ?? []).map((l) => (
                <View key={l.id ?? l.lineNo} style={styles.line}>
                  <View style={styles.flex}>
                    <Text style={styles.number}>{l.sku}</Text>
                    <Text style={styles.sub}>{l.productName}</Text>
                    {(l.qtyBackordered ?? 0) > 0 ? <Text style={styles.sub}>{t('SALES_ORDER.BACKORDERED', { n: l.qtyBackordered })}</Text> : null}
                  </View>
                  <Text style={styles.sub}>{`${l.qtyOrdered ?? 0} × ${money(l.unitPrice)}`}</Text>
                </View>
              ))}
              <View style={styles.sumBox}>
                <SumRow label={t('SALES_ORDER.SUMMARY.SUBTOTAL')} value={money(sum.subtotal)} />
                <SumRow label={t('SALES_ORDER.SUMMARY.TAX')} value={money(sum.tax)} />
                <SumRow label={t('SALES_ORDER.SUMMARY.SHIPPING')} value={sum.shipping > 0 ? money(sum.shipping) : t('SALES_ORDER.SUMMARY.NO_SHIPPING')} note={sum.reasonLabel ? t('SALES_ORDER.SUMMARY.WHY', { reason: sum.reasonLabel }) : null} />
                <SumRow label={t('SALES_ORDER.SUMMARY.TOTAL')} value={money(sum.total)} strong />
              </View>
            </>
          )}
        </ScrollView>
        <View style={styles.sheetFoot}>
          <Button label={t('SALES_ORDER.CLOSE')} onPress={onClose} />
        </View>
      </SafeAreaView>
    </Modal>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  flex: { flex: 1, minWidth: 0 },
  head: { gap: 10, paddingBottom: 6 },
  title: { fontSize: 17, fontWeight: '700', color: colors.text, marginTop: 6 },
  statuses: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  status: { borderWidth: 1, borderColor: colors.gray, borderRadius: 16, paddingHorizontal: 12, paddingVertical: 6, minHeight: 34, justifyContent: 'center' },
  statusText: { fontSize: 13, fontWeight: '600' },
  searchWrap: { flexDirection: 'row', alignItems: 'center', gap: 8, borderWidth: 1, borderColor: colors.gray, borderRadius: 12, paddingHorizontal: 12, minHeight: 46 },
  search: { flex: 1, fontSize: 16, paddingVertical: 8 },
  listBody: { paddingHorizontal: 20, paddingBottom: 24, gap: 10 },
  empty: { textAlign: 'center', marginTop: 16, color: colors.muted },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, padding: 14, gap: 4 },
  rowTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: 8, flexWrap: 'wrap' },
  number: { fontWeight: '700', fontSize: 15, color: colors.text },
  statusLabel: { fontWeight: '700', fontSize: 12, color: colors.primary },
  client: { fontWeight: '600', fontSize: 14, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginTop: 2 },
  continue: { fontSize: 13, fontWeight: '600', color: colors.primary, marginTop: 2 },
  sheetBody: { padding: 20, gap: 10 },
  sheetTitle: { fontSize: 22, fontWeight: '700', color: colors.text },
  line: { flexDirection: 'row', alignItems: 'center', gap: 10, borderBottomWidth: 1, borderBottomColor: colors.gray, paddingVertical: 8 },
  sumBox: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 12, backgroundColor: colors.backgroundSecondary, marginTop: 6 },
  sheetFoot: { padding: 16, borderTopWidth: 1, borderTopColor: colors.gray },
});
