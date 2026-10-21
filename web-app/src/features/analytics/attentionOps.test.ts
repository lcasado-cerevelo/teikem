// Lote 89 (P12) — texto de los avisos ORDERS_NO_DRIVER (envíos sin chofer) y COD_UNRECONCILED (COD sin cuadrar) en "Necesita tu atención".
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionGroupLabel, attentionHref, attentionRowText, isKnownAttention, type AttentionItemDto } from './attention'

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
const tEn = (key: string, params?: Record<string, string | number>) => translate('en', key, params)
beforeAll(() => setLang('es'))

const noDriver = (params: Record<string, string> = {}): AttentionItemDto => ({
  code: 'ORDERS_NO_DRIVER', module: 'OPERATIONS', tone: 'danger', params: { count: '83', oldestDays: '3', ...params }, route: '/ops/dispatch', query: null, sinceUtc: null,
})
const cod = (params: Record<string, string> = {}): AttentionItemDto => ({
  code: 'COD_UNRECONCILED', module: 'ACCOUNTING', tone: 'warn', params: { amount: '2230.00', collections: '14', orders: '9', oldestDays: '4', ...params }, route: '/money/cod', query: { tab: 'reconcile' }, sinceUtc: null,
})

describe('aviso ORDERS_NO_DRIVER', () => {
  it('es un tipo conocido y se rotula', () => {
    expect(isKnownAttention('ORDERS_NO_DRIVER')).toBe(true)
    expect(attentionGroupLabel({ code: 'ORDERS_NO_DRIVER', total: 1, route: '/ops/dispatch' }, t)).toBe('Envíos sin chofer')
  })
  it('título con la cuenta, la más antigua y el enlace a la Sala de despacho', () => {
    const text = attentionRowText(noDriver(), t, 'es')
    expect(text.title).toBe('83 envíos sin chofer')
    expect(text.figures).toEqual([{ label: 'La más antigua', value: '3 días', emphasis: true }])
    expect(text.reviewLabel).toBe('Asignar chofer a 83 envíos')
    expect(attentionHref('/ops/dispatch', null)).toBe('/ops/dispatch')
  })
  it('una sola envío en singular; sin días no hay cifra; en inglés', () => {
    expect(attentionRowText(noDriver({ count: '1', oldestDays: '0' }), t, 'es')).toMatchObject({ title: '1 envío sin chofer', figures: [] })
    expect(attentionRowText(noDriver(), tEn, 'en').title).toBe('83 shipments without a driver')
  })
})

describe('aviso COD_UNRECONCILED', () => {
  it('es un tipo conocido y el enlace abre la pestaña Reconciliar', () => {
    expect(isKnownAttention('COD_UNRECONCILED')).toBe(true)
    expect(attentionGroupLabel({ code: 'COD_UNRECONCILED', total: 1, route: '/money/cod' }, t)).toBe('COD sin cuadrar')
    expect(attentionHref('/money/cod', { tab: 'reconcile' })).toBe('/money/cod?tab=reconcile')
  })
  it('el monto en dinero, los cobros, los envíos y el cobro más antiguo', () => {
    const text = attentionRowText(cod(), t, 'es')
    expect(text.title).toBe('$2,230.00 de COD sin cuadrar')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Cobros 14', 'Envíos 9', 'El más antiguo 4 días'])
    expect(text.figures.at(-1)?.emphasis).toBe(true)
    expect(text.reviewLabel).toBe('Cuadrar $2,230.00 de COD')
  })
  it('menos de 3 días no se destaca; en inglés', () => {
    expect(attentionRowText(cod({ oldestDays: '2' }), t, 'es').figures.at(-1)?.emphasis).toBe(false)
    expect(attentionRowText(cod(), tEn, 'en').title).toBe('$2,230.00 of COD not reconciled')
  })
})

// Lote 122 (P12) — aviso SLA_AT_RISK (envíos a punto de pasar su SLA o ya vencidas).
const sla = (params: Record<string, string> = {}): AttentionItemDto => ({
  code: 'SLA_AT_RISK', module: 'OPERATIONS', tone: 'danger', params: { atRisk: '4', breached: '2', minHours: '-3.5', ...params }, route: '/orders', query: null, sinceUtc: null,
})

describe('aviso SLA_AT_RISK', () => {
  it('es un tipo conocido y se rotula', () => {
    expect(isKnownAttention('SLA_AT_RISK')).toBe(true)
    expect(attentionGroupLabel({ code: 'SLA_AT_RISK', total: 1, route: '/orders' }, t)).toBe('Envíos en riesgo de pasar su SLA')
  })
  it('título con la suma, las cifras y la más apretada vencida', () => {
    const text = attentionRowText(sla(), t, 'es')
    expect(text.title).toBe('6 envíos en riesgo de pasar su SLA')
    expect(text.figures.map((f) => [f.label, f.value])).toEqual([['En riesgo', '4'], ['Vencidas', '2'], ['La más apretada', expect.stringMatching(/^Vencida hace 3[.,]5 h$/)]])
    expect(text.reviewLabel).toBe('Revisar 6 envíos con el SLA en riesgo')
  })
  it('una sola en singular; sin vencidas dice cuánto le queda; en inglés', () => {
    expect(attentionRowText(sla({ atRisk: '1', breached: '0', minHours: '5' }), t, 'es')).toMatchObject({ title: '1 envío en riesgo de pasar su SLA' })
    expect(attentionRowText(sla({ atRisk: '1', breached: '0', minHours: '5' }), t, 'es').figures[2].value).toBe('Quedan 5 h')
    expect(attentionRowText(sla(), tEn, 'en').title).toBe('6 shipments at risk of missing their SLA')
  })
})

// Lote 125 (P12) — alertas operacionales: intento fallido, reclamaciones abiertas, documentos de flota e facturas vencidas.
const alert = (code: string, params: Record<string, string>, route: string): AttentionItemDto => ({ code, module: 'OPERATIONS', tone: 'warn', params, route, query: null, sinceUtc: null })

describe('alertas operacionales', () => {
  it('son tipos conocidos y se rotulan', () => {
    for (const [code, label] of [['ORDERS_FAILED', 'Envíos en intento fallido'], ['CLAIMS_OPEN', 'Reclamaciones abiertas'], ['FLEET_DOCS_DUE', 'Documentos de flota por vencer'], ['INVOICES_OVERDUE', 'Facturas vencidas']] as const) {
      expect(isKnownAttention(code)).toBe(true)
      expect(attentionGroupLabel({ code, total: 1, route: '/orders' }, t)).toBe(label)
    }
  })
  it('intento fallido y facturas vencidas: título con la cuenta y la más antigua', () => {
    const failed = attentionRowText(alert('ORDERS_FAILED', { count: '4', oldestDays: '3' }, '/orders'), t, 'es')
    expect(failed.title).toBe('4 envíos en intento fallido')
    expect(failed.figures).toEqual([{ label: 'La más antigua', value: '3 días', emphasis: true }])
    expect(attentionRowText(alert('ORDERS_FAILED', { count: '1', oldestDays: '0' }, '/orders'), t, 'es')).toMatchObject({ title: '1 envío en intento fallido', figures: [] })
    const inv = attentionRowText(alert('INVOICES_OVERDUE', { count: '2', oldestDays: '12' }, '/money/billing'), t, 'es')
    expect(inv.title).toBe('2 facturas vencidas')
    expect(inv.reviewLabel).toBe('Revisar 2 facturas vencidas')
  })
  it('reclamaciones abiertas: cuenta, la más antigua y el valor declarado', () => {
    const text = attentionRowText(alert('CLAIMS_OPEN', { count: '2', oldestDays: '9', amount: '75.50' }, '/ops/claims'), t, 'es')
    expect(text.title).toBe('2 reclamaciones abiertas')
    expect(text.figures.map((f) => f.label)).toEqual(['La más antigua', 'Valor declarado'])
  })
  it('documentos de flota: vencidos, por vencer y el primero', () => {
    const text = attentionRowText(alert('FLEET_DOCS_DUE', { expired: '2', expiring: '3', minDays: '-4' }, '/catalog/fleet'), t, 'es')
    expect(text.title).toBe('5 documentos de flota vencidos o por vencer')
    expect(text.figures.map((f) => [f.label, f.value])).toEqual([['Vencidos', '2'], ['Por vencer', '3'], ['El primero', 'Vencido hace 4 días']])
    expect(attentionRowText(alert('FLEET_DOCS_DUE', { expired: '0', expiring: '1', minDays: '12' }, '/catalog/fleet'), tEn, 'en').figures[2].value).toBe('Expires in 12 days')
  })
})
