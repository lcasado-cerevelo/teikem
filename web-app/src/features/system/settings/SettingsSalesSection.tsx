// Ajustes → Operación → Ventas (Lote 222, plan §1.4): el cargo de envío de la compañía, el mínimo para envío gratis y si el envío lleva IVU. Cada cliente puede tener su propia
// regla en su ficha (Perfil → Ventas). El mínimo se compara con el total de la orden después del IVU; el recogido no paga envío. Cambiar: admin.tenant; las órdenes ya solicitadas no cambian.
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { IconCart } from '../../../kernel/ui/screenIcons'
import { Panel, Spinner, toast } from '../../../kernel/ui'
import { useChangeSettings, useUpdateChangeSettings } from '../../sales/changes/changesApi'
import { useSalesShippingSettings, useSaveSalesShippingSettings } from '../../sales/salesSettingsApi'
import { shippingSettingsDraftOf, shippingSettingsError, toShippingSettingsRequest, type ShippingSettingsDraft } from '../../sales/salesRules'

/**
 * Lote 249 — Ajustes → Ventas → «Modificaciones de órdenes»: la segunda aprobación (ninguna, siempre o sobre un monto del cambio del total) y si la evidencia de la
 * autorización del cliente es obligatoria. Cada cliente puede tener la suya en su ficha (manda la del cliente). Cambiar: admin.tenant.
 */
function SalesChangeSettingsPanel({ canEdit }: { canEdit: boolean }) {
  const t = useT()
  const settings = useChangeSettings()
  const update = useUpdateChangeSettings()
  const [mode, setMode] = useState('NONE')
  const [amount, setAmount] = useState('')
  const [attachment, setAttachment] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!settings.data) return
    setMode(settings.data.approvalMode ?? 'NONE')
    setAmount(settings.data.approvalAmount != null ? String(settings.data.approvalAmount) : '')
    setAttachment(settings.data.attachmentRequired === true)
  }, [settings.data])
  const amountNum = Number(amount.replace(',', '.'))
  const dirty = settings.data !== undefined && (mode !== (settings.data.approvalMode ?? 'NONE') || attachment !== (settings.data.attachmentRequired === true) || (mode === 'ABOVE_AMOUNT' && amountNum !== (settings.data.approvalAmount ?? 0)))
  async function submit() {
    if (mode === 'ABOVE_AMOUNT' && !(amountNum > 0)) return setError(t('system.settings.salesChanges.amountRequired'))
    setError(null)
    try {
      await update.mutateAsync({ approvalMode: mode, approvalAmount: mode === 'ABOVE_AMOUNT' ? amountNum : undefined, attachmentRequired: attachment })
      toast.success(t('system.settings.salesChanges.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Panel icon={<IconCart />} title={t('system.settings.salesChanges.title')} subtitle={t('system.settings.salesChanges.subtitle')}>
      {settings.isPending ? (
        <Spinner block />
      ) : settings.isError ? (
        <p className="ferr">{applyProblemDetails(settings.error).title}</p>
      ) : (
        <>
          <fieldset className="set-fs" disabled={!canEdit}>
            <div className="r2">
              <div className="f">
                <label htmlFor="set-chg-mode">{t('system.settings.salesChanges.mode')}</label>
                <select id="set-chg-mode" value={mode} onChange={(e) => setMode(e.target.value)}>
                  <option value="NONE">{t('system.settings.salesChanges.modes.NONE')}</option>
                  <option value="ALWAYS">{t('system.settings.salesChanges.modes.ALWAYS')}</option>
                  <option value="ABOVE_AMOUNT">{t('system.settings.salesChanges.modes.ABOVE_AMOUNT')}</option>
                </select>
                <p className="help">{t('system.settings.salesChanges.modeHelp')}</p>
              </div>
              {mode === 'ABOVE_AMOUNT' && (
                <div className="f">
                  <label htmlFor="set-chg-amount">{t('system.settings.salesChanges.amount')}</label>
                  <input id="set-chg-amount" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
                  <p className="help">{t('system.settings.salesChanges.amountHelp')}</p>
                </div>
              )}
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={attachment} onChange={(e) => setAttachment(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('system.settings.salesChanges.attachment')}</span>
            </label>
            <p className="help">{t('system.settings.salesChanges.attachmentHelp')}</p>
            <p className="help">{t('system.settings.salesChanges.clientHelp')}</p>
          </fieldset>
          {error && <p className="ferr" role="alert">{error}</p>}
          {canEdit && (
            <div className="set-actions">
              <button type="button" className="btn flow" disabled={!dirty || update.isPending} onClick={() => void submit()}>
                {update.isPending ? t('system.settings.saving') : t('system.settings.save')}
              </button>
            </div>
          )}
        </>
      )}
    </Panel>
  )
}

export function SettingsSalesSection({ canEdit }: { canEdit: boolean }) {
  return (
    <>
      <SettingsShippingPanel canEdit={canEdit} />
      <SalesChangeSettingsPanel canEdit={canEdit} />
    </>
  )
}

function SettingsShippingPanel({ canEdit }: { canEdit: boolean }) {
  const t = useT()
  const settings = useSalesShippingSettings()
  const save = useSaveSalesShippingSettings()
  const [draft, setDraft] = useState<ShippingSettingsDraft>(() => shippingSettingsDraftOf(undefined))
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (settings.data) setDraft(shippingSettingsDraftOf(settings.data))
  }, [settings.data])

  const original = shippingSettingsDraftOf(settings.data)
  const dirty = settings.data !== undefined && (draft.charge.trim() !== original.charge || draft.freeFrom.trim() !== original.freeFrom || draft.taxable !== original.taxable)
  const set = (patch: Partial<ShippingSettingsDraft>) => {
    setError(null)
    setDraft((d) => ({ ...d, ...patch }))
  }

  async function submit() {
    const problem = shippingSettingsError(draft)
    if (problem) return setError(t(`system.settings.sales.errors.${problem}`))
    setError(null)
    try {
      await save.mutateAsync(toShippingSettingsRequest(draft))
      toast.success(t('system.settings.sales.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel icon={<IconCart />} title={t('system.settings.sales.title')} subtitle={t('system.settings.sales.subtitle')}>
      {settings.isPending ? (
        <Spinner block />
      ) : settings.isError ? (
        <p className="ferr" role="alert">{applyProblemDetails(settings.error).title}</p>
      ) : (
        <>
          <fieldset className="set-fs" disabled={!canEdit}>
            <div className="r2">
              <div className="f">
                <label htmlFor="set-ship-charge">{t('system.settings.sales.charge')}</label>
                <input id="set-ship-charge" inputMode="decimal" value={draft.charge} onChange={(e) => set({ charge: e.target.value })} />
                <p className="help">{t('system.settings.sales.chargeHelp')}</p>
              </div>
              <div className="f">
                <label htmlFor="set-ship-free">{t('system.settings.sales.freeFrom')}</label>
                <input id="set-ship-free" inputMode="decimal" value={draft.freeFrom} onChange={(e) => set({ freeFrom: e.target.value })} />
                <p className="help">{t('system.settings.sales.freeFromHelp')}</p>
              </div>
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={draft.taxable} onChange={(e) => set({ taxable: e.target.checked })} />
              <span className="tk" aria-hidden="true" />
              <span>{t('system.settings.sales.taxable')}</span>
            </label>
            <p className="help">{t('system.settings.sales.taxableHelp')}</p>
            <p className="help">{t('system.settings.sales.clientHelp')}</p>
            {/* Lote 243: el correo con la orden se enciende en la regla de aviso del evento, no aquí (no hay interruptor nuevo) */}
            <Can perm="notifications.manage">
              <p className="help" data-testid="sales-email-help">
                {t('system.settings.sales.emailHelp')}{' '}
                <Link to="/system/notifications">{t('system.settings.sales.emailLink')}</Link>
              </p>
            </Can>
          </fieldset>
          {error && <p className="ferr" role="alert">{error}</p>}
          {canEdit && (
            <div className="set-actions">
              <button type="button" className="btn flow" disabled={!dirty || save.isPending} onClick={() => void submit()}>
                {save.isPending ? t('system.settings.saving') : t('system.settings.save')}
              </button>
            </div>
          )}
        </>
      )}
    </Panel>
  )
}
