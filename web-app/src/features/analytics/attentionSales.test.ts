// Lote 67 (P4 S4) — texto del aviso SALES_BACKORDER_READY (orden de venta con backorder que ya se puede surtir) en "Necesita tu atención".
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionGroupLabel, attentionHref, attentionRowText, isKnownAttention, type AttentionItemDto } from './attention'

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
const tEn = (key: string, params?: Record<string, string | number>) => translate('en', key, params)
beforeAll(() => setLang('es'))

const item = (params: Record<string, string> = {}): AttentionItemDto => ({
  code: 'SALES_BACKORDER_READY',
  module: 'WAREHOUSE',
  tone: 'warn',
  params: { publicId: 'o1', number: 'OV-00007', client: 'Farmacia Central', warehouse: 'Almacén principal', lines: '2', units: '5', days: '3', ...params },
  route: '/sales-orders',
  query: { order: 'o1' },
  sinceUtc: null,
})

describe('aviso SALES_BACKORDER_READY', () => {
  it('es un tipo conocido y el grupo se rotula', () => {
    expect(isKnownAttention('SALES_BACKORDER_READY')).toBe(true)
    expect(attentionGroupLabel({ code: 'SALES_BACKORDER_READY', total: 2, route: '/sales-orders' }, t)).toBe('Órdenes de venta con backorder por surtir')
  })

  it('título, dónde, cifras y el enlace que abre la orden', () => {
    const text = attentionRowText(item(), t, 'es')
    expect(text.title).toBe('Orden OV-00007: ya hay inventario para surtir')
    expect(text.detail).toBe('Farmacia Central · Almacén principal')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Unidades surtibles 5', 'Líneas 2', 'Días esperando 3'])
    expect(text.reviewLabel).toBe('Revisar la orden OV-00007 (Farmacia Central)')
    expect(attentionHref('/sales-orders', { order: 'o1' })).toBe('/sales-orders?order=o1')
  })

  it('más de 7 días esperando se destaca; sin días no se muestra la cifra; en inglés', () => {
    expect(attentionRowText(item({ days: '9' }), t, 'es').figures.at(-1)).toMatchObject({ label: 'Días esperando', emphasis: true })
    expect(attentionRowText(item({ days: '0' }), t, 'es').figures.map((f) => f.label)).toEqual(['Unidades surtibles', 'Líneas'])
    const en = attentionRowText(item(), tEn, 'en')
    expect(en.title).toBe('Order OV-00007: stock is now available to fulfill')
    expect(en.reviewLabel).toBe('Review order OV-00007 (Farmacia Central)')
  })
})
