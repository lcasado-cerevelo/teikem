import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import LookupShell, { InfoLine } from '@components/LookupShell';
import { useAccess } from '@hooks/access';
import { useDebounced } from '@hooks/useDebounced';
import { fetchClient, searchClients, type ClientRow } from '@services/lookupApi';
import { mapsUrl } from '@utils/lookupRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 217 — Clientes en lectura (clients.read): buscar y ver la ficha con sus contactos y direcciones como enlaces. Sin crédito, identificación fiscal, pago ni contratos. Sin señal no hay resultado.
export default function Clients() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const access = useAccess();
  const companies = useMemo(() => access.companiesWith('clients.read'), [access]);
  const [tenantId, setTenantId] = useState<number | null>(null);
  const company = companies.find((c) => c.tenantId === tenantId) ?? companies[0] ?? null;
  const [text, setText] = useState('');
  const query = useDebounced(text.trim());
  const [picked, setPicked] = useState<ClientRow | null>(null);
  const co = company ? { tenantId: company.tenantId, name: company.name } : null;

  const list = useQuery({ queryKey: ['lookup-clients', company?.tenantId ?? 0, query], queryFn: () => searchClients(co!, query), enabled: !!co && !picked, staleTime: 30_000 });
  const ficha = useQuery({ queryKey: ['lookup-client', company?.tenantId ?? 0, picked?.publicId ?? ''], queryFn: () => fetchClient(co!, picked!.publicId), enabled: !!co && !!picked, staleTime: 30_000 });

  return (
    <LookupShell title={t('LOOKUP.CLIENTS')} companies={companies} tenantId={company?.tenantId ?? null} onTenant={(id) => { setTenantId(id); setPicked(null); }} text={text} onText={(v) => { setText(v); setPicked(null); }} placeholder={t('LOOKUP.CLIENTS_SEARCH')}>
      {picked ? (
        <ScrollView contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
          <Pressable onPress={() => setPicked(null)} accessibilityRole="button"><Text style={styles.back}>‹ {t('LOOKUP.BACK')}</Text></Pressable>
          {ficha.isPending ? <ActivityIndicator color={colors.primary} /> : ficha.isError || !ficha.data ? <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text> : (
            <>
              <Text style={styles.title}>{ficha.data.name}</Text>
              <InfoLine label={t('LOOKUP.CODE')} value={ficha.data.code} />
              {ficha.data.legalName ? <InfoLine label={t('LOOKUP.LEGAL_NAME')} value={ficha.data.legalName} /> : null}
              <InfoLine label={t('LOOKUP.STATUS')} value={ficha.data.status} />
              {ficha.data.address ? <InfoLine label={t('LOOKUP.ADDRESS')} value={ficha.data.address} href={mapsUrl(ficha.data.address)} /> : null}
              {ficha.data.pickupAddress ? <InfoLine label={t('LOOKUP.PICKUP')} value={ficha.data.pickupAddress} href={mapsUrl(ficha.data.pickupAddress)} /> : null}
              {ficha.data.points.map((p, i) => <InfoLine key={`p${i}`} value={p.text} href={p.href} />)}
              {ficha.data.contacts.length > 0 ? <Text style={styles.section}>{t('LOOKUP.CONTACTS')}</Text> : null}
              {ficha.data.contacts.map((c, i) => (
                <View key={`c${i}`} style={styles.card}>
                  <Text style={styles.name}>{c.name}{c.role ? ` · ${c.role}` : ''}</Text>
                  {c.points.map((p, j) => <InfoLine key={j} value={p.text} href={p.href} />)}
                </View>
              ))}
            </>
          )}
        </ScrollView>
      ) : list.isPending && !!co ? (
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
              <Text style={styles.sub}>{[item.code, item.status].join(' · ')}</Text>
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
  section: { fontWeight: '700', color: colors.text, marginTop: 16, fontSize: 16 },
  card: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginTop: 8, padding: 12 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 6, marginHorizontal: 20, padding: 14, gap: 4 },
  name: { fontWeight: '700', fontSize: 15, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
});
