import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { KIT_PIECES_UNFOLDED, lineStock, shortHeadKey, warehouseLabel, type KitPieceInput, type KitStock, type StockInput } from '@utils/salesStockRules';

// Lote 235 — lo que el renglón de un producto dice del inventario en el pedido del teléfono: «Disponible: N» en el almacén de la orden; si se pide de más, el
// distintivo «Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.» en el color de falla del tema (no bloquea: se puede guardar y enviar);
// y, si otros almacenes activos tienen, la nota «Hay disponible en {almacén}: N». Sin datos no pinta nada.
// Lote 236: un KIT que se arma al pedir (el kit de oferta) dice «Se pueden armar: N» y debajo su desglose por pieza («52800 Tirillas × 6 — 14,022 disp.»), con la
// pieza que limita marcada; con más de 3 piezas el desglose va plegado («Ver piezas (N) · limita 52800»). Un kit armado no se desglosa.

const num = (n: number) => String(Math.round(n * 1000) / 1000);
/** Cantidades con separador de miles (14,022), como en la web. */
const qty = (n: number) => (Math.round(n * 1000) / 1000).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');

/** El desglose del disponible de un kit por pieza: lo que lleva cada kit y lo que hay de cada una; la que limita va marcada. */
function KitPieces({ kit }: { kit: KitStock }) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const fold = kit.pieces.length > KIT_PIECES_UNFOLDED;
  const [open, setOpen] = useState(false);
  if (kit.pieces.length === 0) return null;
  const limiting = kit.pieces.filter((p) => p.limiting).map((p) => p.sku).join(', ');
  return (
    <View style={styles.kit}>
      {fold ? (
        <Pressable onPress={() => setOpen((v) => !v)} accessibilityRole="button" accessibilityState={{ expanded: open }} hitSlop={6}>
          <Text style={styles.kitToggle}>
            {`${open ? '▾' : '▸'} ${t(open ? 'SALES_ORDER.STOCK.KIT_HIDE' : 'SALES_ORDER.STOCK.KIT_SHOW', { n: kit.pieces.length })}${limiting ? ` · ${t('SALES_ORDER.STOCK.KIT_LIMITED_BY', { sku: limiting })}` : ''}`}
          </Text>
        </Pressable>
      ) : null}
      {!fold || open ? kit.pieces.map((p, i) => (
        <View key={`${p.productPublicId}-${i}`} style={styles.piece}>
          <Text style={[styles.pieceWhat, p.limiting && styles.pieceLimit]}>{`${p.sku} ${p.name} × ${num(p.qtyPerKit)}`}</Text>
          <Text style={[styles.pieceN, p.limiting && styles.pieceLimit]}>
            {t('SALES_ORDER.STOCK.PIECE_AVAILABLE', { n: qty(p.available) })}{p.limiting ? ` · ${t('SALES_ORDER.STOCK.KIT_LIMIT')}` : ''}
          </Text>
        </View>
      )) : null}
    </View>
  );
}

export default function StockInfo({ stock, warehouseId, productPublicId, quantity, onlyBackorder = false, pieces }: {
  stock: StockInput | null | undefined;
  warehouseId: string | null | undefined;
  productPublicId: string | null | undefined;
  quantity: string | number;
  /** Solo el aviso de backorder (resumen de la entrega): sin «Disponible» ni la nota de otros almacenes. */
  onlyBackorder?: boolean;
  /** Lote 236: las piezas del kit en la orden si se cambiaron; sin ellas, las del catálogo. */
  pieces?: readonly KitPieceInput[] | null;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const info = lineStock(stock, productPublicId, warehouseId, quantity, pieces);
  if (!info) return null;
  if (onlyBackorder && info.backorder <= 0) return null;
  const warehouse = warehouseLabel(stock, warehouseId);
  const head = t(`SALES_ORDER.STOCK.${shortHeadKey(info.available)}`, { available: num(info.available), warehouse });
  const tail = t(info.backorder === 1 ? 'SALES_ORDER.STOCK.backOne' : 'SALES_ORDER.STOCK.backMany', { n: num(info.backorder) });
  return (
    <View style={styles.box}>
      {!onlyBackorder ? <Text style={styles.muted}>{t(info.kit ? 'SALES_ORDER.STOCK.KIT_AVAILABLE' : 'SALES_ORDER.STOCK.AVAILABLE', { n: info.kit ? qty(info.available) : num(info.available) })}</Text> : null}
      {!onlyBackorder && info.kit ? <KitPieces kit={info.kit} /> : null}
      {info.backorder > 0 ? (
        <View style={styles.short} accessibilityRole="alert">
          <View style={styles.chip}><Text style={styles.chipText}>{t('SALES_ORDER.STOCK.BACKORDER_CHIP', { n: num(info.backorder) })}</Text></View>
          <Text style={styles.shortText}>{`${head}; ${tail}`}</Text>
        </View>
      ) : null}
      {!onlyBackorder && info.elsewhere.length === 1 ? (
        <Text style={styles.note}>{t('SALES_ORDER.STOCK.ELSEWHERE_ONE', { warehouse: info.elsewhere[0].name, n: num(info.elsewhere[0].available) })}</Text>
      ) : null}
      {!onlyBackorder && info.elsewhere.length > 1 ? (
        <Text style={styles.note}>{t('SALES_ORDER.STOCK.ELSEWHERE_MANY', { list: info.elsewhere.map((e) => `${e.name} ${num(e.available)}`).join(' · ') })}</Text>
      ) : null}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  box: { gap: 2, marginTop: 2 },
  muted: { fontSize: 12, color: colors.muted },
  note: { fontSize: 12, color: colors.muted, fontStyle: 'italic' },
  short: { gap: 3, alignItems: 'flex-start' },
  chip: { borderWidth: 1, borderColor: colors.danger, borderRadius: 10, paddingHorizontal: 8, paddingVertical: 2 },
  chipText: { fontSize: 12, fontWeight: '700', color: colors.danger },
  shortText: { fontSize: 12, fontWeight: '600', color: colors.danger },
  kit: { alignSelf: 'stretch', gap: 3, paddingLeft: 8, borderLeftWidth: 2, borderLeftColor: colors.gray },
  kitToggle: { fontSize: 12, color: colors.text, paddingVertical: 3 },
  piece: { flexDirection: 'row', flexWrap: 'wrap', justifyContent: 'space-between', columnGap: 8 },
  pieceWhat: { flexShrink: 1, fontSize: 12, color: colors.muted },
  pieceN: { fontSize: 12, color: colors.muted, marginLeft: 'auto' },
  pieceLimit: { color: colors.text, fontWeight: '700' },
});
