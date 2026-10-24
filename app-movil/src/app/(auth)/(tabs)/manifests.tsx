import { useMemo } from 'react';
import { ActivityIndicator, FlatList, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useTranslation } from 'react-i18next';
import { useLocalSearchParams } from 'expo-router';
import ScreenHeader from '@components/ScreenHeader';
import ManifestListItem from '@components/ManifestListItem';
import { useDriverWork } from '@hooks/queries';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Manifiestos del chofer de TODAS sus compañías, cada uno con el nombre de su compañía. Modo entrega (por omisión) o recogida (?kind=pickup).
// Los que faltan por aceptar van primero: el chofer decide qué envíos acepta antes de salir.
export default function ManifestsScreen() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const { kind } = useLocalSearchParams<{ kind?: string }>();
  const isPickup = kind === 'pickup';

  const { data, isPending } = useDriverWork();
  const manifests = useMemo(() => {
    const list = (data?.manifests ?? []).filter((m) => (isPickup ? m.kind === 'PICKUP' : m.kind === 'DELIVERY'));
    return [...list].sort((a, b) => Number(a.accepted) - Number(b.accepted) || b.planDate.localeCompare(a.planDate) || a.code.localeCompare(b.code));
  }, [data, isPickup]);

  const title = isPickup ? t('MANIFESTS.PICKUP_MANIFESTS') : t('MANIFESTS.MANIFESTS');

  return (
    <SafeAreaView style={styles.container} edges={['top']}>
      <ScreenHeader title={`${title} (${manifests.length})`} />
      <View style={styles.content}>
        {data?.failedCompanies.length ? <Text style={styles.warn}>{t('MANIFESTS.COMPANY_FAILED', { names: data.failedCompanies.join(', ') })}</Text> : null}
        {isPending ? (
          <ActivityIndicator color={colors.primary} />
        ) : (
          <FlatList data={manifests} keyExtractor={(item) => item.id} renderItem={({ item }) => <ManifestListItem manifest={item} />} />
        )}
      </View>
    </SafeAreaView>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background },
  content: { flex: 1, paddingHorizontal: 2 },
  warn: { color: colors.danger, textAlign: 'center', paddingHorizontal: 20, paddingBottom: 6 },
});
