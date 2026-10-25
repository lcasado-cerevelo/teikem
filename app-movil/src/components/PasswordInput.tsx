import { useState } from 'react';
import { Pressable, StyleSheet, TextInput as RNTextInput, View, type TextInputProps } from 'react-native';
import { FontAwesome } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Campo de contraseña suelto (sin react-hook-form) con el ojito para ver lo que se escribió (Lote 219: «donde quiera que haya un password»). Los de formulario usan FormInput, que trae el mismo ojito.
// El teléfono no deja saber si las mayúsculas están activadas (es del teclado del sistema), así que aquí solo está el ojito.
export default function PasswordInput(props: Omit<TextInputProps, 'secureTextEntry'>) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const [hidden, setHidden] = useState(true);
  const { style, ...rest } = props;
  return (
    <View>
      <RNTextInput style={[style, styles.padRight]} placeholderTextColor={colors.muted} autoCapitalize="none" autoCorrect={false} {...rest} secureTextEntry={hidden} />
      <Pressable style={styles.eye} onPress={() => setHidden((h) => !h)} hitSlop={8} accessibilityRole="button" accessibilityLabel={hidden ? t('LOGIN.SHOW_PASSWORD') : t('LOGIN.HIDE_PASSWORD')} testID="password-eye">
        <FontAwesome name={hidden ? 'eye-slash' : 'eye'} size={18} color={colors.muted} />
      </Pressable>
    </View>
  );
}

const createStyles = (_colors: Colors) => StyleSheet.create({
  padRight: { paddingRight: 42 },
  eye: { position: 'absolute', right: 0, top: 0, bottom: 0, justifyContent: 'center', paddingHorizontal: 12 },
});
