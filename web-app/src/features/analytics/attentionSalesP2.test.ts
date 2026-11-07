// Lote 222 — textos de los avisos SALES_CLIENT_PENDING (cliente nuevo por aprobar) y SALES_ORDER_URGENT (pedido urgente) en "Necesita tu atención", y las claves del panel SALES del Pulso.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionGroupLabel, attentionRowText, isKnownAttention, type AttentionItemDto } from './attention'
import { isKnownPanel } from './pulseLayout'

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
const tEn = (key: string, params?: Record<string, string | number>) => translate('en', key, params)
beforeAll(() => setLang('es'))

const pending = (params: Record<string, string> = {}): AttentionItemDto => ({ code: 'SALES_CLIENT_PENDING', module: 'ACCOUNTING', tone: 'warn', params: { publicId: 'c1', name: 'Farmacia Nueva', code: 'FARM2', seller: 'Ana Ruiz', orders: '2', days: '5', ...params }, route: '/clients', query: { client: 'c1' }, sinceUtc: null })
const urgent = (): AttentionItemDto => ({ code: 'SALES_ORDER_URGENT', module: 'WAREHOUSE', tone: 'danger', params: { publicId: 'o1', number: 'OV-00009', client: 'Farmacia Central', warehouse: 'Principal', note: 'Para hoy' }, route: '/sales-orders', query: { order: 'o1' }, sinceUtc: null })

describe('avisos de ventas (Lote 222)', () => {
  it('son tipos conocidos con su grupo', () => {
    expect(isKnownAttention('SALES_CLIENT_PENDING')).toBe(true)
    expect(isKnownAttention('SALES_ORDER_URGENT')).toBe(true)
    expect(attentionGroupLabel({ code: 'SALES_CLIENT_PENDING', total: 1 }, t)).toBe('Clientes nuevos por aprobar')
    expect(attentionGroupLabel({ code: 'SALES_ORDER_URGENT', total: 1 }, t)).toBe('Pedidos urgentes abiertos')
  })

  it('cliente nuevo por aprobar: título, quién lo dio de alta, cifras y «Revisar»', () => {
    const text = attentionRowText(pending(), t, 'es')
    expect(text.title).toBe('Cliente nuevo por aprobar: Farmacia Nueva')
    expect(text.detail).toBe('Dado de alta por Ana Ruiz')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Órdenes en espera 2', 'Días esperando 5'])
    expect(text.figures[1].emphasis).toBe(true)
    expect(text.reviewLabel).toBe('Revisar al cliente nuevo Farmacia Nueva')
    expect(attentionRowText(pending({ seller: '', orders: '0', days: '0' }), t, 'es')).toMatchObject({ detail: 'Dado de alta por un vendedor', figures: [] })
    expect(attentionRowText(pending(), tEn, 'en').title).toBe('New client to approve: Farmacia Nueva')
  })

  it('pedido urgente: número, cliente, almacén y nota', () => {
    const text = attentionRowText(urgent(), t, 'es')
    expect(text.title).toBe('Pedido urgente OV-00009')
    expect(text.detail).toBe('Farmacia Central · Principal · Para hoy')
    expect(text.reviewLabel).toBe('Revisar el pedido urgente OV-00009 (Farmacia Central)')
    expect(attentionRowText(urgent(), tEn, 'en').title).toBe('Urgent order OV-00009')
  })

  it('el panel SALES del Pulso es conocido y tiene título en los dos idiomas', () => {
    expect(isKnownPanel('SALES')).toBe(true)
    expect(t('analytics.pulse.panels.SALES')).toBe('Ventas')
    expect(tEn('analytics.pulse.panels.SALES')).toBe('Sales')
    expect(t('analytics.pulse.sales.urgentOpen')).toBe('Urgentes abiertos')
  })
})
