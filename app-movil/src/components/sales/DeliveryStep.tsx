import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { fetchClientConsignees, type ClientForOrder, type SalesOrder } from '@services/salesApi';
import { addressLine, money, orderSummary, requiredDateValid, type DeliveryChoice, type WizardState } from '@utils/salesOrderRules';
import { lineStock, type StockInput } from '@utils/salesStockRules';
import { Text } from '@components/Text';
import StockInfo from './StockInfo';
import { Choice, Field, Help, SectionTitle, SumRow, SwitchRow } from './ui';

// Lote 226 — paso 3 del pedido en el teléfono: ENTREGA o RECOGIDO. Con entrega, a dónde (por omisión la dirección física del cliente; con locations.read se puede
// escoger otro consignatario del cliente). Resumen calculado por el servidor al guardar el borrador: subtotal, IVU, cargo de envío con su porqué y total.
// «En consignación» solo con esa forma de pago. Fecha requerida opcional (AAAA-MM-DD).
export default function DeliveryStep({ tenantId, state, onChange, onRecalc, client, saved, recalculating, canPickOther, stock, warehouseId }: {
  /** Lote 235: lo disponible y el almacén de la orden, para avisar en el resumen qué líneas quedan en backorder. */
  stock?: StockInput | null;
  warehouseId?: string | null;
  tenantId: number;
  state: WizardState;
  onChange: (next: (s: WizardState) => WizardState) => void;
  /** Cambió la entrega o el consignatario: hay que volver a calcular el envío (guarda el borrador). */
  onRecalc: (patch: Partial<WizardState>) => void;
  client: ClientForOrder | undefined;
  saved: SalesOrder | null;
  recalculating: boolean;
  canPickOther: boolean;
}) {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const physical = client?.physicalAddress?.isActive === false ? null : client?.physicalAddress ?? null;
  const others = useQuery({
    queryKey: ['sales-consignees', tenantId, state.clientPublicId ?? ''],
    queryFn: () => fetchClientConsignees(tenantId, state.clientPublicId!),
    enabled: canPickOther && !!state.clientPublicId && state.deliveryMode === 'DELIVERY',
    staleTime: 60_000,
  });
  const summary = orderSummary(saved);
  const mode = (m: DeliveryChoice) => onRecalc({ deliveryMode: m, consigneePublicId: m === 'DELIVERY' ? state.consigneePublicId ?? physical?.publicId ?? null : state.consigneePublicId });
  const otherList = (others.data ?? []).filter((c) => c.publicId !== physical?.publicId);
  const dateBad = !requiredDateValid(state.requiredDate);

  const short = stock && warehouseId ? state.cart.filter((l) => (lineStock(stock, l.productPublicId, warehouseId, l.quantity, l.components)?.backorder ?? 0) > 0) : [];

  return (
    <View style={styles.step}>
      <SectionTitle>{t('SALES_ORDER.DELIVERY.TITLE')}</SectionTitle>
      <View style={styles.block} accessibilityRole="radiogroup" accessibilityLabel={t('SALES_ORDER.DELIVERY.HOW')}>
        {(['DELIVERY', 'PICKUP'] as const).map((m) => (
          <Choice key={m} label={t(`SALES_ORDER.DELIVERY.${m}`)} help={t(`SALES_ORDER.DELIVERY.${m}_HELP`)} selected={state.deliveryMode === m} onPress={() => mode(m)} disabled={recalculating} />
        ))}
      </View>

      {state.deliveryMode === 'DELIVERY' ? (
        <View style={styles.block} accessibilityRole="radiogroup" accessibilityLabel={t('SALES_ORDER.DELIVERY.WHERE')}>
          <SectionTitle>{t('SALES_ORDER.DELIVERY.WHERE')}</SectionTitle>
          {physical?.publicId ? (
            <Choice label={t('SALES_ORDER.DELIVERY.PHYSICAL')} help={addressLine(physical)} selected={state.consigneePublicId === physical.publicId} onPress={() => onRecalc({ consigneePublicId: physical.publicId ?? null })} disabled={recalculating} />
          ) : (
            <Help>{t('SALES_ORDER.DELIVERY.NO_PHYSICAL')}</Help>
          )}
          {canPickOther && others.isFetching && !others.data ? <ActivityIndicator color={colors.primary} /> : null}
          {otherList.map((c) => (
            <Choice key={c.publicId} label={c.name} help={c.address} selected={state.consigneePublicId === c.publicId} onPress={() => onRecalc({ consigneePublicId: c.publicId })} disabled={recalculating} />
          ))}
        </View>
      ) : null}

      {state.paymentMethod === 'CONSIGNMENT' ? (
        <SwitchRow label={t('SALES_ORDER.DELIVERY.CONSIGNMENT')} value={state.isConsignment} onChange={(v) => onChange((s) => ({ ...s, isConsignment: v }))} />
      ) : null}

      <Field
        label={t('SALES_ORDER.DELIVERY.REQUIRED_DATE')}
        value={state.requiredDate}
        onChangeText={(v) => onChange((s) => ({ ...s, requiredDate: v }))}
        placeholder={t('SALES_ORDER.DELIVERY.DATE_PLACEHOLDER')}
        keyboardType="numbers-and-punctuation"
        maxLength={10}
        help={t('SALES_ORDER.DELIVERY.REQUIRED_DATE_HELP')}
        error={dateBad ? t('SALES_ORDER.ERRORS.requiredDate') : null}
      />

      <View style={styles.summary} accessibilityLabel={t('SALES_ORDER.SUMMARY.TITLE')}>
        <View style={styles.inline}>
          <SectionTitle>{t('SALES_ORDER.SUMMARY.TITLE')}</SectionTitle>
          {recalculating ? <ActivityIndicator color={colors.primary} accessibilityLabel={t('SALES_ORDER.SUMMARY.CALCULATING')} /> : null}
        </View>
        <SumRow label={t('SALES_ORDER.SUMMARY.SUBTOTAL')} value={money(summary.subtotal)} />
        <SumRow label={t('SALES_ORDER.SUMMARY.TAX')} value={money(summary.tax)} />
        <SumRow
          label={t('SALES_ORDER.SUMMARY.SHIPPING')}
          value={summary.shipping > 0 ? money(summary.shipping) : t('SALES_ORDER.SUMMARY.NO_SHIPPING')}
          note={summary.reasonLabel ? t('SALES_ORDER.SUMMARY.WHY', { reason: summary.reasonLabel }) : null}
        />
        <SumRow label={t('SALES_ORDER.SUMMARY.TOTAL')} value={money(summary.total)} strong />
        <Help>{t('SALES_ORDER.SUMMARY.HELP')}</Help>
        {/* Lote 235: lo que pasa de lo disponible queda marcado (no bloquea): se puede enviar y eso queda en backorder */}
        {short.map((l) => (
          <View key={l.productPublicId} style={styles.shortLine}>
            <Text style={styles.shortName}>{`${l.sku} · ${l.name}`}</Text>
            <StockInfo stock={stock} warehouseId={warehouseId} productPublicId={l.productPublicId} quantity={l.quantity} pieces={l.components} onlyBackorder />
          </View>
        ))}
        {short.length > 0 ? <Help>{t('SALES_ORDER.STOCK.BACKORDER_HELP')}</Help> : null}
      </View>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  step: { gap: 14 },
  block: { gap: 8 },
  inline: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  shortLine: { marginTop: 6, borderWidth: 1, borderColor: colors.danger, borderRadius: 10, padding: 8, gap: 2 },
  shortName: { fontSize: 13, fontWeight: '700', color: colors.text },
  summary: { gap: 2, borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 12, backgroundColor: colors.backgroundSecondary },
});
