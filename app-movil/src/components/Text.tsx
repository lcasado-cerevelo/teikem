import { Text as RNText, TextInput as RNTextInput, type TextInputProps, type TextProps } from 'react-native';
import { useTheme } from '@hooks/ThemeContext';

// Texto y campo de texto con el color del tema por omisión (sin esto, un texto sin color propio se vería negro sobre el fondo oscuro).
// Un `color` en el estilo de la pantalla manda sobre el del tema.
export function Text({ style, ...rest }: TextProps) {
  const { colors } = useTheme();
  return <RNText {...rest} style={[{ color: colors.text }, style]} />;
}

export function TextInput({ style, placeholderTextColor, ...rest }: TextInputProps) {
  const { colors } = useTheme();
  return <RNTextInput {...rest} placeholderTextColor={placeholderTextColor ?? colors.muted} style={[{ color: colors.text }, style]} />;
}
