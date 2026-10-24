import { StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Initials avatar (circle with the first letters of the name). Used in the header
// and profile instead of the company logo.
function initials(name?: string): string {
  if (!name) return '?';
  const words = name.split(/[^A-Za-zÀ-ÿ]+/).filter(Boolean);
  const a = words[0]?.[0] ?? '';
  const b = words[1]?.[0] ?? '';
  return (a + b).toUpperCase() || '?';
}

export default function InitialsAvatar({ name, size = 50 }: { name?: string; size?: number }) {
  const styles = useStyles(createStyles);
  return (
    <View style={[styles.circle, { width: size, height: size, borderRadius: size / 2 }]}>
      <Text style={[styles.text, { fontSize: size * 0.4 }]}>{initials(name)}</Text>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  circle: { backgroundColor: colors.primary, alignItems: 'center', justifyContent: 'center' },
  text: { color: colors.primaryContrast, fontWeight: '700' },
});
