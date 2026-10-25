// La bandeja de las integraciones (Lote 132, P11): lo que entró y no se pudo procesar solo, con el motivo exacto. Por ítem: corregir las celdas tal como llegaron, reprocesar (si ya cuadra nace la orden),
// descartar con nota; arriba: filtro por integración y estatus, descargar los pendientes en CSV y «Reprocesar todo» de una integración. Permiso integrations.inbox.
import { useMemo, useState } from 'react'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Field, IconCheck, IconEdit, IconRotateCcw, IconTrash, Modal, Panel, SelectFilter, Spinner, TextInput, toast, type DataColumn } from '../../kernel/ui'
import { IconClip } from '../../kernel/ui/screenIcons'
import { FormModal } from '../clients/contractUi'
import { useForm } from 'react-hook-form'
import { INBOX_TONE, withCell } from './integrationRules'
import { downloadInboxCsv, useInbox, useInboxItem, useInboxMutations, useIntegrations, type InboxItemDto } from './integrationsApi'

const PAGE = 50
const NO_ROWS: InboxItemDto[] = []

function FixModal({ id, onClose }: { id: number; onClose: () => void }) {
  const t = useT()
  const item = useInboxItem(id)
  const { fix, reprocess } = useInboxMutations()
  const [edited, setEdited] = useState<{ row: number; cells: string[] }[] | null>(null)
  const header = item.data?.header ?? []
  const rows = edited ?? (item.data?.rows ?? []).map((r) => ({ row: r.row ?? 0, cells: [...(r.cells ?? [])] }))
  const save = async (andReprocess: boolean) => {
    try {
      await fix.mutateAsync({ id, rows })
      if (andReprocess) {
        const result = await reprocess.mutateAsync(id)
        if ((result.reprocessed ?? 0) > 0) toast.success(t('integrations.inbox.reprocessed'))
        else toast.error(result.items?.[0]?.reason ?? t('integrations.inbox.stillPending'))
      } else toast.success(t('integrations.inbox.fixed'))
      onClose()
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  return (
    <Modal
      open
      size="lg"
      title={t('integrations.inbox.fixTitle', { ref: item.data?.referenceKey ?? String(id) })}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('integrations.close')}
          </button>
          <button type="button" className="btn" disabled={fix.isPending || reprocess.isPending || !item.data?.hasData} onClick={() => void save(false)}>
            {t('integrations.inbox.save')}
          </button>
          <button type="button" className="btn flow" disabled={fix.isPending || reprocess.isPending || !item.data?.hasData} onClick={() => void save(true)}>
            {t('integrations.inbox.saveAndReprocess')}
          </button>
        </>
      }
    >
      <div className="pb">
        {item.isPending ? (
          <Spinner block />
        ) : (
          <>
            <p className="ferr" role="alert">
              {item.data?.reason}
            </p>
            {!item.data?.hasData ? (
              <p className="help">{t('integrations.inbox.noData')}</p>
            ) : (
              <>
                <p className="help">{t('integrations.inbox.fixHelp')}</p>
                <div className="int-grid-wrap">
                  <table className="lst densetbl int-grid">
                    <thead>
                      <tr>
                        <th>{t('integrations.inbox.row')}</th>
                        {(header.length > 0 ? header : (rows[0]?.cells ?? []).map((_, i) => String(i + 1))).map((h, i) => (
                          <th key={i}>{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((r) => (
                        <tr key={r.row}>
                          <td className="mono">{r.row}</td>
                          {r.cells.map((c, i) => (
                            <td key={i}>
                              <input
                                value={c}
                                aria-label={t('integrations.inbox.cell', { row: r.row, col: header[i] ?? i + 1 })}
                                onChange={(e) => setEdited(withCell(rows, r.row, i, e.target.value))}
                              />
                            </td>
                          ))}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}

function DiscardModal({ item, onClose }: { item: InboxItemDto; onClose: () => void }) {
  const t = useT()
  const { discard } = useInboxMutations()
  const form = useForm<{ note: string }>({ defaultValues: { note: '' } })
  return (
    <FormModal
      id="inbox-discard"
      size="sm"
      title={t('integrations.inbox.discardTitle', { ref: item.referenceKey ?? String(item.id) })}
      open
      onClose={onClose}
      form={form}
      saveLabel={t('integrations.inbox.discard')}
      onSubmit={async (v: { note: string }) => {
        await discard.mutateAsync({ id: item.id ?? 0, note: v.note.trim() })
        toast.success(t('integrations.inbox.discarded'))
        onClose()
      }}
    >
      <p className="help">{item.reason}</p>
      <Field name="note" label={t('integrations.inbox.note')} help={t('integrations.inbox.noteHelp')}>
        <TextInput maxLength={400} />
      </Field>
    </FormModal>
  )
}

function DiscardAllModal({ integration, integrationName, count, onClose }: { integration: string; integrationName: string; count: number; onClose: () => void }) {
  const t = useT()
  const { discardAll } = useInboxMutations()
  const form = useForm<{ note: string }>({ defaultValues: { note: '' } })
  return (
    <FormModal
      id="inbox-discard-all"
      size="sm"
      title={t('integrations.inbox.discardAllTitle', { n: count })}
      open
      onClose={onClose}
      form={form}
      saveLabel={t('integrations.inbox.discardAllConfirm', { n: count })}
      onSubmit={async (v: { note: string }) => {
        const r = await discardAll.mutateAsync({ integration, note: v.note.trim() })
        toast.success(t('integrations.inbox.discardedN', { n: r.discarded ?? 0 }))
        onClose()
      }}
    >
      <p className="help">{t(integration ? 'integrations.inbox.discardAllHelpOne' : 'integrations.inbox.discardAllHelpAll', { n: count, name: integrationName })}</p>
      <Field name="note" label={t('integrations.inbox.note')} help={t('integrations.inbox.noteHelp')}>
        <TextInput maxLength={400} />
      </Field>
    </FormModal>
  )
}

export function InboxTab({ integration, onIntegration }: { integration: string; onIntegration: (publicId: string) => void }) {
  const t = useT()
  const f = useFormat()
  const canInbox = useCan('integrations.inbox')
  const [status, setStatus] = useState('PENDING')
  const [skip, setSkip] = useState(0)
  const integrations = useIntegrations(true)
  const inbox = useInbox(integration, status, skip, PAGE)
  const { reprocess, reprocessAll } = useInboxMutations()
  const [fixing, setFixing] = useState<number | null>(null)
  const [discarding, setDiscarding] = useState<InboxItemDto | null>(null)
  const [discardingAll, setDiscardingAll] = useState(false)
  const items = inbox.data?.items ?? NO_ROWS

  const run = async (p: Promise<{ reprocessed?: number; stillPending?: number; items?: InboxItemDto[] }>) => {
    try {
      const r = await p
      if ((r.reprocessed ?? 0) > 0 && (r.stillPending ?? 0) === 0) toast.success(t('integrations.inbox.reprocessedN', { n: r.reprocessed ?? 0 }))
      else if ((r.reprocessed ?? 0) > 0) toast.success(t('integrations.inbox.reprocessedSome', { done: r.reprocessed ?? 0, still: r.stillPending ?? 0 }))
      else toast.error(r.items?.[0]?.reason ?? t('integrations.inbox.stillPending'))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const integrationOptions = useMemo(() => (integrations.data ?? []).map((i) => ({ value: i.publicId ?? '', label: i.name ?? '' })), [integrations.data])
  const statusOptions = [
    { value: 'PENDING', label: t('integrations.inbox.status.PENDING') },
    { value: 'REPROCESSED', label: t('integrations.inbox.status.REPROCESSED') },
    { value: 'DISCARDED', label: t('integrations.inbox.status.DISCARDED') },
  ]

  const columns = useMemo<DataColumn<InboxItemDto>[]>(
    () => [
      { id: 'status', header: t('integrations.inbox.col.status'), cell: (i) => <Chip tone={INBOX_TONE[i.status ?? 'PENDING']}>{t(`integrations.inbox.status.${i.status}`)}</Chip>, sortValue: (i) => i.status ?? '', card: 'title' },
      { id: 'ref', header: t('integrations.inbox.col.ref'), cell: (i) => <b>{i.referenceKey ?? '—'}</b>, sortValue: (i) => i.referenceKey ?? '' },
      { id: 'integration', header: t('integrations.inbox.col.integration'), cell: (i) => i.integrationName, sortValue: (i) => i.integrationName ?? '' },
      { id: 'file', header: t('integrations.inbox.col.file'), cell: (i) => (i.fileName ? `${i.fileName}${i.sourceRow ? ` · ${t('integrations.inbox.rowN', { n: i.sourceRow })}` : ''}` : '—'), sortValue: (i) => i.fileName ?? '' },
      {
        id: 'reason',
        header: t('integrations.inbox.col.reason'),
        cell: (i) => (
          <>
            <span className={i.status === 'PENDING' ? 'cs-err' : undefined}>{i.reason}</span>
            {i.note && <div className="help">{t('integrations.inbox.noteIs', { note: i.note })}</div>}
          </>
        ),
        exportValue: (i) => i.reason ?? '',
        sortValue: (i) => i.reason ?? '',
      },
      { id: 'date', header: t('integrations.inbox.col.date'), cell: (i) => f.dateTime(i.createdAtUtc), sortValue: (i) => i.createdAtUtc ?? '' },
    ],
    [t, f],
  )

  if (!canInbox) return <EmptyState icon={<IconClip />} title={t('integrations.inbox.noAccess')} />
  const pending = inbox.data?.pending ?? 0
  return (
    <>
      <Panel flush icon={<IconClip />} title={t('integrations.inbox.title')} badge={inbox.isPending ? undefined : pending}>
        <div className="pb">
          <div className="filters" role="group" aria-label={t('ui.filters.label')}>
            <SelectFilter label={t('integrations.inbox.col.integration')} value={integration} onChange={(v) => { onIntegration(v); setSkip(0) }} options={integrationOptions} allLabel={t('integrations.inbox.allIntegrations')} />
            <SelectFilter label={t('integrations.inbox.col.status')} value={status} onChange={(v) => { setStatus(v); setSkip(0) }} options={statusOptions} allLabel={t('integrations.inbox.allStatuses')} />
            <button type="button" className="btn sm" onClick={() => void downloadInboxCsv(integration).catch((e) => toast.error(applyProblemDetails(e).title))}>
              {t('integrations.inbox.download')}
            </button>
            <Can perm="integrations.inbox">
              {integration !== '' && pending > 0 && (
                <button type="button" className="btn sm flow" disabled={reprocessAll.isPending} onClick={() => void run(reprocessAll.mutateAsync(integration))}>
                  <IconRotateCcw /> {t('integrations.inbox.reprocessAll')}
                </button>
              )}
              {pending > 0 && (
                <button type="button" className="btn sm" onClick={() => setDiscardingAll(true)}>
                  <IconTrash /> {t('integrations.inbox.discardAll', { n: pending })}
                </button>
              )}
            </Can>
          </div>
          {inbox.isError ? (
            <p className="ferr" role="alert">
              {applyProblemDetails(inbox.error).title}
            </p>
          ) : inbox.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('integrations.inbox.title')}
              columns={columns}
              rows={items}
              rowKey={(i) => String(i.id)}
              defaultSort={{ id: 'date', desc: true }}
              empty={status === 'PENDING' ? t('integrations.inbox.emptyPending') : t('integrations.inbox.empty')}
              rowActions={[
                { key: 'fix', label: t('integrations.inbox.fix'), icon: <IconEdit />, onClick: (i) => setFixing(i.id ?? 0), visible: (i) => i.status === 'PENDING' && !!i.hasData },
                { key: 'reprocess', label: t('integrations.inbox.reprocess'), icon: <IconCheck />, onClick: (i) => void run(reprocess.mutateAsync(i.id ?? 0)), visible: (i) => i.status === 'PENDING' && !!i.hasData },
                { key: 'discard', label: t('integrations.inbox.discard'), icon: <IconTrash />, onClick: (i) => setDiscarding(i), visible: (i) => i.status === 'PENDING' },
              ]}
            />
          )}
          {inbox.data && (inbox.data.total ?? 0) > PAGE && (
            <div className="int-pager">
              <button type="button" className="btn sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>
                {t('integrations.prev')}
              </button>
              <span className="help">{t('integrations.pageOf', { from: skip + 1, to: Math.min(skip + PAGE, inbox.data.total ?? 0), total: inbox.data.total ?? 0 })}</span>
              <button type="button" className="btn sm" disabled={skip + PAGE >= (inbox.data.total ?? 0)} onClick={() => setSkip(skip + PAGE)}>
                {t('integrations.next')}
              </button>
            </div>
          )}
          <p className="help">{t('integrations.inbox.help')}</p>
        </div>
      </Panel>
      {fixing !== null && <FixModal key={fixing} id={fixing} onClose={() => setFixing(null)} />}
      {discarding && <DiscardModal key={discarding.id} item={discarding} onClose={() => setDiscarding(null)} />}
      {discardingAll && <DiscardAllModal integration={integration} integrationName={integrationOptions.find((o) => o.value === integration)?.label ?? ''} count={pending} onClose={() => setDiscardingAll(false)} />}
    </>
  )
}
