import { afterEach, describe, expect, it } from 'vitest'
import { getLang, setLang, t, translate } from './i18n'

describe('t()', () => {
  const initial = getLang()
  afterEach(() => setLang(initial))

  it('traduce claves anidadas en ambos idiomas', () => {
    expect(translate('es', 'nav.groups.warehouse')).toBe('Almacén')
    expect(translate('en', 'nav.groups.warehouse')).toBe('Warehouse')
  })

  it('con clave faltante devuelve la clave tal cual', () => {
    expect(t('does.not.exist')).toBe('does.not.exist')
    expect(translate('en', 'nav.groups')).toBe('nav.groups') // nodo intermedio, no texto
  })

  it('sustituye parámetros', () => {
    expect(translate('es', 'access.moduleOff.bodyNamed', { module: 'WMS' })).toBe('El módulo WMS no está habilitado para su compañía.')
  })

  it('setLang cambia el idioma de t() sin más efectos', () => {
    setLang('en')
    expect(t('common.cancel')).toBe('Cancel')
    setLang('es')
    expect(t('common.cancel')).toBe('Cancelar')
  })
})
