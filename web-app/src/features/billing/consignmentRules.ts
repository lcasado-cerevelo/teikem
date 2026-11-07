// Reglas puras de la pestaña «Consignación» (Lote 224): validación del pago antes de enviar (el servidor repite todo, ConsignmentRules/PaymentRules).
import { paymentFormError, type PaymentForm } from './billingRules'

export type ConsignmentPayError = 'invoice' | 'amount' | 'decimals' | 'method' | 'exceeds' | 'future' | 'beforeIssue' | 'reference'

/** El pago no pasa del saldo en consignación (la factura puede tener saldo de otras cosas; el servidor revisa eso). */
export function consignmentPayError(invoiceId: string | null, f: PaymentForm, balance: number, today: string, issueDate: string | null | undefined): ConsignmentPayError | null {
  if (!invoiceId) return 'invoice'
  return paymentFormError(f, balance, today, issueDate)
}

/** Con saldo en consignación se puede registrar un pago. */
export function consignmentCanPay(balance: number | null | undefined): boolean {
  return (balance ?? 0) > 0.00001
}

export const BALANCE_FILTERS = ['OPEN', 'PAID', 'ALL'] as const
