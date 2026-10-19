// Lote 89 (P12) — texto de los avisos ORDERS_NO_DRIVER (órdenes sin chofer) y COD_UNRECONCILED (COD sin cuadrar) en "Necesita tu atención".
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
    expect(attentionGroupLabel({ code: 'ORDERS_NO_DRIVER', total: 1, route: '/ops/dispatch' }, t)).toBe('Órdenes sin chofer')
  })
  it('título con la cuenta, la más antigua y el enlace a la Sala de despacho', () => {
    const text = attentionRowText(noDriver(), t, 'es')
    expect(text.title).toBe('83 órdenes sin chofer')
    expect(text.figures).toEqual([{ label: 'La más antigua', value: '3 días', emphasis: true }])
    expect(text.reviewLabel).toBe('Asignar chofer a 83 órdenes')
    expect(attentionHref('/ops/dispatch', null)).toBe('/ops/dispatch')
  })
  it('una sola orden en singular; sin días no hay cifra; en inglés', () => {
    expect(attentionRowText(noDriver({ count: '1', oldestDays: '0' }), t, 'es')).toMatchObject({ title: '1 orden sin chofer', figures: [] })
    expect(attentionRowText(noDriver(), tEn, 'en').title).toBe('83 orders without a driver')
  })
})

describe('aviso COD_UNRECONCILED', () => {
  it('es un tipo conocido y el enlace abre la pestaña Reconciliar', () => {
    expect(isKnownAttention('COD_UNRECONCILED')).toBe(true)
    expect(attentionGroupLabel({ code: 'COD_UNRECONCILED', total: 1, route: '/money/cod' }, t)).toBe('COD sin cuadrar')
    expect(attentionHref('/money/cod', { tab: 'reconcile' })).toBe('/money/cod?tab=reconcile')
  })
  it('el monto en dinero, los cobros, las órdenes y el cobro más antiguo', () => {
    const text = attentionRowText(cod(), t, 'es')
    expect(text.title).toBe('$2,230.00 de COD sin cuadrar')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Cobros 14', 'Órdenes 9', 'El más antiguo 4 días'])
    expect(text.figures.at(-1)?.emphasis).toBe(true)
    expect(text.reviewLabel).toBe('Cuadrar $2,230.00 de COD')
  })
  it('menos de 3 días no se destaca; en inglés', () => {
    expect(attentionRowText(cod({ oldestDays: '2' }), t, 'es').figures.at(-1)?.emphasis).toBe(false)
    expect(attentionRowText(cod(), tEn, 'en').title).toBe('$2,230.00 of COD not reconciled')
  })
})

// Lote 122 (P12) — aviso SLA_AT_RISK (órdenes a punto de pasar su SLA o ya vencidas).
const sla = (params: Record<string, string> = {}): AttentionItemDto => ({
  code: 'SLA_AT_RISK', module: 'OPERATIONS', tone: 'danger', params: { atRisk: '4', breached: '2', minHours: '-3.5', ...params }, route: '/orders', query: null, sinceUtc: null,
})

describe('aviso SLA_AT_RISK', () => {
  it('es un tipo conocido y se rotula', () => {
    expect(isKnownAttention('SLA_AT_RISK')).toBe(true)
    expect(attentionGroupLabel({ code: 'SLA_AT_RISK', total: 1, route: '/orders' }, t)).toBe('Órdenes en riesgo de pasar su SLA')
  })
  it('título con la suma, las cifras y la más apretada vencida', () => {
    const text = attentionRowText(sla(), t, 'es')
    expect(text.title).toBe('6 órdenes en riesgo de pasar su SLA')
    expect(text.figures.map((f) => [f.label, f.value])).toEqual([['En riesgo', '4'], ['Vencidas', '2'], ['La más apretada', expect.stringMatching(/^Vencida hace 3[.,]5 h$/)]])
    expect(text.reviewLabel).toBe('Revisar 6 órdenes con el SLA en riesgo')
  })
  it('una sola en singular; sin vencidas dice cuánto le queda; en inglés', () => {
    expect(attentionRowText(sla({ atRisk: '1', breached: '0', minHours: '5' }), t, 'es')).toMatchObject({ title: '1 orden en riesgo de pasar su SLA' })
    expect(attentionRowText(sla({ atRisk: '1', breached: '0', minHours: '5' }), t, 'es').figures[2].value).toBe('Quedan 5 h')
    expect(attentionRowText(sla(), tEn, 'en').title).toBe('6 orders at risk of missing their SLA')
  })
})
