import en from './en.json'
import es from './es.json'
import { translate } from './i18n'

type Dict = { [k: string]: string | Dict }
function keys(d: Dict, prefix = ''): string[] {
  return Object.entries(d).flatMap(([k, v]) => (typeof v === 'string' ? [prefix + k] : keys(v, `${prefix}${k}.`)))
}
function params(text: string): string[] {
  return [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort()
}
function get(d: Dict, key: string): string {
  return key.split('.').reduce<string | Dict>((n, p) => (n as Dict)[p], d) as string
}

describe('textos es/en', () => {
  it('las dos listas tienen exactamente las mismas claves y los mismos parámetros', () => {
    const k = keys(es as Dict)
    expect(keys(en as Dict).sort()).toEqual([...k].sort())
    for (const key of k) expect(params(get(en as Dict, key))).toEqual(params(get(es as Dict, key)))
  })
  it('sustituye parámetros, cae al español y deja ver la clave que falta', () => {
    expect(translate('es', 'home.greeting', { name: 'Ana' })).toBe('Hola, Ana')
    expect(translate('en', 'home.greeting', { name: 'Ana' })).toBe('Hello, Ana')
    expect(translate('en', 'no.existe.esta.clave')).toBe('no.existe.esta.clave')
    expect(translate('es', 'settings.computerExpires')).toContain('{date}')
  })
})
