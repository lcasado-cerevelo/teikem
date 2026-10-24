import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { useRouter } from 'expo-router';
import { useAppStore } from '@/stores';
import { useShipmentDetail } from '@hooks/queries';
import { useAccess } from '@hooks/access';
import { staffActionsOf } from '@utils/staffOrders';
import ShipmentActionComplete from './ShipmentActionComplete';
import ShipmentActionException from './ShipmentActionException';
import ShipmentActionPickup from './ShipmentActionPickup';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

type ActionOption = 'default' | 'complete' | 'exception' | 'pickup';

// The "Acciones" tab (Phase 5). Mirrors the old app: an initial chooser with two
// options — Excepción de pedido / Completar pedido — each opening its own flow.
export default function ShipmentActions() {
  const shipment = useAppStore((s) => s.shipment);
  const { data: detail } = useShipmentDetail(shipment);
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [option, setOption] = useState<ActionOption>('default');
  const access = useAccess();
  const router = useRouter();
  // Lote 214: una orden del personal se cierra con orders.deliver en esa compañía; el tipo (entrega/recogida) lo dice la ficha del servidor.
  const isStaff = shipment?.source === 'staff';
  const isPickup = (detail ?? shipment)?.kind === 'PICKUP';

  if (option === 'complete') {
    return <ShipmentActionComplete onBack={() => setOption('default')} />;
  }
  if (option === 'pickup') {
    return <ShipmentActionPickup onBack={() => setOption('default')} />;
  }
  if (option === 'exception') {
    return <ShipmentActionException onBack={() => setOption('default')} />;
  }

  if (isStaff) {
    const canDeliver = !!shipment && access.companiesWith('orders.deliver').some((c) => c.tenantId === shipment.tenantId);
    // Lote 215: asignar en la calle (trips.streetassign) — mandar la orden a una ruta, pasarla o sacarla.
    const canAssign = !!shipment && access.companiesWith('trips.streetassign').some((c) => c.tenantId === shipment.tenantId);
    if (!canDeliver && !canAssign) return <Text style={styles.notice}>{t('STAFF_ORDERS.NO_DELIVER_PERM')}</Text>;
    if (!detail?.staff) return <Text style={styles.notice}>{t('STAFF_ORDERS.LOADING')}</Text>;
    const a = canDeliver ? staffActionsOf(detail.staff) : { complete: false, pickup: false, exception: false };
    if (!a.complete && !a.pickup && !a.exception && !canAssign) return <Text style={styles.notice}>{t('STAFF_ORDERS.NOTHING_TO_DO', { status: detail.status })}</Text>;
    return (
      <View style={styles.container}>
        {a.exception ? (
          <Pressable style={styles.item} onPress={() => setOption('exception')}>
            <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>{isPickup ? t('PICKUP.NOT_PICKED') : t('ACTIONS.ORDERS_EXCEPTION')}</Text>
          </Pressable>
        ) : null}
        {a.pickup ? (
          <Pressable style={styles.item} onPress={() => setOption('pickup')}>
            <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>{t('PICKUP.PICK')}</Text>
          </Pressable>
        ) : null}
        {a.complete ? (
          <Pressable style={styles.item} onPress={() => setOption('complete')}>
            <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>{t('ACTIONS.ORDERS_COMPLETE')}</Text>
          </Pressable>
        ) : null}
        {canAssign && shipment ? (
          <Pressable style={styles.item} onPress={() => router.push({ pathname: '/(auth)/StreetAssign', params: { tenantId: String(shipment.tenantId), orderId: shipment.id } })}>
            <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>{t('STREET.ASSIGN_BUTTON')}</Text>
          </Pressable>
        ) : null}
      </View>
    );
  }

  // Solo se cierra un envío de un manifiesto ya aceptado (salida hecha) y que sigue abierto.
  if (shipment && !shipment.manifestAccepted) return <Text style={styles.notice}>{t('ACTIONS.ACCEPT_FIRST')}</Text>;
  if (detail && !detail.canClose) return <Text style={styles.notice}>{t('ACTIONS.ALREADY_CLOSED')}</Text>;

  return (
    <View style={styles.container}>
      <Pressable style={styles.item} onPress={() => setOption('exception')}>
        <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>
          {isPickup ? t('PICKUP.NOT_PICKED') : t('ACTIONS.ORDERS_EXCEPTION')}
        </Text>
      </Pressable>
      <Pressable style={styles.item} onPress={() => setOption(isPickup ? 'pickup' : 'complete')}>
        <Text style={styles.label} numberOfLines={1} adjustsFontSizeToFit>
          {isPickup ? t('PICKUP.PICK') : t('ACTIONS.ORDERS_COMPLETE')}
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
