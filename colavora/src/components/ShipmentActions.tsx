import { useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useTranslation } from 'react-i18next';
import ShipmentActionComplete from './ShipmentActionComplete';
import ShipmentActionException from './ShipmentActionException';
import type { ShipmentTab } from './ShipmentDetailsTabs';
import { colors } from '@constants/theme';

type ActionOption = 'default' | 'complete' | 'exception';

// The "Acciones" tab (Phase 5). Mirrors the old app: an initial chooser with two
// options — Excepción de pedido / Completar pedido — each opening its own flow.
export default function ShipmentActions({ setSelectedTab }: { setSelectedTab?: (tab: ShipmentTab) => void }) {
  const { t } = useTranslation();
  const [option, setOption] = useState<ActionOption>('default');

  if (option === 'complete') {
    return <ShipmentActionComplete onBack={() => setOption('default')} />;
  }
  if (option === 'exception') {
    return <ShipmentActionException onBack={() => setOption('default')} setSelectedTab={setSelectedTab} />;
  }

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

const styles = StyleSheet.create({
  container: { marginTop: 20, marginHorizontal: 20, gap: 15 },
  item: {
    backgroundColor: colors.primary,
    borderRadius: 12,
    paddingHorizontal: 16,
    paddingVertical: 16,
    justifyContent: 'center',
  },
  label: { fontSize: 16, fontWeight: '600', color: colors.primaryContrast, textAlign: 'left' },
});
