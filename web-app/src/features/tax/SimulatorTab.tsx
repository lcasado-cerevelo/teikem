// Impuestos → Simulador (Lote 56): calcula el impuesto de unas líneas con las reglas de la compañía, SIN guardar nada. Es el mismo cálculo que usarán
// las órdenes de venta y la factura; sirve para comprobar con el contador que las tasas y las reglas están bien puestas.
import { useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, Panel, Spinner } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import { useTaxMutations, useTaxSettings, type TaxCalcDto } from './taxApi'

interface Line {
  key: number
  chargeType: string
  amount: string
}

let nextKey = 1
const PRODUCT_SALE = 'PRODUCT_SALE'

export function SimulatorTab() {
  const t = useT()
  const f = useFormat()
  const settings = useTaxSettings()
  const { calculate } = useTaxMutations()
  const [clientPublicId, setClientPublicId] = useState<string | null>(null)
  const [clientId, setClientId] = useState<number | null>(null)
  const [date, setDate] = useState('')
  const [lines, setLines] = useState<Line[]>([{ key: nextKey++, chargeType: PRODUCT_SALE, amount: '100' }])
  const [result, setResult] = useState<TaxCalcDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  const types = [{ value: PRODUCT_SALE, label: t('tax.sim.productSale') }, ...(settings.data?.chargeRules ?? []).map((r) => ({ value: r.chargeType ?? '', label: r.label ?? r.chargeType ?? '' }))]
  const setLine = (key: number, patch: Partial<Line>) => setLines((l) => l.map((x) => (x.key === key ? { ...x, ...patch } : x)))

  const run = async () => {
    setError(null)
    setResult(null)
    if (lines.some((l) => l.amount.trim() === '')) return setError(t('tax.sim.badAmount'))
    const parsed = lines.map((l) => ({ key: String(l.key), chargeType: l.chargeType, amount: Number(l.amount.replace(',', '.')) }))
    if (parsed.some((l) => !Number.isFinite(l.amount) || l.amount < 0)) return setError(t('tax.sim.badAmount'))
    try {
      setResult(await calculate.mutateAsync({ clientId, date: date || undefined, lines: parsed }))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel icon={<IconCash />} title={t('tax.sim.panel')}>
      <div className="pb tax-form">
        <p className="help">{t('tax.sim.help')}</p>
        {settings.isPending ? (
          <Spinner block />
        ) : (
          <>
            <div className="r2">
              <div className="f">
                <label htmlFor="tx-sclient">{t('tax.sim.client')}</label>
                <ClientPicker id="tx-sclient" value={clientPublicId} onChange={(publicId, client) => (setClientPublicId(publicId), setClientId(client?.id ?? null))} />
                <p className="help">{t('tax.sim.clientHelp')}</p>
              </div>
              <div className="f">
                <label htmlFor="tx-sdate">{t('tax.sim.date')}</label>
                <input id="tx-sdate" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
                <p className="help">{t('tax.sim.dateHelp')}</p>
              </div>
            </div>
            <fieldset className="tax-comps">
              <legend>{t('tax.sim.lines')}</legend>
              {lines.map((l, i) => (
                <div key={l.key} className="tax-line">
                  <div className="f">
                    <label htmlFor={`tx-lt-${l.key}`}>{t('tax.sim.type', { n: i + 1 })}</label>
                    <select id={`tx-lt-${l.key}`} value={l.chargeType} onChange={(e) => setLine(l.key, { chargeType: e.target.value })}>
                      {types.map((o) => (
                        <option key={o.value} value={o.value}>{o.label}</option>
                      ))}
                    </select>
                  </div>
                  <div className="f">
                    <label htmlFor={`tx-la-${l.key}`}>{t('tax.sim.amount', { n: i + 1 })}</label>
                    <input id={`tx-la-${l.key}`} inputMode="decimal" value={l.amount} onChange={(e) => setLine(l.key, { amount: e.target.value })} />
                  </div>
                  <button type="button" className="btn ghost" aria-label={t('tax.sim.remove', { n: i + 1 })} disabled={lines.length === 1} onClick={() => setLines((x) => x.filter((y) => y.key !== l.key))}>✕</button>
                </div>
              ))}
              <div className="tax-comps-foot">
                <button type="button" className="btn" onClick={() => setLines((x) => [...x, { key: nextKey++, chargeType: PRODUCT_SALE, amount: '' }])}>{t('tax.sim.add')}</button>
              </div>
            </fieldset>
            {error && <p className="ferr" role="alert">{error}</p>}
            <div>
              <button type="button" className="btn flow" disabled={calculate.isPending} onClick={() => void run()}>{calculate.isPending ? t('common.loading') : t('tax.sim.calculate')}</button>
            </div>
          </>
        )}
        {result && (
          <div className="tax-result" aria-live="polite">
            <h3>{t('tax.sim.result')}</h3>
            <p className="help">
              {result.rateCode ? t('tax.sim.usedRate', { code: result.rateCode, pct: result.ratePercent ?? 0, date: f.date(result.date) }) : t('tax.sim.noRateUsed')}
              {' · '}
              {t(result.pricesIncludeTax ? 'tax.sim.included' : 'tax.sim.notIncluded')} {' · '}
              {t(result.roundingMode === 'DOCUMENT' ? 'tax.rules.roundDocument' : 'tax.rules.roundLine')}
            </p>
            {(result.warnings ?? []).map((w) => (
              <p key={w} className="ferr" role="status">{w}</p>
            ))}
            <table className="tax-table">
              <thead>
                <tr>
                  <th scope="col">{t('tax.sim.colLine')}</th>
                  <th scope="col">{t('tax.sim.colReason')}</th>
                  <th scope="col" className="num">{t('tax.sim.colNet')}</th>
                  <th scope="col" className="num">{t('tax.sim.colTax')}</th>
                  <th scope="col" className="num">{t('tax.sim.colTotal')}</th>
                </tr>
              </thead>
              <tbody>
                {(result.lines ?? []).map((l, i) => (
                  <tr key={l.key}>
                    <td>{i + 1}</td>
                    <td>
                      <Chip tone={l.reason === 'TAXABLE' ? 'deliv' : l.reason === 'EXEMPT_CLIENT' || l.reason === 'EXEMPT_PRODUCT' ? 'warn' : 'neutral'}>{t(`tax.reasons.${l.reason}`)}</Chip>
                      {l.exemptionCertificate && <div className="help">{t('tax.sim.certificate', { n: l.exemptionCertificate })}</div>}
                    </td>
                    <td className="num">{f.money(l.net ?? 0)}</td>
                    <td className="num">{f.money(l.taxAmount ?? 0)}</td>
                    <td className="num">{f.money(l.total ?? 0)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th scope="row" colSpan={2}>{t('tax.sim.totals')}</th>
                  <td className="num">{f.money(result.subtotal ?? 0)}</td>
                  <td className="num">{f.money(result.taxAmount ?? 0)}</td>
                  <td className="num"><b>{f.money(result.total ?? 0)}</b></td>
                </tr>
              </tfoot>
            </table>
            {(result.components ?? []).length > 0 && (
              <p className="help">
                {(result.components ?? []).map((c) => `${c.name}: ${f.money(c.amount ?? 0)}`).join(' · ')}
              </p>
            )}
          </div>
        )}
      </div>
    </Panel>
  )
}
