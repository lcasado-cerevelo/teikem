// Pestaña «Tarifas»: tarifas por servicio (servicio + paquete + tarifa) y pieza extra por tramos. Historial efectivo-fechado:
// «Editar» abre una versión nueva (cierra la vigente en «Vigente desde»), «Quitar» cierra (nunca se borra) y «Ver historial»
// trae también las cerradas. Cada sección se ve solo si su check del modelo de facturación está marcado (con filas
// existentes siempre se ve, pero sin escrituras, como el servidor). Escribe quien tiene contracts.update y `canEdit`.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, type DataColumn, DateInput, EmptyState, Field, IconEdit, IconTrash, NumberInput, Select, Spinner, toast, useElementWidth } from '../../kernel/ui'
import { IconCash } from '../../kernel/ui/screenIcons'
import { useRateComponents, useRateMutations } from './contractApi'
import {
  buildTierPatch,
  defaultEffectiveDate,
  lookupOptions,
  rateSchema,
  serverToday,
  tierRangeText,
  tierSchema,
  type ContractDetail,
  type ExtraPieceRow,
  type RateRow,
  type Tier,
} from './contractRules'
import { FormModal } from './contractUi'

const FORM_ID = 'contract-rate'

// ---- Tarifa por servicio (alta / nueva versión) y componente de pieza extra (alta) ----
function RateModal({ contract, kind, row, open, onClose }: { contract: ContractDetail; kind: 'PER_SERVICE' | 'EXTRA_PIECE'; row: RateRow | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const mutations = useRateMutations(contract.publicId ?? '')
  const { data: services = [] } = useLookups('ServiceType')
  const { data: packages = [] } = useLookups('PackageType')
  const editing = row !== null
  const isExtra = kind === 'EXTRA_PIECE'
  const schema = useMemo(
    () =>
      rateSchema(
        {
          serviceRequired: t('clients.rates.errors.serviceRequired'),
          packageRequired: t('clients.rates.errors.packageRequired'),
          rateRequired: t('clients.rates.errors.rateRequired'),
          rateMin: t('clients.rates.errors.rateMin'),
          dateRequired: t('clients.rates.errors.dateRequired'),
          dateBeforeToday: t('clients.rates.errors.dateBeforeToday'),
        },
        { needsKeys: !editing, needsRate: !isExtra, minDate: editing ? serverToday() : undefined },
      ),
    [t, editing, isExtra],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { serviceType: row?.serviceType ?? '', packageType: row?.packageType ?? '', rate: row?.rate ?? null, effectiveFrom: defaultEffectiveDate(tenantToday()) },
  })

  const title = isExtra ? t('clients.rates.addExtraTitle') : editing ? t('clients.rates.editTitle') : t('clients.rates.addTitle')
  return (
    <FormModal
      id={FORM_ID}
      title={title}
      open={open}
      onClose={onClose}
      form={form}
      onSubmit={async (v) => {
        if (editing) await mutations.update.mutateAsync({ id: row.id, body: { rate: v.rate ?? 0, effectiveFrom: v.effectiveFrom } })
        else await mutations.add.mutateAsync({ kind, serviceType: v.serviceType, packageType: v.packageType, rate: isExtra ? null : v.rate, effectiveFrom: v.effectiveFrom })
        toast.success(t('clients.rates.saved'))
        onClose()
      }}
    >
      {editing ? (
        <>
          <p className="meta">
            {row.serviceTypeLabel ?? row.serviceType} · {row.packageTypeLabel ?? row.packageType}
          </p>
          <p className="help">{t('clients.rates.editHelp')}</p>
        </>
      ) : (
        <div className="r2">
          <Field name="serviceType" label={t('clients.rates.service')} required>
            <Select options={lookupOptions(services)} placeholder={t('clients.rates.choose')} />
          </Field>
          <Field name="packageType" label={t('clients.rates.package')} required>
            <Select options={lookupOptions(packages)} placeholder={t('clients.rates.choose')} />
          </Field>
        </div>
      )}
      <div className="r2">
        {!isExtra && (
          <Field name="rate" label={t('clients.rates.rate')} required>
            <NumberInput min={0} />
          </Field>
        )}
        <Field name="effectiveFrom" label={t('clients.rates.effectiveFrom')} help={editing ? t('clients.rates.effectiveHelpNew') : t('clients.rates.effectiveHelpAdd')} required>
          <DateInput min={editing ? serverToday() : undefined} />
        </Field>
      </div>
    </FormModal>
  )
}

// ---- Tramo de pieza extra (alta / nueva versión) ----
function TierModal({ contract, component, tier, open, onClose }: { contract: ContractDetail; component: ExtraPieceRow; tier: Tier | null; open: boolean; onClose: () => void }) {
  const t = useT()
  const mutations = useRateMutations(contract.publicId ?? '')
  const editing = tier !== null
  const schema = useMemo(
    () =>
      tierSchema(
        {
          fromRequired: t('clients.rates.errors.fromRequired'),
          rangeInvalid: t('clients.rates.errors.rangeInvalid'),
          rateRequired: t('clients.rates.errors.rateRequired'),
          rateMin: t('clients.rates.errors.rateMin'),
          dateRequired: t('clients.rates.errors.dateRequired'),
          dateBeforeToday: t('clients.rates.errors.dateBeforeToday'),
        },
        { minDate: editing ? serverToday() : undefined },
      ),
    [t, editing],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { fromUnit: tier?.fromUnit ?? null, toUnit: tier?.toUnit ?? null, rate: tier?.rate ?? null, effectiveFrom: defaultEffectiveDate(tenantToday()) },
  })

  return (
    <FormModal
      id="contract-tier"
      title={editing ? t('clients.rates.editTierTitle') : t('clients.rates.addTierTitle')}
      open={open}
      onClose={onClose}
      form={form}
      onSubmit={async (v) => {
        if (editing) await mutations.updateTier.mutateAsync({ id: component.componentId, tierId: tier.id, body: buildTierPatch(v, tier) })
        else await mutations.addTier.mutateAsync({ id: component.componentId, body: { fromUnit: v.fromUnit ?? 0, toUnit: v.toUnit, rate: v.rate ?? 0, effectiveFrom: v.effectiveFrom } })
        toast.success(t('clients.rates.saved'))
        onClose()
      }}
    >
      <p className="meta">
        {component.serviceTypeLabel ?? component.serviceType} · {component.packageTypeLabel ?? component.packageType}
      </p>
      {editing && <p className="help">{t('clients.rates.editHelp')}</p>}
      <div className="r3">
        <Field name="fromUnit" label={t('clients.rates.tierFrom')} required>
          <NumberInput min={2} step={1} />
        </Field>
        <Field name="toUnit" label={t('clients.rates.tierTo')} help={t('clients.rates.tierToHelp')}>
          <NumberInput min={2} step={1} />
        </Field>
        <Field name="rate" label={t('clients.rates.rate')} required>
          <NumberInput min={0} />
        </Field>
      </div>
      <Field name="effectiveFrom" label={t('clients.rates.effectiveFrom')} help={editing ? t('clients.rates.effectiveHelpNew') : t('clients.rates.effectiveHelpAdd')} required>
        <DateInput min={editing ? serverToday() : undefined} />
      </Field>
    </FormModal>
  )
}

type Closing = { kind: 'rate'; row: RateRow } | { kind: 'component'; row: ExtraPieceRow } | { kind: 'tier'; component: ExtraPieceRow; tier: Tier }

export function ContractRatesTab({ contract }: { contract: ContractDetail }) {
  const t = useT()
  const f = useFormat()
  const publicId = contract.publicId ?? ''
  const canWrite = useCan('contracts.update') && contract.canEdit
  const [history, setHistory] = useState(false)
  const { data, isPending, error } = useRateComponents(publicId, history)
  const mutations = useRateMutations(publicId)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)
  const [rateModal, setRateModal] = useState<{ kind: 'PER_SERVICE' | 'EXTRA_PIECE'; row: RateRow | null } | null>(null)
  const [tierModal, setTierModal] = useState<{ component: ExtraPieceRow; tier: Tier | null } | null>(null)
  const [closing, setClosing] = useState<Closing | null>(null)

  const perOn = contract.billingModel?.billPerService ?? false
  const extraOn = contract.billingModel?.billExtraPiece ?? false
  const perRows = useMemo(() => data?.perService ?? [], [data?.perService])
  const extraRows = useMemo(() => data?.extraPiece ?? [], [data?.extraPiece])
  const showPer = perOn || perRows.length > 0
  const showExtra = extraOn || extraRows.length > 0
  const currency = contract.currency

  const perColumns = useMemo<DataColumn<RateRow>[]>(
    () => [
      { id: 'service', header: t('clients.rates.service'), cell: (r) => <b>{r.serviceTypeLabel ?? r.serviceType}</b>, sortValue: (r) => r.serviceTypeLabel ?? r.serviceType, card: 'title' },
      { id: 'package', header: t('clients.rates.package'), cell: (r) => r.packageTypeLabel ?? r.packageType, sortValue: (r) => r.packageTypeLabel ?? r.packageType },
      { id: 'rate', header: t('clients.rates.rate'), align: 'end', cell: (r) => <span className="mono">{r.rate == null ? '—' : f.money(r.rate, { currency, unitPrice: true })}</span>, sortValue: (r) => r.rate },
      { id: 'from', header: t('clients.rates.from'), cell: (r) => f.date(r.effectiveFrom), sortValue: (r) => r.effectiveFrom },
      ...(history ? [{ id: 'to', header: t('clients.rates.to'), cell: (r: RateRow) => (r.effectiveTo ? f.date(r.effectiveTo) : '—'), sortValue: (r: RateRow) => r.effectiveTo }] : []),
    ],
    [t, f, history, currency],
  )

  const tierColumns = useMemo<DataColumn<Tier>[]>(
    () => [
      { id: 'range', header: t('clients.rates.range'), cell: (r) => <b className="mono">{tierRangeText(r)}</b>, sortValue: (r) => r.fromUnit, card: 'title' },
      { id: 'rate', header: t('clients.rates.rate'), align: 'end', cell: (r) => <span className="mono">{r.rate == null ? '—' : f.money(r.rate, { currency, unitPrice: true })}</span>, sortValue: (r) => r.rate },
      { id: 'from', header: t('clients.rates.from'), cell: (r) => f.date(r.effectiveFrom), sortValue: (r) => r.effectiveFrom },
      ...(history ? [{ id: 'to', header: t('clients.rates.to'), cell: (r: Tier) => (r.effectiveTo ? f.date(r.effectiveTo) : '—'), sortValue: (r: Tier) => r.effectiveTo }] : []),
    ],
    [t, f, history, currency],
  )

  if (isPending) return <Spinner block />
  if (error) return <EmptyState icon={<IconCash />} title={applyProblemDetails(error).title} />

  const confirmMessage =
    closing?.kind === 'rate'
      ? t('clients.rates.removeBody', { name: `${closing.row.serviceTypeLabel ?? closing.row.serviceType} · ${closing.row.packageTypeLabel ?? closing.row.packageType}` })
      : closing?.kind === 'component'
        ? t('clients.rates.removeComponentBody', { name: `${closing.row.serviceTypeLabel ?? closing.row.serviceType} · ${closing.row.packageTypeLabel ?? closing.row.packageType}` })
        : closing?.kind === 'tier'
          ? t('clients.rates.removeTierBody', { range: tierRangeText(closing.tier) })
          : ''

  return (
    <div className="cl-ctr-tab" ref={ref}>
      <div className="cl-rates-bar">
        <label className="sw">
          <input type="checkbox" role="switch" checked={history} onChange={(e) => setHistory(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('clients.rates.showHistory')}</span>
        </label>
      </div>

      {!showPer && !showExtra && <p className="note cl-ctr-note">{t('clients.rates.noComponents')}</p>}

      {showPer && (
        <section className="cl-rates-sec" aria-label={t('clients.rates.perService')}>
          <div className="cl-sec-head">
            <h3 className="cl-ctr-h">{t('clients.rates.perService')}</h3>
            {canWrite && perOn && (
              <button type="button" className="btn sm flow" onClick={() => setRateModal({ kind: 'PER_SERVICE', row: null })}>
                {t('clients.rates.add')}
              </button>
            )}
          </div>
          {!perOn && <p className="note cl-ctr-note">{t('clients.rates.offPerService')}</p>}
          {perRows.length === 0 ? (
            <EmptyState icon={<IconCash />} title={t('clients.rates.emptyPerService')} />
          ) : (
            <DataTable
              label={t('clients.rates.perService')}
              columns={perColumns}
              rows={perRows}
              rowKey={(r) => String(r.id)}
              pagination={false}
              exportable={false}
              forceCards={width > 0 && width < 640}
              rowClassName={(r) => (r.isCurrent ? undefined : 'dim')}
              rowActions={[
                { key: 'edit', label: t('clients.edit'), icon: <IconEdit />, onClick: (r) => setRateModal({ kind: 'PER_SERVICE', row: r }), visible: (r) => canWrite && perOn && !r.effectiveTo },
                { key: 'remove', label: t('clients.remove'), icon: <IconTrash />, tone: 'danger', onClick: (r) => setClosing({ kind: 'rate', row: r }), visible: (r) => canWrite && perOn && !r.effectiveTo },
              ]}
            />
          )}
        </section>
      )}

      {showExtra && (
        <section className="cl-rates-sec" aria-label={t('clients.rates.extraPiece')}>
          <div className="cl-sec-head">
            <h3 className="cl-ctr-h">{t('clients.rates.extraPiece')}</h3>
            {canWrite && extraOn && (
              <button type="button" className="btn sm flow" onClick={() => setRateModal({ kind: 'EXTRA_PIECE', row: null })}>
                {t('clients.rates.addComponent')}
              </button>
            )}
          </div>
          {!extraOn && <p className="note cl-ctr-note">{t('clients.rates.offExtra')}</p>}
          {extraRows.length === 0 ? (
            <EmptyState icon={<IconCash />} title={t('clients.rates.emptyExtra')} />
          ) : (
            extraRows.map((c) => {
              const open = !c.effectiveTo
              const tiers = c.tiers ?? []
              return (
                <div key={c.componentId} className="cl-comp" role="group" aria-label={`${c.serviceTypeLabel ?? c.serviceType} · ${c.packageTypeLabel ?? c.packageType}`}>
                  <div className="cl-sec-head">
                    <span>
                      <b>
                        {c.serviceTypeLabel ?? c.serviceType} · {c.packageTypeLabel ?? c.packageType}
                      </b>{' '}
                      {!open && <Chip tone="cap">{t('clients.rates.closed')}</Chip>}
                    </span>
                    {canWrite && extraOn && open && (
                      <span className="cl-comp-acts">
                        <button type="button" className="btn sm" onClick={() => setTierModal({ component: c, tier: null })}>
                          {t('clients.rates.addTier')}
                        </button>
                        <button type="button" className="btn sm danger" onClick={() => setClosing({ kind: 'component', row: c })}>
                          {t('clients.rates.removeComponent')}
                        </button>
                      </span>
                    )}
                  </div>
                  {tiers.length === 0 ? (
                    <p className="meta">{t('clients.rates.noTiers')}</p>
                  ) : (
                    <DataTable
                      label={`${t('clients.rates.extraPiece')} ${c.serviceTypeLabel ?? c.serviceType}`}
                      columns={tierColumns}
                      rows={tiers}
                      rowKey={(r) => String(r.id)}
                      pagination={false}
                      exportable={false}
                      forceCards={width > 0 && width < 640}
                      rowClassName={(r) => (r.isCurrent ? undefined : 'dim')}
                      rowActions={[
                        { key: 'edit', label: t('clients.edit'), icon: <IconEdit />, onClick: (r) => setTierModal({ component: c, tier: r }), visible: (r) => canWrite && extraOn && open && !r.effectiveTo },
                        { key: 'remove', label: t('clients.remove'), icon: <IconTrash />, tone: 'danger', onClick: (r) => setClosing({ kind: 'tier', component: c, tier: r }), visible: (r) => canWrite && extraOn && open && !r.effectiveTo },
                      ]}
                    />
                  )}
                </div>
              )
            })
          )}
        </section>
      )}

      {rateModal && <RateModal key={`${rateModal.kind}-${rateModal.row?.id ?? 'new'}`} contract={contract} kind={rateModal.kind} row={rateModal.row} open onClose={() => setRateModal(null)} />}
      {tierModal && <TierModal key={`${tierModal.component.componentId}-${tierModal.tier?.id ?? 'new'}`} contract={contract} component={tierModal.component} tier={tierModal.tier} open onClose={() => setTierModal(null)} />}
      <ConfirmDialog
        open={closing !== null}
        tone="danger"
        title={closing?.kind === 'tier' ? t('clients.rates.removeTierTitle') : closing?.kind === 'component' ? t('clients.rates.removeComponentTitle') : t('clients.rates.removeTitle')}
        message={confirmMessage}
        confirmLabel={t('clients.remove')}
        onConfirm={async () => {
          if (!closing) return
          if (closing.kind === 'rate') await mutations.close.mutateAsync({ id: closing.row.id })
          else if (closing.kind === 'component') await mutations.close.mutateAsync({ id: closing.row.componentId })
          else await mutations.closeTier.mutateAsync({ id: closing.component.componentId, tierId: closing.tier.id })
          toast.success(t('clients.rates.removed'))
        }}
        onClose={() => setClosing(null)}
      />
    </div>
  )
}
