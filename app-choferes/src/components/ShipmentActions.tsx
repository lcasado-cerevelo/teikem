import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useAppStore } from '@/stores';
import { useShipmentDetail } from '@hooks/queries';
import ShipmentActionComplete from './ShipmentActionComplete';
import ShipmentActionException from './ShipmentActionException';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

type ActionOption = 'default' | 'complete' | 'exception';

// The "Acciones" tab (Phase 5). Mirrors the old app: an initial chooser with two
// options — Excepción de pedido / Completar pedido — each opening its own flow.
export default function ShipmentActions() {
  const shipment = useAppStore((s) => s.shipment);
  const { data: detail } = useShipmentDetail(shipment);
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [option, setOption] = useState<ActionOption>('default');

  if (option === 'complete') {
    return <ShipmentActionComplete onBack={() => setOption('default')} />;
  }
  if (option === 'exception') {
    return <ShipmentActionException onBack={() => setOption('default')} />;
  }

  // Solo se cierra un envío de un manifiesto ya aceptado (salida hecha) y que sigue abierto.
  if (shipment && !shipment.manifestAccepted) return <Text style={styles.notice}>{t('ACTIONS.ACCEPT_FIRST')}</Text>;
  if (detail && !detail.canClose) return <Text style={styles.notice}>{t('ACTIONS.ALREADY_CLOSED')}</Text>;

  return (
    <View style={styles.container}>
      <Pressable style={styles.item} onPress={() => setOption('exception')}>
        <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>
          {t('ACTIONS.ORDERS_EXCEPTION')}
        </Text>
      </Pressable>
      <Pressable style={styles.item} onPress={() => setOption('complete')}>
        <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>
          {t('ACTIONS.ORDERS_COMPLETE')}
        </Text>
      </Pressable>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { marginTop: 20, marginHorizontal: 20, gap: 15 },
  item: {
    backgroundColor: colors.primary,
    borderRadius: 12,
    paddingHorizontal: 16,
    paddingVertical: 16,
    justifyContent: 'center',
  },
  notice: { margin: 24, textAlign: 'center', color: colors.muted, fontSize: 15 },
  label: { fontSize: 16, fontWeight: '600', color: colors.primaryContrast, textAlign: 'left' },
});
