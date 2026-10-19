// Reglas puras de las facturas del portal (Lote 119).
import type { Tone } from '../../kernel/ui/ui'

/** El color del chip de estatus: pagada = bien, vencida = alerta, anulada = apagada, emitida = informativa. */
export function invoiceTone(status: string | null | undefined): Tone {
  switch ((status ?? '').toUpperCase()) {
    case 'PAID':
      return 'ok'
    case 'OVERDUE':
      return 'danger'
    case 'VOID':
      return 'neutral'
    default:
      return 'info'
  }
}
