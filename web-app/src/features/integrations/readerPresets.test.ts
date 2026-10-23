// Los modelos de lector (readerPresets.json) son datos que la pantalla agrega tal cual: deben ser lectores válidos. El servidor los prueba con el texto real de los documentos (NoticeDocumentTargetsTests).
import { describe, expect, it } from 'vitest'
import { READER_SOURCES, READER_TARGETS, scopeApplies } from './emailRules'
import presets from './readerPresets.json'

describe('readerPresets', () => {
  it('cada modelo tiene destino, fuente y condiciones válidos y expresiones que compilan', () => {
    expect(presets.length).toBeGreaterThan(0)
    for (const p of presets) {
      expect(READER_TARGETS).toContain(p.reader.target)
      expect(READER_SOURCES).toContain(p.reader.source)
      for (const c of p.reader.conditions) expect(scopeApplies(c.scope, p.reader.source)).toBe(true)
      expect(p.reader.fields.length).toBeGreaterThan(0)
      for (const f of p.reader.fields) if (f.lineRegex) expect(() => new RegExp(f.lineRegex, 'i')).not.toThrow()
    }
  })

  it('las claves no se repiten y todas tienen su texto en español e inglés', async () => {
    const keys = presets.map((p) => p.key)
    expect(new Set(keys).size).toBe(keys.length)
    const es = (await import('../../kernel/i18n/es.json')).default as unknown as { integrations: { email: { readers: { preset: Record<string, string> } } } }
    const en = (await import('../../kernel/i18n/en.json')).default as unknown as { integrations: { email: { readers: { preset: Record<string, string> } } } }
    for (const k of keys) {
      expect(es.integrations.email.readers.preset[k]).toBeTruthy()
      expect(en.integrations.email.readers.preset[k]).toBeTruthy()
    }
  })
})
