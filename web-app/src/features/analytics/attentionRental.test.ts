// Lote F17 (Rentas F-R1) — texto del aviso RENTAL_DUE (renta vencida o por vencer, Lote 29) en "Necesita tu atención":
// título según los días, cliente · localidad · almacén, cifras (recogido, equipos y días vencida) y el enlace de "Revisar".
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionGroupLabel, attentionHref, attentionRowText, isKnownAttention, type AttentionItemDto } from './attention'

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
const tEn = (key: string, params?: Record<string, string | number>) => translate('en', key, params)
beforeAll(() => setLang('es'))

const item = (params: Record<string, string>): AttentionItemDto => ({
  code: 'RENTAL_DUE',
  module: 'WAREHOUSE',
  tone: params.overdue === 'true' ? 'danger' : 'warn',
  params: {
    publicId: 'r1',
    number: 'REN-00004',
    client: 'Hospital Damas',
    location: 'Sala 3',
    warehouse: 'ALM-01',
    pickupDate: '2026-10-08',
    units: '2',
    status: 'ON_RENT',
    ...params,
  },
  route: '/warehouse/rentals',
  query: { rental: 'r1' },
  sinceUtc: null,
})

describe('aviso RENTAL_DUE', () => {
  it('es un tipo conocido; el grupo se rotula "Rentas vencidas o por vencer"', () => {
    expect(isKnownAttention('RENTAL_DUE')).toBe(true)
    expect(attentionGroupLabel({ code: 'RENTAL_DUE', total: 3, route: '/warehouse/rentals' }, t)).toBe('Rentas vencidas o por vencer')
  })

  it('por vencer: "vence en N días", dónde y cifras; "Revisar" abre la renta', () => {
    const text = attentionRowText(item({ daysToPickup: '3', overdue: 'false' }), t, 'es')
    expect(text.title).toBe('Renta REN-00004: vence en 3 días')
    expect(text.detail).toBe('Hospital Damas · Sala 3 · ALM-01')
    expect(text.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Recogido 10/08/2026', 'Equipos 2'])
    expect(text.reviewLabel).toBe('Revisar la renta REN-00004 (Hospital Damas)')
    expect(attentionHref('/warehouse/rentals', { rental: 'r1' })).toBe('/warehouse/rentals?rental=r1')
    expect(attentionRowText(item({ daysToPickup: '1', overdue: 'false' }), t, 'es').title).toBe('Renta REN-00004: vence en 1 día')
    expect(attentionRowText(item({ daysToPickup: '0', overdue: 'false' }), t, 'es').title).toBe('Renta REN-00004: se recoge hoy')
  })

  it('vencida: título, días vencida destacados; en inglés', () => {
    const text = attentionRowText(item({ daysToPickup: '-4', overdue: 'true' }), t, 'es')
    expect(text.title).toBe('Renta REN-00004 vencida')
    expect(text.figures.at(-1)).toEqual({ label: 'Días vencida', value: '4', emphasis: true })
    expect(attentionRowText(item({ daysToPickup: '-4', overdue: 'true' }), tEn, 'en').title).toBe('Rental REN-00004 overdue')
  })
})
