import { useState } from 'react';
import { useController, type UseControllerProps } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Platform, Pressable, StyleSheet, TextInput as RNTextInput, type TextInputProps as RNTextInputProps, View } from 'react-native';
import { Text } from '@components/Text';
import { FontAwesome } from '@expo/vector-icons';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

type FormInputProps = UseControllerProps &
  RNTextInputProps & {
    label?: string;
  };

// Controlled input matching the old app's ControlledInput:
// label (16), bordered field radius 10 with shadow, height 40, red error text.
// For password fields (secureTextEntry) it shows an eye toggle to reveal/hide.
export default function FormInput(props: FormInputProps) {
  const { colors } = useTheme();
  const styles = useStyles(createStyles);
  const { t } = useTranslation();
  const { name, rules, defaultValue, label, style, secureTextEntry, ...rest } = props;
  const { field, fieldState } = useController({ name, rules, defaultValue });
  const isPassword = Boolean(secureTextEntry);
  const [hidden, setHidden] = useState(true);

  return (
    <View style={styles.container}>
      {label ? <Text style={styles.label}>{label}</Text> : null}
      <View style={[styles.inputContainer, styles.inputShadow]}>
        <RNTextInput
          style={[styles.input, isPassword && styles.inputWithIcon, style]}
          onChangeText={field.onChange}
          onBlur={field.onBlur}
          value={field.value}
          placeholderTextColor={colors.muted}
          secureTextEntry={isPassword && hidden}
          {...rest}
        />
        {isPassword ? (
          <Pressable
            style={styles.eye}
            onPress={() => setHidden((h) => !h)}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel={hidden ? t('LOGIN.SHOW_PASSWORD') : t('LOGIN.HIDE_PASSWORD')}
          >
            <FontAwesome name={hidden ? 'eye-slash' : 'eye'} size={18} color={colors.muted} />
          </Pressable>
        ) : null}
      </View>
      {fieldState.error?.message ? (
        <Text style={styles.error}>{fieldState.error.message}</Text>
      ) : null}
    </View>
  );
}

const borderRadius = 10;
const createStyles = (colors: Colors) => StyleSheet.create({
  container: { width: '100%', justifyContent: 'center' },
  label: { fontSize: 16, marginVertical: 4, color: colors.text },
  inputContainer: { borderWidth: 0.5, borderColor: colors.text, borderRadius, width: '100%' },
  input: {
    width: '100%',
    borderRadius,
    paddingHorizontal: 7,
    height: 40,
    color: colors.text,
    backgroundColor: colors.background,
  },
  inputWithIcon: { paddingRight: 42 },
  eye: {
    position: 'absolute',
    right: 0,
    top: 0,
    bottom: 0,
    justifyContent: 'center',
    paddingHorizontal: 12,
  },
  inputShadow: Platform.select({
    ios: { shadowOffset: { width: 0, height: 4 }, shadowColor: '#000', shadowOpacity: 0.25, borderRadius },
    android: { elevation: 3, borderRadius },
    default: {},
  }),
  error: { marginLeft: 7, color: colors.danger },
});
