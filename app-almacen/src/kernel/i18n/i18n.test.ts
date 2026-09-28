import { __resetAllForTests } from 'expo-sqlite'

import { __resetDbForTests } from '../db/database'
import { getLang, __resetLangForTests, setLang, t } from './i18n'

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetLangForTests()
})

describe('i18n', () => {
  it('empieza en español y traduce', () => {
    expect(getLang()).toBe('es')
    expect(t('common.cancel')).toBe('Cancelar')
  })

  it('cambia de idioma y persiste', () => {
    setLang('en')
    expect(getLang()).toBe('en')
    expect(t('common.cancel')).toBe('Cancel')
  })

  it('sustituye parámetros', () => {
    expect(t('home.syncStatusPending', { count: 3 })).toBe('3 pendientes de enviar')
  })

  it('cae a español y luego a la clave si falta la traducción', () => {
    expect(t('clave.que.no.existe')).toBe('clave.que.no.existe')
  })
})
