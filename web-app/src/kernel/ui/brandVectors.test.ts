// Paridad con el servidor: los vectores compartidos (`tests/shared/brand-vectors.json`) los lee también xunit
// (BrandingRulesTests). Cada caso trae la entrada, si es válida, el código y el mensaje exactos y, cuando la validación llegó
// a las comprobaciones de color, el resultado de cada una (razón de contraste y distancia de matiz).
import { describe, expect, it } from 'vitest'
import vectors from '../../../../tests/shared/brand-vectors.json'
import { BRAND_JSON_MAX_CHARS, BRAND_PRESETS, MIN_HUE_DISTANCE, validateBrandingJson } from './brandTheme'

interface VectorCase {
  name: string
  input: string
  padToLength?: number
  valid: boolean
  code?: string
  message?: string
  checks?: unknown[]
}

const cases = vectors.cases as VectorCase[]

describe('vectores compartidos con el servidor (BrandingRules)', () => {
  it('los límites del archivo son los de la web', () => {
    expect(vectors.limits.maxChars).toBe(BRAND_JSON_MAX_CHARS)
    expect(vectors.limits.minHueDistance).toBe(MIN_HUE_DISTANCE)
  })

  it('cubre cada tema predefinido, y casos límite e inválidos', () => {
    for (const p of BRAND_PRESETS) expect(cases.some((c) => c.name === `predefinido ${p.id}` && c.valid), p.id).toBe(true)
    expect(cases.filter((c) => !c.valid).length).toBeGreaterThanOrEqual(30)
    expect(new Set(cases.map((c) => c.code).filter(Boolean))).toEqual(
      new Set(['tooLarge', 'malformed', 'notObject', 'unknownField', 'statusColor', 'badType', 'badHex', 'unknownPreset', 'contrast', 'hue']),
    )
  })

  it.each(cases.map((c) => [c.name, c] as const))('%s', (_name, c) => {
    const raw = c.padToLength ? c.input + ' '.repeat(c.padToLength - c.input.length) : c.input
    const v = validateBrandingJson(raw)
    expect(v.ok).toBe(c.valid)
    if (!v.ok) {
      expect(v.code).toBe(c.code)
      expect(v.message).toBe(c.message)
    }
    expect(v.checks).toEqual(c.checks ?? [])
  })
})
