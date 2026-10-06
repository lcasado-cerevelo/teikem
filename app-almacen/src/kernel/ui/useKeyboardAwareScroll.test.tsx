import { act, renderHook } from '@testing-library/react-native'
import { Keyboard } from 'react-native'

import { FIELD_TOP_MARGIN, scrollTargetY, useKeyboardAwareScroll } from './useKeyboardAwareScroll'

type Handler = (e: { endCoordinates?: { height: number } }) => void
const handlers: Record<string, Handler> = {}

beforeEach(() => {
  jest.useFakeTimers()
  jest.spyOn(Keyboard, 'addListener').mockImplementation(((event: string, h: Handler) => {
    handlers[event] = h
    return { remove: jest.fn() }
  }) as never)
})
afterEach(() => {
  jest.useRealTimers()
  jest.restoreAllMocks()
})

describe('campo que sube sobre el teclado en pantalla', () => {
  it('el espacio de abajo es el alto del teclado y vuelve a cero al esconderlo', async () => {
    const { result } = await renderHook(() => useKeyboardAwareScroll())
    expect(result.current.keyboardHeight).toBe(0)
    await act(async () => handlers.keyboardDidShow({ endCoordinates: { height: 280 } }))
    expect(result.current.keyboardHeight).toBe(280)
    await act(async () => handlers.keyboardDidHide({}))
    expect(result.current.keyboardHeight).toBe(0)
  })

  it('al enfocar un campo sube el scroll hasta él (y otra vez cuando sale el teclado)', async () => {
    const { result } = await renderHook(() => useKeyboardAwareScroll())
    const scrollTo = jest.fn()
    ;(result.current.scrollRef as { current: unknown }).current = { scrollTo }
    result.current.wrapperProps('code').onLayout({ nativeEvent: { layout: { y: 300 } } } as never)

    result.current.inputProps('code').onFocus()
    await act(async () => {
      jest.advanceTimersByTime(250)
    })
    expect(scrollTo).toHaveBeenLastCalledWith({ y: 300 - FIELD_TOP_MARGIN, animated: true })

    scrollTo.mockClear()
    await act(async () => handlers.keyboardDidShow({ endCoordinates: { height: 250 } }))
    await act(async () => {
      jest.advanceTimersByTime(100)
    })
    expect(scrollTo).toHaveBeenCalledWith({ y: 300 - FIELD_TOP_MARGIN, animated: true })
  })

  it('sin campo enfocado no mueve nada; y el destino nunca es negativo', async () => {
    const { result } = await renderHook(() => useKeyboardAwareScroll())
    const scrollTo = jest.fn()
    ;(result.current.scrollRef as { current: unknown }).current = { scrollTo }
    await act(async () => handlers.keyboardDidShow({ endCoordinates: { height: 250 } }))
    await act(async () => {
      jest.advanceTimersByTime(300)
    })
    expect(scrollTo).not.toHaveBeenCalled()
    expect(scrollTargetY(5)).toBe(0)
    expect(scrollTargetY(undefined)).toBe(0)
  })
})
