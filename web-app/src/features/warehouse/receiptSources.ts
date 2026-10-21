// Lote 180 (menores, autónomo) — qué orígenes ofrece «Nuevo recibo» según «el recibo exige documento» de la compañía (Lote 149): libre = a ciegas, devolución, aviso y orden de compra;
// cualquier documento = aviso u orden; aviso = solo aviso de llegada; orden de compra = solo orden de compra. Sin estos filtros el servidor igual rechaza (409), pero la pantalla ya no ofrece lo que no se puede.
export type ReceiptSource = 'BLIND' | 'RETURN' | 'ASN' | 'PO'

export function allowedReceiptSources(requirement: string | null | undefined, canReceivePo: boolean): ReceiptSource[] {
  const po: ReceiptSource[] = canReceivePo ? ['PO'] : []
  switch ((requirement ?? '').trim().toUpperCase()) {
    case 'ANY_DOCUMENT':
      return ['ASN', ...po]
    case 'ASN':
      return ['ASN']
    case 'PURCHASE_ORDER':
      return canReceivePo ? ['PO'] : ['ASN']   // sin permiso de compras no se podría recibir nada: se deja el aviso y el servidor responde si no corresponde
    default:
      return ['BLIND', 'RETURN', 'ASN', ...po]
  }
}

/** El origen con que abre el formulario: el preajustado (un aviso elegido), si no el primero permitido. */
export function initialReceiptSource(allowed: readonly ReceiptSource[], preset: boolean): ReceiptSource {
  if (preset && allowed.includes('ASN')) return 'ASN'
  return allowed[0] ?? 'ASN'
}
