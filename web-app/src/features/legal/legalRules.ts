// Documento legal único (Lote 99): reglas puras de la pantalla de aceptación.

/** ¿Llegó el lector al final del texto? (con un margen de unos píxeles; un texto que cabe sin scroll cuenta como leído). */
export function reachedEnd(el: { scrollTop: number; clientHeight: number; scrollHeight: number }, margin = 8): boolean {
  return el.scrollTop + el.clientHeight >= el.scrollHeight - margin
}

/** El API responde 403 con este código mientras falte aceptar la versión vigente. */
export const LEGAL_REQUIRED_CODE = 'legal_acceptance_required'

export type AcceptanceFilter = '' | 'accepted' | 'pending'

export function acceptanceStatusParam(v: AcceptanceFilter): 'accepted' | 'pending' | undefined {
  return v === 'accepted' || v === 'pending' ? v : undefined
}
