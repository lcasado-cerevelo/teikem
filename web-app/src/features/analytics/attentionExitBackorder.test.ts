// Lote 194 — texto del aviso EXIT_NOTICE_BACKORDER (aviso de salida con pendiente por reservar) en "Necesita tu atención".
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { attentionGroupLabel, attentionHref, attentionRowText, isKnownAttention, type AttentionItemDto } from './attention'

const t = (key: string, params?: Record<string, string | number>) => translate('es', key, params)
beforeAll(() => setLang('es'))

const item = (params: Record<string, string> = {}): AttentionItemDto => ({
  code: 'EXIT_NOTICE_BACKORDER',
  module: 'WAREHOUSE',
  tone: 'warn',
  params: { publicId: 'n1', number: 'SAL-00007', client: 'Farmacia Central', warehouse: 'Almacén principal', lines: '2', units: '5', fulfillable: '0', days: '3', ...params },
  route: '/warehouse/notices',
  query: { tab: 'exit-notices', notice: 'n1' },
  sinceUtc: null,
})

describe('aviso EXIT_NOTICE_BACKORDER', () => {
  it('es un tipo conocido, rotula el grupo y abre el aviso', () => {
    expect(isKnownAttention('EXIT_NOTICE_BACKORDER')).toBe(true)
    expect(attentionGroupLabel({ code: 'EXIT_NOTICE_BACKORDER', total: 2, route: '/warehouse/notices' }, t)).toBe('Avisos de salida con pendiente')
    expect(attentionHref('/warehouse/notices', { tab: 'exit-notices', notice: 'n1' })).toBe('/warehouse/notices?tab=exit-notices&notice=n1')
  })

  it('esperando mercancía: título simple y cifras de pendiente; con inventario ya reservable, otro título y la cifra destacada', () => {
    const waiting = attentionRowText(item(), t, 'es')
    expect(waiting.title).toBe('Aviso SAL-00007: tiene pendiente por reservar')
    expect(waiting.figures.map((f) => `${f.label} ${f.value}`)).toEqual(['Unidades pendientes 5', 'Líneas 2', 'Días esperando 3'])
    const ready = attentionRowText(item({ fulfillable: '3' }), t, 'es')
    expect(ready.title).toBe('Aviso SAL-00007: ya hay inventario para su pendiente')
    expect(ready.figures).toContainEqual({ label: 'Ya reservables', value: '3', emphasis: true })
    expect(ready.reviewLabel).toBe('Revisar el aviso SAL-00007 (Farmacia Central)')
  })
})
