// Lote 98 — reglas puras de «Avisos al cliente».
import { describe, expect, it } from 'vitest'
import { enabledCount, isDirty, NOTIFICATION_KINDS, rowError, rowsFrom, type NotificationRow } from './notificationRules'

const row = (p: Partial<NotificationRow> = {}): NotificationRow => ({ kind: 'DELIVERED', emailEnabled: false, appEnabled: false, smsEnabled: false, extraEmails: '', extraPhones: '', ...p })

describe('avisos al cliente: reglas', () => {
  it('siempre salen los cinco avisos, apagados si no hay nada guardado', () => {
    const rows = rowsFrom(undefined)
    expect(rows.map((r) => r.kind)).toEqual([...NOTIFICATION_KINDS])
    expect(enabledCount(rows)).toBe(0)
    const saved = rowsFrom([{ kind: 'DELIVERED', emailEnabled: true, extraEmails: 'a@b.co' }, { kind: 'PICKED_UP', smsEnabled: true }])
    expect(enabledCount(saved)).toBe(2)
    expect(saved.find((r) => r.kind === 'DELIVERED')?.extraEmails).toBe('a@b.co')
  })

  it('compara lo editado con lo guardado sin fijarse en espacios ni separadores', () => {
    const saved = rowsFrom([{ kind: 'DELIVERED', emailEnabled: true, extraEmails: 'a@b.co, c@d.org' }])
    expect(isDirty(saved, saved)).toBe(false)
    expect(isDirty(saved.map((r) => (r.kind === 'DELIVERED' ? { ...r, extraEmails: 'a@b.co;  c@d.org' } : r)), saved)).toBe(false)
    expect(isDirty(saved.map((r) => (r.kind === 'DELIVERED' ? { ...r, appEnabled: true } : r)), saved)).toBe(true)
  })

  it('revisa correos, teléfonos y cantidades antes de enviar', () => {
    expect(rowError(row({ extraEmails: 'a@b.co; c@d.org' }))).toBeNull()
    expect(rowError(row({ extraEmails: 'a@b.co, nope' }))).toEqual({ error: 'email', value: 'nope' })
    expect(rowError(row({ extraPhones: '787-555-0100' }))).toBeNull()
    expect(rowError(row({ extraPhones: '12' }))).toEqual({ error: 'phone', value: '12' })
    expect(rowError(row({ extraEmails: Array.from({ length: 11 }, (_, i) => `u${i}@x.co`).join(',') }))).toEqual({ error: 'tooMany' })
    expect(rowError(row({ extraEmails: 'a@b.co,'.repeat(70) }))).toEqual({ error: 'tooLong' })
  })
})
