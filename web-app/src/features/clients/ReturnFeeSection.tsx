// Contrato → «Cargo por devolución» (Lote 115): opcional; por orden (un monto a todos los tipos de paquete), por tipo de paquete (un monto por cada tipo) o por pieza. Se escribe con contracts.update y solo si el estatus del contrato permite editar.
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { toast } from '../../kernel/ui'
import { useSetReturnFee } from './contractApi'
import type { ContractDetail } from './contractRules'
import { RETURN_FEE_MODES, returnFeeError, returnFeeFormOf, toReturnFeeRequest, type ReturnFeeForm } from './returnFeeRules'

export function ReturnFeeSection({ contract, clientPublicId, canWrite }: { contract: ContractDetail; clientPublicId: string; canWrite: boolean }) {
  const t = useT()
  const { data: packageTypes = [] } = useLookups('PackageType')
  const [form, setForm] = useState<ReturnFeeForm>(() => returnFeeFormOf(contract.returnFee))
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setForm(returnFeeFormOf(contract.returnFee))
  }, [contract.returnFee])

  const save = useSetReturnFee(contract.publicId ?? '', clientPublicId)

  const set = (patch: Partial<ReturnFeeForm>) => {
    setError(null)
    setForm((f) => ({ ...f, ...patch }))
  }
  const setRow = (i: number, patch: Partial<ReturnFeeForm['rows'][number]>) => set({ rows: form.rows.map((r, k) => (k === i ? { ...r, ...patch } : r)) })

  async function submit() {
    const problem = returnFeeError(form)
    if (problem) return setError(t(`clients.contracts.returnFee.errors.${problem}`))
    try {
      await save.mutateAsync(toReturnFeeRequest(form))
      toast.success(t('clients.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <div className="cl-fs" data-testid="return-fee">
      <div className="f">
        <label htmlFor="rf-mode">{t('clients.contracts.returnFee.mode')}</label>
        <select id="rf-mode" value={form.mode} disabled={!canWrite} onChange={(e) => set({ mode: e.target.value as ReturnFeeForm['mode'] })}>
          <option value="">{t('clients.contracts.returnFee.none')}</option>
          {RETURN_FEE_MODES.map((m) => (
            <option key={m} value={m}>{t(`clients.contracts.returnFee.modes.${m}`)}</option>
          ))}
        </select>
        <p className="help">{t('clients.contracts.returnFee.help')}</p>
      </div>
      {(form.mode === 'ORDER' || form.mode === 'PIECE') && (
        <div className="f">
          <label htmlFor="rf-amount">{form.mode === 'ORDER' ? t('clients.contracts.returnFee.amountOrder') : t('clients.contracts.returnFee.amountPiece')}</label>
          <input id="rf-amount" inputMode="decimal" value={form.amount} disabled={!canWrite} autoComplete="off" onChange={(e) => set({ amount: e.target.value })} />
        </div>
      )}
      {form.mode === 'PACKAGE_TYPE' && (
        <div className="f">
          <span className="meta">{t('clients.contracts.returnFee.byType')}</span>
          {form.rows.map((r, i) => (
            <div className="r3" key={i}>
              <select aria-label={t('clients.contracts.returnFee.packageType')} value={r.packageType} disabled={!canWrite} onChange={(e) => setRow(i, { packageType: e.target.value })}>
                <option value="">{t('clients.contracts.returnFee.pickType')}</option>
                {packageTypes.map((p) => (
                  <option key={p.code} value={p.code}>{p.label}</option>
                ))}
              </select>
              <input aria-label={t('clients.contracts.returnFee.amountType')} inputMode="decimal" value={r.amount} disabled={!canWrite} autoComplete="off" onChange={(e) => setRow(i, { amount: e.target.value })} />
              {canWrite && (
                <button type="button" className="btn sm" onClick={() => set({ rows: form.rows.filter((_, k) => k !== i) })}>{t('clients.contracts.returnFee.remove')}</button>
              )}
            </div>
          ))}
          {canWrite && (
            <button type="button" className="btn sm" onClick={() => set({ rows: [...form.rows, { packageType: '', amount: '' }] })}>{t('clients.contracts.returnFee.add')}</button>
          )}
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
