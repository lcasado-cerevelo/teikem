// Bloque «listo para pegarle al cliente» de una llave de API nueva (dictado de Luis, 2026-10-19): llave, secreto, URL base y cómo pedir el token.
// Puro: la URL base la pone quien llama (la del API, o la de esta página si el API cuelga del mismo servidor).
type Translate = (key: string, params?: Record<string, string | number>) => string

export function apiBaseUrl(configured: string, pageOrigin: string): string {
  return (configured || pageOrigin).replace(/\/+$/, '')
}

export interface ApiHandoff {
  baseUrl: string
  keyId: string
  secret: string
  scopes: readonly string[]
}

/** Texto que se copia: las 4 cosas que necesita el sistema del cliente y el comando de ejemplo para pedir el token (15 minutos). */
export function apiHandoffText(h: ApiHandoff, t: Translate): string {
  const token = `${h.baseUrl}/api/v1/api-auth/token`
  return [
    t('integrations.api.handoff.intro'),
    '',
    `${t('integrations.api.handoff.baseUrl')}: ${h.baseUrl}`,
    `${t('integrations.api.handoff.key')}: ${h.keyId}`,
    `${t('integrations.api.handoff.secret')}: ${h.secret}`,
    h.scopes.length > 0 ? `${t('integrations.api.handoff.scopes')}: ${h.scopes.join(', ')}` : '',
    '',
    t('integrations.api.handoff.how'),
    `curl -X POST ${token} -H "Content-Type: application/json" -d '{"clientId":"${h.keyId}","clientSecret":"${h.secret}"}'`,
    t('integrations.api.handoff.then'),
    `curl ${h.baseUrl}/api/v1/orders -H "Authorization: Bearer ACCESS_TOKEN"`,
  ]
    .filter((line, i, all) => line !== '' || (i > 0 && all[i - 1] !== ''))
    .join('\n')
}
