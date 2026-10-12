// Panel de la fórmula de pago (arriba de la pantalla, como la maqueta): el selector de la fórmula vigente de la COMPAÑÍA (editable con
// `driverpay.manage`) y la vista previa «ejemplo con 2 intentos (1 fallido + 1 entregado)» del chofer elegido, calculada por el
// SERVIDOR (`POST /driver-pay-policy/preview`, no escribe nada). Lee `driverpay.view`. Manual 04 §8.4.
import { useId } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Panel, toast } from '../../kernel/ui'
import { useDriver, useDriverRates, usePayoutPreview, usePayPolicy, useUpdatePayPolicy } from './api'
import { previewRequestFor } from './rateRules'

export function PayPolicyPanel({ driverPublicId }: { driverPublicId: string | null }) {
  const t = useT()
  const f = useFormat()
  const id = useId()
  const canView = useCan('driverpay.view')
  const canManage = useCan('driverpay.manage')
  const policy = usePayPolicy(canView)
  const update = useUpdatePayPolicy()
  const driverId = driverPublicId
  // el nombre sale de la ficha (misma consulta que el panel de la derecha: no pide dos veces)
  const driver = useDriver(canView ? driverId : null).data
  const rates = useDriverRates(driverId, false, canView && !!driverId)
  // la vista previa es de un chofer: espera a tenerlo elegido y a conocer sus tarifas (o su error)
  const ready = !!driverId && (rates.isSuccess || rates.isError)
  const preview = usePayoutPreview(previewRequestFor(rates.data), canView && policy.isSuccess && ready)

  if (!canView) return null

  const change = async (code: string) => {
    try {
      await update.mutateAsync(code)
      toast.success(t('drivers.pay.saved'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }

  const formulaText = (code: string | null | undefined, fallback: string | null | undefined) => {
    const key = `drivers.pay.formulas.${code}`
    const text = t(key)
    return text === key ? (fallback ?? code ?? '') : text
  }

  const lineLabel = (l: { kind?: string | null; attemptNumber?: number | null; note?: string | null }) => {
    const base = l.kind === 'DELIVERY' ? t('drivers.pay.delivery') : t('drivers.pay.attempt', { n: l.attemptNumber ?? 0 })
    return l.note ? `${base} (${t('drivers.pay.noRateNote')})` : base
  }

  return (
    <Panel>
      <div className="drv-formula">
        <div className="f">
          <label htmlFor={id}>{t('drivers.pay.formula')}</label>
          <select id={id} value={policy.data?.payoutFormulaCode ?? ''} disabled={!canManage || !policy.data || update.isPending} onChange={(e) => void change(e.target.value)}>
            {(policy.data?.formulas ?? []).map((o) => (
              <option key={o.code ?? ''} value={o.code ?? ''}>
                {formulaText(o.code, o.label)}
              </option>
            ))}
          </select>
          {policy.isError && (
            <p className="ferr" role="alert">
              {applyProblemDetails(policy.error).title}
            </p>
          )}
        </div>
        <div className="drv-preview" aria-live="polite">
          <div className="drv-preview-h">
            {t('drivers.pay.preview')}
            {driver?.fullName ? ` · ${driver.fullName}` : ''}
          </div>
          {preview.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(preview.error).title}
            </p>
          ) : (
            <>
              {(preview.data?.lines ?? []).map((l, i) => (
                <div className="drv-pline" key={`${l.kind}-${l.attemptNumber ?? i}`}>
                  <span>{lineLabel(l)}</span>
                  <span className="mono">{f.money(l.amount)}</span>
                </div>
              ))}
              {preview.data && (
                <div className="drv-ptotal">
                  <b>{t('drivers.pay.total')}</b>
                  <b className="mono">{f.money(preview.data.total)}</b>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </Panel>
  )
}
