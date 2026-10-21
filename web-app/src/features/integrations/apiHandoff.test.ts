import { describe, expect, it } from 'vitest'
import { apiBaseUrl, apiHandoffText } from './apiHandoff'

const t = (k: string) => k.replace('integrations.api.handoff.', '')

describe('apiBaseUrl', () => {
  it('usa la URL configurada o, si no hay, la de la página; sin barra al final', () => {
    expect(apiBaseUrl('https://api.x.com/', 'https://web.x.com')).toBe('https://api.x.com')
    expect(apiBaseUrl('', 'https://web.x.com')).toBe('https://web.x.com')
  })
})

describe('apiHandoffText', () => {
  it('lleva URL base, llave, secreto, alcances y el comando del token con la llave y el secreto', () => {
    const text = apiHandoffText({ baseUrl: 'https://api.x.com', keyId: 'tk_abc', secret: 'tks_xyz', scopes: ['orders:read', 'orders:write'] }, t)
    expect(text).toContain('baseUrl: https://api.x.com')
    expect(text).toContain('key: tk_abc')
    expect(text).toContain('secret: tks_xyz')
    expect(text).toContain('scopes: orders:read, orders:write')
    expect(text).toContain(`curl -X POST https://api.x.com/api/v1/api-auth/token -H "Content-Type: application/json" -d '{"clientId":"tk_abc","clientSecret":"tks_xyz"}'`)
  })

  it('sin alcances no deja la línea de alcances', () => {
    expect(apiHandoffText({ baseUrl: 'https://a', keyId: 'k', secret: 's', scopes: [] }, t)).not.toContain('scopes:')
  })
})
