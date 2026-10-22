import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { NO_COMPANY_LOGOS, getCompanyLogos, setCompanyLogos } from './brandLogos'
import { loadPublicBrand, readLastTenant, rememberLastTenant } from './publicBrand'

const ID = '11111111-1111-1111-1111-111111111111'

function mockFetch(body: unknown, ok = true) {
  const fn = vi.fn(async () => ({ ok, json: async () => body }) as Response)
  vi.stubGlobal('fetch', fn)
  return fn
}

describe('logo de la pantalla de entrada', () => {
  beforeEach(() => {
    window.localStorage.clear()
    setCompanyLogos(NO_COMPANY_LOGOS)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('recuerda la última compañía y no guarda vacíos', () => {
    rememberLastTenant(null)
    expect(readLastTenant()).toBeNull()
    rememberLastTenant(ID)
    expect(readLastTenant()).toBe(ID)
  })

  it('manda la última compañía y la dirección de la página, y deja las URL de los logos', async () => {
    rememberLastTenant(ID)
    const f = mockFetch({ source: 'tenant', tenantPublicId: ID, name: 'Acme', logos: [{ slot: 'lockup', stampUtc: '2026-10-22T00:00:00Z' }] })
    await loadPublicBrand()
    const url = String((f.mock.calls[0] as unknown[])[0])
    expect(url).toContain('/api/v1/public/brand?')
    expect(url).toContain(`tenant=${ID}`)
    expect(url).toContain(`host=${window.location.hostname}`)
    const logos = getCompanyLogos()
    expect(logos.name).toBe('Acme')
    expect(logos.urls.lockup).toContain(`/api/v1/public/brand/logo/lockup?tenant=${ID}`)
  })

  it('sin compañía o sin logos no toca nada (queda el de Teikem)', async () => {
    mockFetch({ source: 'none', logos: [] })
    await loadPublicBrand()
    expect(getCompanyLogos()).toBe(NO_COMPANY_LOGOS)
    mockFetch({ source: 'domain', tenantPublicId: ID, name: 'Acme', logos: [] })
    await loadPublicBrand()
    expect(getCompanyLogos()).toBe(NO_COMPANY_LOGOS)
    mockFetch({}, false)
    await loadPublicBrand()
    expect(getCompanyLogos()).toBe(NO_COMPANY_LOGOS)
  })
})
