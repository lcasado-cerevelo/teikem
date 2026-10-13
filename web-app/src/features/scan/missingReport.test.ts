// Lote F-G2 — Informe de faltantes: agrupado por zona (las zonas primero; «Sin zona» y «Zona ambigua» al final), columnas de la maqueta,
// subtotales y el resumen de pendientes y escaneadas en la sesión.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import { missingReportSpec, zoneGroupOf } from './missingReport'

const o = (n: number, zoneCode: string | null, zoneAmbiguous = false, pieces = 1) => ({
  id: n, publicId: `o-${n}`, orderNumber: `ORD-${n}`, packBatchNumber: `EMP-${n}`, clientInvoiceNumber: `F-${n}`, clientName: 'Acme', consigneeName: `Cons ${n}`,
  city: 'Bayamón', postalCode: '00961', zoneCode, zoneAmbiguous, statusCode: 'CONFIRMED', status: 'Confirmada', requestedDate: '2026-10-13', pieces,
})

beforeAll(() => setLang('es'))

describe('missingReportSpec', () => {
  it('grupos por zona y resumen', () => {
    const orders = [o(1, null), o(2, 'R-02', false, 3), o(3, 'R-01'), o(4, null, true), o(5, 'R-01', false, 2)]
    const s = missingReportSpec(orders, { t, locale: 'es', filters: [], scanned: 7, date: (iso) => iso ?? '' })
    expect(s.sections.map((x) => x.title)).toEqual(['R-01 (2)', 'R-02 (1)', 'Sin zona (1)', 'Zona ambigua (1)'])
    expect(s.columns.map((c) => c.header)).toEqual(['Empaque', 'Orden', 'Factura', 'Cliente', 'Consignatario', 'Pueblo', 'ZIP', 'Piezas', 'Fecha pedida'])
    expect(s.sections[0].rows[0]).toEqual(['EMP-3', 'ORD-3', 'F-3', 'Acme', 'Cons 3', 'Bayamón', '00961', 1, '2026-10-13'])
    expect(s.sections[0].subtotal).toEqual(['Subtotal (2 órdenes)', '', '', '', '', '', '', 3, ''])
    expect(s.totals).toEqual([['Total (5 órdenes)', '', '', '', '', '', '', 8, '']])
    expect(s.summary).toEqual([
      { label: 'Pendientes', value: '5', tone: 'money' },
      { label: 'Escaneadas en esta sesión', value: '7', tone: 'flow' },
    ])
    expect(s.paper).toBe('letter')
    expect(zoneGroupOf(o(9, 'R-09', true), t)).toBe('Zona ambigua')
  })
})
