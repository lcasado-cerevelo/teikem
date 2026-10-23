import { ActivityIndicator, FlatList, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useTranslation } from 'react-i18next';
import { usePieceIds, usePiecesByIds } from '@hooks/queries';
import { useAppStore } from '@/stores';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Pieces tab: lists the shipment's pieces (barcode + package type + comments).
// Was "En construcción" in the old app — built here against the real piece API.
export default function ShipmentPieces() {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const shipment = useAppStore((s) => s.shipment);

  const { data: pieceIds, isLoading: idsLoading } = usePieceIds(shipment?.shipmentID);
  const { data: pieces, pending } = usePiecesByIds(pieceIds ?? []);

  if (idsLoading || pending) {
    return (
      <View style={styles.center}>
        <ActivityIndicator color={colors.primary} />
      </View>
    );
  }

  if (!pieces.length) {
    return (
      <View style={styles.center}>
        <Text style={styles.muted}>{t('SHIPMENT_DETAILS.NO_PIECES')}</Text>
      </View>
    );
  }

  return (
    <FlatList
      contentContainerStyle={styles.list}
      data={pieces}
      keyExtractor={(p) => String(p.pieceID)}
      renderItem={({ item }) => (
        <View style={styles.card}>
          <Text style={styles.title}>
            {t('SHIPMENT_DETAILS.BARCODE')}: {item.barcode}
          </Text>
          <Text style={styles.body}>
            {t('SHIPMENT_DETAILS.PACKAGE_TYPE')}: {item.packageTypeName ?? item.packageType}
          </Text>
          {item.comments ? <Text style={styles.body}>{item.comments}</Text> : null}
        </View>
      )}
    />
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  muted: { color: colors.muted, fontSize: 14 },
  list: { padding: 16, gap: 12 },
  card: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 13,
    padding: 14,
    gap: 4,
  },
  title: { fontSize: 15, fontWeight: '600', color: colors.text },
  body: { fontSize: 13, color: colors.text },
});
