import { StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import { lineStock, shortHeadKey, warehouseLabel, type StockInput } from '@utils/salesStockRules';

// Lote 235 — lo que el renglón de un producto dice del inventario en el pedido del teléfono: «Disponible: N» en el almacén de la orden; si se pide de más, el
// distintivo «Backorder: N» y «Solo hay X disponibles en {almacén}; N quedan en backorder.» en el color de falla del tema (no bloquea: se puede guardar y enviar);
// y, si otros almacenes activos tienen, la nota «Hay disponible en {almacén}: N». Sin datos no pinta nada.

const num = (n: number) => String(Math.round(n * 1000) / 1000);

export default function StockInfo({ stock, warehouseId, productPublicId, quantity, onlyBackorder = false }: {
  stock: StockInput | null | undefined;
  warehouseId: string | null | undefined;
  productPublicId: string | null | undefined;
  quantity: string | number;
  /** Solo el aviso de backorder (resumen de la entrega): sin «Disponible» ni la nota de otros almacenes. */
  onlyBackorder?: boolean;
}) {
  const { t } = useTranslation();
  const styles = useStyles(createStyles);
  const info = lineStock(stock, productPublicId, warehouseId, quantity);
  if (!info) return null;
  if (onlyBackorder && info.backorder <= 0) return null;
  const warehouse = warehouseLabel(stock, warehouseId);
  const head = t(`SALES_ORDER.STOCK.${shortHeadKey(info.available)}`, { available: num(info.available), warehouse });
  const tail = t(info.backorder === 1 ? 'SALES_ORDER.STOCK.backOne' : 'SALES_ORDER.STOCK.backMany', { n: num(info.backorder) });
  return (
    <View style={styles.box}>
      {!onlyBackorder ? <Text style={styles.muted}>{t('SALES_ORDER.STOCK.AVAILABLE', { n: num(info.available) })}</Text> : null}
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
});
