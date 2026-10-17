// Lote 92 — programación de informes por correo: armar y leer el cron restringido, revisar destinatarios.
import { describe, expect, it } from 'vitest'
import { buildCron, checkEmails, deliveryTone, parseSchedule, splitEmails } from './reportSchedule'

describe('programación del informe', () => {
  it('lee los tres tipos de cron y rechaza lo demás', () => {
    expect(parseSchedule('0 7 * * *')).toMatchObject({ kind: 'DAILY', time: '07:00' })
    expect(parseSchedule(' 30 18 * * 1 ')).toMatchObject({ kind: 'WEEKLY', time: '18:30', dayOfWeek: 1 })
    expect(parseSchedule('0 7 * * 7')).toMatchObject({ kind: 'WEEKLY', dayOfWeek: 0 })
    expect(parseSchedule('15 6 28 * *')).toMatchObject({ kind: 'MONTHLY', time: '06:15', dayOfMonth: 28 })
    for (const bad of [null, undefined, '', '0 7 * *', '60 7 * * *', '0 24 * * *', '0 7 29 * *', '0 7 1 * 1', '*/5 * * * *']) expect(parseSchedule(bad)).toBeNull()
  })

  it('arma el cron y vuelve a leerlo igual', () => {
    for (const cron of ['0 7 * * *', '30 18 * * 1', '15 6 28 * *', '0 0 * * 0']) {
      const form = parseSchedule(cron)!
      expect(buildCron(form)).toBe(cron)
    }
    expect(buildCron({ kind: 'DAILY', time: 'xx', dayOfWeek: 1, dayOfMonth: 1 })).toBe('0 7 * * *')
  })

  it('separa y revisa los correos', () => {
    expect(splitEmails('a@b.co; c@d.org, e@f.net\n g@h.io')).toEqual(['a@b.co', 'c@d.org', 'e@f.net', 'g@h.io'])
    expect(checkEmails('a@b.co, A@B.CO')).toEqual({ emails: ['a@b.co'], bad: null, tooMany: false })
    expect(checkEmails('a@b.co, nope').bad).toBe('nope')
    expect(checkEmails(Array.from({ length: 11 }, (_, i) => `u${i}@x.co`).join(',')).tooMany).toBe(true)
    expect(checkEmails('')).toEqual({ emails: [], bad: null, tooMany: false })
  })

  it('tono del último envío', () => {
    expect([deliveryTone('SENT'), deliveryTone('SKIPPED'), deliveryTone('FAILED'), deliveryTone(null)]).toEqual(['ok', 'warn', 'bad', 'none'])
  })
})
