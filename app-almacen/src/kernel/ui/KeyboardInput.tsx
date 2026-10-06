import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { Keyboard, Pressable, StyleSheet, Text, TextInput, View, type TextInputProps } from 'react-native'

import { useT } from '../i18n/useT'
import { useFieldFocus, type MeasurableField } from './keyboardScroll'
import { colors, radius, spacing, touchTarget } from './theme'

/**
 * Campo de texto con el botón «⌨» (pedido del dueño 2026-10-05: donde haya un campo de texto, la opción de ver el teclado en pantalla).
 * Igual que ScanField (docs/mobile/mejoras-ux-zebra.md §2): el teclado en pantalla NO aparece al enfocar (el Zebra tiene teclado físico y lector);
 * el botón lo muestra para escribir a mano y lo vuelve a esconder.
 *
 * - Por defecto trae su propio botón al lado (`toggle`).
 * - Con `toggle={false}` no pinta el botón: el teclado lo gobierna quien lo usa con `softKeyboard` (una lista de campos con UN solo botón,
 *   ver `useSoftKeyboard` (useSoftKeyboard.ts) y `KeyboardToggleButton`).
 */
export interface KeyboardInputProps extends TextInputProps {
  /** Pinta el botón «⌨» junto al campo (por defecto sí). */
  toggle?: boolean
  /** Con `toggle={false}`: si el teclado en pantalla está visible (lo manda quien tiene el botón compartido). */
  softKeyboard?: boolean
}

export function KeyboardToggleButton({ on, onPress }: { on: boolean; onPress: () => void }) {
  const { t } = useT()
  const label = t(on ? 'scan.hideKeyboard' : 'scan.showKeyboard')
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ selected: on }} onPress={onPress} style={[styles.btn, on && styles.btnOn]}>
      <Text style={styles.icon}>⌨</Text>
    </Pressable>
  )
}

export const KeyboardInput = forwardRef<TextInput, KeyboardInputProps>(function KeyboardInput({ toggle = true, softKeyboard, style, onFocus, ...props }, ref) {
  const inner = useRef<TextInput>(null)
  useImperativeHandle(ref, () => inner.current as TextInput)
  // Lote A9: dentro de un KeyboardScreen, al enfocarse con el teclado en pantalla ya fuera, la pantalla se desplaza hasta este campo
  const getField = useCallback(() => inner.current as unknown as MeasurableField | null, [])
  const handleFocus = useFieldFocus(getField, onFocus)
  const [own, setOwn] = useState(false)
  const visible = toggle ? own : softKeyboard === true

  // Cambiar la visibilidad con el campo enfocado: no basta la propiedad, hay que volver a enfocarlo (igual que ScanField).
  const first = useRef(true)
  useEffect(() => {
    if (first.current) {
      first.current = false
      return
    }
    const input = inner.current
    if (!input?.isFocused?.()) return
    input.blur()
    const id = setTimeout(() => input.focus(), 50)
    return () => clearTimeout(id)
  }, [visible])

  const toggleOwn = () => {
    if (own) Keyboard.dismiss()
    setOwn((v) => !v)
    if (!inner.current) return
    // vuelve a enfocar para que el teclado físico del Zebra siga escribiendo aquí
    setTimeout(() => inner.current?.focus(), 50)
  }

  if (!toggle) return <TextInput ref={inner} {...props} onFocus={handleFocus} style={style} showSoftInputOnFocus={visible} />
  return (
    <View style={styles.row}>
      <TextInput ref={inner} {...props} onFocus={handleFocus} style={[style, styles.grow]} showSoftInputOnFocus={visible} />
      <KeyboardToggleButton on={own} onPress={toggleOwn} />
    </View>
  )
})

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'stretch', gap: spacing.sm },
  grow: { flex: 1, minWidth: 0 },
  btn: { minHeight: touchTarget, width: 44, borderRadius: radius.md, borderWidth: 2, borderColor: colors.line, alignItems: 'center', justifyContent: 'center' },
  btnOn: { borderColor: colors.brand, backgroundColor: colors.panelAlt },
  icon: { color: colors.text, fontSize: 22 },
})
