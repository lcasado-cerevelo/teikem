import { Pressable } from 'react-native';
import { SimpleLineIcons } from '@expo/vector-icons';
import { useAuth } from '@hooks/AuthContext';
import { colors } from '@constants/theme';

// Replicates the old LogoutButton (login icon, top-right).
export default function LogoutButton() {
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
