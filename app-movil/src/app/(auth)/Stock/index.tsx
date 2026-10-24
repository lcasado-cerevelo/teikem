import { useMemo, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import LookupShell, { InfoLine } from '@components/LookupShell';
import { useAccess } from '@hooks/access';
import { useDebounced } from '@hooks/useDebounced';
import { fetchBinContent, fetchStock, productByCode, searchBins, searchProducts, type BinHit, type ProductRow } from '@services/lookupApi';
import { groupStock, looksLikeCode, qty } from '@utils/lookupRules';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 217 — Almacén, solo consulta (inventory.view): buscar un producto (o leer su código de barras) y ver cuánto hay en cada almacén y posición; o buscar una posición y ver qué tiene.
// No mueve nada y no muestra costos, precios ni valores. Sin señal no hay resultado.
type Mode = 'PRODUCT' | 'BIN';

export default function Stock() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const access = useAccess();
  const companies = useMemo(() => access.companiesWith('inventory.view'), [access]);
  const [tenantId, setTenantId] = useState<number | null>(null);
  const company = companies.find((c) => c.tenantId === tenantId) ?? companies[0] ?? null;
  const [mode, setMode] = useState<Mode>('PRODUCT');
  const [text, setText] = useState('');
  const query = useDebounced(text.trim());
  const [product, setProduct] = useState<ProductRow | null>(null);
  const [bin, setBin] = useState<BinHit | null>(null);
  const co = company ? { tenantId: company.tenantId, name: company.name } : null;
  const tid = company?.tenantId ?? 0;

  const products = useQuery({
    queryKey: ['lookup-products', tid, query],
    queryFn: async () => {
      // un código leído o tecleado sin espacios se prueba primero exacto (barras o SKU); si no hay, se busca por texto
      if (looksLikeCode(query)) {
        const exact = await productByCode(co!, query);
        if (exact) return [exact];
      }
      return searchProducts(co!, query);
    },
    enabled: !!co && mode === 'PRODUCT' && !product && query.length > 0, staleTime: 30_000,
  });
  const stock = useQuery({ queryKey: ['lookup-stock', tid, product?.publicId ?? ''], queryFn: async () => groupStock(await fetchStock(co!, product!.publicId)), enabled: !!co && !!product, staleTime: 15_000 });
  const bins = useQuery({ queryKey: ['lookup-bins', tid, query], queryFn: () => searchBins(co!, query), enabled: !!co && mode === 'BIN' && !bin && query.length > 0, staleTime: 30_000 });
  const content = useQuery({ queryKey: ['lookup-bin', tid, bin?.id ?? 0], queryFn: () => fetchBinContent(co!, bin!), enabled: !!co && !!bin, staleTime: 15_000 });

  const reset = () => { setProduct(null); setBin(null); };
  const changeText = (v: string) => { setText(v); reset(); };

  return (
    <LookupShell title={t('LOOKUP.STOCK')} companies={companies} tenantId={company?.tenantId ?? null} onTenant={(id) => { setTenantId(id); reset(); }} text={text} onText={changeText} placeholder={mode === 'PRODUCT' ? t('LOOKUP.PRODUCT_SEARCH') : t('LOOKUP.BIN_SEARCH')} scan>
      <View style={styles.modes}>
        {(['PRODUCT', 'BIN'] as Mode[]).map((m) => (
          <Pressable key={m} style={[styles.mode, mode === m && styles.modeOn]} onPress={() => { setMode(m); reset(); }} accessibilityRole="button" accessibilityState={{ selected: mode === m }}>
            <Text style={[styles.modeText, mode === m && styles.modeTextOn]}>{t(m === 'PRODUCT' ? 'LOOKUP.BY_PRODUCT' : 'LOOKUP.BY_BIN')}</Text>
          </Pressable>
        ))}
      </View>
      {product ? (
        <ScrollView contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
          <Pressable onPress={() => setProduct(null)} accessibilityRole="button"><Text style={styles.back}>‹ {t('LOOKUP.BACK')}</Text></Pressable>
          <Text style={styles.title}>{product.name}</Text>
          <InfoLine label="SKU" value={product.sku} />
          {product.barcode ? <InfoLine label={t('LOOKUP.BARCODE')} value={product.barcode} /> : null}
          {product.owner ? <InfoLine label={t('LOOKUP.OWNER')} value={product.owner} /> : null}
          {product.category ? <InfoLine label={t('LOOKUP.CATEGORY')} value={product.category} /> : null}
          {product.brand ? <InfoLine label={t('LOOKUP.BRAND')} value={product.brand} /> : null}
          <Text style={styles.section}>{t('LOOKUP.TOTALS', { onHand: qty(product.onHand), available: qty(product.available) })}</Text>
          {stock.isPending ? <ActivityIndicator color={colors.primary} /> : stock.isError ? <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text> : (stock.data?.length ?? 0) === 0 ? <Text style={styles.empty}>{t('LOOKUP.NO_STOCK')}</Text> : stock.data!.map((w) => (
            <View key={w.warehouseCode} style={styles.card}>
              <Text style={styles.name}>{w.warehouseCode} · {t('LOOKUP.ON_HAND_AVAILABLE', { onHand: qty(w.onHand), available: qty(w.available) })}</Text>
              {w.bins.map((b, i) => <Text key={i} style={styles.sub}>{[b.binCode ?? t('LOOKUP.NO_BIN'), b.lotNumber ? `${t('LOOKUP.LOT')} ${b.lotNumber}` : null].filter(Boolean).join(' · ')}: {t('LOOKUP.ON_HAND_AVAILABLE', { onHand: qty(b.onHand), available: qty(b.available) })}</Text>)}
            </View>
          ))}
        </ScrollView>
      ) : bin ? (
        <ScrollView contentContainerStyle={styles.body} keyboardShouldPersistTaps="handled">
          <Pressable onPress={() => setBin(null)} accessibilityRole="button"><Text style={styles.back}>‹ {t('LOOKUP.BACK')}</Text></Pressable>
          <Text style={styles.title}>{bin.code}</Text>
          <InfoLine label={t('LOOKUP.WAREHOUSE')} value={bin.warehouseCode} />
          {bin.zone ? <InfoLine label={t('LOOKUP.ZONE')} value={bin.zone} /> : null}
          {content.isPending ? <ActivityIndicator color={colors.primary} /> : content.isError ? <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text> : (content.data?.length ?? 0) === 0 ? <Text style={styles.empty}>{t('LOOKUP.BIN_EMPTY')}</Text> : content.data!.map((p) => (
            <View key={p.productPublicId} style={styles.card}><Text style={styles.name}>{p.name}</Text><Text style={styles.sub}>SKU {p.sku}</Text></View>
          ))}
        </ScrollView>
      ) : query.length === 0 ? (
        <Text style={styles.empty}>{t('LOOKUP.TYPE_TO_SEARCH')}</Text>
      ) : mode === 'PRODUCT' ? (
        products.isPending ? <ActivityIndicator color={colors.primary} /> : products.isError ? <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text> : (products.data?.length ?? 0) === 0 ? <Text style={styles.empty}>{t('LOOKUP.NONE')}</Text> : (
          <FlatList data={products.data} keyExtractor={(r) => r.publicId} keyboardShouldPersistTaps="handled" renderItem={({ item }) => (
            <Pressable style={styles.row} onPress={() => setProduct(item)} accessibilityRole="button">
              <Text style={styles.name}>{item.name}</Text>
              <Text style={styles.sub}>{[`SKU ${item.sku}`, item.owner].filter(Boolean).join(' · ')}</Text>
              <Text style={styles.sub}>{t('LOOKUP.ON_HAND_AVAILABLE', { onHand: qty(item.onHand), available: qty(item.available) })}</Text>
            </Pressable>
          )} />
        )
      ) : bins.isPending ? <ActivityIndicator color={colors.primary} /> : bins.isError ? <Text style={styles.empty}>{t('LOOKUP.ERROR')}</Text> : (bins.data?.length ?? 0) === 0 ? <Text style={styles.empty}>{t('LOOKUP.NONE')}</Text> : (
        <FlatList data={bins.data} keyExtractor={(r) => String(r.id)} keyboardShouldPersistTaps="handled" renderItem={({ item }) => (
          <Pressable style={styles.row} onPress={() => setBin(item)} accessibilityRole="button">
            <Text style={styles.name}>{item.code}</Text>
            <Text style={styles.sub}>{[item.warehouseCode, item.zone].filter(Boolean).join(' · ')}</Text>
          </Pressable>
        )} />
      )}
    </LookupShell>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  modes: { flexDirection: 'row', gap: 8, paddingHorizontal: 20, paddingBottom: 6 },
  mode: { flex: 1, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingVertical: 8, alignItems: 'center' },
  modeOn: { backgroundColor: colors.primary, borderColor: colors.primary },
  modeText: { color: colors.text, fontWeight: '600' },
  modeTextOn: { color: colors.primaryContrast },
  body: { paddingHorizontal: 20, paddingBottom: 30 },
  back: { color: colors.primary, fontWeight: '700', paddingVertical: 8 },
  title: { fontSize: 20, fontWeight: '700', color: colors.text, marginBottom: 4 },
  section: { fontWeight: '700', color: colors.text, marginTop: 14, marginBottom: 4, fontSize: 15 },
  card: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginTop: 8, padding: 12, gap: 4 },
  empty: { textAlign: 'center', marginTop: 24, color: colors.muted, paddingHorizontal: 20 },
  row: { borderWidth: 1, borderColor: colors.gray, borderRadius: 13, marginVertical: 6, marginHorizontal: 20, padding: 14, gap: 4 },
  name: { fontWeight: '700', fontSize: 15, color: colors.text },
  sub: { fontSize: 12, color: colors.muted },
});
