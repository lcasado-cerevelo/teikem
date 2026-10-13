// Lote F-G2 — Waybill summary (por cliente con subtotales y total, sin precio) y Packing list (una sección por orden con sus paquetes),
// en carta; las canceladas no entran y se avisa.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../../kernel/i18n/i18n'
import { renderReportPdf } from '../../kernel/ui/reportPdf'
import type { OrderListItemDto } from './api'
import { packingListSpec, waybillSummarySpec } from './orderDocuments'

const base = { consigneeCity: 'San Juan', consigneePostalCode: '00901', serviceType: 'STD', serviceTypeLabel: 'Estándar', isActive: true, status: 'CONFIRMED' }
const ORDERS: OrderListItemDto[] = [
  { ...base, packBatchNumber: 'EMP-2', orderNumber: 'ORD-2', clientInvoiceNumber: 'F-2', clientName: 'Beta', consigneeName: 'Colmado', packagesSummary: 'Caja ×1', totalPieces: 1, codAmount: null, quotedAmount: 99,
    packages: [{ id: 3, packageTypeLabel: 'Caja', packageNumber: 'PQT-3', description: 'Arroz', pieces: 1, weightKg: 2.5 }] },
  { ...base, packBatchNumber: 'EMP-1', orderNumber: 'ORD-1', clientInvoiceNumber: 'F-1', clientName: 'Acme', consigneeName: 'Farmacia', consigneeLine1: 'Calle 1', packagesSummary: 'Caja ×2', totalPieces: 2, codAmount: 50, quotedAmount: 120,
    packages: [{ id: 1, packageTypeLabel: 'Caja', packageNumber: 'PQT-1', description: 'Ropa', pieces: 2, weightKg: 4 }] },
  { ...base, packBatchNumber: 'EMP-3', orderNumber: 'ORD-3', clientInvoiceNumber: 'F-3', clientName: 'Acme', consigneeName: 'Otra', packagesSummary: 'Caja ×5', totalPieces: 5, codAmount: 10, status: 'CANCELLED', packages: [] },
]
const ctx = () => ({ t, locale: 'es', company: 'Advance Logistics', user: 'Ana', filters: [] })

beforeAll(() => setLang('es'))

describe('waybillSummarySpec', () => {
  it('por cliente (orden alfabético) con subtotal de órdenes, piezas y COD; total general; sin precio; canceladas fuera con aviso', () => {
    const s = waybillSummarySpec(ORDERS, ctx())
    expect(s.paper).toBe('letter')
    expect(s.orientation).toBe('landscape')
    expect(s.columns.map((c) => c.header)).toEqual(['Empaque', 'N.º de orden', 'Factura', 'Cliente', 'Consignatario', 'Pueblo', 'ZIP', 'Servicio', 'Paquetes', 'Piezas', 'COD'])
    expect(s.sections.map((x) => x.title)).toEqual(['Acme', 'Beta'])
    expect(s.sections[0].rows).toEqual([['EMP-1', 'ORD-1', 'F-1', 'Acme', 'Farmacia', 'San Juan', '00901', 'Estándar', 'Caja ×2', 2, 50]])
    expect(s.sections[0].subtotal).toEqual(['Subtotal (1 órdenes)', '', '', '', '', '', '', '', '', 2, 50])
    expect(s.totals).toEqual([['Total (2 órdenes)', '', '', '', '', '', '', '', '', 3, 50]])
    expect(JSON.stringify(s)).not.toContain('120') // el precio nunca sale
    expect(s.notices).toEqual(['1 órdenes canceladas no se incluyeron.'])
  })
})

describe('packingListSpec', () => {
  it('una sección por orden con sus paquetes y subtotal; total de piezas y peso', () => {
    const s = packingListSpec(ORDERS, ctx(), (kg) => `${kg} kg`)
    expect(s.sections.map((x) => x.title)).toEqual(['EMP-2 · ORD-2 · Factura F-2 · Colmado · San Juan 00901', 'EMP-1 · ORD-1 · Factura F-1 · Farmacia · Calle 1, San Juan 00901'])
    expect(s.sections[1].rows).toEqual([['Caja', 'PQT-1', 'Ropa', 2, 4]])
    expect(s.sections[1].subtotal).toEqual(['1 paquetes', '', '', 2, 4])
    expect(s.totals).toEqual([['Total (2 órdenes)', '', '', 3, 6.5]])
    expect(s.summary?.find((x) => x.label === 'Peso')?.value).toBe('6.5 kg')
  })

  it('el PDF sale en carta (612×792 pt; apaisado 792×612)', async () => {
    const pdf = (await renderReportPdf(waybillSummarySpec(ORDERS, ctx()), { compress: false })).output()
    expect(pdf).toMatch(/\/MediaBox \[0 0 792\.\d* 612\.\d*\]|\/MediaBox \[0 0 792 612\]/)
    const portrait = (await renderReportPdf(packingListSpec(ORDERS, ctx()), { compress: false })).output()
    expect(portrait).toMatch(/\/MediaBox \[0 0 612\.?\d* 792\.?\d*\]/)
  })
})
