import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { useDebounced } from '@hooks/useDebounced';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { searchClientsForOrder, type ClientForOrder, type LookupOption } from '@services/salesApi';
import { isCreditTerm, NOTES_MAX, withPaymentMethod, type WizardState } from '@utils/salesOrderRules';
import NewClientModal from './NewClientModal';
import { Badge, Button, Choice, Field, Help, SectionTitle, SwitchRow } from './ui';

// Lote 226 — paso 1 del pedido en el teléfono: el CLIENTE (buscador por número, nombre o teléfono; «Cliente nuevo» con el alta mínima), Urgente con su nota y la
// forma de pago del pedido, prellenada con la del cliente y editable (si cambia, queda la marca). El almacén solo se pide si la compañía tiene más de uno.
export default function ClientStep({ tenantId, state, onChange, onPickClient, client, clientLoading, paymentChanged, warehouses, canCreateClient, methods, terms, onClientCreated }: {
  tenantId: number;
  state: WizardState;
  onChange: (next: (s: WizardState) => WizardState) => void;
  onPickClient: (publicId: string | null, label: string) => void;
  client: ClientForOrder | undefined;
  clientLoading: boolean;
  paymentChanged: boolean;
  /** Almacenes activos: solo se muestran si hay más de uno. */
  warehouses: LookupOption[];
  canCreateClient: boolean;
  methods: LookupOption[];
  terms: LookupOption[];
  onClientCreated: (client: ClientForOrder) => void;
}) {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const [q, setQ] = useState('');
  const [creating, setCreating] = useState(false);
  const search = useDebounced(q.trim(), 400);
  const results = useQuery({
    queryKey: ['sales-client-search', tenantId, search],
    queryFn: () => searchClientsForOrder(tenantId, search),
    enabled: search.length > 0,
    staleTime: 30_000,
  });
  const isProspect = (client?.status ?? '').toUpperCase() === 'PROSPECT';
  const creditTerms = terms.filter((o) => isCreditTerm(o.code));

  return (
    <View style={styles.step}>
      <SectionTitle>{t('SALES_ORDER.CLIENT.TITLE')}</SectionTitle>
      {state.clientPublicId ? (
        <View style={styles.picked} accessibilityLiveRegion="polite">
          <View style={styles.flex}>
            <Text style={styles.pickedName}>{client?.name ?? state.clientLabel}</Text>
            <Text style={styles.meta}>{[client?.code ? t('SALES_ORDER.CLIENT.NUMBER', { code: client.code }) : '', client?.physicalAddress?.city ?? ''].filter(Boolean).join(' · ')}</Text>
            {isProspect ? <Badge tone="warn" label={t('SALES_ORDER.CLIENT.PROSPECT')} /> : null}
          </View>
          {clientLoading ? <ActivityIndicator color={colors.primary} /> : null}
          <Button kind="outline" label={t('SALES_ORDER.CLIENT.CHANGE')} onPress={() => onPickClient(null, '')} />
        </View>
      ) : (
        <View style={styles.block}>
          <Field label={t('SALES_ORDER.CLIENT.SEARCH')} value={q} onChangeText={setQ} placeholder={t('SALES_ORDER.CLIENT.SEARCH_PLACEHOLDER')} autoCapitalize="none" autoCorrect={false} returnKeyType="search" help={t('SALES_ORDER.CLIENT.SEARCH_HELP')} />
          {search !== '' && results.isFetching && !results.data ? <ActivityIndicator color={colors.primary} /> : null}
          {results.isError ? <Text style={styles.err}>{t('SALES_ORDER.NO_SIGNAL')}</Text> : null}
          {search !== '' && results.data && results.data.length === 0 ? <Help>{t('SALES_ORDER.CLIENT.NONE')}</Help> : null}
          {(results.data ?? []).map((c) => (
            <Pressable key={c.publicId} style={styles.row} onPress={() => onPickClient(c.publicId, `${c.code} · ${c.name}`)} accessibilityRole="button" accessibilityLabel={`${c.name}, ${t('SALES_ORDER.CLIENT.NUMBER', { code: c.code })}`}>
              <Text style={styles.rowName}>{c.name}</Text>
              <Text style={styles.meta}>
                {t('SALES_ORDER.CLIENT.NUMBER', { code: c.code })}
                {(c.status ?? '').toUpperCase() === 'PROSPECT' ? ` · ${t('SALES_ORDER.CLIENT.PROSPECT')}` : ''}
              </Text>
            </Pressable>
          ))}
          {canCreateClient ? <Button kind="outline" label={t('SALES_ORDER.CLIENT.NEW')} onPress={() => setCreating(true)} /> : null}
        </View>
      )}

      {warehouses.length > 1 ? (
        <View style={styles.block}>
          <SectionTitle>{t('SALES_ORDER.CLIENT.WAREHOUSE')}</SectionTitle>
          {warehouses.map((w) => (
            <Choice key={w.code} label={w.label} selected={state.warehousePublicId === w.code} onPress={() => onChange((s) => ({ ...s, warehousePublicId: w.code }))} />
          ))}
          <Help>{t('SALES_ORDER.CLIENT.WAREHOUSE_HELP')}</Help>
        </View>
      ) : null}

      <View style={styles.block}>
        <SectionTitle>{t('SALES_ORDER.CLIENT.ORDER_DATA')}</SectionTitle>
        <SwitchRow label={t('SALES_ORDER.CLIENT.URGENT')} help={t('SALES_ORDER.CLIENT.URGENT_HELP')} value={state.isUrgent} onChange={(v) => onChange((s) => ({ ...s, isUrgent: v }))} />
        <Field
          label={t('SALES_ORDER.CLIENT.NOTES')}
          value={state.notes}
          onChangeText={(v) => onChange((s) => ({ ...s, notes: v }))}
          multiline
          maxLength={NOTES_MAX}
          help={t('SALES_ORDER.CLIENT.NOTES_COUNT', { n: state.notes.length, max: NOTES_MAX })}
        />
      </View>

      <View style={styles.block}>
        <SectionTitle>{t('SALES_ORDER.CLIENT.PAYMENT')}</SectionTitle>
        {paymentChanged ? (
          <View style={styles.changed}>
            <Badge tone="warn" label={t('SALES_ORDER.CLIENT.PAYMENT_CHANGED')} />
            <Help>{t('SALES_ORDER.CLIENT.PAYMENT_CHANGED_HELP')}</Help>
          </View>
        ) : null}
        {methods.map((m) => (
          <Choice key={m.code} label={m.label} selected={state.paymentMethod === m.code} onPress={() => onChange((s) => withPaymentMethod(s, m.code))} />
        ))}
        {state.paymentMethod === 'CREDIT' ? (
          <View style={styles.block}>
            <Text style={styles.subTitle}>{t('SALES_ORDER.CLIENT.TERM')}</Text>
            {creditTerms.map((o) => (
              <Choice key={o.code} label={o.label} selected={state.paymentTerm === o.code} onPress={() => onChange((s) => ({ ...s, paymentTerm: o.code }))} />
            ))}
          </View>
        ) : null}
      </View>

      <NewClientModal
        visible={creating}
        tenantId={tenantId}
        terms={terms}
        onClose={() => setCreating(false)}
        onCreated={(c) => {
          setCreating(false);
          onClientCreated(c);
        }}
      />
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  step: { gap: 14 },
  block: { gap: 8 },
  flex: { flex: 1, minWidth: 0, gap: 4 },
  picked: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: 10, borderWidth: 1, borderColor: colors.primary, borderRadius: 12, padding: 12 },
  pickedName: { fontSize: 16, fontWeight: '700', color: colors.text },
  meta: { fontSize: 12, color: colors.muted },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 12, gap: 2 },
  rowName: { fontSize: 15, fontWeight: '600', color: colors.text },
  err: { color: colors.danger },
  changed: { gap: 4 },
  subTitle: { fontSize: 14, fontWeight: '600', color: colors.text, marginTop: 4 },
});
