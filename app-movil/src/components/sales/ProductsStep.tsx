import { useMemo, useState } from 'react';
import { ActivityIndicator, Image, Modal, Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Text, TextInput } from '@components/Text';
import { getAccessToken } from '@config/axios';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { fetchCatalog, fileSource, type SalesCatalogBrand, type SalesCatalogItem } from '@services/salesApi';
import {
  cartLineAmount, cartQuantity, cartSummary, money, removeFromCart, setCartQuantity, stepCartQuantity, type WizardState,
} from '@utils/salesOrderRules';
import type { StockInput } from '@utils/salesStockRules';
import StockInfo from './StockInfo';
import { Badge, Button, Help, SectionTitle } from './ui';

// Lote 226 — paso 2 del pedido en el teléfono: los PRODUCTOS. Pestañas por marca (solo marcas con productos activos; los sin marca al final); arriba de cada marca
// sus ofertas vigentes como tarjetas con foto, precio y + / −; debajo la lista completa de la marca (SKU, nombre, precio de la lista del cliente o la general) con
// su casilla de cantidad: teclear una cantidad agrega la línea. El carrito (`CartBar`) va fijo abajo; tocarlo muestra las líneas para corregir o quitar.
// Lote 235: cada renglón (lista, oferta y carrito) dice «Disponible: N» en el almacén de la orden, marca el backorder si se pide de más (no bloquea) y la nota de
// otros almacenes (`StockInfo`).
// El vendedor no teclea precios. Los kits ajustables se piden con las piezas del catálogo (ajustarlas se hace en la web).

const NO_BRAND = '__none__';
const brandKey = (b: SalesCatalogBrand) => b.brand ?? NO_BRAND;
const matches = (q: string, ...texts: (string | null | undefined)[]) => {
  const n = q.trim().toLocaleLowerCase();
  return n === '' || texts.some((x) => (x ?? '').toLocaleLowerCase().includes(n));
};

export default function ProductsStep({ tenantId, state, onChange, stock, warehouseId }: { tenantId: number; state: WizardState; onChange: (next: (s: WizardState) => WizardState) => void; stock?: StockInput | null; warehouseId?: string | null }) {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const catalog = useQuery({
    queryKey: ['sales-catalog', tenantId, state.clientPublicId ?? ''],
    queryFn: () => fetchCatalog(tenantId, state.clientPublicId!),
    enabled: !!state.clientPublicId,
    staleTime: 60_000,
  });
  const brands = useMemo(() => (catalog.data?.brands ?? []).filter((b) => b.offers.length + b.products.length > 0), [catalog.data]);
  const [tab, setTab] = useState('');
  const [q, setQ] = useState('');
  const current = brands.find((b) => brandKey(b) === tab) ?? brands[0];
  const brandName = (b: SalesCatalogBrand | undefined) => b?.brand ?? t('SALES_ORDER.PRODUCTS.NO_BRAND');
  const products = useMemo(() => (current?.products ?? []).filter((p) => matches(q, p.sku, p.name)), [current, q]);

  const set = (item: SalesCatalogItem, text: string) => onChange((s) => ({ ...s, cart: setCartQuantity(s.cart, item, text) }));
  const step = (item: SalesCatalogItem, delta: number) => onChange((s) => ({ ...s, cart: stepCartQuantity(s.cart, item, delta) }));

  if (catalog.isError) return <Text style={styles.err}>{t('SALES_ORDER.NO_SIGNAL')}</Text>;
  if (catalog.isPending) return <ActivityIndicator color={colors.primary} />;
  if (brands.length === 0) return <Help>{t('SALES_ORDER.PRODUCTS.EMPTY')}</Help>;

  return (
    <View style={styles.step}>
      <SectionTitle>{t('SALES_ORDER.PRODUCTS.TITLE')}</SectionTitle>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.tabs} accessibilityLabel={t('SALES_ORDER.PRODUCTS.BRANDS')}>
        {brands.map((b) => {
          const on = brandKey(b) === brandKey(current!);
          return (
            <Pressable
              key={brandKey(b)}
              onPress={() => { setTab(brandKey(b)); setQ(''); }}
              style={[styles.tab, on ? { backgroundColor: colors.primary, borderColor: colors.primary } : null]}
              accessibilityRole="tab"
              accessibilityState={{ selected: on }}
            >
              <Text style={[styles.tabText, { color: on ? colors.primaryContrast : colors.text }]} numberOfLines={1}>{brandName(b)}</Text>
            </Pressable>
          );
        })}
      </ScrollView>

      {(current?.offers ?? []).length > 0 ? (
        <View style={styles.block}>
          <Text style={styles.sub}>{t('SALES_ORDER.PRODUCTS.OFFERS')}</Text>
          {current!.offers.map((o) => (
            <OfferCard key={o.productPublicId} tenantId={tenantId} item={o} quantity={cartQuantity(state.cart, o.productPublicId)} onStep={(d) => step(o, d)} onSet={(text) => set(o, text)} stock={stock} warehouseId={warehouseId} />
          ))}
        </View>
      ) : null}

      <View style={styles.block}>
        <Text style={styles.sub}>{t('SALES_ORDER.PRODUCTS.ALL', { brand: brandName(current) })}</Text>
        <TextInput style={styles.search} value={q} onChangeText={setQ} placeholder={t('SALES_ORDER.PRODUCTS.SEARCH')} accessibilityLabel={t('SALES_ORDER.PRODUCTS.SEARCH')} autoCapitalize="none" autoCorrect={false} />
        {products.length === 0 ? <Help>{t('SALES_ORDER.PRODUCTS.NO_MATCH')}</Help> : null}
        {products.map((p) => {
          const qty = cartQuantity(state.cart, p.productPublicId);
          const canSell = p.price != null;
          return (
            <View key={p.productPublicId} style={[styles.prod, qty ? { borderColor: colors.primary } : null]}>
              <View style={styles.flex}>
                <Text style={styles.sku}>{p.sku}</Text>
                <Text style={styles.name}>{p.name}</Text>
                <View style={styles.inline}>
                  <Text style={canSell ? styles.price : styles.noPrice}>{canSell ? money(p.price) : t('SALES_ORDER.PRODUCTS.NO_PRICE')}</Text>
                  {p.isKit ? <Badge label={t('SALES_ORDER.PRODUCTS.KIT')} /> : null}
                </View>
                <StockInfo stock={stock} warehouseId={warehouseId} productPublicId={p.productPublicId} quantity={qty} />
              </View>
              <TextInput
                style={[styles.qty, !canSell && styles.disabled]}
                value={qty}
                onChangeText={(text) => set(p, text)}
                editable={canSell}
                keyboardType="decimal-pad"
                placeholder="0"
                accessibilityLabel={t('SALES_ORDER.PRODUCTS.QTY_OF', { sku: p.sku })}
              />
            </View>
          );
        })}
      </View>
    </View>
  );
}

function OfferCard({ tenantId, item, quantity, onStep, onSet, stock, warehouseId }: { tenantId: number; item: SalesCatalogItem; quantity: string; onStep: (d: number) => void; onSet: (text: string) => void; stock?: StockInput | null; warehouseId?: string | null }) {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const [failed, setFailed] = useState(false);
  const name = item.name || item.sku;
  const canSell = item.price != null;
  return (
    <View style={[styles.offer, quantity ? { borderColor: colors.primary, borderWidth: 2 } : null]}>
      {item.imagePublicId && !failed ? (
        <Image source={fileSource(tenantId, item.imagePublicId, getAccessToken(tenantId))} style={styles.offerImg} resizeMode="cover" onError={() => setFailed(true)} accessibilityLabel={t('SALES_ORDER.PRODUCTS.OFFER_PHOTO', { name })} />
      ) : (
        <View style={[styles.offerImg, styles.offerEmpty]}><Text style={styles.muted}>{t('SALES_ORDER.PRODUCTS.NO_PHOTO')}</Text></View>
      )}
      <View style={styles.offerBody}>
        <Badge label={t('SALES_ORDER.PRODUCTS.OFFER')} />
        <Text style={styles.name}>{name}</Text>
        <Text style={styles.muted}>{item.sku}</Text>
        <Text style={canSell ? styles.price : styles.noPrice}>{canSell ? money(item.price) : t('SALES_ORDER.PRODUCTS.NO_PRICE')}</Text>
        <StockInfo stock={stock} warehouseId={warehouseId} productPublicId={item.productPublicId} quantity={quantity} />
        <View style={styles.stepper}>
          <Pressable style={[styles.stepBtn, (!canSell || !quantity) && styles.disabled]} disabled={!canSell || !quantity} onPress={() => onStep(-1)} accessibilityRole="button" accessibilityLabel={t('SALES_ORDER.PRODUCTS.LESS', { name })}>
            <Text style={styles.stepText}>−</Text>
          </Pressable>
          <TextInput style={styles.qty} value={quantity} onChangeText={onSet} editable={canSell} keyboardType="number-pad" placeholder="0" accessibilityLabel={t('SALES_ORDER.PRODUCTS.QTY_OF', { sku: item.sku })} />
          <Pressable style={[styles.stepBtn, !canSell && styles.disabled]} disabled={!canSell} onPress={() => onStep(1)} accessibilityRole="button" accessibilityLabel={t('SALES_ORDER.PRODUCTS.MORE', { name })}>
            <Text style={styles.stepText}>+</Text>
          </Pressable>
        </View>
      </View>
    </View>
  );
}

/** El carrito fijo abajo: conteo y subtotal; abierto, las líneas para corregir la cantidad o quitar. */
export function CartBar({ state, onChange, stock, warehouseId }: { state: WizardState; onChange: (next: (s: WizardState) => WizardState) => void; stock?: StockInput | null; warehouseId?: string | null }) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const [open, setOpen] = useState(false);
  const sum = cartSummary(state.cart);
  const setQty = (id: string, text: string) => onChange((s) => ({ ...s, cart: s.cart.map((x) => (x.productPublicId === id ? { ...x, quantity: text } : x)) }));
  return (
    <>
      <Pressable style={styles.cartBar} onPress={() => setOpen(true)} accessibilityRole="button" accessibilityLabel={`${t('SALES_ORDER.CART.SUMMARY', { lines: sum.lines, units: sum.units })}, ${money(sum.subtotal)}. ${t('SALES_ORDER.CART.SHOW')}`}>
        <Text style={styles.cartText}>{t('SALES_ORDER.CART.SUMMARY', { lines: sum.lines, units: sum.units })}</Text>
        <Text style={styles.cartTotal}>{money(sum.subtotal)}</Text>
        <Text style={styles.cartLink}>{t('SALES_ORDER.CART.SHOW')}</Text>
      </Pressable>
      <Modal visible={open} animationType="slide" onRequestClose={() => setOpen(false)}>
        <SafeAreaView style={styles.sheet}>
          <Text style={styles.sheetTitle} accessibilityRole="header">{t('SALES_ORDER.CART.TITLE')}</Text>
          <ScrollView contentContainerStyle={styles.sheetBody} keyboardShouldPersistTaps="handled">
            {state.cart.length === 0 ? <Help>{t('SALES_ORDER.CART.EMPTY')}</Help> : null}
            {state.cart.map((l) => (
              <View key={l.productPublicId} style={styles.prod}>
                <View style={styles.flex}>
                  <Text style={styles.sku}>{l.sku}</Text>
                  <Text style={styles.name}>{l.name}</Text>
                  <Text style={styles.price}>{l.price == null ? '—' : money(cartLineAmount(l))}</Text>
                  <StockInfo stock={stock} warehouseId={warehouseId} productPublicId={l.productPublicId} quantity={l.quantity} />
                </View>
                <View style={styles.cartActions}>
                  <TextInput style={styles.qty} value={l.quantity} onChangeText={(text) => setQty(l.productPublicId, text)} keyboardType="decimal-pad" accessibilityLabel={t('SALES_ORDER.PRODUCTS.QTY_OF', { sku: l.sku })} />
                  <Button kind="danger" label={t('SALES_ORDER.CART.REMOVE')} onPress={() => onChange((s) => ({ ...s, cart: removeFromCart(s.cart, l.productPublicId) }))} />
                </View>
              </View>
            ))}
          </ScrollView>
          <View style={styles.sheetFoot}>
            <Text style={styles.cartTotal}>{`${t('SALES_ORDER.SUMMARY.SUBTOTAL')}: ${money(sum.subtotal)}`}</Text>
            <Button label={t('SALES_ORDER.CART.HIDE')} onPress={() => setOpen(false)} />
          </View>
        </SafeAreaView>
      </Modal>
    </>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  step: { gap: 12 },
  block: { gap: 8 },
  flex: { flex: 1, minWidth: 0, gap: 2 },
  inline: { flexDirection: 'row', alignItems: 'center', gap: 8, flexWrap: 'wrap' },
  err: { color: colors.danger, textAlign: 'center' },
  tabs: { gap: 8, paddingVertical: 2 },
  tab: { borderWidth: 1, borderColor: colors.gray, borderRadius: 18, paddingHorizontal: 14, paddingVertical: 8, minHeight: 38, justifyContent: 'center' },
  tabText: { fontSize: 14, fontWeight: '600' },
  sub: { fontSize: 15, fontWeight: '700', color: colors.text },
  search: { borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, paddingVertical: 10, fontSize: 16 },
  prod: { flexDirection: 'row', alignItems: 'center', gap: 10, borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 10 },
  sku: { fontSize: 13, fontWeight: '700', color: colors.text },
  name: { fontSize: 14, color: colors.text },
  price: { fontSize: 14, fontWeight: '600', color: colors.text },
  noPrice: { fontSize: 13, color: colors.danger },
  muted: { fontSize: 12, color: colors.muted },
  qty: { width: 72, borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingVertical: 8, paddingHorizontal: 8, fontSize: 16, textAlign: 'center' },
  disabled: { opacity: 0.45 },
  offer: { flexDirection: 'row', gap: 10, borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 10, flexWrap: 'wrap' },
  offerImg: { width: 96, height: 96, borderRadius: 10 },
  offerEmpty: { alignItems: 'center', justifyContent: 'center', backgroundColor: colors.backgroundSecondary, borderWidth: 1, borderColor: colors.gray },
  offerBody: { flex: 1, minWidth: 150, gap: 3 },
  stepper: { flexDirection: 'row', alignItems: 'center', gap: 8, marginTop: 4 },
  stepBtn: { width: 44, height: 44, borderRadius: 22, borderWidth: 1, borderColor: colors.primary, alignItems: 'center', justifyContent: 'center' },
  stepText: { fontSize: 22, fontWeight: '700', color: colors.primary },
  cartBar: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: 8, borderWidth: 1, borderColor: colors.primary, borderRadius: 12, paddingHorizontal: 12, paddingVertical: 10, backgroundColor: colors.backgroundSecondary },
  cartText: { flex: 1, minWidth: 140, fontSize: 14, color: colors.text },
  cartTotal: { fontSize: 16, fontWeight: '700', color: colors.text },
  cartLink: { fontSize: 13, fontWeight: '600', color: colors.primary },
  cartActions: { alignItems: 'flex-end', gap: 6 },
  sheet: { flex: 1, backgroundColor: colors.background },
  sheetTitle: { fontSize: 20, fontWeight: '700', color: colors.text, padding: 20, paddingBottom: 8 },
  sheetBody: { paddingHorizontal: 20, gap: 8, paddingBottom: 20 },
  sheetFoot: { gap: 10, padding: 16, borderTopWidth: 1, borderTopColor: colors.gray },
});
