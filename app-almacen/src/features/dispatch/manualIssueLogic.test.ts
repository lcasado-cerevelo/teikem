import {
  buildManualIssueBody,
  FACTORY_MANUAL_ISSUE_REASONS,
  issueTotals,
  MANUAL_ISSUE_NOTE_MAX,
  manualIssueBlock,
  manualIssueNumber,
  noteTooLong,
  reasonOptions,
  WAREHOUSE_ISSUE,
  type StoredReason,
} from './manualIssueLogic'

const t = (key: string) => `t:${key}`

describe('despacho manual — reglas puras (2026-10-11)', () => {
  it('el permiso y los cinco motivos de fábrica son los códigos exactos del servidor', () => {
    expect(WAREHOUSE_ISSUE).toBe('warehouse.issue')
    expect(FACTORY_MANUAL_ISSUE_REASONS.map((r) => r.code)).toEqual(['SAMPLE', 'INTERNAL_USE', 'CUSTOMER_PICKUP', 'SALE', 'OTHER'])
  })

  it('sin copia del servidor (null) ofrece los de fábrica traducidos', () => {
    expect(reasonOptions(null, 'es', t)).toEqual([
      { code: 'SAMPLE', label: 't:dispatch.reasonSample' },
      { code: 'INTERNAL_USE', label: 't:dispatch.reasonInternalUse' },
      { code: 'CUSTOMER_PICKUP', label: 't:dispatch.reasonCustomerPickup' },
      { code: 'SALE', label: 't:dispatch.reasonSale' },
      { code: 'OTHER', label: 't:dispatch.reasonOther' },
    ])
  })

  it('con copia: los de la compañía en su orden y en el idioma de la pantalla (respaldo: la etiqueta guardada, la de fábrica, el código)', () => {
    const stored: StoredReason[] = [
      { code: 'OTHER', label: 'Otro', labels: { es: 'Otro', en: 'Other' }, sortOrder: 9 },
      { code: 'SAMPLE', label: 'Muestra gratis', labels: { es: 'Muestra gratis' }, sortOrder: 1 },
      { code: 'SALE', label: '', labels: {}, sortOrder: 4 },
      { code: 'GIFT', label: '', labels: {}, sortOrder: 5 },
    ]
    expect(reasonOptions(stored, 'en', t)).toEqual([
      { code: 'SAMPLE', label: 'Muestra gratis' },
      { code: 'SALE', label: 't:dispatch.reasonSale' },
      { code: 'GIFT', label: 'GIFT' },
      { code: 'OTHER', label: 'Other' },
    ])
    // una lista vacía del servidor (todos deshabilitados) se respeta: no se inventan motivos
    expect(reasonOptions([], 'es', t)).toEqual([])
  })

  it('motivo obligatorio y nota de hasta 500 (sin contar espacios de los extremos)', () => {
    expect(manualIssueBlock(null, '')).toBe('reason')
    expect(manualIssueBlock('  ', '')).toBe('reason')
    expect(manualIssueBlock('SAMPLE', '')).toBeNull()
    expect(manualIssueBlock('SAMPLE', 'x'.repeat(MANUAL_ISSUE_NOTE_MAX))).toBeNull()
    expect(manualIssueBlock('SAMPLE', ` ${'x'.repeat(MANUAL_ISSUE_NOTE_MAX)} `)).toBeNull()
    expect(manualIssueBlock('SAMPLE', 'x'.repeat(MANUAL_ISSUE_NOTE_MAX + 1))).toBe('noteTooLong')
    expect(noteTooLong('x'.repeat(501))).toBe(true)
  })

  it('arma el cuerpo de POST /manual-issues con las líneas resueltas, el motivo y la nota (vacía → null)', () => {
    const lines = [
      { productPublicId: 'p1', sku: 'A', productName: 'A', quantity: 2, fromBinCode: 'A-01', fromBinId: 5 },
      { productPublicId: 'p2', sku: 'B', productName: 'B', quantity: 1.5, fromBinCode: 'B-01', fromBinId: 6 },
    ]
    expect(buildManualIssueBody('wh-1', 'SALE', ' mostrador ', lines)).toEqual({
      warehousePublicId: 'wh-1',
      lines: [
        { productPublicId: 'p1', quantity: 2, binId: 5 },
        { productPublicId: 'p2', quantity: 1.5, binId: 6 },
      ],
      reasonCode: 'SALE',
      note: 'mostrador',
    })
    expect(buildManualIssueBody('wh-1', 'SALE', '', lines).note).toBeNull()
  })

  it('lee el número DMA de la respuesta y suma líneas y unidades', () => {
    expect(manualIssueNumber({ number: 'DMA-00012' })).toBe('DMA-00012')
    expect(manualIssueNumber({ number: '' })).toBeNull()
    expect(manualIssueNumber(null)).toBeNull()
    expect(issueTotals([{ quantity: 0.1 }, { quantity: 0.2 }])).toEqual({ lines: 2, qty: 0.3 })
  })
})
