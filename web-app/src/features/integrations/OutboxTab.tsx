// La bandeja de SALIDAS de las integraciones (Lote 136, P11): los avisos por webhook que no se pudieron entregar tras todos los reintentos (1 min, 5, 30, 2 h, 12 h), con el motivo exacto, el cuerpo que se
// iba a mandar y los botones «Reintentar» y «Descartar». Permiso integrations.inbox. Si el webhook está suspendido hay que reactivarlo primero (Integraciones → Webhooks).
import { useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, IconRotateCcw, IconTrash, Modal, Panel, SelectFilter, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconSwap } from '../../kernel/ui/screenIcons'
import { OUTBOX_TONE } from './integrationRules'
import { useIntegrations, useOutbox, useOutboxMutations, type WebhookDeliveryDto } from './integrationsApi'

const PAGE = 50
const NO_ROWS: WebhookDeliveryDto[] = []

function pretty(json: string | null | undefined) {
  try {
    return JSON.stringify(JSON.parse(json ?? ''), null, 2)
  } catch {
    return json ?? ''
  }
}

export function OutboxTab({ integration, onIntegration }: { integration: string; onIntegration: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const canInbox = useCan('integrations.inbox')
  const [status, setStatus] = useState('UNDELIVERED')
  const [skip, setSkip] = useState(0)
  const [viewing, setViewing] = useState<WebhookDeliveryDto | null>(null)
  const integrations = useIntegrations(true)
  const outbox = useOutbox(integration, status, skip, PAGE)
  const { retry, discard } = useOutboxMutations()
  const items = outbox.data?.items ?? NO_ROWS

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const integrationOptions = useMemo(() => (integrations.data ?? []).filter((i) => i.connectorKind === 'API').map((i) => ({ value: i.publicId ?? '', label: i.name ?? '' })), [integrations.data])
  const statusOptions = ['UNDELIVERED', 'PENDING', 'DELIVERED', 'DISCARDED'].map((s) => ({ value: s, label: t(`integrations.outbox.status.${s}`) }))

  const columns = useMemo<DataColumn<WebhookDeliveryDto>[]>(
    () => [
      { id: 'status', header: t('integrations.outbox.col.status'), cell: (d) => <Chip tone={OUTBOX_TONE[d.status ?? 'PENDING']}>{t(`integrations.outbox.status.${d.status}`)}</Chip>, sortValue: (d) => d.status ?? '', card: 'title' },
      { id: 'integration', header: t('integrations.outbox.col.integration'), cell: (d) => d.integrationName, sortValue: (d) => d.integrationName ?? '' },
      { id: 'event', header: t('integrations.outbox.col.event'), cell: (d) => <span className="mono">{d.eventType}</span>, sortValue: (d) => d.eventType ?? '' },
      { id: 'url', header: t('integrations.outbox.col.url'), cell: (d) => <span className="mono">{d.url}</span>, sortValue: (d) => d.url ?? '' },
      { id: 'attempts', header: t('integrations.outbox.col.attempts'), cell: (d) => d.attemptCount, sortValue: (d) => d.attemptCount ?? 0 },
      {
        id: 'reason',
        header: t('integrations.outbox.col.reason'),
        cell: (d) => (
          <>
            <span className={d.status === 'UNDELIVERED' ? 'cs-err' : undefined}>{d.error ?? '—'}</span>
            {d.lastAttemptUtc && <div className="help">{f.dateTime(d.lastAttemptUtc)}</div>}
          </>
        ),
        exportValue: (d) => d.error ?? '',
        sortValue: (d) => d.error ?? '',
      },
      { id: 'date', header: t('integrations.outbox.col.date'), cell: (d) => f.dateTime(d.createdAtUtc), sortValue: (d) => d.createdAtUtc ?? '' },
    ],
    [t, f],
  )

  if (!canInbox) return <EmptyState icon={<IconSwap />} title={t('integrations.inbox.noAccess')} />
  return (
    <>
      <Panel flush icon={<IconSwap />} title={t('integrations.outbox.title')} badge={outbox.isPending ? undefined : outbox.data?.undelivered}>
        <div className="pb">
          <div className="filters" role="group" aria-label={t('ui.filters.label')}>
            <SelectFilter label={t('integrations.outbox.col.integration')} value={integration} onChange={(v) => { onIntegration(v); setSkip(0) }} options={integrationOptions} allLabel={t('integrations.inbox.allIntegrations')} />
            <SelectFilter label={t('integrations.outbox.col.status')} value={status} onChange={(v) => { setStatus(v); setSkip(0) }} options={statusOptions} allLabel={t('integrations.inbox.allStatuses')} />
          </div>
          {outbox.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(outbox.error).title}</p>
          ) : outbox.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('integrations.outbox.title')}
              columns={columns}
              rows={items}
              rowKey={(d) => String(d.id)}
              defaultSort={{ id: 'date', desc: true }}
              empty={status === 'UNDELIVERED' ? t('integrations.outbox.emptyUndelivered') : t('integrations.outbox.empty')}
              rowActions={[
                { key: 'view', label: t('integrations.outbox.view'), onClick: (d) => setViewing(d) },
                { key: 'retry', label: t('integrations.outbox.retry'), icon: <IconRotateCcw />, visible: (d) => d.status === 'UNDELIVERED', onClick: (d) => void run(retry.mutateAsync(d.id ?? 0), t('integrations.outbox.retried')) },
                { key: 'discard', label: t('integrations.outbox.discard'), icon: <IconTrash />, visible: (d) => d.status === 'UNDELIVERED' || d.status === 'PENDING', onClick: (d) => void run(discard.mutateAsync(d.id ?? 0), t('integrations.outbox.discarded')) },
              ]}
            />
          )}
          {outbox.data && (outbox.data.total ?? 0) > PAGE && (
            <div className="int-pager">
              <button type="button" className="btn sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>{t('integrations.prev')}</button>
              <span className="help">{t('integrations.pageOf', { from: skip + 1, to: Math.min(skip + PAGE, outbox.data.total ?? 0), total: outbox.data.total ?? 0 })}</span>
              <button type="button" className="btn sm" disabled={skip + PAGE >= (outbox.data.total ?? 0)} onClick={() => setSkip(skip + PAGE)}>{t('integrations.next')}</button>
            </div>
          )}
          <p className="help">{t('integrations.outbox.help')}</p>
        </div>
      </Panel>
      {viewing && (
        <Modal open size="lg" title={t('integrations.outbox.viewTitle', { event: viewing.eventType ?? '' })} onClose={() => setViewing(null)} footer={<button type="button" className="btn" onClick={() => setViewing(null)}>{t('integrations.close')}</button>}>
          <div className="pb">
            <p className="help"><span className="mono">{viewing.url}</span></p>
            {viewing.error && <p className="ferr" role="alert">{viewing.error}</p>}
            <pre className="int-json" data-testid="outbox-payload">{pretty(viewing.payloadJson)}</pre>
          </div>
        </Modal>
      )}
    </>
  )
}
