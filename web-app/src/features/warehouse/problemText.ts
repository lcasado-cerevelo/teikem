// Lote 14 — texto de un error del API para mostrarlo tal cual fuera de un <Form> (descuadres, conciliación, crear conteo):
// los mensajes por campo de un 400 (p. ej. 'Escriba una nota que explique por qué se descarta el descuadre.') o el título de
// un 404/409/422 ('El descuadre ya está cerrado; solo se consulta.').
import { applyProblemDetails } from '../../kernel/api/problem'

export function problemText(err: unknown): string {
  const p = applyProblemDetails(err)
  const fieldMessages = Object.values(p.errors).flat()
  return fieldMessages.length > 0 ? fieldMessages.join(' ') : p.title
}

/** Nota de resolución de un descuadre: máx. 500 (DiscrepancyResolveRequest.Notes). */
export const DISCREPANCY_NOTES_MAX = 500
