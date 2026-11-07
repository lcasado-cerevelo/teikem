// Ajustes → Operación → Ventas (Lote 222, plan §1.4): el cargo de envío de la compañía, el mínimo para envío gratis y si el envío lleva IVU. Cada cliente puede tener su propia
// regla en su ficha (Perfil → Ventas). El mínimo se compara con el total de la orden después del IVU; el recogido no paga envío. Cambiar: admin.tenant; las órdenes ya solicitadas no cambian.
import { useEffect, useState } from 'react'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { useT } from '../../../kernel/i18n'
import { IconCart } from '../../../kernel/ui/screenIcons'
import { Panel, Spinner, toast } from '../../../kernel/ui'
import { useSalesShippingSettings, useSaveSalesShippingSettings } from '../../sales/salesSettingsApi'
import { shippingSettingsDraftOf, shippingSettingsError, toShippingSettingsRequest, type ShippingSettingsDraft } from '../../sales/salesRules'

export function SettingsSalesSection({ canEdit }: { canEdit: boolean }) {
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
