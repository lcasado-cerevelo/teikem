import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import { adjustQuantitySchema, decimals, moneySchema, volumeM3Schema, weightKgSchema } from './productRules'

beforeAll(() => setLang('es'))

/** Primer mensaje de error del esquema, o null si el valor es válido. */
function firstError(schema: { safeParse: (v: unknown) => { success: boolean; error?: { issues: { message: string }[] } } }, value: unknown) {
  const r = schema.safeParse(value)
  return r.success ? null : (r.error?.issues[0]?.message ?? null)
}

describe('productRules (ProductRules del dominio, mensajes del manual 06)', () => {
  it('decimals cuenta los decimales escritos', () => {
    expect(decimals(1)).toBe(0)
    expect(decimals(1.25)).toBe(2)
    expect(decimals(0.0001)).toBe(4)
  })

  it('peso: negativo → mensaje de peso/volumen (no el de costo/precio); 3 decimales y < 10⁹', () => {
    const s = weightKgSchema(t)
    expect(firstError(s, null)).toBeNull()
    expect(firstError(s, 12.345)).toBeNull()
    expect(firstError(s, -1)).toBe('El peso y el volumen no pueden ser negativos.')
    expect(firstError(s, 1.2345)).toBe('El peso admite como máximo 3 decimales y debe ser menor que 1,000,000,000.')
    expect(firstError(s, 1_000_000_000)).toBe('El peso admite como máximo 3 decimales y debe ser menor que 1,000,000,000.')
  })

  it('volumen: negativo → mensaje de peso/volumen; 4 decimales y < 10⁸', () => {
    const s = volumeM3Schema(t)
    expect(firstError(s, 0.1234)).toBeNull()
    expect(firstError(s, -0.5)).toBe('El peso y el volumen no pueden ser negativos.')
    expect(firstError(s, 0.12345)).toBe('El volumen admite como máximo 4 decimales y debe ser menor que 100,000,000.')
    expect(firstError(s, 100_000_000)).toBe('El volumen admite como máximo 4 decimales y debe ser menor que 100,000,000.')
  })

  it('costo/precio: negativo, decimales por campo (ProductRules.MoneyDecimals) y tope MoneyTooLarge', () => {
    const cost = moneySchema(t, 'cost')
    const price = moneySchema(t, 'price')
    expect(firstError(cost, null)).toBeNull()
    expect(firstError(cost, 12.3456)).toBeNull()
    expect(firstError(cost, -1)).toBe('El costo y el precio no pueden ser negativos.')
    expect(firstError(cost, 1.23456)).toBe('El costo admite como máximo 4 decimales.')
    expect(firstError(price, 1.23456)).toBe('El precio admite como máximo 4 decimales.')
    expect(firstError(cost, 99_999_999_999_999)).toBeNull()
    expect(firstError(cost, 100_000_000_000_000)).toBe('El costo o el precio excede el máximo permitido.')
    expect(firstError(price, 100_000_000_000_000)).toBe('El costo o el precio excede el máximo permitido.')
  })

  it('cantidad de ajuste: obligatoria, distinta de cero, ± y a lo sumo 3 decimales', () => {
    const s = adjustQuantitySchema(t)
    expect(firstError(s, 5)).toBeNull()
    expect(firstError(s, -2.125)).toBeNull()
    expect(firstError(s, null)).toBe('Se esperaba un número.')
    expect(firstError(s, 0)).toBe('La cantidad del ajuste no puede ser cero.')
    expect(firstError(s, 1.2345)).toBe('La cantidad admite como máximo 3 decimales.')
  })
})
