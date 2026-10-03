// Logos de la compañía: qué variante toca por tema (la invertida en oscuro, la normal en claro, la otra si falta, null = Teikem)
// y el almacén de URL de objeto que usan el lockup y la marca cuadrada.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { getCompanyLogos, isInvertedSlot, LOGO_MAX_BYTES, LOGO_SLOTS, NO_COMPANY_LOGOS, pickLogoUrl, setCompanyLogos, subscribeCompanyLogos } from './brandLogos'

afterEach(() => setCompanyLogos(NO_COMPANY_LOGOS))

describe('pickLogoUrl', () => {
  const all = { lockup: 'L', 'lockup-inverted': 'LI', mark: 'M', 'mark-inverted': 'MI' }

  it('tema oscuro → variante invertida; claro → la normal', () => {
    expect(pickLogoUrl(all, 'lockup', 'dark')).toBe('LI')
    expect(pickLogoUrl(all, 'lockup', 'light')).toBe('L')
    expect(pickLogoUrl(all, 'mark', 'dark')).toBe('MI')
    expect(pickLogoUrl(all, 'mark', 'light')).toBe('M')
  })

  it('si solo hay una variante se reusa en ambos temas', () => {
    expect(pickLogoUrl({ lockup: 'L' }, 'lockup', 'dark')).toBe('L')
    expect(pickLogoUrl({ 'mark-inverted': 'MI' }, 'mark', 'light')).toBe('MI')
  })

  it('sin ninguna de la pieza, null (respaldo de Teikem); el lockup no sustituye a la marca cuadrada', () => {
    expect(pickLogoUrl({}, 'lockup', 'dark')).toBeNull()
    expect(pickLogoUrl({ lockup: 'L', 'lockup-inverted': 'LI' }, 'mark', 'dark')).toBeNull()
    expect(pickLogoUrl({ mark: 'M' }, 'lockup', 'light')).toBeNull()
  })
})

describe('almacén de logos', () => {
  it('las cuatro ranuras, las invertidas son las de fondo oscuro y el tope es 512 KB', () => {
    expect(LOGO_SLOTS).toEqual(['lockup', 'lockup-inverted', 'mark', 'mark-inverted'])
    expect(LOGO_SLOTS.filter(isInvertedSlot)).toEqual(['lockup-inverted', 'mark-inverted'])
    expect(LOGO_MAX_BYTES).toBe(524288)
  })

  it('avisa a los suscriptores y libera las URL de objeto que dejan de usarse', () => {
    const revoke = vi.fn()
    const original = URL.revokeObjectURL
    URL.revokeObjectURL = revoke
    try {
      const seen = vi.fn()
      const off = subscribeCompanyLogos(seen)
      setCompanyLogos({ urls: { lockup: 'blob:a', mark: 'blob:b' }, name: 'Advance' })
      expect(getCompanyLogos().urls.lockup).toBe('blob:a')
      expect(seen).toHaveBeenCalledTimes(1)
      expect(revoke).not.toHaveBeenCalled()
      setCompanyLogos({ urls: { lockup: 'blob:c', mark: 'blob:b' }, name: 'Advance' })   // cambió solo el lockup
      expect(revoke).toHaveBeenCalledTimes(1)
      expect(revoke).toHaveBeenCalledWith('blob:a')
      setCompanyLogos(NO_COMPANY_LOGOS)
      expect(revoke).toHaveBeenCalledTimes(3)
      off()
      setCompanyLogos({ urls: {}, name: null })
      expect(seen).toHaveBeenCalledTimes(3)
    } finally {
      URL.revokeObjectURL = original
    }
  })
})
