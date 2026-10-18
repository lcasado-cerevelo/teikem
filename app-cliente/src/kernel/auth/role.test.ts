// ¿La sesión es de un consignatario? El token trae el claim `loc`; sin él, o con un token que no se puede leer, es una persona del cliente.
import { isConsigneeToken } from './role'

function token(payload: object): string {
  const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${b64({ alg: 'HS256' })}.${b64(payload)}.firma`
}

describe('isConsigneeToken', () => {
  it('con el claim loc es consignatario', () => {
    expect(isConsigneeToken(token({ kind: 'PORTAL', cid: '7', loc: '12' }))).toBe(true)
  })
  it('sin loc es una persona del cliente', () => {
    expect(isConsigneeToken(token({ kind: 'PORTAL', cid: '7' }))).toBe(false)
  })
  it('un token ausente o ilegible no es consignatario (el servidor igual limita)', () => {
    expect(isConsigneeToken(null)).toBe(false)
    expect(isConsigneeToken(undefined)).toBe(false)
    expect(isConsigneeToken('')).toBe(false)
    expect(isConsigneeToken('abc')).toBe(false)
    expect(isConsigneeToken('a.%%%.c')).toBe(false)
  })
})
