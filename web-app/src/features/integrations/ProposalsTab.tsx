// Las PROPUESTAS de cierre de las integraciones (2026-10-23): lo que los lectores de cierre (p. ej. la hoja «Ruta completada» de CMS) encontraron en los documentos — «llevar la orden X a Recogida porque este
// documento lo dice». Nada cambia solo: una persona confirma (la orden cambia en ese momento, a su nombre) o rechaza, con una nota opcional. Permiso integrations.inbox.
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Field, IconCheck, IconTrash, Panel, SelectFilter, Spinner, TextInput, toast, type DataColumn } from '../../kernel/ui'
import { IconClip } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { PROPOSAL_TONE } from './integrationRules'
import { useProposalMutations, useProposals, type ProposalDto } from './integrationsApi'

const PAGE = 50
const NO_ROWS: ProposalDto[] = []

function DecisionModal({ item, kind, onClose }: { item: ProposalDto; kind: 'confirm' | 'reject'; onClose: () => void }) {
  const t = useT()
  const { confirm, reject } = useProposalMutations()
  const form = useForm<{ note: string }>({ defaultValues: { note: '' } })
  return (
    <FormModal
      id={`proposal-${kind}`}
      size="sm"
      title={t(`integrations.proposals.${kind}Title`, { order: item.orderNumber ?? '' })}
      open
      onClose={onClose}
      form={form}
      saveLabel={t(`integrations.proposals.${kind}`)}
      onSubmit={async (v: { note: string }) => {
        const id = item.publicId ?? ''
        const done = kind === 'confirm' ? await confirm.mutateAsync({ id, note: v.note.trim() }) : await reject.mutateAsync({ id, note: v.note.trim() })
        if (done.status === 'FAILED') toast.error(done.message ?? '')
        else toast.success(t(`integrations.proposals.${kind === 'confirm' ? 'confirmed' : 'rejected'}`))
        onClose()
      }}
    >
      <p className="help">{item.summary}</p>
      <Field name="note" label={t('integrations.proposals.note')} help={t('integrations.proposals.noteHelp')}>
        <TextInput maxLength={400} />
      </Field>
    </FormModal>
  )
}

export function ProposalsTab() {
  const t = useT()
  const f = useFormat()
  const canInbox = useCan('integrations.inbox')
  const [status, setStatus] = useState('PENDING')
  const [skip, setSkip] = useState(0)
  const [deciding, setDeciding] = useState<{ item: ProposalDto; kind: 'confirm' | 'reject' } | null>(null)
  const proposals = useProposals(status, skip, PAGE)
  const items = proposals.data?.items ?? NO_ROWS
  const statusOptions = ['PENDING', 'CONFIRMED', 'APPLIED', 'REJECTED', 'FAILED'].map((s) => ({ value: s, label: t(`integrations.proposals.status.${s}`) }))

  const columns = useMemo<DataColumn<ProposalDto>[]>(
    () => [
      { id: 'status', header: t('integrations.proposals.col.status'), cell: (p) => <Chip tone={PROPOSAL_TONE[p.status ?? 'PENDING']}>{t(`integrations.proposals.status.${p.status}`)}</Chip>, sortValue: (p) => p.status ?? '', card: 'title' },
      { id: 'order', header: t('integrations.proposals.col.order'), cell: (p) => <span className="mono">{p.orderNumber}</span>, sortValue: (p) => p.orderNumber ?? '' },
      { id: 'action', header: t('integrations.proposals.col.action'), cell: (p) => t(`integrations.proposals.to.${p.toStatus}`), sortValue: (p) => p.toStatus ?? '' },
      { id: 'document', header: t('integrations.proposals.col.document'), cell: (p) => p.document ?? '—', sortValue: (p) => p.document ?? '' },
      { id: 'integration', header: t('integrations.proposals.col.integration'), cell: (p) => p.integrationName, sortValue: (p) => p.integrationName ?? '' },
      {
        id: 'decision',
        header: t('integrations.proposals.col.decision'),
        cell: (p) => (
          <>
            {p.status === 'FAILED' ? <span className="cs-err">{p.message}</span> : p.message ? <span className="help">{p.message}</span> : null}
            {p.decidedAtUtc && <div className="help">{`${p.decidedByName ?? ''} · ${f.dateTime(p.decidedAtUtc)}`}</div>}
            {p.note && <div className="help">{p.note}</div>}
            {!p.message && !p.decidedAtUtc && !p.note && '—'}
          </>
        ),
        exportValue: (p) => p.message ?? p.note ?? '',
        sortValue: (p) => p.decidedAtUtc ?? '',
      },
      { id: 'date', header: t('integrations.proposals.col.date'), cell: (p) => f.dateTime(p.createdAtUtc), sortValue: (p) => p.createdAtUtc ?? '' },
    ],
    [t, f],
  )

  if (!canInbox) return <EmptyState icon={<IconClip />} title={t('integrations.proposals.noAccess')} />
  return (
    <>
      <Panel flush icon={<IconClip />} title={t('integrations.proposals.title')} badge={proposals.isPending ? undefined : proposals.data?.pending}>
        <div className="pb">
          <div className="filters" role="group" aria-label={t('ui.filters.label')}>
            <SelectFilter label={t('integrations.proposals.col.status')} value={status} onChange={(v) => { setStatus(v); setSkip(0) }} options={statusOptions} allLabel={t('integrations.inbox.allStatuses')} />
          </div>
          {proposals.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(proposals.error).title}</p>
          ) : proposals.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('integrations.proposals.title')}
              columns={columns}
              rows={items}
              rowKey={(p) => p.publicId ?? ''}
              defaultSort={{ id: 'date', desc: true }}
              empty={status === 'PENDING' ? t('integrations.proposals.emptyPending') : t('integrations.proposals.empty')}
              rowActions={[
                { key: 'confirm', label: t('integrations.proposals.confirm'), icon: <IconCheck />, visible: (p) => p.status === 'PENDING', onClick: (p) => setDeciding({ item: p, kind: 'confirm' }) },
                { key: 'reject', label: t('integrations.proposals.reject'), icon: <IconTrash />, visible: (p) => p.status === 'PENDING', onClick: (p) => setDeciding({ item: p, kind: 'reject' }) },
              ]}
            />
          )}
          {proposals.data && (proposals.data.total ?? 0) > PAGE && (
            <div className="int-pager">
              <button type="button" className="btn sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>{t('integrations.prev')}</button>
              <span className="help">{t('integrations.pageOf', { from: skip + 1, to: Math.min(skip + PAGE, proposals.data.total ?? 0), total: proposals.data.total ?? 0 })}</span>
              <button type="button" className="btn sm" disabled={skip + PAGE >= (proposals.data.total ?? 0)} onClick={() => setSkip(skip + PAGE)}>{t('integrations.next')}</button>
            </div>
          )}
          <p className="help">{t('integrations.proposals.help')}</p>
        </div>
      </Panel>
      {deciding && <DecisionModal key={`${deciding.kind}-${deciding.item.publicId}`} item={deciding.item} kind={deciding.kind} onClose={() => setDeciding(null)} />}
    </>
  )
}
