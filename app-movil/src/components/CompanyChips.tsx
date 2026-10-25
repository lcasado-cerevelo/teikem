import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Selector de compañía de las pantallas del personal (Lote 219, pedido de Luis: el de antes salía alto y chiquito). Las compañías van una al lado de otra, bajitas y con letra legible; si no caben
// en una línea pasan a la siguiente (nunca se recortan ni se estiran a lo alto). La activa va en azul marino.
export default function CompanyChips({
  companies,
  activeId,
  onPick,
}: {
  companies: { tenantId: number; name: string }[];
  activeId: number | null | undefined;
  onPick: (tenantId: number) => void;
}) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  return (
    <View style={styles.row} testID="company-chips">
      {companies.map((c) => {
        const active = c.tenantId === activeId;
        return (
          <Pressable
            key={c.tenantId}
            onPress={() => onPick(c.tenantId)}
            accessibilityRole="button"
            accessibilityState={{ selected: active }}
            style={[styles.chip, active ? { backgroundColor: colors.primary, borderColor: colors.primary } : { backgroundColor: colors.background, borderColor: colors.gray }]}
          >
            <Text numberOfLines={1} style={[styles.label, { color: active ? colors.primaryContrast : colors.text, fontWeight: active ? '700' : '500' }]}>
              {c.name}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const createStyles = (_colors: Colors) => StyleSheet.create({
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, paddingHorizontal: 20, paddingBottom: 8 },
  chip: { minHeight: 36, borderRadius: 18, borderWidth: 1, paddingHorizontal: 14, paddingVertical: 6, alignItems: 'center', justifyContent: 'center', maxWidth: '100%' },
  label: { fontSize: 14 },
});
