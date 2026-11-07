// «Compañía enlazada» (Lote 227, P7), pestaña de Integraciones / API (módulo INTERCOMPANY): los enlaces de esta compañía con otras («X es cliente de Y») con su estado (propuesto, activo, rechazado,
// terminado) y las entregas que se mandan por ellos. Proponer y aceptar piden reautenticación (el cliente del API la pide solo). Quien da el transporte elige, al aceptar o proponer, el cliente a cuyo
// nombre nacerán las órdenes. Acciones: aceptar / rechazar (o retirar) / terminar; en las entregas, «Reintentar» (las que la otra compañía no aceptó) y «Revisar ahora». Permisos intercompany.view / .manage.
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Can, ModuleGate, useCan } from '../../kernel/access'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Field, IconCheck, IconPlay, IconPlus, IconTrash, Panel, QBox, Select, Spinner, TextArea, toast, type ChipTone, type DataColumn } from '../../kernel/ui'
import { IconSwap } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { useClientOptions } from '../consignees/api'
import { LinkedDeliveryInfo } from './LinkedDeliveryInfo'
import {
  DELIVERY_STATES,
  tenantLinkDeliveryPageReader,
  useTenantLinkDeliveries,
  useTenantLinkMutations,
  useTenantLinkOptions,
  useTenantLinks,
  type TenantLinkDeliveryDto,
  type TenantLinkDto,
} from './tenantLinkApi'

const NO_LINKS: TenantLinkDto[] = []
const NO_DELIVERIES: TenantLinkDeliveryDto[] = []
const LINK_TONE: Record<string, ChipTone> = { PROPOSED: 'warn', ACTIVE: 'deliv', REJECTED: 'fail', ENDED: 'neutral' }

function useDebounced(value: string, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setV(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return v
}

// ---------------------------------------------------------------- proponer

interface ProposeForm {
  companyPublicId: string
  role: string
  clientPublicId: string
  note: string
}

function ProposeModal({ onClose }: { onClose: () => void }) {
  const t = useT()
  const { propose } = useTenantLinkMutations()
  const options = useTenantLinkOptions(true)
  const clients = useClientOptions(false)
  const form = useForm<ProposeForm>({ defaultValues: { companyPublicId: '', role: 'CLIENT', clientPublicId: '', note: '' } })
  const role = form.watch('role')
  return (
    <FormModal
      id="tenant-link-propose"
      size="md"
      title={t('tenantLink.propose.title')}
      open
      onClose={onClose}
      form={form}
      saveLabel={t('tenantLink.propose.save')}
      onSubmit={async (v: ProposeForm) => {
        const missing = !v.companyPublicId
        const noClient = v.role === 'PROVIDER' && !v.clientPublicId
        const longNote = v.note.length > 250
        if (missing) form.setError('companyPublicId', { message: t('tenantLink.errors.companyRequired') })
        if (noClient) form.setError('clientPublicId', { message: t('tenantLink.errors.clientRequired') })
        if (longNote) form.setError('note', { message: t('tenantLink.errors.noteMax') })
        if (missing || noClient || longNote) return
        await propose.mutateAsync({ companyPublicId: v.companyPublicId, role: v.role, clientPublicId: v.role === 'PROVIDER' ? v.clientPublicId : null, note: v.note.trim() || null })
        toast.success(t('tenantLink.propose.done'))
        onClose()
      }}
    >
      <Field name="role" label={t('tenantLink.propose.role')} required>
        <Select
          options={[
            { value: 'CLIENT', label: t('tenantLink.propose.roleClient') },
            { value: 'PROVIDER', label: t('tenantLink.propose.roleProvider') },
          ]}
        />
      </Field>
      <Field name="companyPublicId" label={t('tenantLink.propose.company')} required help={options.isPending ? t('common.loading') : (options.data?.companies ?? []).length === 0 ? t('tenantLink.propose.noCompanies') : t('tenantLink.propose.companyHelp')}>
        <Select placeholder={t('tenantLink.propose.choose')} options={(options.data?.companies ?? []).map((c) => ({ value: c.publicId ?? '', label: c.hasModule ? (c.name ?? '') : t('tenantLink.propose.noModule', { name: c.name ?? '' }) }))} />
      </Field>
      {role === 'PROVIDER' && (
        <Field name="clientPublicId" label={t('tenantLink.propose.client')} required help={t('tenantLink.propose.clientHelp')}>
          <Select placeholder={t('tenantLink.propose.choose')} options={(clients.data ?? []).map((c) => ({ value: c.publicId ?? '', label: c.name ?? '' }))} />
        </Field>
      )}
      <Field name="note" label={t('tenantLink.note')} help={t('tenantLink.noteHelp')}>
        <TextArea rows={2} maxLength={250} />
      </Field>
    </FormModal>
  )
}

// ---------------------------------------------------------------- aceptar

function AcceptModal({ link, onClose }: { link: TenantLinkDto; onClose: () => void }) {
  const t = useT()
  const { accept } = useTenantLinkMutations()
  const clients = useClientOptions(false)
  // quien acepta como proveedor (la otra es su cliente) elige el cliente a cuyo nombre nacerán las órdenes
  const needsClient = link.role === 'PROVIDER'
  const form = useForm<{ clientPublicId: string; note: string }>({ defaultValues: { clientPublicId: '', note: '' } })
  return (
    <FormModal
      id="tenant-link-accept"
      size="md"
      title={t('tenantLink.accept.title', { name: link.otherCompanyName ?? '' })}
      open
      onClose={onClose}
      form={form}
      saveLabel={t('tenantLink.accept.save')}
      onSubmit={async (v) => {
        if (needsClient && !v.clientPublicId) return void form.setError('clientPublicId', { message: t('tenantLink.errors.clientRequired') })
        if (v.note.length > 250) return void form.setError('note', { message: t('tenantLink.errors.noteMax') })
        await accept.mutateAsync({ publicId: link.publicId!, clientPublicId: needsClient ? v.clientPublicId : null, note: v.note.trim() || null })
        toast.success(t('tenantLink.accept.done'))
        onClose()
      }}
    >
      <p className="help">{t(link.role === 'PROVIDER' ? 'tenantLink.summary.PROVIDER' : 'tenantLink.summary.CLIENT', { name: link.otherCompanyName ?? '' })}</p>
      {needsClient && (
        <Field name="clientPublicId" label={t('tenantLink.propose.client')} required help={t('tenantLink.accept.clientHelp')}>
          <Select placeholder={t('tenantLink.propose.choose')} options={(clients.data ?? []).map((c) => ({ value: c.publicId ?? '', label: c.name ?? '' }))} />
        </Field>
      )}
      <Field name="note" label={t('tenantLink.note')} help={t('tenantLink.noteHelp')}>
        <TextArea rows={2} maxLength={250} />
      </Field>
    </FormModal>
  )
}

// ---------------------------------------------------------------- rechazar / terminar

function ReasonModal({ link, kind, onClose }: { link: TenantLinkDto; kind: 'reject' | 'end'; onClose: () => void }) {
  const t = useT()
  const { reject, end } = useTenantLinkMutations()
  const form = useForm<{ reason: string }>({ defaultValues: { reason: '' } })
  // quien propuso retira su propuesta; la otra compañía la rechaza
  const withdraw = kind === 'reject' && link.proposedByUs === true
  const key = kind === 'end' ? 'end' : withdraw ? 'withdraw' : 'reject'
  return (
    <FormModal
      id={`tenant-link-${kind}`}
      size="sm"
      title={t(`tenantLink.${key}.title`, { name: link.otherCompanyName ?? '' })}
      open
      onClose={onClose}
      form={form}
      saveLabel={t(`tenantLink.${key}.save`)}
      onSubmit={async (v: { reason: string }) => {
        const reason = v.reason.trim()
        if (!reason) return void form.setError('reason', { message: t('tenantLink.errors.reasonRequired') })
        if (reason.length > 250) return void form.setError('reason', { message: t('tenantLink.errors.noteMax') })
        if (kind === 'end') await end.mutateAsync({ publicId: link.publicId!, reason })
        else await reject.mutateAsync({ publicId: link.publicId!, reason })
        toast.success(t(`tenantLink.${key}.done`))
        onClose()
      }}
    >
      <p className="help">{t(`tenantLink.${key}.help`)}</p>
      <Field name="reason" label={t('tenantLink.reason')} required>
        <TextArea rows={2} maxLength={250} />
      </Field>
    </FormModal>
  )
}

// ---------------------------------------------------------------- enlaces

function LinksPanel() {
  const t = useT()
  const f = useFormat()
  const canManage = useCan('intercompany.manage')
  const links = useTenantLinks()
  const [proposing, setProposing] = useState(false)
  const [deciding, setDeciding] = useState<{ link: TenantLinkDto; kind: 'accept' | 'reject' | 'end' } | null>(null)
  const rows = links.data ?? NO_LINKS

  const columns = useMemo<DataColumn<TenantLinkDto>[]>(
    () => [
      {
        id: 'company',
        header: t('tenantLink.col.company'),
        cell: (l) => (
          <>
            <b>{l.otherCompanyName}</b>
            <div className="help">{t(`tenantLink.summary.${l.role === 'PROVIDER' ? 'PROVIDER' : 'CLIENT'}`, { name: l.otherCompanyName ?? '' })}</div>
          </>
        ),
        exportValue: (l) => l.otherCompanyName ?? '',
        sortValue: (l) => l.otherCompanyName ?? '',
        card: 'title',
      },
      { id: 'status', header: t('tenantLink.col.status'), cell: (l) => <Chip tone={LINK_TONE[l.status ?? ''] ?? 'neutral'}>{t(`tenantLink.status.${l.status}`)}</Chip>, exportValue: (l) => t(`tenantLink.status.${l.status}`), sortValue: (l) => l.status ?? '' },
      { id: 'client', header: t('tenantLink.col.client'), cell: (l) => l.providerClientName ?? '—', exportValue: (l) => l.providerClientName ?? '', sortValue: (l) => l.providerClientName ?? '' },
      {
        id: 'proposed',
        header: t('tenantLink.col.proposed'),
        cell: (l) => (
          <>
            {l.proposedByUs ? t('tenantLink.proposedByUs') : t('tenantLink.proposedByThem')}
            <div className="help">{`${l.proposedByName ?? ''} · ${f.dateTime(l.proposedAtUtc)}`}</div>
            {l.note && <div className="help">{l.note}</div>}
          </>
        ),
        exportValue: (l) => `${l.proposedByName ?? ''} ${l.proposedAtUtc ?? ''}`.trim(),
        sortValue: (l) => l.proposedAtUtc ?? '',
      },
      {
        id: 'response',
        header: t('tenantLink.col.response'),
        cell: (l) =>
          l.respondedAtUtc || l.endedAtUtc ? (
            <>
              {l.respondedAtUtc && <div className="help">{`${l.respondedByName ?? ''} · ${f.dateTime(l.respondedAtUtc)}`}</div>}
              {l.responseNote && <div className="help">{l.responseNote}</div>}
              {l.endedAtUtc && <div className="help">{t(l.endedByUs ? 'tenantLink.endedByUs' : 'tenantLink.endedByThem', { when: f.dateTime(l.endedAtUtc) })}</div>}
              {l.endReason && <div className="help">{l.endReason}</div>}
            </>
          ) : (
            '—'
          ),
        exportValue: (l) => `${l.responseNote ?? ''} ${l.endReason ?? ''}`.trim(),
        sortValue: (l) => l.respondedAtUtc ?? '',
      },
      { id: 'deliveries', header: t('tenantLink.col.deliveries'), cell: (l) => l.deliveryCount ?? 0, sortValue: (l) => l.deliveryCount ?? 0, align: 'end' },
    ],
    [t, f],
  )

  return (
    <>
      <Panel
        flush
        icon={<IconSwap />}
        title={t('tenantLink.links.title')}
        badge={links.isPending ? undefined : rows.length}
        actions={
          <Can perm="intercompany.manage">
            <button type="button" className="btn flow" onClick={() => setProposing(true)}>
              <IconPlus /> {t('tenantLink.propose.button')}
            </button>
          </Can>
        }
      >
        <div className="pb">
          {links.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(links.error).title}</p>
          ) : links.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('tenantLink.links.title')}
              columns={columns}
              rows={rows}
              rowKey={(l) => l.publicId ?? ''}
              defaultSort={{ id: 'proposed', desc: true }}
              empty={t('tenantLink.links.empty')}
              rowActions={[
                { key: 'accept', label: t('tenantLink.accept.button'), icon: <IconCheck />, visible: (l) => canManage && l.status === 'PROPOSED' && l.canRespond === true, onClick: (l) => setDeciding({ link: l, kind: 'accept' }) },
                {
                  key: 'reject',
                  label: t('tenantLink.reject.button'),
                  icon: <IconTrash />,
                  visible: (l) => canManage && l.status === 'PROPOSED' && (l.canRespond === true || l.proposedByUs === true),
                  onClick: (l) => setDeciding({ link: l, kind: 'reject' }),
                },
                { key: 'end', label: t('tenantLink.end.button'), icon: <IconTrash />, visible: (l) => canManage && l.status === 'ACTIVE' && l.canEnd === true, onClick: (l) => setDeciding({ link: l, kind: 'end' }) },
              ]}
            />
          )}
          <p className="help">{t('tenantLink.links.help')}</p>
        </div>
      </Panel>
      {proposing && <ProposeModal onClose={() => setProposing(false)} />}
      {deciding?.kind === 'accept' && <AcceptModal key={deciding.link.publicId} link={deciding.link} onClose={() => setDeciding(null)} />}
      {deciding && deciding.kind !== 'accept' && <ReasonModal key={`${deciding.kind}-${deciding.link.publicId}`} link={deciding.link} kind={deciding.kind} onClose={() => setDeciding(null)} />}
    </>
  )
}

// ---------------------------------------------------------------- entregas

function DeliveriesPanel() {
  const t = useT()
  const f = useFormat()
  const canManage = useCan('intercompany.manage')
  const canPick = useCan('warehouse.pick')
  const [state, setState] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const search = useDebounced(q.trim())
  const list = useTenantLinkDeliveries(state, search, (page - 1) * pageSize, pageSize)
  const { retry, sync } = useTenantLinkMutations()
  const act = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const busy = (d: TenantLinkDeliveryDto) => (retry.isPending && retry.variables === d.publicId) || (sync.isPending && sync.variables === d.publicId)

  const columns = useMemo<DataColumn<TenantLinkDeliveryDto>[]>(
    () => [
      { id: 'order', header: t('tenantLink.col.salesOrder'), cell: (d) => <b>{d.salesOrderNumber}</b>, exportValue: (d) => d.salesOrderNumber ?? '', sortValue: (d) => d.salesOrderNumber ?? '', card: 'title' },
      { id: 'dispatch', header: t('tenantLink.col.dispatch'), cell: (d) => d.pickBatchNumber ?? '—', exportValue: (d) => d.pickBatchNumber ?? '', sortValue: (d) => d.pickBatchNumber ?? '' },
      { id: 'delivery', header: t('tenantLink.col.delivery'), cell: (d) => <LinkedDeliveryInfo delivery={d} />, exportValue: (d) => `${t(`tenantLink.delivery.state.${d.state}`)} ${d.providerOrderNumber ?? ''} ${d.providerStatusLabel ?? ''}`.trim(), sortValue: (d) => d.state ?? '' },
      { id: 'attempts', header: t('tenantLink.col.attempts'), cell: (d) => d.attempts ?? 0, sortValue: (d) => d.attempts ?? 0, align: 'end' },
      { id: 'synced', header: t('tenantLink.col.synced'), cell: (d) => (d.lastSyncedAtUtc ? f.dateTime(d.lastSyncedAtUtc) : '—'), exportValue: (d) => d.lastSyncedAtUtc ?? '', sortValue: (d) => d.lastSyncedAtUtc ?? '' },
      { id: 'created', header: t('tenantLink.col.created'), cell: (d) => f.dateTime(d.createdAtUtc), exportValue: (d) => d.createdAtUtc ?? '', sortValue: (d) => d.createdAtUtc ?? '' },
    ],
    [t, f],
  )

  return (
    <Panel flush icon={<IconPlay />} title={t('tenantLink.deliveries.title')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow">
          <QBox value={q} onChange={(v) => (setQ(v), setPage(1))} placeholder={t('tenantLink.deliveries.search')} />
          <select aria-label={t('tenantLink.col.state')} value={state} onChange={(e) => (setState(e.target.value), setPage(1))}>
            <option value="">{t('tenantLink.deliveries.allStates')}</option>
            {DELIVERY_STATES.map((s) => (
              <option key={s} value={s}>{t(`tenantLink.delivery.state.${s}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('tenantLink.deliveries.title')}
            columns={columns}
            rows={list.data?.items ?? NO_DELIVERIES}
            rowKey={(d) => d.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            empty={t('tenantLink.deliveries.empty')}
            exportRows={() => fetchAllPages<TenantLinkDeliveryDto>(tenantLinkDeliveryPageReader(state, search))}
            rowActions={[
              {
                key: 'retry',
                label: t('tenantLink.deliveries.retry'),
                icon: <IconPlay />,
                visible: (d) => (canManage || canPick) && d.state === 'FAILED',
                disabled: busy,
                onClick: (d) => void act(retry.mutateAsync(d.publicId!), t('tenantLink.deliveries.retried')),
              },
              {
                key: 'sync',
                label: t('tenantLink.deliveries.sync'),
                icon: <IconPlay />,
                visible: (d) => (canManage || canPick) && d.state === 'CREATED',
                disabled: busy,
                onClick: (d) => void act(sync.mutateAsync(d.publicId!), t('tenantLink.deliveries.synced')),
              },
            ]}
          />
        )}
        <p className="help">{t('tenantLink.deliveries.help')}</p>
      </div>
    </Panel>
  )
}

/** Pestaña «Compañías enlazadas» de Integraciones: enlaces y entregas. */
export function TenantLinkPanel() {
  return (
    <ModuleGate module="INTERCOMPANY" perm="intercompany.view">
      <div className="tl-panel">
        <LinksPanel />
        <DeliveriesPanel />
      </div>
    </ModuleGate>
  )
}

export default TenantLinkPanel
