// Lote A9 — pantalla con campos de captura que nunca deja el campo que se escribe debajo del teclado en pantalla. Por qué y cómo:
// keyboardScroll.ts.
import { forwardRef, useCallback, useEffect, useImperativeHandle, useMemo, useRef, type ReactNode } from 'react'
import { KeyboardAvoidingView, ScrollView, StyleSheet, TextInput, type ScrollViewProps, type TextInputProps } from 'react-native'

import { KeyboardScrollContext, useFieldFocus, useKeyboardScroll, type KeyboardScrollContextValue, type MeasurableField } from './keyboardScroll'

export interface KeyboardScreenProps extends ScrollViewProps {
  children?: ReactNode
  /** Aviso fijo (StickyAlert) que se dibuja encima del área desplazable: no se va al desplazarse. */
  banner?: ReactNode
  /** Solo para pruebas: de dónde sale el campo enfocado. */
  getFocused?: () => MeasurableField | null
  /** Cada vez que cambia (y no es 0), la pantalla se desplaza hasta arriba: p. ej. al tocar un producto de la lista para ponerlo en el campo de
   *  escaneo que está arriba, sin tener que subir a mano. */
  scrollToTopKey?: number
}

/**
 * Reemplaza a `<ScrollView contentContainerStyle={…} keyboardShouldPersistTaps="handled">` (mismas propiedades; `keyboardShouldPersistTaps`
 * ya viene en "handled" para que tocar un botón no solo cierre el teclado). `KeyboardAvoidingView` con `behavior="padding"` en las dos
 * plataformas + desplazamiento automático hasta el campo enfocado.
 */
export function KeyboardScreen({ children, banner, getFocused, scrollToTopKey, onScroll, onLayout, keyboardShouldPersistTaps = 'handled', ...props }: KeyboardScreenProps) {
  const { scrollRef, onScroll: trackScroll, onLayout: trackLayout, onFieldFocus } = useKeyboardScroll({ getFocused })
  useEffect(() => {
    if (scrollToTopKey) scrollRef.current?.scrollTo({ y: 0, animated: true })
  }, [scrollToTopKey, scrollRef])
  const ctx = useMemo<KeyboardScrollContextValue>(() => ({ onFieldFocus }), [onFieldFocus])
  return (
    <KeyboardAvoidingView style={styles.fill} behavior="padding" testID="keyboard-screen">
      {banner}
      <ScrollView
        ref={scrollRef}
        keyboardShouldPersistTaps={keyboardShouldPersistTaps}
        scrollEventThrottle={16}
        {...props}
        onScroll={(e) => {
          trackScroll(e)
          onScroll?.(e)
        }}
        onLayout={(e) => {
          trackLayout(e)
          onLayout?.(e)
        }}
      >
        <KeyboardScrollContext.Provider value={ctx}>{children}</KeyboardScrollContext.Provider>
      </ScrollView>
    </KeyboardAvoidingView>
  )
}

/**
 * TextInput de siempre (el teclado del sistema sale al enfocar, como en Registrar el aparato) que avisa al KeyboardScreen que lo contiene
 * al enfocarse, para pasar de un campo a otro con el teclado ya abierto sin que el segundo quede tapado. Los campos del almacén usan
 * KeyboardInput o ScanField, que ya avisan solos.
 */
export const KeyboardScreenInput = forwardRef<TextInput, TextInputProps>(function KeyboardScreenInput({ onFocus, ...props }, ref) {
  const inner = useRef<TextInput>(null)
  useImperativeHandle(ref, () => inner.current as TextInput)
  const getField = useCallback(() => inner.current as unknown as MeasurableField | null, [])
  const handleFocus = useFieldFocus(getField, onFocus)
  return <TextInput ref={inner} {...props} onFocus={handleFocus} />
})

const styles = StyleSheet.create({
  fill: { flex: 1 },
})
