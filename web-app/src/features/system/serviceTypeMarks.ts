// Lote 114 — marcas de un tipo de servicio en su ExtraJson: «pick up» (recojo y traigo: termina En almacén) y «facturable» (interruptor de cobro). Sin marcas: no es pick up y sí es facturable.
export interface ServiceTypeMarks {
  pickUp: boolean
  billable: boolean
}

export const SERVICE_TYPE_ENTITY = 'ServiceType'

export function parseServiceTypeMarks(json: string | null | undefined): ServiceTypeMarks {
  const marks: ServiceTypeMarks = { pickUp: false, billable: true }
  if (!json || !json.trim()) return marks
  try {
    const o = JSON.parse(json) as Record<string, unknown>
    if (o && typeof o === 'object' && !Array.isArray(o)) {
      for (const [k, v] of Object.entries(o)) {
        if (typeof v !== 'boolean') continue
        if (k.toLowerCase() === 'pickup') marks.pickUp = v
        else if (k.toLowerCase() === 'billable') marks.billable = v
      }
    }
  } catch {
    // un JSON roto se ignora, como en el servidor
  }
  return marks
}

export function serviceTypeMarksJson(marks: ServiceTypeMarks): string {
  return JSON.stringify({ pickUp: marks.pickUp, billable: marks.billable })
}
