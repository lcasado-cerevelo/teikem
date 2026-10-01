import { setLang, t } from '../i18n/i18n'
import { syncStatusKey } from './syncStatus'

describe('syncStatusKey (texto de estado de Inicio)', () => {
  beforeEach(() => setLang('es'))

  it('sin falla: pendientes o "Todo enviado"', () => {
    expect(t(syncStatusKey(0, false))).toBe('Todo enviado')
    expect(t(syncStatusKey(3, false), { count: 3 })).toBe('3 pendientes de enviar')
  })

  it('falla y nada pendiente: no dice "0 con error", dice qué hacer', () => {
    const text = t(syncStatusKey(0, true), { count: 0 })
    expect(text).toBe('No se pudo sincronizar. Toca "Sincronizar ahora".')
    expect(text).not.toMatch(/con error/)
  })

  it('falla con pendientes: cuántos y que no se pudo sincronizar', () => {
    expect(t(syncStatusKey(2, true), { count: 2 })).toBe('2 pendientes · no se pudo sincronizar')
  })

  it('en inglés', () => {
    setLang('en')
    expect(t(syncStatusKey(0, true))).toBe('Could not sync. Tap "Sync now".')
    expect(t(syncStatusKey(2, true), { count: 2 })).toBe('2 pending · could not sync')
  })
})
