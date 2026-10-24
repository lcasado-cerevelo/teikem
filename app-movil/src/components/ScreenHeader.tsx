import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from '@components/Text';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Replicates the old PageHeader: centered title (20/700), back arrow at left.
export default function ScreenHeader({ title }: { title: string }) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const router = useRouter();
  return (
    <View style={styles.header}>
      <Pressable style={styles.back} onPress={() => router.back()} accessibilityRole="button">
        <Ionicons name="arrow-back" size={26} color={colors.text} />
      </Pressable>
      <Text style={styles.title}>{title}</Text>
    </View>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  header: { position: 'relative', alignItems: 'center', justifyContent: 'center', height: 110 },
  title: { fontWeight: '700', fontSize: 20, color: colors.text },
  back: { position: 'absolute', left: 24 },
});
