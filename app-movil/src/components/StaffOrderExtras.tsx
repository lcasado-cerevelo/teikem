import { StyleSheet, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import { Text } from '@components/Text';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import type { StaffOrderExtras as Extras } from '@/types/models';

// Lote 214 — lo que la ficha de una orden del personal muestra además de lo del chofer: empaque y factura, la ruta vigente y su chofer, el comprobante y los intentos.
const when = (iso: string) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
};

export default function StaffOrderExtras({ x }: { x: Extras }) {
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  return (
    <>
      <View style={styles.section}>
        <Text style={styles.body}>{t('STAFF_ORDERS.PACK_BATCH')}: {x.packBatchNumber}</Text>
        {x.clientInvoiceNumber ? <Text style={styles.body}>{t('SHIPMENT_DETAILS.INVOICE')}: {x.clientInvoiceNumber}</Text> : null}
        <Text style={styles.body}>{t('SHIPMENT_DETAILS.SERVICE_TYPE')}: {x.serviceTypeLabel}</Text>
        <Text style={styles.body}>
          {t('STAFF_ORDERS.ROUTE')}: {x.tripCode ? `${x.tripCode}${x.tripStatus ? ` (${x.tripStatus})` : ''}` : t('STAFF_ORDERS.NO_ROUTE')}
        </Text>
        {x.driverName ? <Text style={styles.body}>{t('STAFF_ORDERS.DRIVER')}: {x.driverName}</Text> : null}
      </View>
      {x.proof ? (
        <View style={styles.section}>
          <Text style={styles.title}>{t('STAFF_ORDERS.PROOF')}</Text>
          <Text style={styles.body}>{t('STAFF_ORDERS.RECEIVED_BY')}: {x.proof.recipientName}</Text>
          <Text style={styles.body}>{when(x.proof.deliveredAtUtc)}</Text>
          {x.proof.distanceFromStopMeters != null ? <Text style={styles.body}>{t('STAFF_ORDERS.DISTANCE', { m: x.proof.distanceFromStopMeters })}</Text> : null}
          <Text style={styles.muted}>
            {[x.proof.hasSignature ? t('STAFF_ORDERS.WITH_SIGNATURE') : null, x.proof.photos > 0 ? t('STAFF_ORDERS.PHOTOS', { n: x.proof.photos }) : null].filter(Boolean).join(' · ')}
          </Text>
          {x.proof.notes ? <Text style={styles.body}>{x.proof.notes}</Text> : null}
        </View>
      ) : null}
      {x.attempts.length > 0 ? (
        <View style={styles.section}>
          <Text style={styles.title}>{t('STAFF_ORDERS.ATTEMPTS')}</Text>
          {x.attempts.map((a) => (
            <View key={a.number} style={styles.attempt}>
              <Text style={styles.body}>
                #{a.number} · {a.delivered ? t('STAFF_ORDERS.ATTEMPT_DELIVERED') : a.reason ?? t('STAFF_ORDERS.ATTEMPT_FAILED')} · {when(a.attemptedAtUtc)}
              </Text>
              <Text style={styles.muted}>{[a.driver, a.notes].filter(Boolean).join(' — ')}</Text>
            </View>
          ))}
        </View>
      ) : null}
    </>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  section: { gap: 6, borderRadius: 13, marginHorizontal: 'auto', width: '70%' },
  title: { fontWeight: '700', fontSize: 13, color: colors.primary },
  body: { fontWeight: '600', fontSize: 12, color: colors.text },
  muted: { fontSize: 12, color: colors.muted },
  attempt: { gap: 2 },
});
