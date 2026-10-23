import { useMemo } from 'react';
import { ActivityIndicator, FlatList, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import { useLocalSearchParams } from 'expo-router';
import ScreenHeader from '@components/ScreenHeader';
import ManifestListItem from '@components/ManifestListItem';
import { useMe, useDriverShipments } from '@hooks/queries';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

const isCreated = (s?: string | null) => (s ?? '').trim().toLowerCase() === 'created';

// Una sola fuente de verdad = los envíos de hoy del chofer. Los agrupamos por su
// manifiesto. Modo entrega (default): por manifestDL. Modo recogida (?kind=pickup):
// solo los Created asignados al chofer (AssignPk), agrupados por manifestPk.
export default function ManifestsScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const { kind } = useLocalSearchParams<{ kind?: string }>();
  const isPickup = kind === 'pickup';

  const { data: me } = useMe();
  const driverId = me?.userID;
  const { data: shipmentsData, isPending: pending } = useDriverShipments(driverId);
  const shipments = shipmentsData ?? [];

  const groups = useMemo(() => {
    const map = new Map<string, { ids: number[]; date: string }>();
    for (const s of shipments) {
      if (isPickup && !(isCreated(s.status) && s.assignPK === driverId)) continue;
      const code = (isPickup ? s.manifestPk : s.manifestDL) ?? '—';
      const entry = map.get(code) ?? { ids: [], date: '' };
      entry.ids.push(s.shipmentID);
      if (!entry.date && s.createdDate) {
        const d = new Date(s.createdDate);
        if (!Number.isNaN(d.getTime())) {
          entry.date = `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;
        }
      }
      map.set(code, entry);
    }
    return Array.from(map.entries()).map(([code, v]) => ({ code, date: v.date, ids: v.ids }));
  }, [shipments, isPickup, driverId]);

  const title = isPickup ? t('MANIFESTS.PICKUP_MANIFESTS') : t('MANIFESTS.MANIFESTS');

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={`${title} (${groups.length})`} />
      <View style={styles.content}>
        {pending ? (
          <ActivityIndicator color={colors.primary} />
        ) : (
          <FlatList
            data={groups}
            keyExtractor={(item) => item.code}
            renderItem={({ item }) => (
              <ManifestListItem code={item.code} date={item.date} shipmentIds={item.ids} />
            )}
          />
        )}
      </View>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1, paddingHorizontal: 2 },
});
