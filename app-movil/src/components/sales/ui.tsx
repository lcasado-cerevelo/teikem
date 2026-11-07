import type { ReactNode } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Switch, View, type TextInputProps } from 'react-native';
import { Text, TextInput } from '@components/Text';
import { useStyles, useTheme } from '@hooks/ThemeContext';
import type { Colors } from '@constants/theme';

// Lote 226 — piezas chicas del pedido en el teléfono (campo con su etiqueta, interruptor, opción, botones, distintivo, aviso de error). Colores del tema
// (claro y oscuro); textos siempre visibles en los botones; cada campo con su etiqueta (también para el lector de pantalla).

export function Field({ label, required, help, error, ...input }: TextInputProps & { label: string; required?: boolean; help?: string | null; error?: string | null }) {
  const styles = useStyles(createStyles);
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{`${label}${required ? ' *' : ''}`}</Text>
      <TextInput {...input} accessibilityLabel={label} style={[styles.input, error ? styles.inputError : null, input.multiline ? styles.multiline : null, input.style]} />
      {error ? <Text style={styles.error} accessibilityLiveRegion="polite">{error}</Text> : help ? <Text style={styles.help}>{help}</Text> : null}
    </View>
  );
}

export function SwitchRow({ label, help, value, onChange, disabled }: { label: string; help?: string | null; value: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  const styles = useStyles(createStyles);
  const { colors } = useTheme();
  return (
    <Pressable style={styles.switchRow} onPress={() => !disabled && onChange(!value)} accessibilityRole="switch" accessibilityState={{ checked: value, disabled }} accessibilityLabel={label}>
      <View style={styles.flex}>
        <Text style={styles.switchLabel}>{label}</Text>
        {help ? <Text style={styles.help}>{help}</Text> : null}
      </View>
      <Switch value={value} onValueChange={onChange} disabled={disabled} trackColor={{ true: colors.primary, false: colors.gray }} accessibilityLabel={label} />
    </Pressable>
  );
}

/** Una opción de una lista corta (entrega o recogido, a dónde, forma de pago): tarjeta con su marca de escogida. */
export function Choice({ label, help, selected, onPress, disabled }: { label: string; help?: string | null; selected: boolean; onPress: () => void; disabled?: boolean }) {
  const styles = useStyles(createStyles);
  const { colors } = useTheme();
  return (
    <Pressable
      style={[styles.choice, selected && { borderColor: colors.primary, borderWidth: 2 }, disabled && styles.disabled]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="radio"
      accessibilityState={{ selected, disabled }}
      accessibilityLabel={help ? `${label}. ${help}` : label}
    >
      <View style={[styles.dot, { borderColor: selected ? colors.primary : colors.muted }]}>{selected ? <View style={[styles.dotIn, { backgroundColor: colors.primary }]} /> : null}</View>
      <View style={styles.flex}>
        <Text style={styles.choiceLabel}>{label}</Text>
        {help ? <Text style={styles.help}>{help}</Text> : null}
      </View>
    </Pressable>
  );
}

export function Button({ label, onPress, kind = 'primary', busy, disabled, flex, testID }: {
  label: string;
  onPress: () => void;
  kind?: 'primary' | 'outline' | 'danger';
  busy?: boolean;
  disabled?: boolean;
  flex?: number;
  testID?: string;
}) {
  const styles = useStyles(createStyles);
  const { colors } = useTheme();
  const primary = kind === 'primary';
  return (
    <Pressable
      testID={testID}
      style={[primary ? styles.primary : styles.outline, kind === 'danger' && { borderColor: colors.danger }, flex ? { flex } : null, (disabled || busy) && styles.disabled]}
      onPress={onPress}
      disabled={disabled || busy}
      accessibilityRole="button"
      accessibilityState={{ disabled: disabled || busy, busy }}
      accessibilityLabel={label}
    >
      {busy ? (
        <ActivityIndicator color={primary ? colors.primaryContrast : colors.primary} />
      ) : (
        <Text style={[primary ? styles.primaryText : styles.outlineText, kind === 'danger' && { color: colors.danger }]} numberOfLines={2}>{label}</Text>
      )}
    </Pressable>
  );
}

export function Badge({ label, tone = 'info' }: { label: string; tone?: 'info' | 'warn' }) {
  const styles = useStyles(createStyles);
  const { colors } = useTheme();
  const color = tone === 'warn' ? colors.danger : colors.primary;
  return (
    <View style={[styles.badge, { borderColor: color }]}>
      <Text style={[styles.badgeText, { color }]} numberOfLines={1}>{label}</Text>
    </View>
  );
}

export function SectionTitle({ children }: { children: ReactNode }) {
  const styles = useStyles(createStyles);
  return <Text style={styles.section} accessibilityRole="header">{children}</Text>;
}

export function ErrorText({ children }: { children: ReactNode }) {
  const styles = useStyles(createStyles);
  return <Text style={styles.errorBox} accessibilityRole="alert" accessibilityLiveRegion="polite">{children}</Text>;
}

export function Help({ children }: { children: ReactNode }) {
  const styles = useStyles(createStyles);
  return <Text style={styles.help}>{children}</Text>;
}

/** Una fila «etiqueta … valor» del resumen. */
export function SumRow({ label, value, strong, note }: { label: string; value: string; strong?: boolean; note?: string | null }) {
  const styles = useStyles(createStyles);
  return (
    <View style={styles.sumRow}>
      <View style={styles.flex}>
        <Text style={[styles.sumLabel, strong && styles.strong]}>{label}</Text>
        {note ? <Text style={styles.help}>{note}</Text> : null}
      </View>
      <Text style={[styles.sumValue, strong && styles.strong]}>{value}</Text>
    </View>
  );
}

export const createStyles = (colors: Colors) => StyleSheet.create({
  flex: { flex: 1, minWidth: 0 },
  field: { gap: 4 },
  label: { fontSize: 14, fontWeight: '600', color: colors.text },
  input: { borderWidth: 1, borderColor: colors.gray, borderRadius: 10, paddingHorizontal: 12, paddingVertical: 10, fontSize: 16, color: colors.text, backgroundColor: colors.background },
  inputError: { borderColor: colors.danger },
  multiline: { minHeight: 80, textAlignVertical: 'top' },
  help: { fontSize: 12, color: colors.muted },
  error: { fontSize: 13, color: colors.danger },
  errorBox: { fontSize: 14, color: colors.danger, fontWeight: '600' },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 6 },
  switchLabel: { fontSize: 15, color: colors.text, fontWeight: '600' },
  choice: { flexDirection: 'row', alignItems: 'center', gap: 12, borderWidth: 1, borderColor: colors.gray, borderRadius: 12, padding: 12, backgroundColor: colors.background },
  choiceLabel: { fontSize: 15, fontWeight: '600', color: colors.text },
  dot: { width: 20, height: 20, borderRadius: 10, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
  dotIn: { width: 10, height: 10, borderRadius: 5 },
  primary: { backgroundColor: colors.primary, borderRadius: 12, paddingVertical: 13, paddingHorizontal: 16, alignItems: 'center', justifyContent: 'center', minHeight: 48 },
  primaryText: { color: colors.primaryContrast, fontWeight: '700', fontSize: 15, textAlign: 'center' },
  outline: { borderWidth: 1, borderColor: colors.primary, borderRadius: 12, paddingVertical: 12, paddingHorizontal: 16, alignItems: 'center', justifyContent: 'center', minHeight: 48 },
  outlineText: { color: colors.primary, fontWeight: '600', fontSize: 15, textAlign: 'center' },
  disabled: { opacity: 0.5 },
  badge: { borderWidth: 1, borderRadius: 10, paddingHorizontal: 8, paddingVertical: 2, alignSelf: 'flex-start' },
  badgeText: { fontSize: 12, fontWeight: '700' },
  section: { fontSize: 16, fontWeight: '700', color: colors.text, marginTop: 6 },
  sumRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 12, paddingVertical: 4 },
  sumLabel: { fontSize: 14, color: colors.text },
  sumValue: { fontSize: 14, color: colors.text, fontVariant: ['tabular-nums'] },
  strong: { fontWeight: '700', fontSize: 16 },
});
