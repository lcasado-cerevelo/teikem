// La marca de la compañía en el portal: con la de siempre no cambia nada; con otra se derivan colores legibles (AA) en claro y en oscuro y los de estado no se tocan.
import { pickLogoSlot } from '../brand/portalBrand'
import { parseBranding } from './brandTheme'
import { PALETTES, brandedPalette, contrastRatio } from './theme'

describe('brandedPalette', () => {
  it('sin marca o con la de siempre es la paleta de siempre (la misma instancia)', () => {
    expect(brandedPalette('light', null)).toBe(PALETTES.light)
    expect(brandedPalette('dark', parseBranding(null))).toBe(PALETTES.dark)
    expect(brandedPalette('light', parseBranding('{"preset":"advance"}'))).toBe(PALETTES.light)
  })

  it.each(['light', 'dark'] as const)('con otro tema cambia los colores de la marca y deja los de estado en %s', (scheme) => {
    const base = PALETTES[scheme]
    const p = brandedPalette(scheme, parseBranding('{"preset":"carretera"}'))
    expect(p.flow).not.toBe(base.flow)
    expect(p.brand).not.toBe(base.brand)
    expect([p.danger, p.ok, p.warn, p.info]).toEqual([base.danger, base.ok, base.warn, base.info])
    // lo que se lee sobre cada color de la marca llega a AA (4.5:1)
    expect(contrastRatio(p.onFlow, p.flow)).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(p.onBrand, p.brand)).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(p.onSecondary, p.secondary)).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(p.text, p.bg)).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(p.link, p.card)).toBeGreaterThanOrEqual(4.5)
  })

  it('los colores propios de la compañía se aplican al botón principal', () => {
    const json = JSON.stringify({ preset: 'advance', useCustom: true, custom: { flow: '#1F6FE5', money: '#FF6A1A', neutral: '#03204B' } })
    expect(brandedPalette('light', parseBranding(json)).flow).toBe('#1F6FE5')
  })
})

describe('pickLogoSlot', () => {
  it('prefiere el invertido (la barra es oscura), luego el normal y las marcas cuadradas', () => {
    expect(pickLogoSlot(['lockup', 'lockup-inverted'])).toBe('lockup-inverted')
    expect(pickLogoSlot(['mark', 'lockup'])).toBe('lockup')
    expect(pickLogoSlot(['mark'])).toBe('mark')
    expect(pickLogoSlot([])).toBeNull()
  })
})
