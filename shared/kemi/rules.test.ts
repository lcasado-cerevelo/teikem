// Pruebas de lo compartido de Kemi (Lote 218): voz y cerebro. Corren en la web (vitest) y en el portal (jest) con el mismo archivo.
import { createSearchBrain } from './brain'
import type { HelpEntry } from './helpSearch'
import { cleanTranscript, speechErrorCode, speechLocale } from './speech'

describe('voz', () => {
  it('el idioma de reconocimiento sigue al de la pantalla', () => {
    expect(speechLocale('es')).toBe('es-PR')
    expect(speechLocale('en')).toBe('en-US')
    expect(speechLocale('en-GB')).toBe('en-US')
    expect(speechLocale('')).toBe('es-PR')
  })
  it('los errores se reducen a los casos que se explican', () => {
    expect(speechErrorCode('not-allowed')).toBe('denied')
    expect(speechErrorCode('service-not-allowed')).toBe('denied')
    expect(speechErrorCode('no-speech')).toBe('no-speech')
    expect(speechErrorCode('network')).toBe('network')
    expect(speechErrorCode('language-not-supported')).toBe('unsupported')
    expect(speechErrorCode('algo raro')).toBe('other')
    expect(speechErrorCode(undefined)).toBe('other')
  })
  it('lo dicho queda como una pregunta escrita', () => {
    expect(cleanTranscript('  cómo   cancelo un aviso de llegada. ')).toBe('cómo cancelo un aviso de llegada')
    expect(cleanTranscript('')).toBe('')
  })
})

const ENTRIES: HelpEntry[] = [
  { k: 'm', t: 'Cancelar un aviso de llegada', c: 'Almacén', x: 'Mientras el aviso sigue esperado se puede cancelar desde Llegadas con el botón Cancelar aviso.' },
  { k: 'f', t: 'No me deja editar el aviso de salida', c: 'Almacén', x: 'Solo se edita mientras sigue Solicitado; ya despachado no se puede cambiar.' },
  { k: 'm', t: 'Cambiar el idioma', c: 'Ajustes', x: 'En Ajustes se escoge español o inglés.' },
]

describe('cerebro de búsqueda', () => {
  it('trae las opciones de su índice y carga el índice una sola vez', async () => {
    let loads = 0
    const brain = createSearchBrain('portal', async () => {
      loads++
      return ENTRIES
    })
    expect(brain.audience).toBe('portal')
    const first = await brain.ask({ text: 'cómo cancelo un aviso de llegada', lang: 'es', topics: [] })
    expect(first[0]?.t).toBe('Cancelar un aviso de llegada')
    const again = await brain.ask({ text: 'cambiar idioma', lang: 'es', topics: [] })
    expect(again[0]?.t).toBe('Cambiar el idioma')
    expect(loads).toBe(1)
  })
  it('«busca por otro lado» no repite lo ya mostrado', async () => {
    const brain = createSearchBrain('staff', async () => ENTRIES)
    const first = await brain.ask({ text: 'aviso', lang: 'es', topics: [], limit: 1 })
    const second = await brain.ask({ text: 'aviso', lang: 'es', topics: [], exclude: new Set(first) })
    expect(second.some((e) => first.includes(e))).toBe(false)
  })
  it('si el índice no carga, vuelve a intentar la próxima vez', async () => {
    let n = 0
    const brain = createSearchBrain('portal', async () => {
      if (++n === 1) throw new Error('sin red')
      return ENTRIES
    })
    await expect(brain.ask({ text: 'idioma', lang: 'es', topics: [] })).rejects.toThrow('sin red')
    expect((await brain.ask({ text: 'idioma', lang: 'es', topics: [] })).length).toBeGreaterThan(0)
  })
})

import { faceOf } from './rules'
import { portalScreenTopics } from './helpSearch'

describe('la cara de Kemi', () => {
  it('en reposo sonríe con los ojos abiertos', () => {
    expect(faceOf('calm', false, false)).toEqual({ eyeL: { sx: 1, sy: 1 }, eyeR: { sx: 1, sy: 1 }, pupil: { x: 0, y: 0 }, mouth: 'smile' })
  })
  it('parpadea con los dos ojos y guiña con el derecho', () => {
    expect(faceOf('calm', true, false).eyeL.sy).toBe(0.08)
    expect(faceOf('calm', true, false).eyeR.sy).toBe(0.08)
    const wink = faceOf('wink', false, false)
    expect(wink.eyeL.sy).toBe(1)
    expect(wink.eyeR.sy).toBe(0.08)
  })
  it('cuando llega algo nuevo abre los ojos y sonríe grande', () => {
    const f = faceOf('calm', false, true)
    expect(f.eyeL).toEqual({ sx: 1.12, sy: 1.12 })
    expect(f.mouth).toBe('happy')
  })
  it('mira hacia donde toca: gestos de mirar y, con el puntero encima, hacia el puntero', () => {
    expect(faceOf('look-l', false, false).pupil).toEqual({ x: -4, y: 0 })
    expect(faceOf('look-r', false, false).pupil).toEqual({ x: 4, y: 0 })
    expect(faceOf('worried', false, false)).toMatchObject({ pupil: { x: 0, y: -3 }, mouth: 'worried' })
    expect(faceOf('alert', false, false, { x: 2.5, y: -1 }).pupil).toEqual({ x: 2.5, y: -1 })
  })
  it('cada gesto grande tiene su boca', () => {
    expect(faceOf('tongue', false, false).mouth).toBe('tongue')
    expect(faceOf('surprised', false, false).mouth).toBe('surprised')
    expect(faceOf('happy', false, false)).toMatchObject({ mouth: 'happy', eyeL: { sy: 0.8 } })
  })
})

describe('temas por pantalla del portal', () => {
  it('cada sección del portal sube lo suyo', () => {
    expect(portalScreenTopics('/warehouse')).toContain('portal del cliente de almacen')
    expect(portalScreenTopics('/order/abc')).toContain('como va mi envio')
    expect(portalScreenTopics('/messages')).toContain('mensajes del portal')
    expect(portalScreenTopics('/pantalla-rara')).toEqual([])
    expect(portalScreenTopics('/warehouse/')).toContain('almacen')
  })
})
