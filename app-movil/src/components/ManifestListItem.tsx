import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { FontAwesome } from '@expo/vector-icons';
import Card from './Card';
import { useAppStore } from '@/stores';
import { useDriverWork } from '@hooks/queries';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import type { Manifest } from '@/types/models';

// Un manifiesto de la lista. Si todavía no se acepta, abre la pantalla de aceptación (qué envíos acepta y cuáles no); si ya salió, abre sus envíos.
export default function ManifestListItem({ manifest }: { manifest: Manifest }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const router = useRouter();
  const { data } = useDriverWork();
  const addShipmentIds = useAppStore((s) => s.addShipmentIds);
  const open = (data?.shipments ?? []).filter((s) => s.manifestId === manifest.id);

  const onPress = () => {
    if (!manifest.accepted) {
      router.push({ pathname: '/(auth)/AcceptManifest', params: { id: manifest.id } });
      return;
    }
    addShipmentIds(open.map((s) => s.id));
    router.push('/(auth)/OrdersList');
  };

  return (
    <Pressable onPress={onPress} accessibilityRole="button">
      <Card style={styles.container}>
        <FontAwesome name={manifest.accepted ? 'list-ul' : 'hand-o-right'} size={25} color={colors.muted} />
        <View style={styles.descriptionContainer}>
          <Text style={styles.description}>{`${manifest.code} - ${manifest.planDate}`}</Text>
          <Text style={styles.company}>{manifest.companyName}</Text>
          {!manifest.accepted && <Text style={styles.pending}>{t('MANIFESTS.TO_ACCEPT')}</Text>}
        </View>
        <Text style={styles.count}>{`(${manifest.accepted ? open.length : manifest.shipmentCount})`}</Text>
      </Card>
    </Pressable>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: { flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-start', marginVertical: 14, marginHorizontal: 20, paddingHorizontal: 10, paddingVertical: 25, gap: 16 },
  descriptionContainer: { flex: 1, gap: 2 },
  description: { fontSize: 16, color: colors.text },
  company: { fontSize: 13, color: colors.muted },
  pending: { fontSize: 13, fontWeight: '700', color: colors.primary },
  count: { fontSize: 20, fontWeight: '300', color: colors.text },
});
