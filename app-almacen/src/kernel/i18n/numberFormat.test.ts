import { formatQuantity } from './numberFormat'
import { translate } from './i18n'

describe('formatQuantity (coma de miles, punto decimal)', () => {
  it('agrupa miles y conserva hasta 3 decimales', () => {
    expect(formatQuantity(61023)).toBe('61,023')
    expect(formatQuantity(1250)).toBe('1,250')
    expect(formatQuantity(999)).toBe('999')
    expect(formatQuantity(1.5)).toBe('1.5')
    expect(formatQuantity(-1250.1234)).toBe('-1,250.123')
    expect(formatQuantity(1234567)).toBe('1,234,567')
  })
})

describe('translate con números', () => {
  it('los parámetros numéricos llevan coma de miles, salvo los identificadores', () => {
    const text = translate('es', 'no.existe.{count}', { count: 1250 })
    expect(text).toBe('no.existe.{count}'.replace('{count}', '1,250'))
    expect(translate('es', 'no.existe.{id}', { id: 1250 })).toBe('no.existe.1250')
  })
})
