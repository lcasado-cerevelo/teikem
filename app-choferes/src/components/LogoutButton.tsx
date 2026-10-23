import { Pressable } from 'react-native';
import { SimpleLineIcons } from '@expo/vector-icons';
import { useAuth } from '@hooks/AuthContext';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Replicates the old LogoutButton (login icon, top-right).
export default function LogoutButton() {
  const { colors } = useTheme();
  const { logout } = useAuth();
  return (
    <Pressable onPress={() => void logout()} accessibilityRole="button">
      {({ pressed }) => (
        <SimpleLineIcons
          name="login"
          size={25}
          color={colors.text}
          style={{ marginRight: 15, opacity: pressed ? 0.5 : 1 }}
        />
      )}
    </Pressable>
  );
}
