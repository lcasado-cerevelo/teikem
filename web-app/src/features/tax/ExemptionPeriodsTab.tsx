// Impuestos → Exención por temporada (Lote 248): un decreto que exime del IVU a todos los productos o a categorías y productos escogidos entre dos fechas
// (p. ej. después de un huracán). Las líneas cubiertas salen con el motivo «Exención de producto por temporada» y el nombre del período; la exención del
// cliente no se le suma. Programado = se edita todo; ya empezado = solo la fecha final o darlo de baja; terminado = solo consulta. Nunca se borra: se da de baja.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { CategoryProductPicker, Chip, type ChipTone, type DataColumn, DataTable, DateField, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconCheckCircle, IconEdit, IconPower } from '../../kernel/ui/actionIcons'
import { IconClock } from '../../kernel/ui/screenIcons'
import { useProductCategories } from '../warehouse/api'
import { useExemptionPeriods, useTaxMutations, type TaxExemptionPeriodDto } from './taxApi'
import { periodEditable, periodError, type PeriodDraft, type PeriodScope } from './taxRules'

const STATE_TONE: Record<string, ChipTone> = { ACTIVE: 'deliv', SCHEDULED: 'warn', ENDED: 'neutral', INACTIVE: 'neutral' }
const NO_ROWS: TaxExemptionPeriodDto[] = []
const SCOPES: PeriodScope[] = ['ALL', 'SELECTED']

interface PickedProduct {
  publicId: string
  label: string
}

function PeriodModal({ period, onClose }: { period: TaxExemptionPeriodDto | null; onClose: () => void }) {
  const t = useT()
  const { createPeriod, updatePeriod } = useTaxMutations()
  const categories = useProductCategories({ includeInactive: false })
  const editing = period !== null
  const editable = editing ? periodEditable(period.state) : 'ALL'
  const locked = editable !== 'ALL'
  const [draft, setDraft] = useState<PeriodDraft>({
    name: period?.name ?? '',
    reason: period?.reason ?? '',
    from: period?.validFrom ?? '',
    to: period?.validTo ?? '',
    scope: period?.scope === 'SELECTED' ? 'SELECTED' : 'ALL',
    productPublicIds: (period?.products ?? []).map((p) => p.productPublicId ?? ''),
    categoryIds: (period?.categories ?? []).map((c) => c.categoryId ?? 0),
  })
  const [productNames, setProductNames] = useState<Record<string, string>>(() => Object.fromEntries((period?.products ?? []).map((p) => [p.productPublicId ?? '', [p.sku, p.name].filter(Boolean).join(' · ')])))
  const [error, setError] = useState<string | null>(null)
  const busy = createPeriod.isPending || updatePeriod.isPending
  const set = (patch: Partial<PeriodDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const categoryName = (id: number) => (categories.data ?? []).find((c) => c.id === id)?.path || (categories.data ?? []).find((c) => c.id === id)?.name || (period?.categories ?? []).find((c) => c.categoryId === id)?.name || String(id)
  const picked: PickedProduct[] = draft.productPublicIds.map((id) => ({ publicId: id, label: productNames[id] ?? id }))

  const save = async () => {
    setError(null)
    const problem = periodError(draft)
    if (problem) return setError(t(`tax.periods.errors.${problem}`))
    try {
      if (editing && editable === 'END_DATE') {
        await updatePeriod.mutateAsync({ publicId: period.publicId!, body: { validTo: draft.to, rowVersion: period.rowVersion } })
      } else if (editing) {
        await updatePeriod.mutateAsync({
          publicId: period.publicId!,
          body: { name: draft.name, reason: draft.reason, validFrom: draft.from, validTo: draft.to, scope: draft.scope, productPublicIds: draft.scope === 'SELECTED' ? draft.productPublicIds : [], categoryIds: draft.scope === 'SELECTED' ? draft.categoryIds : [], rowVersion: period.rowVersion },
        })
      } else {
        await createPeriod.mutateAsync({ name: draft.name, reason: draft.reason, validFrom: draft.from, validTo: draft.to, scope: draft.scope, productPublicIds: draft.scope === 'SELECTED' ? draft.productPublicIds : undefined, categoryIds: draft.scope === 'SELECTED' ? draft.categoryIds : undefined })
      }
      toast.success(t(editing ? 'tax.periods.updated' : 'tax.periods.created'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t(editing ? 'tax.periods.edit' : 'tax.periods.new')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          {editable !== 'NONE' && <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>{busy ? t('common.loading') : t('tax.periods.save')}</button>}
        </>
      }
    >
      <div className="pb tax-form">
        {locked && <p className="txp-lock" role="status">{t(editable === 'END_DATE' ? 'tax.periods.lockStarted' : 'tax.periods.lockEnded')}</p>}
        <div className="f">
          <label htmlFor="txp-name">{t('tax.periods.name')}</label>
          <input id="txp-name" value={draft.name} maxLength={100} autoComplete="off" disabled={locked} onChange={(e) => set({ name: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor="txp-reason">{t('tax.periods.reason')}</label>
          <input id="txp-reason" value={draft.reason} maxLength={300} autoComplete="off" disabled={locked} onChange={(e) => set({ reason: e.target.value })} />
          <p className="help">{t('tax.periods.reasonHelp')}</p>
        </div>
        <div className="r2">
          <div className="f">
            <label htmlFor="txp-from">{t('tax.exemptions.from')}</label>
            <DateField id="txp-from" value={draft.from} onChange={(v) => set({ from: v })} disabled={locked} />
          </div>
          <div className="f">
            <label htmlFor="txp-to">{t('tax.capture.to')}</label>
            <DateField id="txp-to" value={draft.to} onChange={(v) => set({ to: v })} disabled={editable === 'NONE'} />
          </div>
        </div>
        <fieldset className="tax-comps tax-scope" disabled={locked}>
          <legend>{t('tax.periods.scope')}</legend>
          <div className="tax-scope-options" role="radiogroup" aria-label={t('tax.periods.scope')}>
            {SCOPES.map((s) => (
              <label key={s} className="tax-check">
                <input type="radio" name="txp-scope" value={s} checked={draft.scope === s} onChange={() => set({ scope: s })} />
                <span>{t(`tax.periods.scopes.${s}`)}</span>
              </label>
            ))}
          </div>
          {draft.scope === 'SELECTED' && (
            <>
              {!locked && (
                <CategoryProductPicker
                  id="txp-pick"
                  label={t('tax.periods.pick')}
                  value={null}
                  categories={categories.data ?? []}
                  categoriesLoading={categories.isPending}
                  onChange={(value, detail) => {
                    if (value?.kind === 'category' && !draft.categoryIds.includes(value.id)) set({ categoryIds: [...draft.categoryIds, value.id] })
                    if (value?.kind === 'product' && !draft.productPublicIds.includes(value.publicId)) {
                      setProductNames((n) => ({ ...n, [value.publicId]: [detail?.product?.sku, detail?.product?.name].filter(Boolean).join(' · ') || value.publicId }))
                      set({ productPublicIds: [...draft.productPublicIds, value.publicId] })
                    }
                  }}
                />
              )}
              <ul className="txp-picked" aria-label={t('tax.periods.selection')}>
                {draft.categoryIds.map((id) => (
                  <li key={`c${id}`}>
                    <Chip tone="route">{t('tax.periods.categoryChip')}</Chip> <span>{categoryName(id)}</span>
                    {!locked && <button type="button" className="btn sm" aria-label={t('tax.periods.removeItem', { name: categoryName(id) })} onClick={() => set({ categoryIds: draft.categoryIds.filter((x) => x !== id) })}>{t('tax.periods.remove')}</button>}
                  </li>
                ))}
                {picked.map((p) => (
                  <li key={`p${p.publicId}`}>
                    <Chip tone="cap">{t('tax.periods.productChip')}</Chip> <span>{p.label}</span>
                    {!locked && <button type="button" className="btn sm" aria-label={t('tax.periods.removeItem', { name: p.label })} onClick={() => set({ productPublicIds: draft.productPublicIds.filter((x) => x !== p.publicId) })}>{t('tax.periods.remove')}</button>}
                  </li>
                ))}
              </ul>
              {draft.categoryIds.length + draft.productPublicIds.length === 0 && <p className="help">{t('tax.periods.selectionEmpty')}</p>}
              <p className="help">{t('tax.periods.categoriesHelp')}</p>
            </>
          )}
        </fieldset>
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}

export function ExemptionPeriodsTab() {
  const t = useT()
  const f = useFormat()
  const [showInactive, setShowInactive] = useState(false)
  const list = useExemptionPeriods(showInactive)
  const { setPeriodActive } = useTaxMutations()
  const [modal, setModal] = useState<TaxExemptionPeriodDto | 'new' | null>(null)

  const toggle = async (p: TaxExemptionPeriodDto) => {
    try {
      await setPeriodActive.mutateAsync({ publicId: p.publicId!, active: !p.isActive })
      toast.success(t(p.isActive ? 'tax.periods.deactivated' : 'tax.periods.reactivated'))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const scopeText = (p: TaxExemptionPeriodDto) => (p.scope === 'SELECTED' ? t('tax.periods.scopeCount', { products: p.products?.length ?? 0, categories: p.categories?.length ?? 0 }) : t('tax.periods.scopes.ALL'))

  const columns = useMemo<DataColumn<TaxExemptionPeriodDto>[]>(
    () => [
      { id: 'name', header: t('tax.periods.name'), cell: (p) => <b>{p.name}</b>, sortValue: (p) => p.name ?? '', exportValue: (p) => p.name ?? '', card: 'title' },
      { id: 'reason', header: t('tax.periods.reason'), cell: (p) => <span className="txp-reason" title={p.reason ?? undefined}>{p.reason}</span>, sortValue: (p) => p.reason ?? '', exportValue: (p) => p.reason ?? '' },
      { id: 'from', header: t('tax.exemptions.from'), cell: (p) => f.date(p.validFrom), exportValue: (p) => p.validFrom ?? '', sortValue: (p) => p.validFrom ?? '' },
      { id: 'to', header: t('tax.exemptions.to'), cell: (p) => f.date(p.validTo), exportValue: (p) => p.validTo ?? '', sortValue: (p) => p.validTo ?? '' },
      { id: 'scope', header: t('tax.periods.scope'), cell: scopeText, exportValue: scopeText, sortValue: scopeText },
      { id: 'state', header: t('tax.exemptions.status'), cell: (p) => <Chip tone={STATE_TONE[p.state ?? 'INACTIVE']}>{t(`tax.periods.states.${p.state}`)}</Chip>, exportValue: (p) => t(`tax.periods.states.${p.state}`), sortValue: (p) => p.state ?? '' },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f],
  )

  return (
    <Panel
      flush
      icon={<IconClock />}
      title={t('tax.periods.panel')}
      badge={list.isPending ? undefined : (list.data ?? NO_ROWS).length}
      actions={<button type="button" className="btn flow" onClick={() => setModal('new')}>{t('tax.periods.new')}</button>}
    >
      <div className="pb">
        <p className="help">{t('tax.periods.help')}</p>
        <label className="sw txp-inactive">
          <input type="checkbox" role="switch" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('tax.periods.showInactive')}</span>
        </label>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('tax.periods.panel')}
            columns={columns}
            rows={list.data}
            rowKey={(p) => p.publicId ?? ''}
            defaultSort={{ id: 'from', desc: true }}
            empty={t('tax.periods.empty')}
            rowActions={[
              { key: 'edit', label: t('tax.periods.edit'), icon: <IconEdit />, onClick: (p) => setModal(p) },
              { key: 'deactivate', label: t('tax.exemptions.deactivate'), icon: <IconPower />, onClick: (p) => void toggle(p), visible: (p) => !!p.isActive },
              { key: 'reactivate', label: t('tax.exemptions.reactivate'), icon: <IconCheckCircle />, onClick: (p) => void toggle(p), visible: (p) => !p.isActive },
            ]}
          />
        )}
      </div>
      {modal && <PeriodModal key={modal === 'new' ? 'new' : modal.publicId} period={modal === 'new' ? null : modal} onClose={() => setModal(null)} />}
    </Panel>
  )
}
