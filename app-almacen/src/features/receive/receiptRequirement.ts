// Lote 161 — «el recibo exige documento» en la app: qué se puede ofrecer al empezar un recibo según el ajuste de la compañía (viaja en el registro y en el latido del aparato).
// FREE = como siempre (a ciegas, aviso u orden); ANY_DOCUMENT = aviso u orden (nunca a ciegas); ASN = solo aviso de llegada; PURCHASE_ORDER = solo orden de compra.
// El servidor vuelve a validar (409), esto solo evita ofrecer lo que sería rechazado.
export type ReceiptRequirement = 'FREE' | 'ANY_DOCUMENT' | 'ASN' | 'PURCHASE_ORDER'

const ALL: readonly ReceiptRequirement[] = ['FREE', 'ANY_DOCUMENT', 'ASN', 'PURCHASE_ORDER']

export function parseReceiptRequirement(value: string | null | undefined): ReceiptRequirement {
  const v = (value ?? '').trim().toUpperCase()
  return (ALL as readonly string[]).includes(v) ? (v as ReceiptRequirement) : 'FREE'
}

export interface ReceiveOptions {
  /** Recibo a ciegas (sin documento). */
  blind: boolean
  /** Contra un aviso de llegada. */
  asn: boolean
  /** Contra una orden de compra. */
  purchaseOrder: boolean
}

export function receiveOptions(requirement: ReceiptRequirement): ReceiveOptions {
  switch (requirement) {
    case 'ANY_DOCUMENT':
      return { blind: false, asn: true, purchaseOrder: true }
    case 'ASN':
      return { blind: false, asn: true, purchaseOrder: false }
    case 'PURCHASE_ORDER':
      return { blind: false, asn: false, purchaseOrder: true }
    default:
      return { blind: true, asn: true, purchaseOrder: true }
  }
}
