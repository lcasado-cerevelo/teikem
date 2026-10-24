import { StyleSheet, Switch, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 214 — solo cuando el PERSONAL cierra la orden: «Acreditar al chofer» (sí por omisión). Sí = el intento y el cobro cuentan para la liquidación del chofer de la ruta;
// no = quedan sin chofer. En ambos casos el historial y la auditoría dicen quién lo cerró.
export default function CreditToggle({ value, onChange }: { value: boolean; onChange: (v: boolean) => void }) {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  return (
    <View style={styles.row}>
      <View style={styles.text}>
        <Text style={styles.label}>{t('STAFF_ORDERS.CREDIT')}</Text>
        <Text style={styles.hint}>{t('STAFF_ORDERS.CREDIT_HINT')}</Text>
      </View>
      <Switch value={value} onValueChange={onChange} accessibilityLabel={t('STAFF_ORDERS.CREDIT')} />
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, marginVertical: 10 },
  text: { flex: 1, gap: 2 },
  label: { fontWeight: '700', color: colors.text },
  hint: { fontSize: 12, color: colors.muted },
});
