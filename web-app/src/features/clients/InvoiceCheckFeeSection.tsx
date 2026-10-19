// Contrato → «Cotejo de factura» (Lote 126): si la compañía cobra cuando procesa la factura del cliente contra su orden entregada (monto por factura procesada). Apagado por omisión.
// Se escribe con contracts.update y solo si el contrato se puede editar. La corrida de facturación mete el cargo como línea INVOICE_CHECK de la orden.
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { toast } from '../../kernel/ui'
import { useSetInvoiceCheckFee } from './contractApi'
import type { ContractDetail } from './contractRules'

export function InvoiceCheckFeeSection({ contract, clientPublicId, canWrite }: { contract: ContractDetail; clientPublicId: string; canWrite: boolean }) {
  const t = useT()
  const [enabled, setEnabled] = useState(contract.invoiceCheckFee?.enabled ?? false)
  const [amount, setAmount] = useState(contract.invoiceCheckFee?.amount != null ? String(contract.invoiceCheckFee.amount) : '')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    setEnabled(contract.invoiceCheckFee?.enabled ?? false)
    setAmount(contract.invoiceCheckFee?.amount != null ? String(contract.invoiceCheckFee.amount) : '')
  }, [contract.invoiceCheckFee])
  const save = useSetInvoiceCheckFee(contract.publicId ?? '', clientPublicId)

  async function submit() {
    const n = Number(amount.replace(',', '.'))
    if (enabled && (amount.trim() === '' || !Number.isFinite(n))) return setError(t('clients.contracts.invoiceCheck.errors.amount'))
    if (enabled && n < 0) return setError(t('clients.contracts.invoiceCheck.errors.negative'))
    setError(null)
    try {
      await save.mutateAsync({ enabled, amount: enabled ? n : null })
      toast.success(t('clients.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <div className="cl-fs" data-testid="invoice-check-fee">
      <div className="f">
        <label className="chk">
          <input type="checkbox" checked={enabled} disabled={!canWrite} onChange={(e) => (setError(null), setEnabled(e.target.checked))} />
          <span>{t('clients.contracts.invoiceCheck.enabled')}</span>
        </label>
        <p className="help">{t('clients.contracts.invoiceCheck.help')}</p>
      </div>
      {enabled && (
        <div className="f">
          <label htmlFor="icf-amount">{t('clients.contracts.invoiceCheck.amount')}</label>
          <input id="icf-amount" inputMode="decimal" value={amount} disabled={!canWrite} autoComplete="off" onChange={(e) => (setError(null), setAmount(e.target.value))} />
        </div>
      )}
      {error && <p className="ferr" role="alert">{error}</p>}
      {canWrite && (
        <div className="form-acts">
          <button type="button" className="btn flow" disabled={save.isPending} onClick={() => void submit()}>
            {save.isPending ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      )}
    </div>
  )
}
