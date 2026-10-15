// Impuestos → Reglas (Lote 56): lo que define el contador de la compañía. Precios con impuesto incluido o no, redondeo por línea o por documento,
// la tasa que se usa por omisión y qué tipos de cargo (flete, manejo…) pagan impuesto. La venta de producto la decide el producto.
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Panel, Spinner, toast } from '../../kernel/ui'
import { IconGear } from '../../kernel/ui/screenIcons'
import { useTaxMutations, useTaxRates, useTaxSettings, type TaxSettingsDto } from './taxApi'

function RulesForm({ settings, rateCodes }: { settings: TaxSettingsDto; rateCodes: string[] }) {
  const t = useT()
  const { saveSettings } = useTaxMutations()
  const [includeTax, setIncludeTax] = useState(!!settings.pricesIncludeTax)
  const [rounding, setRounding] = useState(settings.roundingMode ?? 'LINE')
  const [rateCode, setRateCode] = useState(settings.defaultRateCode ?? '')
  const [taxable, setTaxable] = useState<Record<string, boolean>>(() => Object.fromEntries((settings.chargeRules ?? []).map((r) => [r.chargeType ?? '', !!r.isTaxable])))
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    setError(null)
    try {
      await saveSettings.mutateAsync({
        pricesIncludeTax: includeTax,
        roundingMode: rounding,
        defaultRateCode: rateCode || undefined,
        clearDefaultRate: rateCode === '' ? true : undefined,
        chargeRules: (settings.chargeRules ?? []).map((r) => ({ chargeType: r.chargeType, isTaxable: !!taxable[r.chargeType ?? ''] })),
      })
      toast.success(t('tax.rules.saved'))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <div className="pb tax-form">
      <div className="f">
        <label htmlFor="tx-rate">{t('tax.rules.defaultRate')}</label>
        <select id="tx-rate" value={rateCode} onChange={(e) => setRateCode(e.target.value)}>
          <option value="">{t('tax.rules.noRate')}</option>
          {rateCodes.map((c) => (
            <option key={c} value={c}>{c}</option>
          ))}
        </select>
        <p className="help">{t('tax.rules.defaultRateHelp')}</p>
      </div>
      <div className="f">
        <label className="sw">
          <input type="checkbox" role="switch" checked={includeTax} onChange={(e) => setIncludeTax(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('tax.rules.includeTax')}</span>
        </label>
        <p className="help">{t('tax.rules.includeTaxHelp')}</p>
      </div>
      <div className="f">
        <label htmlFor="tx-round">{t('tax.rules.rounding')}</label>
        <select id="tx-round" value={rounding} onChange={(e) => setRounding(e.target.value)}>
          <option value="LINE">{t('tax.rules.roundLine')}</option>
          <option value="DOCUMENT">{t('tax.rules.roundDocument')}</option>
        </select>
        <p className="help">{t('tax.rules.roundingHelp')}</p>
      </div>
      <fieldset className="tax-comps">
        <legend>{t('tax.rules.charges')}</legend>
        <p className="help">{t('tax.rules.chargesHelp')}</p>
        {(settings.chargeRules ?? []).map((r) => (
          <label key={r.chargeType} className="sw">
            <input type="checkbox" role="switch" aria-label={t('tax.rules.chargeTaxable', { name: r.label ?? r.chargeType ?? '' })} checked={!!taxable[r.chargeType ?? '']} onChange={(e) => setTaxable((m) => ({ ...m, [r.chargeType ?? '']: e.target.checked }))} />
            <span className="tk" aria-hidden="true" />
            <span>{r.label}</span>
          </label>
        ))}
      </fieldset>
      {error && <p className="ferr" role="alert">{error}</p>}
      <div>
        <button type="button" className="btn flow" disabled={saveSettings.isPending} onClick={() => void save()}>{saveSettings.isPending ? t('common.loading') : t('tax.rules.save')}</button>
      </div>
    </div>
  )
}

export function RulesTab() {
  const t = useT()
  const settings = useTaxSettings()
  const rates = useTaxRates()
  const codes = [...new Set((rates.data ?? []).map((r) => r.code ?? '').filter(Boolean))].sort()
  return (
    <Panel icon={<IconGear />} title={t('tax.rules.panel')}>
      {settings.isError ? (
        <div className="pb"><p className="ferr" role="alert">{applyProblemDetails(settings.error).title}</p></div>
      ) : settings.isPending || rates.isPending ? (
        <Spinner block />
      ) : (
        // key: al volver a cargar las reglas (otro usuario las cambió) el formulario parte de cero
        <RulesForm key={JSON.stringify(settings.data)} settings={settings.data} rateCodes={codes} />
      )}
    </Panel>
  )
}
