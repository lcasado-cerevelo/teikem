import { describe, expect, it } from 'vitest'
import { applyRequest, canApply, canExclude, chainOrNone, effectiveAction, keepExclusions, previewIsCurrent, previewRequest, prefixOk, skippedCount, summarize } from './chainRules'
import type { ChainBulkPreview } from './chainsApi'

const preview: ChainBulkPreview = {
  prefix: 'Walgreens',
  wholeWord: true,
  moveFromOtherChains: false,
  matched: 4,
  toAssign: 2,
  toMove: 0,
  alreadyInChain: 1,
  skippedOtherChain: 1,
  excluded: 0,
  tooMany: false,
  maxBulk: 2000,
  fingerprint: 'abc',
  rows: [
    { clientPublicId: 'a', code: 'C1', name: 'Walgreens 1', action: 'ASSIGN' },
    { clientPublicId: 'b', code: 'C2', name: 'Walgreens 2', action: 'ASSIGN' },
    { clientPublicId: 'c', code: 'C3', name: 'Walgreens 3', action: 'ALREADY' },
    { clientPublicId: 'd', code: 'C4', name: 'Walgreens 4', action: 'SKIP_OTHER_CHAIN', currentChainName: 'CVS' },
  ],
}
const settings = { prefix: ' Walgreens ', wholeWord: true, moveFromOtherChains: false }

describe('chainRules', () => {
  it('la simulación no lleva exclusiones ni huella', () => {
    expect(previewRequest(settings)).toEqual({ prefix: 'Walgreens', wholeWord: true, moveFromOtherChains: false })
    expect(prefixOk('ab')).toBe(false)
    expect(prefixOk(' abc ')).toBe(true)
  })

  it('aplicar lleva los ajustes simulados, la huella y las exclusiones', () => {
    expect(applyRequest(preview, settings, new Set(['b']))).toEqual({ prefix: 'Walgreens', wholeWord: true, moveFromOtherChains: false, excludeClientPublicIds: ['b'], fingerprint: 'abc' })
  })

  it('desmarcar una fila escribible la pasa a EXCLUDED sin volver a simular', () => {
    const rows = preview.rows ?? []
    expect(effectiveAction(rows[0], new Set(['a']))).toBe('EXCLUDED')
    expect(effectiveAction(rows[0], new Set())).toBe('ASSIGN')
    expect(canExclude(rows[2])).toBe(false)
    expect(effectiveAction(rows[2], new Set(['c']))).toBe('ALREADY')
  })

  it('el resumen descuenta las exclusiones', () => {
    expect(summarize(preview, new Set())).toMatchObject({ toAssign: 2, already: 1, skippedOtherChain: 1, excluded: 0, applicable: 2 })
    expect(summarize(preview, new Set(['a']))).toMatchObject({ toAssign: 1, excluded: 1, applicable: 1 })
  })

  it('sin simulación vigente no se puede aplicar', () => {
    expect(canApply(null, null, settings, new Set())).toBe(false)
    expect(canApply(preview, settings, settings, new Set())).toBe(true)
    expect(previewIsCurrent(settings, { ...settings, moveFromOtherChains: true })).toBe(false)
    expect(canApply(preview, settings, { ...settings, prefix: 'Walg' }, new Set())).toBe(false)
    expect(canApply(preview, settings, settings, new Set(['a', 'b']))).toBe(false)
    expect(canApply({ ...preview, tooMany: true }, settings, settings, new Set())).toBe(false)
  })

  it('al volver a simular se conservan solo las exclusiones vigentes', () => {
    expect([...keepExclusions(new Set(['a', 'zzz', 'c']), preview)]).toEqual(['a'])
    expect(keepExclusions(new Set(['a']), null).size).toBe(0)
  })

  it('«Sin cadena» y los saltados', () => {
    const t = (k: string) => (k === 'chains.noChain' ? 'Sin cadena' : k)
    expect(chainOrNone(null, t)).toBe('Sin cadena')
    expect(chainOrNone('CVS', t)).toBe('CVS')
    expect(skippedCount({ skippedOtherChain: [{}, {}] })).toBe(2)
    expect(skippedCount(undefined)).toBe(0)
  })
})
