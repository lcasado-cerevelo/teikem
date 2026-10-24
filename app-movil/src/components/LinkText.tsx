import { Linking, StyleSheet } from 'react-native';
import { Text } from '@components/Text';
import { splitLinks } from '@/utils/links';
import { useStyles } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';
import type { StyleProp, TextStyle } from 'react-native';

// Un texto donde los teléfonos, correos y direcciones web se pueden tocar: el teléfono abre el marcador con el número puesto (el chofer solo confirma la llamada),
// el correo abre el correo y el enlace abre el navegador. En una tableta sin llamadas no pasa nada grave: el sistema decide qué hacer con el enlace.
export default function LinkText({ children, style, linkStyle }: { children: string; style?: StyleProp<TextStyle>; linkStyle?: StyleProp<TextStyle> }) {
  const styles = useStyles(createStyles);
  const parts = splitLinks(children);
  return (
    <Text style={style}>
      {parts.map((p, i) =>
        p.type === 'text' ? (
          p.text
        ) : (
          <Text key={i} style={[styles.link, linkStyle]} onPress={() => void Linking.openURL(p.href).catch(() => undefined)} accessibilityRole="link">
            {p.text}
          </Text>
        ),
      )}
    </Text>
  );
}

const createStyles = (colors: Colors) => StyleSheet.create({
  link: { color: colors.primary, textDecorationLine: 'underline' },
});
