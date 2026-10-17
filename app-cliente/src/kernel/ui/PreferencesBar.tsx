// Las dos preferencias que acompañan a toda pantalla (decidido con Luis, 2026-10-17): el IDIOMA (español por omisión, inglés) y el TEMA (Automático, Claro, Oscuro). Una fila compacta que se pone en el login, en la página
// pública «¿Cómo va mi orden?» y en el documento legal (donde todavía no hay Ajustes); en Ajustes van como tarjetas. La elección se recuerda y cambia al instante, sin recargar.
import { Pressable, Text, View } from 'react-native'

import { LANGS, type Lang } from '../i18n/i18n'
import { useT } from '../i18n/useT'
import { THEME_PREFS, type ThemePref, useTheme } from './theme'

const LANG_NAME: Record<Lang, string> = { es: 'Español', en: 'English' }

function Segment({ label, selected, onPress, testID, accessibilityLabel }: { label: string; selected: boolean; onPress: () => void; testID?: string; accessibilityLabel?: string }) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ selected }}
      aria-checked={selected}
      accessibilityLabel={accessibilityLabel ?? label}
      testID={testID}
      onPress={onPress}
      style={{ paddingHorizontal: 12, minHeight: 36, justifyContent: 'center', borderRadius: 8, backgroundColor: selected ? colors.flow : 'transparent' }}
    >
      <Text style={{ fontSize: 13, fontWeight: '700', color: selected ? '#fff' : colors.link }}>{label}</Text>
    </Pressable>
  )
}

function Group({ children, label }: { children: React.ReactNode; label: string }) {
  const { colors } = useTheme()
  return (
    <View accessibilityRole="radiogroup" accessibilityLabel={label} style={{ flexDirection: 'row', gap: 2, padding: 2, borderRadius: 10, borderWidth: 1, borderColor: colors.line, backgroundColor: colors.card }}>
      {children}
    </View>
  )
}

/** Selector de idioma: «Español» / «English». */
export function LanguageSwitch() {
  const { t, lang, setLang } = useT()
  return (
    <Group label={t('prefs.language')}>
      {LANGS.map((l) => (
        <Segment key={l} label={LANG_NAME[l]} selected={lang === l} onPress={() => setLang(l)} testID={`lang-${l}`} accessibilityLabel={LANG_NAME[l]} />
      ))}
    </Group>
  )
}

/** Selector de tema: Automático (sigue al aparato), Claro, Oscuro. */
export function ThemeSwitch() {
  const { t } = useT()
  const { pref, setPref } = useTheme()
  return (
    <Group label={t('prefs.theme')}>
      {THEME_PREFS.map((p: ThemePref) => (
        <Segment key={p} label={t(`prefs.themes.${p}`)} selected={pref === p} onPress={() => setPref(p)} testID={`theme-${p}`} />
      ))}
    </Group>
  )
}

/** Idioma y tema en una fila (se parte en dos líneas si no caben). */
export function PreferencesBar() {
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8, justifyContent: 'center' }}>
      <LanguageSwitch />
      <ThemeSwitch />
    </View>
  )
}
