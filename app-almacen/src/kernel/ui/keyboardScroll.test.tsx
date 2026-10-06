// Lote A9 — el campo que se escribe no queda debajo del teclado en pantalla: KeyboardScreen (KeyboardAvoidingView + ScrollView) se
// desplaza hasta el campo enfocado cuando sale el teclado, cuando se enfoca otro campo con el teclado ya fuera y cuando el
// KeyboardAvoidingView encoge la pantalla. Sin teclado en pantalla (lector o teclado físico del Zebra) no se mueve nada.
import { act, cleanup, fireEvent, render } from '@testing-library/react-native'
import { Keyboard, ScrollView, StyleSheet, TextInput, View } from 'react-native'

import { KeyboardInput } from './KeyboardInput'
import { KeyboardScreen, KeyboardScreenInput } from './KeyboardScreen'
import { KEYBOARD_FIELD_MARGIN, scrollDeltaToShow, type MeasurableField } from './keyboardScroll'
import { ScanField } from './ScanField'

describe('scrollDeltaToShow', () => {
  const visible = { top: 0, bottom: 300 }

  it('el campo ya se ve completo (con su aire): no se desplaza', () => {
    expect(scrollDeltaToShow({ top: 100, bottom: 156 }, visible)).toBe(0)
  })

  it('el campo queda debajo del borde (tapado por el teclado): sube lo justo para dejarlo con aire', () => {
    expect(scrollDeltaToShow({ top: 500, bottom: 556 }, visible)).toBe(556 + KEYBOARD_FIELD_MARGIN - 300)
    // apenas tapado
    expect(scrollDeltaToShow({ top: 250, bottom: 290 }, visible, 24)).toBe(14)
  })

  it('el campo quedó arriba de lo visible: baja hasta mostrarlo', () => {
    expect(scrollDeltaToShow({ top: 400, bottom: 456 }, { top: 500, bottom: 800 }, 24)).toBe(-124)
  })

  it('un campo más alto que lo visible: gana su principio (donde está el cursor)', () => {
    expect(scrollDeltaToShow({ top: 100, bottom: 600 }, visible, 24)).toBe(76)
  })

  it('sin espacio visible (todavía sin medir) no hace nada', () => {
    expect(scrollDeltaToShow({ top: 500, bottom: 556 }, { top: 0, bottom: 0 })).toBe(0)
  })
})

type Listener = (e?: unknown) => void

describe('KeyboardScreen', () => {
  let listeners: Record<string, Listener[]>
  let scrollTo: jest.SpyInstance

  beforeEach(() => {
    jest.useFakeTimers()
    listeners = {}
    jest.spyOn(Keyboard, 'addListener').mockImplementation(((event: string, fn: Listener) => {
      ;(listeners[event] ??= []).push(fn)
      return { remove: () => (listeners[event] = (listeners[event] ?? []).filter((f) => f !== fn)) }
    }) as never)
    // el contenido interno del ScrollView (contra el que se mide el campo) y el desplazamiento
    jest.spyOn(ScrollView.prototype as never, 'getInnerViewRef' as never).mockReturnValue({} as never)
    // el mock de ScrollView ya trae `scrollTo` como un jest.fn compartido: spyOn devuelve ese mismo, por eso se limpia aquí
    scrollTo = jest.spyOn(ScrollView.prototype as never, 'scrollTo' as never).mockImplementation((() => {}) as never)
    scrollTo.mockClear()
  })

  afterEach(async () => {
    await cleanup()
    jest.clearAllTimers()
    jest.useRealTimers()
    jest.restoreAllMocks()
    // los jest.fn compartidos de los mocks de RN (spyOn no los envuelve): se dejan como estaban
    for (const fn of [ScrollView.prototype.scrollTo, (ScrollView.prototype as unknown as { getInnerViewRef: jest.Mock }).getInnerViewRef, TextInput.prototype.measureLayout]) {
      ;(fn as unknown as jest.Mock).mockReset()
    }
  })

  const fieldAt = (y: number, h = 56): MeasurableField => ({ measureLayout: (_rel, ok) => ok(0, y, 300, h) })

  async function emit(event: string) {
    await act(async () => {
      for (const fn of listeners[event] ?? []) fn({ endCoordinates: { screenY: 300, height: 250, screenX: 0, width: 360 } })
    })
  }

  async function settle() {
    await act(async () => {
      jest.advanceTimersByTime(200)
    })
  }

  async function layout(root: ReturnType<typeof render> extends Promise<infer R> ? R : never, height: number) {
    await fireEvent(root.getByTestId('scroll'), 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 360, height } } })
  }

  it('usa KeyboardAvoidingView con behavior "padding" (también en Android: la app es de borde a borde) y un ScrollView', async () => {
    const root = await render(
      <KeyboardScreen testID="scroll">
        <View />
      </KeyboardScreen>,
    )
    expect(root.getByTestId('scroll').type).toBe('RCTScrollView')
    expect(root.getByTestId('scroll').props.keyboardShouldPersistTaps).toBe('handled')
    // la pantalla mide 600 y el teclado empieza en 300: con "padding" se agrega abajo lo que tapa el teclado (300)
    await fireEvent(root.getByTestId('keyboard-screen'), 'layout', { nativeEvent: { layout: { x: 0, y: 0, width: 360, height: 600 } }, persist: () => {} })
    await emit('keyboardWillShow') // el entorno de pruebas es iOS; en Android KeyboardAvoidingView escucha keyboardDidShow
    await emit('keyboardDidShow')
    await settle()
    expect(StyleSheet.flatten(root.getByTestId('keyboard-screen').props.style)).toMatchObject({ paddingBottom: 300 })
  })

  it('al salir el teclado se desplaza hasta el campo enfocado que quedó tapado', async () => {
    const root = await render(
      <KeyboardScreen testID="scroll" getFocused={() => fieldAt(500)}>
        <View />
      </KeyboardScreen>,
    )
    await layout(root, 300)
    await emit('keyboardDidShow')
    await settle()
    expect(scrollTo).toHaveBeenCalledWith({ y: 500 + 56 + KEYBOARD_FIELD_MARGIN - 300, animated: true })
  })

  it('cuenta lo ya desplazado, y no se mueve si el campo ya se ve', async () => {
    const root = await render(
      <KeyboardScreen testID="scroll" getFocused={() => fieldAt(500)}>
        <View />
      </KeyboardScreen>,
    )
    await layout(root, 300)
    await fireEvent.scroll(root.getByTestId('scroll'), { nativeEvent: { contentOffset: { x: 0, y: 100 } } })
    await emit('keyboardDidShow')
    await settle()
    expect(scrollTo).toHaveBeenCalledWith({ y: 100 + (500 + 56 + KEYBOARD_FIELD_MARGIN - 400), animated: true })

    scrollTo.mockClear()
    await fireEvent.scroll(root.getByTestId('scroll'), { nativeEvent: { contentOffset: { x: 0, y: 400 } } })
    await emit('keyboardDidShow')
    await settle()
    expect(scrollTo).not.toHaveBeenCalled()
  })

  it('cuando KeyboardAvoidingView encoge la pantalla (después del evento) vuelve a medir', async () => {
    const root = await render(
      <KeyboardScreen testID="scroll" getFocused={() => fieldAt(250)}>
        <View />
      </KeyboardScreen>,
    )
    await layout(root, 500)
    await emit('keyboardDidShow')
    await settle()
    // con 500 de alto el campo se veía
    expect(scrollTo).not.toHaveBeenCalled()
    await layout(root, 280)
    expect(scrollTo).toHaveBeenCalledWith({ y: 250 + 56 + KEYBOARD_FIELD_MARGIN - 280, animated: true })
  })

  it('con el teclado ya fuera, enfocar otro campo del kit (de «Filas» a «Sueltas») desplaza hasta ese campo', async () => {
    const measure = jest.spyOn(TextInput.prototype as never, 'measureLayout' as never).mockImplementation(((_rel: unknown, ok: (x: number, y: number, w: number, h: number) => void) =>
      ok(0, 700, 300, 56)) as never)
    const root = await render(
      <KeyboardScreen testID="scroll" getFocused={() => null}>
        <KeyboardInput toggle={false} softKeyboard accessibilityLabel="Filas" />
        <KeyboardInput toggle={false} softKeyboard accessibilityLabel="Sueltas" />
      </KeyboardScreen>,
    )
    await layout(root, 300)

    // sin teclado en pantalla (lector o teclado físico): enfocar no mueve nada
    await fireEvent(root.getByLabelText('Sueltas'), 'focus')
    await settle()
    expect(scrollTo).not.toHaveBeenCalled()

    await emit('keyboardDidShow')
    await settle()
    await fireEvent(root.getByLabelText('Sueltas'), 'focus')
    await settle()
    expect(measure).toHaveBeenCalled()
    expect(scrollTo).toHaveBeenLastCalledWith({ y: 700 + 56 + KEYBOARD_FIELD_MARGIN - 300, animated: true })

    // el teclado se escondió: enfocar ya no desplaza
    scrollTo.mockClear()
    await emit('keyboardDidHide')
    await fireEvent(root.getByLabelText('Filas'), 'focus')
    await settle()
    expect(scrollTo).not.toHaveBeenCalled()
  })

  it('ScanField (con el teclado pedido con ⌨) y KeyboardScreenInput también avisan al enfocarse, y el onFocus propio se sigue llamando', async () => {
    jest.spyOn(TextInput.prototype as never, 'measureLayout' as never).mockImplementation(((_rel: unknown, ok: (x: number, y: number, w: number, h: number) => void) =>
      ok(0, 900, 300, 56)) as never)
    const ownFocus = jest.fn()
    const root = await render(
      <KeyboardScreen testID="scroll" getFocused={() => null}>
        <ScanField label="Escanea la posición" onSubmit={() => {}} autoFocus={false} />
        <KeyboardScreenInput accessibilityLabel="Código" onFocus={ownFocus} />
      </KeyboardScreen>,
    )
    await layout(root, 300)
    await emit('keyboardDidShow')
    await settle()

    await fireEvent(root.getByLabelText('Escanea la posición'), 'focus')
    await settle()
    expect(scrollTo).toHaveBeenLastCalledWith({ y: 900 + 56 + KEYBOARD_FIELD_MARGIN - 300, animated: true })

    scrollTo.mockClear()
    await fireEvent(root.getByLabelText('Código'), 'focus')
    await settle()
    expect(scrollTo).toHaveBeenCalledTimes(1)
    expect(ownFocus).toHaveBeenCalledTimes(1)
  })

  it('fuera de un KeyboardScreen los campos se enfocan como siempre (sin error)', async () => {
    const ownFocus = jest.fn()
    const root = await render(<KeyboardInput accessibilityLabel="Lote" onFocus={ownFocus} />)
    await fireEvent(root.getByLabelText('Lote'), 'focus')
    expect(ownFocus).toHaveBeenCalledTimes(1)
  })
})
