// Lote A9 (pruebas del dueño en el Zebra, 2026-10-06): el teclado en pantalla tapaba el campo que se estaba escribiendo (p. ej.
// «Sueltas» de la calculadora). Causa: las pantallas eran un ScrollView suelto y, en Android, la app dibuja de borde a borde
// (edge-to-edge, obligatorio desde Expo SDK 54 / Android 16): el `adjustResize` del manifiesto (app.config.ts no fija
// `softwareKeyboardLayoutMode`, y el valor por defecto de Expo es `adjustResize`) ya NO encoge la ventana cuando sale el teclado,
// y nadie desplazaba el contenido. Arreglo compartido (KeyboardScreen.tsx) en vez de un parche por pantalla:
//  1. `KeyboardAvoidingView` con `behavior="padding"` también en Android: con la ventana sin encoger, agrega abajo el alto que
//     tapa el teclado; si la ventana sí se encoge (aparato sin edge-to-edge), el cálculo de RN da 0 y no estorba.
//  2. Un ScrollView que, al salir el teclado o al enfocar otro campo con el teclado ya fuera, se desplaza lo justo para que el
//     campo enfocado quede completo y visible sobre el teclado (este archivo: `useKeyboardScroll`).
// Los campos de escaneo (ScanField) y los del kit (KeyboardInput) no muestran el teclado al enfocar (showSoftInputOnFocus=false,
// docs/mobile/mejoras-ux-zebra.md §2): sin teclado no hay evento y nada se mueve; solo actúa cuando el operario pide el teclado (⌨).
import { createContext, useCallback, useContext, useEffect, useRef } from 'react'
import { Keyboard, TextInput, type LayoutChangeEvent, type NativeScrollEvent, type NativeSyntheticEvent, type ScrollView } from 'react-native'

/** Aire (dp) que se deja entre el campo y el borde del teclado (o el borde de arriba), para ver también el borde del campo. */
export const KEYBOARD_FIELD_MARGIN = 24

/** Lo que hace falta de un campo para medirlo dentro del contenido del ScrollView (un TextInput real lo cumple). */
export interface MeasurableField {
  measureLayout: (relativeTo: never, onSuccess: (x: number, y: number, width: number, height: number) => void, onFail?: () => void) => void
}

/**
 * Cuánto desplazar (positivo = subir el contenido) para que el campo `[top, bottom]` quede dentro de la parte visible `[top, bottom]`
 * con `margin` de aire. Si el campo no cabe entero, gana que se vea su principio (donde está el cursor). 0 si ya se ve.
 * Lógica pura (coordenadas del contenido del ScrollView).
 */
export function scrollDeltaToShow(
  field: { top: number; bottom: number },
  visible: { top: number; bottom: number },
  margin: number = KEYBOARD_FIELD_MARGIN,
): number {
  if (visible.bottom - visible.top <= 0) return 0
  // lo más que se puede subir sin que el principio del campo se salga por arriba
  const keepTop = field.top - margin - visible.top
  if (field.bottom + margin > visible.bottom) return Math.min(field.bottom + margin - visible.bottom, keepTop)
  if (keepTop < 0) return keepTop
  return 0
}

/** El campo enfocado ahora (el de RN). */
function currentFocusedField(): MeasurableField | null {
  const state = (TextInput as unknown as { State?: { currentlyFocusedInput?: () => unknown } }).State
  const input = state?.currentlyFocusedInput?.()
  return input ? (input as MeasurableField) : null
}

export interface UseKeyboardScrollOptions {
  /** Devuelve el campo enfocado (por defecto `TextInput.State.currentlyFocusedInput()`); se cambia en pruebas. */
  getFocused?: () => MeasurableField | null
  /** Espera (ms) antes de medir tras salir el teclado o enfocar un campo: deja que KeyboardAvoidingView aplique su espacio. */
  settleMs?: number
}

/**
 * Desplazamiento automático hasta el campo enfocado cuando el teclado en pantalla está fuera. Devuelve el `ref` y los manejadores que
 * hay que pasar al ScrollView, y `onFieldFocus` para los campos del kit (lo reciben por `KeyboardScrollContext`).
 */
export function useKeyboardScroll({ getFocused = currentFocusedField, settleMs = 120 }: UseKeyboardScrollOptions = {}) {
  const scrollRef = useRef<ScrollView>(null)
  const scrollY = useRef(0)
  const viewportHeight = useRef(0)
  const keyboardVisible = useRef(false)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const getFocusedRef = useRef(getFocused)
  useEffect(() => {
    getFocusedRef.current = getFocused
  }, [getFocused])

  const ensureVisible = useCallback((target?: MeasurableField | null) => {
    const scroll = scrollRef.current
    const field = target ?? getFocusedRef.current()
    if (!scroll || !field || !keyboardVisible.current || viewportHeight.current <= 0) return
    const inner = (scroll as unknown as { getInnerViewRef?: () => unknown }).getInnerViewRef?.()
    if (!inner) return
    field.measureLayout(
      inner as never,
      (_x, y, _w, h) => {
        const top = scrollY.current
        const delta = scrollDeltaToShow({ top: y, bottom: y + h }, { top, bottom: top + viewportHeight.current })
        if (delta !== 0) scroll.scrollTo({ y: Math.max(0, top + delta), animated: true })
      },
      () => {},
    )
  }, [])

  const schedule = useCallback(
    (target?: MeasurableField | null) => {
      if (timer.current) clearTimeout(timer.current)
      timer.current = setTimeout(() => {
        timer.current = null
        ensureVisible(target)
      }, settleMs)
    },
    [ensureVisible, settleMs],
  )

  useEffect(() => {
    const show = Keyboard.addListener('keyboardDidShow', () => {
      keyboardVisible.current = true
      schedule()
    })
    const hide = Keyboard.addListener('keyboardDidHide', () => {
      keyboardVisible.current = false
    })
    return () => {
      show.remove()
      hide.remove()
      if (timer.current) clearTimeout(timer.current)
    }
  }, [schedule])

  const onScroll = useCallback((e: NativeSyntheticEvent<NativeScrollEvent>) => {
    scrollY.current = e.nativeEvent.contentOffset.y
  }, [])

  // KeyboardAvoidingView encoge el ScrollView después del evento del teclado: al cambiar de alto, se vuelve a medir.
  const onLayout = useCallback(
    (e: LayoutChangeEvent) => {
      const h = e.nativeEvent.layout.height
      const changed = h !== viewportHeight.current
      viewportHeight.current = h
      if (changed && keyboardVisible.current) ensureVisible()
    },
    [ensureVisible],
  )

  /** Un campo del kit se enfocó: con el teclado ya fuera (pasar de «Filas» a «Sueltas») no llega un evento nuevo del teclado. */
  const onFieldFocus = useCallback(
    (field?: MeasurableField | null) => {
      if (keyboardVisible.current) schedule(field)
    },
    [schedule],
  )

  return { scrollRef, onScroll, onLayout, onFieldFocus, ensureVisible }
}

export interface KeyboardScrollContextValue {
  onFieldFocus: (field?: MeasurableField | null) => void
}

/** Lo usan KeyboardInput, ScanField y KeyboardScreenInput para avisar que se enfocaron; fuera de un KeyboardScreen no hace nada. */
export const KeyboardScrollContext = createContext<KeyboardScrollContextValue | null>(null)

/** `onFocus` para un campo: avisa al KeyboardScreen que lo contiene (si hay) y llama al `onFocus` propio del campo. */
export function useFieldFocus<E>(getField: () => MeasurableField | null, own?: (e: E) => void) {
  const ctx = useContext(KeyboardScrollContext)
  return useCallback(
    (e: E) => {
      ctx?.onFieldFocus(getField())
      own?.(e)
    },
    [ctx, getField, own],
  )
}
