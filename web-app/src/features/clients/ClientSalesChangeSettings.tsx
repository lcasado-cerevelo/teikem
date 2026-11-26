// Lote 249 — ficha del cliente → Perfil → «Modificaciones de órdenes»: la segunda aprobación y la evidencia obligatoria de ESTE cliente. Vacío = «como la compañía»; manda el
// del cliente (se guarda aparte del perfil, con su propio botón). Cambiar: clients.update; verlo: sales.view o clients.read.
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Spinner, toast } from '../../kernel/ui'
import { useClientChangeSettings, useUpdateClientChangeSettings } from '../sales/changes/changesApi'

type Inherit = '' | 'yes' | 'no'

export function ClientSalesChangeSettings({ clientPublicId, canEdit }: { clientPublicId: string; canEdit: boolean }) {
  const t = useT()
  const f = useFormat()
  const settings = useClientChangeSettings(clientPublicId)
  const update = useUpdateClientChangeSettings(clientPublicId)
  const [mode, setMode] = useState('')
  const [amount, setAmount] = useState('')
  const [attachment, setAttachment] = useState<Inherit>('')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    const d = settings.data
    if (!d) return
    setMode(d.approvalMode ?? '')
    setAmount(d.approvalAmount != null ? String(d.approvalAmount) : '')
    setAttachment(d.attachmentRequired == null ? '' : d.attachmentRequired ? 'yes' : 'no')
  }, [settings.data])
  const d = settings.data
  const amountNum = Number(amount.replace(',', '.'))
  const originalAttachment: Inherit = d?.attachmentRequired == null ? '' : d.attachmentRequired ? 'yes' : 'no'
  const dirty = d !== undefined && (mode !== (d.approvalMode ?? '') || attachment !== originalAttachment || (mode === 'ABOVE_AMOUNT' && amountNum !== (d.approvalAmount ?? 0)))
  const eff = d?.effective

  async function submit() {
    if (mode === 'ABOVE_AMOUNT' && !(amountNum > 0)) return setError(t('system.settings.salesChanges.amountRequired'))
    setError(null)
    try {
      await update.mutateAsync({ approvalMode: mode, approvalAmount: mode === 'ABOVE_AMOUNT' ? amountNum : undefined, attachmentRequired: attachment === '' ? undefined : attachment === 'yes' })
      toast.success(t('clients.salesChanges.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <section className="soc-settings" aria-label={t('clients.salesChanges.title')}>
      <h3 className="set-h3">{t('clients.salesChanges.title')}</h3>
      {settings.isPending ? (
        <Spinner block />
      ) : settings.isError ? (
        <p className="ferr">{applyProblemDetails(settings.error).title}</p>
      ) : (
        <>
          <fieldset className="set-fs" disabled={!canEdit}>
            <div className="r2">
              <div className="f">
                <label htmlFor="cl-chg-mode">{t('system.settings.salesChanges.mode')}</label>
                <select id="cl-chg-mode" value={mode} onChange={(e) => setMode(e.target.value)}>
                  <option value="">{t('clients.salesChanges.inherit')}</option>
                  <option value="NONE">{t('system.settings.salesChanges.modes.NONE')}</option>
                  <option value="ALWAYS">{t('system.settings.salesChanges.modes.ALWAYS')}</option>
                  <option value="ABOVE_AMOUNT">{t('system.settings.salesChanges.modes.ABOVE_AMOUNT')}</option>
                </select>
              </div>
              {mode === 'ABOVE_AMOUNT' && (
                <div className="f">
                  <label htmlFor="cl-chg-amount">{t('system.settings.salesChanges.amount')}</label>
                  <input id="cl-chg-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
                </div>
              )}
            </div>
            <div className="f">
              <label htmlFor="cl-chg-att">{t('system.settings.salesChanges.attachment')}</label>
              <select id="cl-chg-att" value={attachment} onChange={(e) => setAttachment(e.target.value as Inherit)}>
                <option value="">{t('clients.salesChanges.inherit')}</option>
                <option value="yes">{t('clients.salesChanges.attachmentYes')}</option>
                <option value="no">{t('clients.salesChanges.attachmentNo')}</option>
              </select>
            </div>
          </fieldset>
          {eff && (
            <p className="help" role="status">
              {t('clients.salesChanges.effective', {
                mode: t(`system.settings.salesChanges.modes.${eff.approvalMode}`),
                amount: eff.approvalMode === 'ABOVE_AMOUNT' ? ` (${f.money(eff.approvalAmount ?? 0)})` : '',
                attachment: t(eff.attachmentRequired ? 'clients.salesChanges.attachmentYes' : 'clients.salesChanges.attachmentNo'),
              })}
            </p>
          )}
          {error && <p className="ferr" role="alert">{error}</p>}
          {canEdit && (
            <div className="form-acts">
              <button type="button" className="btn flow" disabled={!dirty || update.isPending} onClick={() => void submit()}>
                {update.isPending ? t('common.loading') : t('ui.form.save')}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  )
}
