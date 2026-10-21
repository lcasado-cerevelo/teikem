import { describe, expect, it } from 'vitest'
import { sortTemplates } from './templatesApi'

describe('sortTemplates', () => {
  it('activas primero y por nombre sin distinguir mayúsculas', () => {
    const out = sortTemplates([
      { name: 'zeta', isActive: true },
      { name: 'Beta', isActive: false },
      { name: 'alfa', isActive: true },
    ])
    expect(out.map((x) => x.name)).toEqual(['alfa', 'zeta', 'Beta'])
  })
})
