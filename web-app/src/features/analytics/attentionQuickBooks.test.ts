// Lote 225 — texto del aviso SALES_QUICKBOOKS_PENDING («Facturas por pasar a QuickBooks: N») en "Necesita tu atención" y la cifra del panel Ventas del Pulso.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionGroupLabel, attentionHref, attentionRowText, isKnownAttention, type AttentionItemDto } from './attention'

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
const tEn = (key: string, params?: Record<string, string | number>) => translate('en', key, params)
beforeAll(() => setLang('es'))

const item = (params: Record<string, string> = {}): AttentionItemDto => ({ code: 'SALES_QUICKBOOKS_PENDING', module: 'ACCOUNTING', tone: 'danger', params: { count: '12', exported: '3', oldest: '2026-10-28', days: '10', ...params }, route: '/sales-orders', query: { tab: 'quickbooks' }, sinceUtc: null })

describe('aviso de QuickBooks (Lote 225)', () => {
  it('es un tipo conocido con su grupo y abre la pestaña QuickBooks', () => {
    expect(isKnownAttention('SALES_QUICKBOOKS_PENDING')).toBe(true)
    expect(attentionGroupLabel({ code: 'SALES_QUICKBOOKS_PENDING', total: 1 }, t)).toBe('Facturas por pasar a QuickBooks')
    expect(attentionHref(item().route, item().query)).toBe('/sales-orders?tab=quickbooks')
  })

  it('título con la cifra, las que ya salieron, la más vieja y los días', () => {
    const text = attentionRowText(item(), t, 'es')
    expect(text.title).toBe('Facturas por pasar a QuickBooks: 12')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Ya salieron en un archivo 3', 'La más vieja 10/28/2026', 'Días esperando 10'])
    expect(text.figures[2].emphasis).toBe(true)
    expect(text.reviewLabel).toBe('Pasar 12 facturas a QuickBooks')
    expect(attentionRowText(item({ exported: '0', days: '0' }), t, 'es').figures.map((f) => f.label)).toEqual(['La más vieja'])
    expect(attentionRowText(item(), tEn, 'en').title).toBe('Invoices to pass to QuickBooks: 12')
  })

  it('la cifra del panel Ventas tiene su texto en los dos idiomas', () => {
    expect(t('analytics.pulse.sales.invoicesToQuickBooks')).toBe('Facturas por pasar a QuickBooks')
    expect(tEn('analytics.pulse.sales.invoicesToQuickBooks')).toBe('Invoices to send to QuickBooks')
  })
})
