// Lote F-G2 — de las órdenes a los labels: uno por pieza con el número de su paquete, «Paquete i de N», paquetes sin número y órdenes
// canceladas avisados, y el aviso desde 500.
import { describe, expect, it } from 'vitest'
import { buildLabelJob, cityLine, labelFileBase, labelOrderFromDetail, labelOrderFromList, LARGE_LABEL_JOB, orderLabelsOf, type LabelOrder } from './orderLabels'

const money = (n: number) => `$${n.toFixed(2)}`
const ORDER: LabelOrder = {
  publicId: 'o-1',
  orderNumber: 'ORD-00007',
  clientInvoiceNumber: 'FAC-7',
  clientName: 'Acme Corp',
  consigneeName: 'Farmacia Central',
  addressLines: ['Calle Luna 12', 'San Juan, PR 00901'],
  serviceLabel: 'Estándar',
  codAmount: 50,
  packages: [
    { packageNumber: 'PQT-00012', pieces: 2 },
    { packageNumber: 'PQT-00013', pieces: 1 },
  ],
  cancelled: false,
}

describe('orderLabelsOf', () => {
  it('un label por pieza: la cantidad de la línea es el número de labels, con el número de SU paquete y «i de N» corrido', () => {
    const { labels, withoutNumber } = orderLabelsOf(ORDER, 'Advance Logistics', money)
    expect(withoutNumber).toBe(0)
    expect(labels.map((l) => [l.packageNumber, l.packageIndex, l.packageCount])).toEqual([
      ['PQT-00012', 1, 3],
      ['PQT-00012', 2, 3],
      ['PQT-00013', 3, 3],
    ])
    expect(labels[0]).toMatchObject({ company: 'Advance Logistics', sender: 'Acme Corp', consignee: 'Farmacia Central', waybill: 'ORD-00007', invoice: 'FAC-7', cod: 50, codText: '$50.00', service: 'Estándar' })
  })

  it('el «Waybill» del label es el número de conduce; sin él (orden vieja) sale el número de orden', () => {
    expect(orderLabelsOf({ ...ORDER, waybillNumber: 'CND-0000042' }, 'Advance Logistics', money).labels[0].waybill).toBe('CND-0000042')
    expect(orderLabelsOf({ ...ORDER, waybillNumber: null }, 'Advance Logistics', money).labels[0].waybill).toBe('ORD-00007')
  })

  it('una línea sin número de paquete no lleva label pero cuenta en N; COD 0 sin texto', () => {
    const { labels, withoutNumber } = orderLabelsOf({ ...ORDER, codAmount: 0, packages: [{ packageNumber: null, pieces: 2 }, { packageNumber: 'PQT-9', pieces: 1 }] }, 'X', money)
    expect(withoutNumber).toBe(2)
    expect(labels).toHaveLength(1)
    expect(labels[0]).toMatchObject({ packageNumber: 'PQT-9', packageIndex: 3, packageCount: 3, codText: null })
  })
})

describe('buildLabelJob', () => {
  it('omite canceladas, suma las piezas sin número y avisa desde 500', () => {
    const job = buildLabelJob([ORDER, { ...ORDER, publicId: 'o-2', cancelled: true }, { ...ORDER, publicId: 'o-3', packages: [{ packageNumber: null, pieces: 4 }] }], 'X', money)
    expect(job).toMatchObject({ orders: 2, cancelled: 1, withoutNumber: 4, large: false })
    expect(job.labels).toHaveLength(3)
    const big = buildLabelJob([{ ...ORDER, packages: [{ packageNumber: 'P', pieces: LARGE_LABEL_JOB }] }], 'X', money)
    expect(big.large).toBe(true)
    expect(big.labels).toHaveLength(500) // sin tope
    expect(labelFileBase([ORDER], 3)).toBe('labels-ORD-00007')
    expect(labelFileBase([ORDER, { ...ORDER, orderNumber: 'B' }], 6)).toBe('labels-6')
  })
})

describe('mapeo de la lista y de la ficha', () => {
  it('lista: dirección de entrega, servicio y paquetes; cancelada o de baja', () => {
    const o = labelOrderFromList({
      publicId: 'o-1', orderNumber: 'ORD-1', clientInvoiceNumber: 'F-1', clientName: 'Acme', consigneeName: 'Farmacia', consigneeCity: 'Toa Baja', consigneeState: 'PR',
      consigneePostalCode: '00949', consigneeLine1: 'Carr. 2 km 14.5', consigneeLine2: null, serviceType: 'NEXT', serviceTypeLabel: 'Next day', codAmount: 12.5,
      status: 'CONFIRMED', isActive: true, packages: [{ id: 1, packageNumber: ' PQT-1 ', pieces: 2, description: '' }],
    })
    expect(o.addressLines).toEqual(['Carr. 2 km 14.5', 'Toa Baja, PR 00949'])
    expect(o.packages).toEqual([{ packageNumber: 'PQT-1', pieces: 2, pieceNumbers: [] }])
    expect(o.serviceLabel).toBe('Next day')
    expect(labelOrderFromList({ status: 'CANCELLED', isActive: true }).cancelled).toBe(true)
    expect(labelOrderFromList({ status: 'DRAFT', isActive: false }).cancelled).toBe(true)
  })

  it('ficha: la parada de entrega', () => {
    const o = labelOrderFromDetail({
      publicId: 'o-9', orderNumber: 'ORD-9', clientInvoiceNumber: 'F-9', clientName: 'Acme', serviceType: 'STD', serviceTypeLabel: 'Estándar', codAmount: null,
      status: 'DRAFT', isActive: true, packages: [{ id: 1, packageNumber: 'PQT-9', pieces: 1, description: 'Caja' }],
      delivery: { id: 1, stopType: 'DELIVERY', sequence: 2, name: 'Colmado', line1: 'Calle 1', line2: 'Apt 2', city: 'Ponce', state: null, postalCode: '00716', countryCode: 'PR', serviceMinutes: 0, status: 'PENDING', statusLabel: 'Pendiente' },
    })
    expect(o.consigneeName).toBe('Colmado')
    expect(o.addressLines).toEqual(['Calle 1', 'Apt 2', 'Ponce 00716'])
    expect(cityLine('', 'PR', '')).toBe('PR')
  })

  it('Lote 109: cada pieza lleva su propio número único en el código de barras', () => {
    const order = { ...ORDER, codAmount: 0, packages: [{ packageNumber: 'ACME5261018', pieces: 2, pieceNumbers: ['ACME52610181', 'ACME52610182'] }, { packageNumber: null, pieces: 1, pieceNumbers: ['ACME52610183'] }] }
    const { labels, withoutNumber } = orderLabelsOf(order, 'X', money)
    expect(labels.map((l) => l.packageNumber)).toEqual(['ACME52610181', 'ACME52610182', 'ACME52610183'])
    expect(labels.map((l) => `${l.packageIndex}/${l.packageCount}`)).toEqual(['1/3', '2/3', '3/3'])
    expect(withoutNumber).toBe(0)
  })
})
