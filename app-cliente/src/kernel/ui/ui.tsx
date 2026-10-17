// Kit mínimo de la app del cliente: colores de la marca Teikem y los componentes que usan todas las pantallas (Screen, Card, Button, Field, Check, Chip, Notice, Spinner). Móvil primero; en la web el contenido se
// centra en una columna de 560 px. Solo tema claro.
import type { ReactNode } from 'react'
import { ActivityIndicator, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View, type StyleProp, type TextInputProps, type ViewStyle } from 'react-native'

export const colors = {
  brand: '#0B2C66',
  flow: '#1D6FE8',
  bg: '#F1F3F8',
  card: '#FFFFFF',
  line: '#D9DDE3',
  text: '#111827',
  muted: '#6B7280',
  danger: '#B42318',
  dangerBg: '#FDECEA',
  ok: '#067647',
  okBg: '#E7F6EC',
  warn: '#B54708',
  warnBg: '#FEF0C7',
  info: '#175CD3',
  infoBg: '#E5EEFE',
} as const

/** Pantalla con desplazamiento, márgenes y (en la web) columna centrada. */
export function Screen({ children, padded = true, scroll = true }: { children: ReactNode; padded?: boolean; scroll?: boolean }) {
  const inner = <View style={[styles.column, padded && styles.padded]}>{children}</View>
  return scroll ? (
    <ScrollView style={styles.screen} contentContainerStyle={styles.screenContent} keyboardShouldPersistTaps="handled">
      {inner}
    </ScrollView>
  ) : (
    <View style={[styles.screen, styles.screenContent]}>{inner}</View>
  )
}

export function Title({ children }: { children: ReactNode }) {
  return <Text accessibilityRole="header" style={styles.title}>{children}</Text>
}

export function Muted({ children, style }: { children: ReactNode; style?: StyleProp<ViewStyle> }) {
  return <Text style={[styles.muted, style as never]}>{children}</Text>
}

export function Card({ children, onPress, style }: { children: ReactNode; onPress?: () => void; style?: StyleProp<ViewStyle> }) {
  if (onPress) {
    return (
      <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.card, pressed && styles.pressed, style]}>
        {children}
      </Pressable>
    )
  }
  return <View style={[styles.card, style]}>{children}</View>
}

export function Button({ title, onPress, variant = 'primary', disabled, busy, testID }: { title: string; onPress: () => void; variant?: 'primary' | 'secondary' | 'danger' | 'link'; disabled?: boolean; busy?: boolean; testID?: string }) {
  const off = disabled || busy
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: !!off, busy: !!busy }}
      testID={testID}
      disabled={off}
      onPress={onPress}
      style={({ pressed }) => [styles.btn, styles[`btn_${variant}`], off && styles.btnOff, pressed && styles.pressed]}
    >
      {busy ? <ActivityIndicator color={variant === 'primary' || variant === 'danger' ? '#fff' : colors.brand} /> : <Text style={[styles.btnText, styles[`btnText_${variant}`]]}>{title}</Text>}
    </Pressable>
  )
}

export function Field({ label, error, ...input }: { label: string; error?: string | null } & TextInputProps) {
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <TextInput accessibilityLabel={label} placeholderTextColor={colors.muted} style={[styles.input, !!error && styles.inputError]} {...input} />
      {!!error && <Text style={styles.error}>{error}</Text>}
    </View>
  )
}

/** Casilla con texto (y una ayuda debajo, opcional). */
export function Check({ label, help, value, onChange, disabled, testID }: { label: string; help?: string; value: boolean; onChange: (v: boolean) => void; disabled?: boolean; testID?: string }) {
  return (
    <Pressable accessibilityRole="checkbox" accessibilityState={{ checked: value, disabled: !!disabled }} aria-checked={value} accessibilityLabel={label} testID={testID} disabled={disabled} onPress={() => onChange(!value)} style={styles.check}>
      <View style={[styles.box, value && styles.boxOn]}>{value && <Text style={styles.tick}>✓</Text>}</View>
      <View style={styles.checkText}>
        <Text style={styles.checkLabel}>{label}</Text>
        {!!help && <Text style={styles.muted}>{help}</Text>}
      </View>
    </Pressable>
  )
}

type Tone = 'ok' | 'warn' | 'danger' | 'info' | 'neutral'
const toneColors: Record<Tone, { bg: string; fg: string }> = {
  ok: { bg: colors.okBg, fg: colors.ok },
  warn: { bg: colors.warnBg, fg: colors.warn },
  danger: { bg: colors.dangerBg, fg: colors.danger },
  info: { bg: colors.infoBg, fg: colors.info },
  neutral: { bg: '#EEF0F4', fg: colors.muted },
}

export function Chip({ label, tone = 'neutral' }: { label: string; tone?: Tone }) {
  return (
    <View style={[styles.chip, { backgroundColor: toneColors[tone].bg }]}>
      <Text style={[styles.chipText, { color: toneColors[tone].fg }]} numberOfLines={1}>{label}</Text>
    </View>
  )
}

export function Notice({ children, tone = 'danger' }: { children: ReactNode; tone?: Tone }) {
  return (
    <View accessibilityRole={tone === 'danger' ? 'alert' : undefined} style={[styles.notice, { backgroundColor: toneColors[tone].bg }]}>
      <Text style={[styles.noticeText, { color: toneColors[tone].fg }]}>{children}</Text>
    </View>
  )
}

export function Spinner() {
  return (
    <View style={styles.spinner}>
      <ActivityIndicator color={colors.flow} size="large" />
    </View>
  )
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.bg },
  screenContent: { flexGrow: 1, alignItems: 'center' },
  column: { width: '100%', maxWidth: Platform.OS === 'web' ? 560 : undefined, gap: 12 },
  padded: { padding: 16 },
  title: { fontSize: 22, fontWeight: '700', color: colors.text },
  muted: { fontSize: 13, color: colors.muted, lineHeight: 18 },
  card: { backgroundColor: colors.card, borderRadius: 12, borderWidth: 1, borderColor: colors.line, padding: 14, gap: 6 },
  pressed: { opacity: 0.85 },
  btn: { minHeight: 48, borderRadius: 10, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 16 },
  btn_primary: { backgroundColor: colors.flow },
  btn_secondary: { backgroundColor: '#fff', borderWidth: 1, borderColor: colors.line },
  btn_danger: { backgroundColor: colors.danger },
  btn_link: { backgroundColor: 'transparent' },
  btnOff: { opacity: 0.5 },
  btnText: { fontSize: 16, fontWeight: '600' },
  btnText_primary: { color: '#fff' },
  btnText_secondary: { color: colors.brand },
  btnText_danger: { color: '#fff' },
  btnText_link: { color: colors.flow },
  field: { gap: 4 },
  label: { fontSize: 13, fontWeight: '600', color: colors.text },
  input: { minHeight: 48, borderRadius: 10, borderWidth: 1, borderColor: colors.line, backgroundColor: '#fff', paddingHorizontal: 12, fontSize: 16, color: colors.text },
  inputError: { borderColor: colors.danger },
  error: { fontSize: 12, color: colors.danger },
  check: { flexDirection: 'row', gap: 10, alignItems: 'flex-start', paddingVertical: 4, minHeight: 44 },
  box: { width: 24, height: 24, borderRadius: 6, borderWidth: 2, borderColor: colors.muted, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff', marginTop: 1 },
  boxOn: { backgroundColor: colors.flow, borderColor: colors.flow },
  tick: { color: '#fff', fontWeight: '800', fontSize: 15, lineHeight: 18 },
  checkText: { flex: 1, gap: 2 },
  checkLabel: { fontSize: 15, color: colors.text, fontWeight: '500' },
  chip: { alignSelf: 'flex-start', borderRadius: 999, paddingHorizontal: 10, paddingVertical: 3, maxWidth: '100%' },
  chipText: { fontSize: 12, fontWeight: '700' },
  notice: { borderRadius: 10, padding: 12 },
  noticeText: { fontSize: 14, fontWeight: '600', lineHeight: 20 },
  spinner: { padding: 32, alignItems: 'center' },
})
