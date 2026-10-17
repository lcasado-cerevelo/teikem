import { formatDate, formatDateTime, formatMoney, statusTone } from './format'

describe('formatos', () => {
  it('el color del estatus: entregada verde, fallida o cancelada roja, pendiente amarilla, lo demás azul', () => {
    expect(statusTone('DELIVERED')).toBe('ok')
    expect(statusTone('FAILED')).toBe('danger')
    expect(statusTone('CANCELLED')).toBe('danger')
    expect(statusTone('DRAFT')).toBe('warn')
    expect(statusTone('IN_TRANSIT')).toBe('info')
    expect(statusTone('PICKED_UP')).toBe('info')
    expect(statusTone(null)).toBe('neutral')
  })
  it('fechas y dinero, con guion si falta el dato', () => {
    expect(formatDateTime(null, 'es')).toBe('—')
    expect(formatDate(undefined, 'en')).toBe('—')
    expect(formatDateTime('no es fecha', 'es')).toBe('—')
    expect(formatMoney(null, 'es')).toBe('—')
    expect(formatMoney(12.5, 'en')).toBe('$12.50')
    expect(formatDate('2026-10-17', 'en')).toContain('2026')
    // una fecha sin «Z» se lee como UTC (no se corre un día)
    expect(formatDateTime('2026-10-17T15:00:00', 'en')).toContain('2026')
  })
})
