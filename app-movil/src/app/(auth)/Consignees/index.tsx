import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import LookupShell, { InfoLine } from '@components/LookupShell';
import { useAccess } from '@hooks/access';
import { useDebounced } from '@hooks/useDebounced';
import { fetchConsignee, searchConsignees, type ConsigneeRow } from '@services/lookupApi';
import { contactHref, contactText, mapsUrl, usableContacts } from '@utils/lookupRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 217 — Consignatarios (locations.read) en lectura: dirección con «cómo llegar», notas de acceso y entrega, y teléfonos/correos que se tocan. Sin señal no hay resultado.
export default function Consignees() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const access = useAccess();
  const companies = useMemo(() => access.companiesWith('locations.read'), [access]);
  const [tenantId, setTenantId] = useState<number | null>(null);
  const company = companies.find((c) => c.tenantId === tenantId) ?? companies[0] ?? null;
  const [text, setText] = useState('');
  const query = useDebounced(text.trim());
  const [picked, setPicked] = useState<ConsigneeRow | null>(null);
  const co = company ? { tenantId: company.tenantId, name: company.name } : null;

  const list = useQuery({ queryKey: ['lookup-locations', company?.tenantId ?? 0, query], queryFn: () => searchConsignees(co!, query), enabled: !!co && !picked && query.length > 0, staleTime: 30_000 });
  const ficha = useQuery({ queryKey: ['lookup-location', company?.tenantId ?? 0, picked?.publicId ?? ''], queryFn: () => fetchConsignee(co!, picked!), enabled: !!co && !!picked, staleTime: 30_000 });

  return (
    <LookupShell title={t('LOOKUP.CONSIGNEES')} companies={companies} tenantId={company?.tenantId ?? null} onTenant={(id) => { setTenantId(id); setPicked(null); }} text={text} onText={(v) => { setText(v); setPicked(null); }} placeholder={t('LOOKUP.CONSIGNEES_SEARCH')}>
      {picked ? (
        <ScrollView contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
          <Pressable onPress={() => setPicked(null)} accessibilityRole="button"><Text style={styles.back}>‹ {t('LOOKUP.BACK')}</Text></Pressable>
          {ficha.isPending ? <ActivityIndicator color={colors.primary} /> : ficha.isError || !ficha.data ? <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text> : (
            <>
              <Text style={styles.title}>{ficha.data.name}</Text>
              {ficha.data.clientName ? <InfoLine label={t('LOOKUP.CLIENT')} value={ficha.data.clientName} /> : null}
              {ficha.data.address ? <InfoLine label={t('LOOKUP.ADDRESS')} value={ficha.data.address} href={mapsUrl(ficha.data.address)} /> : null}
              {ficha.data.contactName ? <InfoLine label={t('LOOKUP.CONTACT')} value={ficha.data.contactName} /> : null}
              {usableContacts(ficha.data.points).map((p, i) => <InfoLine key={i} value={contactText(p, (p as { contactTypeLabel?: string }).contactTypeLabel)} href={contactHref(p)} />)}
              {ficha.data.accessNotes ? <View style={styles.card}><Text style={styles.cardTitle}>{t('LOOKUP.ACCESS_NOTES')}</Text><Text style={styles.note}>{ficha.data.accessNotes}</Text></View> : null}
              {ficha.data.deliveryNotes ? <View style={styles.card}><Text style={styles.cardTitle}>{t('LOOKUP.DELIVERY_NOTES')}</Text><Text style={styles.note}>{ficha.data.deliveryNotes}</Text></View> : null}
            </>
          )}
        </ScrollView>
      ) : query.length === 0 ? (
        <Text style={styles.empty}>{t('LOOKUP.TYPE_TO_SEARCH')}</Text>
      ) : list.isPending ? (
        <ActivityIndicator color={colors.primary} />
      ) : list.isError ? (
        <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text>
      ) : (list.data?.length ?? 0) === 0 ? (
        <Text style={styles.empty}>{t('LOOKUP.NONE')}</Text>
      ) : (
        <FlatList
          data={list.data}
          keyExtractor={(r) => r.publicId}
          keyboardShouldPersistTaps="handled"
          renderItem={({ item }) => (
            <Pressable style={styles.row} onPress={() => setPicked(item)} accessibilityRole="button">
              <Text style={styles.name}>{item.name}</Text>
              <Text style={styles.sub}>{[item.clientName, item.address].filter(Boolean).join(' · ')}</Text>
            </Pressable>
          )}
        />
      )}
    </LookupShell>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  body: { paddingHorizontal: 20, paddingBottom: 30 },
  back: { color: colors.primary, fontWeight: '700', paddingVertical: 8 },
  title: { fontSize: 20, fontWeight: '700', color: colors.text, marginBottom: 4 },
  card: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginTop: 12, padding: 12, gap: 4 },
  cardTitle: { fontWeight: '700', color: colors.muted, fontSize: 12 },
  note: { color: colors.text, fontSize: 14 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 6, marginHorizontal: 20, padding: 14, gap: 4 },
  name: { fontWeight: '700', fontSize: 15, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
});
