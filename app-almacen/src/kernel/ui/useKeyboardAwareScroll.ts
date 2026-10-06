// El teclado en pantalla tapaba el campo que se está llenando (pedido del dueño 2026-10-06, registro del aparato). En Android con borde a borde
// la ventana no se achica al salir el teclado, y `KeyboardAvoidingView` no hace nada con `behavior` vacío: el campo queda debajo del teclado.
// Este hook mide el teclado, agrega ese alto como espacio al final del ScrollView y sube el campo enfocado para que se vea lo que se teclea.
import { useCallback, useEffect, useRef, useState } from 'react'
import { Keyboard, type LayoutChangeEvent, type ScrollView } from 'react-native'

/** Margen que se deja entre el borde de arriba y el campo enfocado al subirlo. */
export const FIELD_TOP_MARGIN = 24

/** Dónde poner el scroll para que el campo (a `fieldY` del inicio del contenido) quede arriba y a la vista. Nunca negativo. */
export function scrollTargetY(fieldY: number | undefined): number {
  return Math.max(0, (fieldY ?? 0) - FIELD_TOP_MARGIN)
}

export function useKeyboardAwareScroll() {
  const scrollRef = useRef<ScrollView>(null)
  const [keyboardHeight, setKeyboardHeight] = useState(0)
  const ys = useRef<Record<string, number>>({})
  const focused = useRef<string | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const scrollToFocused = useCallback((delay: number) => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = setTimeout(() => {
      const key = focused.current
      if (key === null) return
      scrollRef.current?.scrollTo({ y: scrollTargetY(ys.current[key]), animated: true })
    }, delay)
  }, [])

  useEffect(() => {
    const show = Keyboard.addListener('keyboardDidShow', (e) => {
      setKeyboardHeight(e.endCoordinates?.height ?? 0)
      scrollToFocused(80) // después de que el espacio nuevo ya esté puesto
    })
    const hide = Keyboard.addListener('keyboardDidHide', () => setKeyboardHeight(0))
    return () => {
      show.remove()
      hide.remove()
      if (timer.current) clearTimeout(timer.current)
    }
  }, [scrollToFocused])

  /** Va en la vista que envuelve al campo (guarda dónde está). */
  const wrapperProps = (key: string) => ({
    onLayout: (e: LayoutChangeEvent) => {
      ys.current[key] = e.nativeEvent.layout.y
    },
  })

  /** Va en el TextInput: al enfocarlo sube el campo (y vuelve a subirlo cuando sale el teclado). */
  const inputProps = (key: string) => ({
    onFocus: () => {
      focused.current = key
      scrollToFocused(200)
    },
    onBlur: () => {
      if (focused.current === key) focused.current = null
    },
  })

  return { scrollRef, keyboardHeight, wrapperProps, inputProps }
}
