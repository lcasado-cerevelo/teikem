import { View, type ViewProps, StyleSheet } from 'react-native';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Bordered card matching the old atoms/Card.
export default function Card({ style, children, ...rest }: ViewProps) {
  const styles = useStyles(createStyles);
  return (
    <View style={[styles.card, style]} {...rest}>
      {children}
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  card: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 13,
    backgroundColor: colors.background,
  },
});
