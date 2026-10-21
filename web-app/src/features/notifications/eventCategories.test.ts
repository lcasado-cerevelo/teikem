import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, translate } from '../../kernel/i18n/i18n'
import { groupEventsByCategory } from './eventCategories'

beforeAll(() => setLang('es'))
const ev = (code: string, name: string, category?: string) => ({ code, name, nameEn: name, category, isOptional: true, variables: [], defaultSubject: '', defaultSubjectEn: '', defaultBody: '', defaultBodyEn: '' })

describe('eventos de aviso agrupados por categoría (Lote 197)', () => {
  it('los grupos salen en orden fijo, los eventos por nombre y uno sin categoría cae en Otros', () => {
    const groups = groupEventsByCategory([
      ev('FLEET_MAINTENANCE_DUE_SOON', 'Mantenimiento próximo', 'MAINTENANCE'),
      ev('INVENTORY_STOCK_LOW', 'Existencia baja', 'WAREHOUSE'),
      ev('ASN_RECEIVED', 'Aviso de llegada recibido', 'WAREHOUSE'),
      ev('FLEET_DOC_EXPIRING', 'Documentos de la flota por vencer', 'TRANSPORT'),
      ev('TEST', 'Aviso de prueba'),
    ])
    expect(groups.map((g) => g.category)).toEqual(['WAREHOUSE', 'TRANSPORT', 'MAINTENANCE', 'OTHER'])
    expect(groups[0].events.map((e) => e.code)).toEqual(['ASN_RECEIVED', 'INVENTORY_STOCK_LOW'])
  })

  it('cada categoría tiene su rótulo en español e inglés', () => {
    for (const c of ['WAREHOUSE', 'SALES', 'TRANSPORT', 'MAINTENANCE', 'BILLING', 'INTEGRATIONS', 'OTHER']) {
      expect(translate('es', `notifications.rules.categories.${c}`)).not.toBe(`notifications.rules.categories.${c}`)
      expect(translate('en', `notifications.rules.categories.${c}`)).not.toBe(`notifications.rules.categories.${c}`)
    }
  })
})
