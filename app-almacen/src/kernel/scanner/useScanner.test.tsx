// Indicador "Lector: listo / sin perfil / sin confirmar" (docs/mobile/mejoras-ux-zebra.md §2.3): nunca dice "listo" sin
// que DataWedge lo confirme. El módulo nativo se simula (no existe en Jest).
import { act, renderHook } from '@testing-library/react-native'
import { Platform } from 'react-native'

import en from '../i18n/en.json'
import es from '../i18n/es.json'
import { scannerStatusKey, useScannerStatus } from './useScanner'

type StatusListener = (e: { state: 'ready' | 'noProfile' | 'unconfirmed' | 'unavailable'; detail: string }) => void
const mockStatusListeners = new Set<StatusListener>()
const mockDw = {
  available: true,
  createProfile: jest.fn(),
  refreshProfileStatus: jest.fn(),
}
jest.mock('../../../modules/datawedge', () => ({
  __esModule: true,
  default: {
    isAvailable: () => mockDw.available,
    createProfile: () => mockDw.createProfile(),
    refreshProfileStatus: () => mockDw.refreshProfileStatus(),
    getProfileStatus: () => ({ state: 'unconfirmed', detail: '' }),
    addListener: (event: string, listener: StatusListener) => {
      if (event === 'onProfileStatus') mockStatusListeners.add(listener)
      return { remove: () => mockStatusListeners.delete(listener) }
    },
  },
}))

const originalOS = Platform.OS
beforeEach(() => {
  mockStatusListeners.clear()
  mockDw.available = true
  mockDw.createProfile.mockReset()
  mockDw.refreshProfileStatus.mockReset()
  Platform.OS = 'android'
})
afterAll(() => {
  Platform.OS = originalOS
})

describe('estado del lector', () => {
  it('cada estado tiene su texto en español y en inglés', () => {
    for (const state of ['ready', 'noProfile', 'unconfirmed', 'unavailable'] as const) {
      const [section, key] = scannerStatusKey(state).split('.')
      expect((es as Record<string, Record<string, string>>)[section][key]).toMatch(/^Lector: /)
      expect((en as Record<string, Record<string, string>>)[section][key]).toMatch(/^Scanner: /)
    }
  })

  it('arranca "sin confirmar", pregunta a DataWedge y pasa a "listo" solo cuando DataWedge lo confirma', async () => {
    const { result } = await renderHook(() => useScannerStatus())
    expect(result.current.status.state).toBe('unconfirmed')
    expect(mockDw.refreshProfileStatus).toHaveBeenCalled()
    await act(async () => mockStatusListeners.forEach((l) => l({ state: 'ready', detail: 'TeikemAlmacen' })))
    expect(result.current.status.state).toBe('ready')
    await act(async () => mockStatusListeners.forEach((l) => l({ state: 'noProfile', detail: 'activo: Profile0' })))
    expect(result.current.status).toEqual({ state: 'noProfile', detail: 'activo: Profile0' })
  })

  it('"Volver a configurar" vuelve a crear el perfil y queda "sin confirmar" hasta la respuesta', async () => {
    const { result } = await renderHook(() => useScannerStatus())
    await act(async () => mockStatusListeners.forEach((l) => l({ state: 'noProfile', detail: '' })))
    await act(async () => result.current.recheck())
    expect(mockDw.createProfile).toHaveBeenCalled()
    expect(result.current.status.state).toBe('unconfirmed')
  })

  it('sin DataWedge (no es un Zebra): "no disponible"', async () => {
    mockDw.available = false
    const { result } = await renderHook(() => useScannerStatus())
    expect(result.current.status.state).toBe('unavailable')
  })
})
