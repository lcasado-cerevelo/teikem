// Kit mínimo de la app del cliente: los componentes que usan todas las pantallas (Screen, Card, Button, Field, Check, Chip, Notice, Spinner). Los colores NUNCA son fijos: salen de la paleta del tema vigente
// (claro u oscuro, theme.ts), así que todo cambia de tema al instante. Móvil primero; en la web el contenido se centra en una columna de 560 px.
import { useMemo, type ReactNode } from 'react'
import { ActivityIndicator, Platform, Pressable, ScrollView, StyleSheet, Text, TextInput, View, type StyleProp, type TextInputProps, type ViewStyle } from 'react-native'

import { LegalFooter } from './LegalFooter'
import { WideBack } from '../../features/shell/WideBack'
import { NARROW_COLUMN, WIDE_COLUMN, useIsWide } from './layout'
import { useColors, type Palette } from './theme'

export { useColors } from './theme'

function makeStyles(c: Palette) {
  return StyleSheet.create({
    screen: { flex: 1, backgroundColor: c.bg },
    screenContent: { flexGrow: 1 },
    // 2026-10-23 (pedido de Luis): el cuerpo crece hasta llenar la ventana, así el pie legal queda pegado abajo cuando el contenido es corto y al final del contenido cuando es largo (nunca «flotando» a media página)
    body: { flexGrow: 1, width: '100%', alignItems: 'center' },
    column: { width: '100%', maxWidth: Platform.OS === 'web' ? NARROW_COLUMN : undefined, gap: 12, zIndex: 1 },
    columnWide: { width: '100%', maxWidth: WIDE_COLUMN, gap: 12, zIndex: 1 },
    padded: { padding: 16 },
    paddedWide: { padding: 24 },
    title: { fontSize: 22, fontWeight: '700', color: c.text },
    muted: { fontSize: 13, color: c.muted, lineHeight: 18 },
    card: { backgroundColor: c.card, borderRadius: 12, borderWidth: 1, borderColor: c.line, padding: 14, gap: 6 },
    pressed: { opacity: 0.85 },
    btn: { minHeight: 48, borderRadius: 10, alignItems: 'center', justifyContent: 'center', paddingHorizontal: 16 },
    btn_primary: { backgroundColor: c.flow },
    btn_secondary: { backgroundColor: c.secondary, borderWidth: 1, borderColor: c.secondaryLine },
    btn_danger: { backgroundColor: c.dangerSolid },
    btn_link: { backgroundColor: 'transparent' },
    btnOff: { opacity: 0.5 },
    btnText: { fontSize: 16, fontWeight: '600' },
    btnText_primary: { color: c.onFlow },
    btnText_secondary: { color: c.onSecondary },
    btnText_danger: { color: '#fff' },
    btnText_link: { color: c.link },
    field: { gap: 4 },
    label: { fontSize: 13, fontWeight: '600', color: c.text },
    input: { minHeight: 48, borderRadius: 10, borderWidth: 1, borderColor: c.line, backgroundColor: c.card, paddingHorizontal: 12, fontSize: 16, color: c.text },
    inputError: { borderColor: c.danger },
    error: { fontSize: 12, color: c.danger },
    check: { flexDirection: 'row', gap: 10, alignItems: 'flex-start', paddingVertical: 4, minHeight: 44 },
    box: { width: 24, height: 24, borderRadius: 6, borderWidth: 2, borderColor: c.muted, alignItems: 'center', justifyContent: 'center', backgroundColor: c.card, marginTop: 1 },
    boxOn: { backgroundColor: c.flow, borderColor: c.flow },
    tick: { color: c.onFlow, fontWeight: '800', fontSize: 15, lineHeight: 18 },
    checkText: { flex: 1, gap: 2 },
    checkLabel: { fontSize: 15, color: c.text, fontWeight: '500' },
    chip: { alignSelf: 'flex-start', borderRadius: 999, paddingHorizontal: 10, paddingVertical: 3, maxWidth: '100%' },
    chipText: { fontSize: 12, fontWeight: '700' },
    notice: { borderRadius: 10, padding: 12 },
    noticeText: { fontSize: 14, fontWeight: '600', lineHeight: 20 },
    spinner: { padding: 32, alignItems: 'center' },
  })
}

export function useStyles() {
  const c = useColors()
  return useMemo(() => makeStyles(c), [c])
}

/** Pantalla con desplazamiento, márgenes y (en la web) columna centrada. */
export function Screen({ children, padded = true, scroll = true, wide = false }: { children: ReactNode; padded?: boolean; scroll?: boolean; wide?: boolean }) {
  const styles = useStyles()
  const isWide = useIsWide()
  // 2026-10-22: en escritorio las listas y los detalles (wide) usan una columna ancha; los formularios siguen angostos. El pie legal va al final de cada pantalla con desplazamiento.
  const inner = <View style={[wide && isWide ? styles.columnWide : styles.column, padded && (isWide ? styles.paddedWide : styles.padded)]}>
      {isWide && <WideBack />}
      {children}
    </View>
  return scroll ? (
    <ScrollView style={styles.screen} contentContainerStyle={styles.screenContent} keyboardShouldPersistTaps="handled">
      <View style={styles.body}>{inner}</View>
      <LegalFooter />
    </ScrollView>
  ) : (
    <View style={[styles.screen, styles.body]}>{inner}</View>
  )
}

export function Title({ children }: { children: ReactNode }) {
  const styles = useStyles()
  return <Text accessibilityRole="header" style={styles.title}>{children}</Text>
}

export function Muted({ children, style }: { children: ReactNode; style?: StyleProp<ViewStyle> }) {
  const styles = useStyles()
  return <Text style={[styles.muted, style as never]}>{children}</Text>
}

export function Card({ children, onPress, style }: { children: ReactNode; onPress?: () => void; style?: StyleProp<ViewStyle> }) {
  const styles = useStyles()
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
  const styles = useStyles()
  const c = useColors()
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
      {busy ? <ActivityIndicator color={variant === 'primary' ? c.onFlow : variant === 'secondary' ? c.onSecondary : variant === 'danger' ? '#fff' : c.link} /> : <Text style={[styles.btnText, styles[`btnText_${variant}`]]}>{title}</Text>}
    </Pressable>
  )
}

export function Field({ label, error, ...input }: { label: string; error?: string | null } & TextInputProps) {
  const styles = useStyles()
  const c = useColors()
  return (
    <View style={styles.field}>
      <Text style={styles.label}>{label}</Text>
      <TextInput accessibilityLabel={label} placeholderTextColor={c.muted} style={[styles.input, !!error && styles.inputError]} {...input} />
      {!!error && <Text style={styles.error}>{error}</Text>}
    </View>
  )
}

/** Casilla con texto (y una ayuda debajo, opcional). `compact`: baja y con letra chica (celdas de tabla y barras de acciones). */
export function Check({ label, help, value, onChange, disabled, testID, compact }: { label: string; help?: string; value: boolean; onChange: (v: boolean) => void; disabled?: boolean; testID?: string; compact?: boolean }) {
  const styles = useStyles()
  return (
    <Pressable accessibilityRole="checkbox" accessibilityState={{ checked: value, disabled: !!disabled }} aria-checked={value} accessibilityLabel={label} testID={testID} disabled={disabled} onPress={() => onChange(!value)} style={[styles.check, compact && { minHeight: 28, paddingVertical: 0, alignItems: 'center' }, disabled && { opacity: 0.55 }]}>
      <View style={[styles.box, value && styles.boxOn, compact && { width: 20, height: 20, marginTop: 0 }]}>{value && <Text style={[styles.tick, compact && { fontSize: 12, lineHeight: 14 }]}>✓</Text>}</View>
      {(!!label || !!help) && (
        <View style={styles.checkText}>
          {!!label && <Text numberOfLines={compact ? 1 : undefined} style={[styles.checkLabel, compact && { fontSize: 13 }]}>{label}</Text>}
          {!!help && <Text style={styles.muted}>{help}</Text>}
        </View>
      )}
    </Pressable>
  )
}

export type Tone = 'ok' | 'warn' | 'danger' | 'info' | 'neutral'

function toneColors(c: Palette): Record<Tone, { bg: string; fg: string }> {
  return {
    ok: { bg: c.okBg, fg: c.ok },
    warn: { bg: c.warnBg, fg: c.warn },
    danger: { bg: c.dangerBg, fg: c.danger },
    info: { bg: c.infoBg, fg: c.info },
    neutral: { bg: c.neutralBg, fg: c.muted },
  }
}

export function Chip({ label, tone = 'neutral' }: { label: string; tone?: Tone }) {
  const styles = useStyles()
  const t = toneColors(useColors())[tone]
  return (
    <View style={[styles.chip, { backgroundColor: t.bg }]}>
      <Text style={[styles.chipText, { color: t.fg }]} numberOfLines={1}>{label}</Text>
    </View>
  )
}

export function Notice({ children, tone = 'danger' }: { children: ReactNode; tone?: Tone }) {
  const styles = useStyles()
  const t = toneColors(useColors())[tone]
  return (
    <View accessibilityRole={tone === 'danger' ? 'alert' : undefined} style={[styles.notice, { backgroundColor: t.bg }]}>
      <Text style={[styles.noticeText, { color: t.fg }]}>{children}</Text>
    </View>
  )
}

export function Spinner() {
  const styles = useStyles()
  const c = useColors()
  return (
    <View style={styles.spinner}>
      <ActivityIndicator color={c.link} size="large" />
    </View>
  )
}
