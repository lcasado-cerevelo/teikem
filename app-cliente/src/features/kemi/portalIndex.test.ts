// El índice de ayuda del PORTAL (Lote 218): el cliente encuentra lo suyo y NO encuentra lo interno del personal. Se prueba con el índice real que se versiona.
import { buildIndex, portalScreenTopics, type HelpEntry } from '../../../../shared/kemi/helpSearch'
import portal from './helpIndex.json'

const index = buildIndex(portal as HelpEntry[])
const top = (q: string, path = '/') => index.search(q, 5, undefined, portalScreenTopics(path)).hits.map((h) => h.entry)

describe('índice de ayuda del portal', () => {
  it('trae solo lo marcado para el portal, sin marcas internas', () => {
    expect(index.size).toBeGreaterThan(80)
    for (const e of portal as Array<HelpEntry & Record<string, unknown>>) {
      expect(e.p).toBeUndefined()
      expect(e.s).toBeUndefined()
      expect(e.x).not.toMatch(/kemi:(portal|staff)/)
    }
  })

  it('el cliente encuentra cómo cancelar y cambiar un aviso de llegada', () => {
    const t = top('cómo cancelo un aviso de llegada', '/warehouse').map((e) => e.t)
    expect(t.some((x) => /cancelo un aviso de llegada/i.test(x))).toBe(true)
    expect(top('puedo cambiar un aviso de salida', '/warehouse').some((e) => /cambiar un aviso de salida/i.test(e.t))).toBe(true)
  })

  it('encuentra el idioma, la huella y los mensajes', () => {
    expect(top('cómo cambio al inglés').some((e) => /inglés/i.test(e.t))).toBe(true)
    expect(top('no me aparece abrir con huella').some((e) => /huella/i.test(e.t))).toBe(true)
    expect(top('mensajes con el equipo de servicio al cliente', '/messages').length).toBeGreaterThan(0)
  })

  it('lo interno del personal no está: ni permisos, ni liquidación de choferes, ni endpoints, ni cómo publicar el documento legal', () => {
    const all = (portal as HelpEntry[]).map((e) => `${e.t} ${e.c}`).join('\n')
    expect(all).not.toMatch(/liquidaci[oó]n de choferes|rutas del d[ií]a|roles y usuarios|Endpoints|Quién aceptó|para el dueño/i)
    // preguntar por lo del personal no trae un apartado de lo del personal (a lo sumo, algo del cliente que menciona la palabra)
    expect(top('liquidación de choferes').every((e) => !/liquidaci/i.test(e.t))).toBe(true)
    expect(top('cómo creo un rol y le doy permisos a un usuario').every((e) => !/\broles?\b|permisos?\b/i.test(e.t))).toBe(true)
  })
})
