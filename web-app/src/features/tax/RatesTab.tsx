// Impuestos → Tasas (Lote 56): el historial de tasas con su vigencia y componentes. Una tasa NO se edita: se cierra su vigencia y se crea otra.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, type ChipTone, type DataColumn, DataTable, DateField, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconCash, IconClock } from '../../kernel/ui/screenIcons'
import { componentsText, nextDay, parsePercent, rateError, sumPercent, type ComponentDraft, type RateDraft } from './taxRules'
import { useTaxMutations, useTaxRates, type TaxRateDto } from './taxApi'

const STATUS_TONE: Record<string, ChipTone> = { ACTIVE: 'deliv', SCHEDULED: 'warn', CLOSED: 'neutral' }
const NO_ROWS: TaxRateDto[] = []
let nextKey = 1
const newComponent = (): ComponentDraft => ({ key: nextKey++, code: '', name: '', percent: '' })

/** Si se parte de una tasa cerrada, la nueva propone su mismo código y nombre desde el día siguiente. */
function RateModal({ base, onClose }: { base: TaxRateDto | null; onClose: () => void }) {
  const t = useT()
  const { createRate } = useTaxMutations()
  const [draft, setDraft] = useState<RateDraft>(() => ({
    code: base?.code ?? '',
    name: base?.name ?? '',
    from: base?.effectiveTo ? nextDay(base.effectiveTo) : '',
    to: '',
    components: base?.components?.length ? base.components.map((c) => ({ key: nextKey++, code: c.code ?? '', name: c.name ?? '', percent: String(c.percent ?? '') })) : [newComponent()],
  }))
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Partial<RateDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const setComp = (key: number, patch: Partial<ComponentDraft>) => set({ components: draft.components.map((c) => (c.key === key ? { ...c, ...patch } : c)) })
  const total = sumPercent(draft.components)

  const save = async () => {
    setError(null)
    const problem = rateError(draft)
    if (problem) return setError(t(`tax.rates.errors.${problem}`))
    try {
      await createRate.mutateAsync({
        code: draft.code,
        name: draft.name,
        effectiveFrom: draft.from,
        effectiveTo: draft.to || undefined,
        components: draft.components.map((c) => ({ code: c.code, name: c.name, percent: parsePercent(c.percent) ?? 0 })),
      })
      toast.success(t('tax.rates.created', { code: draft.code.trim().toUpperCase() }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('tax.rates.new')}
      onClose={onClose}
      dismissible={!createRate.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={createRate.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={createRate.isPending}>{createRate.isPending ? t('common.loading') : t('tax.rates.save')}</button>
        </>
      }
    >
      <div className="pb tax-form">
        <div className="r2">
          <div className="f">
            <label htmlFor="tx-code">{t('tax.rates.code')}</label>
            <input id="tx-code" value={draft.code} maxLength={30} autoComplete="off" onChange={(e) => set({ code: e.target.value })} />
            <p className="help">{t('tax.rates.codeHelp')}</p>
          </div>
          <div className="f">
            <label htmlFor="tx-name">{t('tax.rates.name')}</label>
            <input id="tx-name" value={draft.name} maxLength={100} autoComplete="off" onChange={(e) => set({ name: e.target.value })} />
          </div>
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="tx-from">{t('tax.rates.from')}</label>
            <DateField id="tx-from" value={draft.from} onChange={(v) => set({ from: v })} />
          </div>
          <div className="f">
            <label htmlFor="tx-to">{t('tax.rates.to')}</label>
            <DateField id="tx-to" value={draft.to} onChange={(v) => set({ to: v })} />
            <p className="help">{t('tax.rates.toHelp')}</p>
          </div>
        </div>
        <fieldset className="tax-comps">
          <legend>{t('tax.rates.components')}</legend>
          {draft.components.map((c, i) => (
            <div key={c.key} className="tax-comp">
              <div className="f">
                <label htmlFor={`tx-cc-${c.key}`}>{t('tax.rates.compCode', { n: i + 1 })}</label>
                <input id={`tx-cc-${c.key}`} value={c.code} maxLength={30} autoComplete="off" onChange={(e) => setComp(c.key, { code: e.target.value })} />
              </div>
              <div className="f">
                <label htmlFor={`tx-cn-${c.key}`}>{t('tax.rates.compName', { n: i + 1 })}</label>
                <input id={`tx-cn-${c.key}`} value={c.name} maxLength={100} autoComplete="off" onChange={(e) => setComp(c.key, { name: e.target.value })} />
              </div>
              <div className="f">
                <label htmlFor={`tx-cp-${c.key}`}>{t('tax.rates.compPercent', { n: i + 1 })}</label>
                <input id={`tx-cp-${c.key}`} inputMode="decimal" value={c.percent} onChange={(e) => setComp(c.key, { percent: e.target.value })} />
              </div>
              <button type="button" className="btn ghost" aria-label={t('tax.rates.removeComp', { n: i + 1 })} disabled={draft.components.length === 1} onClick={() => set({ components: draft.components.filter((x) => x.key !== c.key) })}>✕</button>
            </div>
          ))}
          <div className="tax-comps-foot">
            <button type="button" className="btn" onClick={() => set({ components: [...draft.components, newComponent()] })}>{t('tax.rates.addComp')}</button>
            <b role="status">{t('tax.rates.total', { n: total })}</b>
          </div>
        </fieldset>
        <p className="help">{t('tax.rates.immutable')}</p>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

function CloseModal({ rate, onClose }: { rate: TaxRateDto; onClose: () => void }) {
  const t = useT()
  const { closeRate } = useTaxMutations()
  const [date, setDate] = useState('')
  const [error, setError] = useState<string | null>(null)
  const save = async () => {
    setError(null)
    if (!date) return setError(t('tax.rates.closeRequired'))
    try {
      await closeRate.mutateAsync({ id: rate.id!, effectiveTo: date })
      toast.success(t('tax.rates.closed', { code: rate.code ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="sm"
      title={t('tax.rates.closeTitle', { code: rate.code ?? '' })}
      onClose={onClose}
      dismissible={!closeRate.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={closeRate.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={closeRate.isPending}>{t('tax.rates.close')}</button>
        </>
      }
    >
      <div className="pb tax-form">
        <p>{t('tax.rates.closeHelp')}</p>
        <div className="f">
          <label htmlFor="tx-close">{t('tax.rates.closeDate')}</label>
          <DateField id="tx-close" min={rate.effectiveFrom ?? undefined} value={date} onChange={(v) => setDate(v)} />
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

export function RatesTab() {
  const t = useT()
  const f = useFormat()
  const rates = useTaxRates()
  const [creating, setCreating] = useState<TaxRateDto | 'new' | null>(null)
  const [closing, setClosing] = useState<TaxRateDto | null>(null)

  const columns = useMemo<DataColumn<TaxRateDto>[]>(
    () => [
      { id: 'code', header: t('tax.rates.code'), cell: (r) => <b>{r.code}</b>, exportValue: (r) => r.code ?? '', sortValue: (r) => r.code ?? '', card: 'title' },
      { id: 'name', header: t('tax.rates.name'), cell: (r) => r.name, sortValue: (r) => r.name ?? '' },
      {
        id: 'percent',
        header: t('tax.rates.percent'),
        cell: (r) => (
          <>
            <b>{r.percent} %</b>
            <div className="help">{componentsText(r.components)}</div>
          </>
        ),
        exportValue: (r) => `${r.percent} % (${componentsText(r.components)})`,
        sortValue: (r) => r.percent ?? 0,
        align: 'end',
      },
      { id: 'from', header: t('tax.rates.from'), cell: (r) => f.date(r.effectiveFrom), exportValue: (r) => r.effectiveFrom ?? '', sortValue: (r) => r.effectiveFrom ?? '' },
      { id: 'to', header: t('tax.rates.to'), cell: (r) => (r.effectiveTo ? f.date(r.effectiveTo) : t('tax.rates.open')), exportValue: (r) => r.effectiveTo ?? '', sortValue: (r) => r.effectiveTo ?? '9999-12-31' },
      { id: 'status', header: t('tax.rates.status'), cell: (r) => <Chip tone={STATUS_TONE[r.status ?? 'CLOSED']}>{t(`tax.rates.statuses.${r.status}`)}</Chip>, exportValue: (r) => t(`tax.rates.statuses.${r.status}`), sortValue: (r) => r.status ?? '' },
    ],
    [t, f],
  )

  return (
    <Panel
      flush
      icon={<IconCash />}
      title={t('tax.rates.panel')}
      badge={rates.isPending ? undefined : (rates.data ?? NO_ROWS).length}
      actions={<button type="button" className="btn flow" onClick={() => setCreating('new')}>{t('tax.rates.new')}</button>}
    >
      <div className="pb">
        {rates.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(rates.error).title}</p>
        ) : rates.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('tax.rates.panel')}
            columns={columns}
            rows={rates.data}
            rowKey={(r) => String(r.id)}
            defaultSort={{ id: 'from', desc: true }}
            empty={t('tax.rates.empty')}
            rowActions={[
              { key: 'close', label: t('tax.rates.close'), icon: <IconClock />, onClick: (r) => setClosing(r), visible: (r) => !r.effectiveTo },
              { key: 'again', label: t('tax.rates.newFrom'), icon: <IconCash />, onClick: (r) => setCreating(r), visible: (r) => !!r.effectiveTo },
            ]}
          />
        )}
        <p className="help">{t('tax.rates.help')}</p>
      </div>
      {creating && <RateModal key={creating === 'new' ? 'new' : creating.id} base={creating === 'new' ? null : creating} onClose={() => setCreating(null)} />}
      {closing && <CloseModal key={closing.id} rate={closing} onClose={() => setClosing(null)} />}
    </Panel>
  )
}
