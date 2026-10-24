import { Pressable, StyleSheet } from 'react-native';
import { Text } from '@components/Text';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Ported from the old app's atoms/Chip: a pill that's filled navy when active,
// white with a gray border + gray text when inactive. Used by the ShipmentDetails
// tabs (Detalles / Piezas / Comentarios / Acciones).
export default function Chip({
  label,
  active,
  onPress,
}: {
  label: string;
  active: boolean;
  onPress: () => void;
}) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  return (
    <Pressable
      onPress={onPress}
      style={[
        styles.container,
        active
          ? { backgroundColor: colors.primary, borderColor: colors.primary }
          : { backgroundColor: colors.background, borderColor: colors.gray },
      ]}
    >
      <Text
        adjustsFontSizeToFit
        numberOfLines={1}
        style={[styles.label, { color: active ? colors.primaryContrast : colors.muted }]}
      >
        {label}
      </Text>
    </Pressable>
  );
}

const TEXT_SIZE = 12;

const createStyles = (colors: Colors) => StyleSheet.create({
  container: {
    flex: 1,
    borderRadius: TEXT_SIZE + TEXT_SIZE / 2,
    paddingHorizontal: TEXT_SIZE,
    paddingVertical: TEXT_SIZE / 2,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  label: { fontSize: TEXT_SIZE, fontWeight: '400' },
});
