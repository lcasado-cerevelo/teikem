import { View, type ViewProps, StyleSheet } from 'react-native';
import { colors } from '@constants/theme';

// Bordered card matching the old atoms/Card.
export default function Card({ style, children, ...rest }: ViewProps) {
  return (
    <View style={[styles.card, style]} {...rest}>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderWidth: 1,
    borderColor: colors.gray,
    borderRadius: 13,
    backgroundColor: colors.background,
  },
});
