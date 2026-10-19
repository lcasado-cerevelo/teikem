// Lote 124 — la búsqueda de Kemi sin IA: palabras sin acentos ni plurales, intención por la forma de la pregunta, suelo de calidad y búsqueda "por otro lado".
import { describe, expect, it } from 'vitest'
import idx from './helpIndex.json'
import { buildIndex, intentOf, norm, palabras, type HelpEntry } from './helpSearch'

const e = (k: 'f' | 'm', t: string, x: string, c = 'Capítulo'): HelpEntry => ({ k, t, c, x })
const small = buildIndex([
  e('m', 'Cambio de contraseña', 'Cómo cambiar la contraseña desde el perfil: Mi perfil, Cambiar contraseña, escriba la actual y la nueva.'),
  e('f', '¿Qué significa "Credenciales inválidas."?', 'El correo no existe o la contraseña es incorrecta; el mensaje es el mismo a propósito.'),
  e('m', 'Registrar la entrega', 'El personal registra que una orden se entregó sin esperar a la app del chofer; queda el comprobante.'),
  e('m', 'Facturas', 'Las facturas del cliente se ven en su portal, de solo lectura.'),
])

describe('palabras', () => {
  it('sin acentos, sin palabras vacías, sin plurales y con las formas del verbo juntas', () => {
    expect(norm('¿Cómo ENTREGÓ la Órden?')).toBe('como entrego la orden')
    expect(palabras('Los clientes pagan facturas')).toEqual(['client', 'pagan', 'factur'])
    expect(palabras('registro')[0]).toBe(palabras('registrar')[0])
    expect(palabras('suspendo')[0]).toBe(palabras('suspender')[0])
    expect(palabras('contrato')[0]).not.toBe(palabras('contraseña')[0])
  })
})

describe('intentOf', () => {
  it('un fallo es de la FAQ, un procedimiento es del manual', () => {
    expect(intentOf('no me deja entrar')).toBe('blocked')
    expect(intentOf('qué significa credenciales inválidas')).toBe('blocked')
    expect(intentOf('cómo se hace un pick up')).toBe('how')
    expect(intentOf('facturas')).toBe('any')
  })
})

describe('búsqueda', () => {
  it('encuentra por las palabras de la pregunta, sin importar plurales ni acentos', () => {
    expect(small.search('como cambio mi contrasena').hits[0].entry.t).toBe('Cambio de contraseña')
    expect(small.search('credenciales invalidas').hits[0].entry.k).toBe('f')
    expect(small.search('quiero registrar una entrega').hits[0].entry.t).toBe('Registrar la entrega')
    expect(small.search('facturas').hits[0].entry.t).toBe('Facturas')
  })
  it('sin nada parecido no contesta (el suelo de calidad): "no lo sé" es una respuesta', () => {
    expect(small.search('xyzzy plugh').hits).toEqual([])
    expect(small.search('de la el').hits).toEqual([])
  })
  it('"por otro lado": salta lo que ya mostró', () => {
    const first = small.search('contraseña correo', 1).hits[0].entry
    const second = small.search('contraseña correo', 5, new Set([first])).hits
    expect(second.some((h) => h.entry === first)).toBe(false)
  })
})

describe('con el manual y la FAQ de verdad', () => {
  const all = idx as HelpEntry[]
  const real = buildIndex(all)
  it('el índice trae FAQ y manual', () => {
    expect(real.size).toBeGreaterThan(2000)
    expect(all.some((x) => x.k === 'f')).toBe(true)
    expect(all.some((x) => x.k === 'm')).toBe(true)
  })
  it.each([
    ['cómo cambio mi contraseña', 'contraseña'],
    ['no me deja entrar credenciales inválidas', 'Credenciales inválidas'],
    ['qué es el SLA en riesgo', 'SLA en riesgo'],
    ['COD sin cuadrar', 'COD'],
  ])('«%s» trae algo sobre «%s» entre las primeras opciones', (q, word) => {
    const hits = real.search(q).hits
    expect(hits.length).toBeGreaterThan(0)
    expect(hits.some((h) => (h.entry.t + h.entry.x).toLowerCase().includes(word.toLowerCase()))).toBe(true)
  })
  it('algo que no existe responde vacío', () => {
    expect(real.search('xyzzy plugh').hits).toEqual([])
  })
})
