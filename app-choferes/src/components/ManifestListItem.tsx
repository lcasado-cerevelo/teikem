import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { Link } from 'expo-router';
import { FontAwesome } from '@expo/vector-icons';
import Card from './Card';
import { useAppStore } from '@/stores';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// "code - date" + (count). The shipment ids for this manifest are passed in
// (already grouped on the manifests screen), so there's no per-item query.
// Tapping loads them into the store and opens OrdersList.
export default function ManifestListItem({
  code,
  date,
  shipmentIds,
}: {
  code: string;
  date: string;
  shipmentIds: number[];
}) {
  const styles = useStyles(createStyles);
  const addShipmentIds = useAppStore((s) => s.addShipmentIds);

  return (
    <Link href="/(auth)/OrdersList" asChild>
      <Pressable onPress={() => addShipmentIds(shipmentIds)}>
        <Card style={styles.container}>
          <FontAwesome name="list-ul" size={25} color="gray" />
          <View style={styles.descriptionContainer}>
            <Text style={styles.description}>{date ? `${code} - ${date}` : code}</Text>
            <Text style={styles.count}>{`(${shipmentIds.length})`}</Text>
          </View>
        </Card>
      </Pressable>
    </Link>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  container: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-start',
    marginVertical: 14,
    marginHorizontal: 20,
    paddingHorizontal: 10,
    paddingVertical: 25,
    gap: 16,
  },
  descriptionContainer: { flexDirection: 'row', justifyContent: 'center', alignItems: 'center', gap: 10 },
  description: { fontSize: 16, color: colors.text },
  count: { fontSize: 20, fontWeight: '300', color: colors.text },
});
