// La bandeja de SALIDAS de archivos y API (Lote 137b, P11): los envíos de las integraciones de salida (SFTP, FTP y API saliente) con su estatus — los «no entregados» agotaron los reintentos (1 min, 5, 30, 2 h, 12 h) —,
// el contenido que se mandó y los botones «Reintentar» y «Descartar». Permiso integrations.inbox.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, IconRotateCcw, IconTrash, Modal, Panel, SelectFilter, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconSwap } from '../../kernel/ui/screenIcons'
import { OUTBOX_TONE } from './integrationRules'
import { useIntegrations, useOutputMutations, useOutputs, type OutputItemDto } from './integrationsApi'

const PAGE = 50
const NO_ROWS: OutputItemDto[] = []

export function OutputsPanel({ integration, onIntegration }: { integration: string; onIntegration: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const [status, setStatus] = useState('UNDELIVERED')
  const [skip, setSkip] = useState(0)
  const [viewing, setViewing] = useState<OutputItemDto | null>(null)
  const integrations = useIntegrations(true)
  const outputs = useOutputs(integration, status, skip, PAGE)
  const { retry, discard } = useOutputMutations()
  const items = outputs.data?.items ?? NO_ROWS

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const integrationOptions = useMemo(() => (integrations.data ?? []).filter((i) => i.direction === 'OUTBOUND').map((i) => ({ value: i.publicId ?? '', label: i.name ?? '' })), [integrations.data])
  const statusOptions = ['UNDELIVERED', 'PENDING', 'SENT', 'DISCARDED'].map((s) => ({ value: s, label: t(`integrations.outputs.status.${s}`) }))

  const columns = useMemo<DataColumn<OutputItemDto>[]>(
    () => [
      { id: 'status', header: t('integrations.outputs.col.status'), cell: (o) => <Chip tone={OUTBOX_TONE[o.status === 'SENT' ? 'DELIVERED' : (o.status ?? 'PENDING')]}>{t(`integrations.outputs.status.${o.status}`)}</Chip>, sortValue: (o) => o.status ?? '', card: 'title' },
      { id: 'integration', header: t('integrations.outputs.col.integration'), cell: (o) => o.integrationName, sortValue: (o) => o.integrationName ?? '' },
      { id: 'dest', header: t('integrations.outputs.col.dest'), cell: (o) => t(`integrations.connector.${o.connectorKind}`), sortValue: (o) => o.connectorKind ?? '' },
      { id: 'file', header: t('integrations.outputs.col.file'), cell: (o) => <span className="mono">{o.fileName}</span>, sortValue: (o) => o.fileName ?? '' },
      { id: 'orders', header: t('integrations.outputs.col.orders'), cell: (o) => o.orderCount, sortValue: (o) => o.orderCount ?? 0 },
      { id: 'attempts', header: t('integrations.outputs.col.attempts'), cell: (o) => o.attemptCount, sortValue: (o) => o.attemptCount ?? 0 },
      {
        id: 'reason',
        header: t('integrations.outputs.col.reason'),
        cell: (o) => (
          <>
            <span className={o.status === 'UNDELIVERED' ? 'cs-err' : undefined}>{o.error ?? '—'}</span>
            {o.lastAttemptUtc && <div className="help">{f.dateTime(o.lastAttemptUtc)}</div>}
          </>
        ),
        exportValue: (o) => o.error ?? '',
        sortValue: (o) => o.error ?? '',
      },
      { id: 'date', header: t('integrations.outputs.col.date'), cell: (o) => f.dateTime(o.createdAtUtc), sortValue: (o) => o.createdAtUtc ?? '' },
    ],
    [t, f],
  )

  return (
    <>
      <Panel flush icon={<IconSwap />} title={t('integrations.outputs.title')} badge={outputs.isPending ? undefined : outputs.data?.undelivered}>
        <div className="pb">
          <div className="filters" role="group" aria-label={t('ui.filters.label')}>
            <SelectFilter label={t('integrations.outputs.col.integration')} value={integration} onChange={(v) => { onIntegration(v); setSkip(0) }} options={integrationOptions} allLabel={t('integrations.inbox.allIntegrations')} />
            <SelectFilter label={t('integrations.outputs.col.status')} value={status} onChange={(v) => { setStatus(v); setSkip(0) }} options={statusOptions} allLabel={t('integrations.inbox.allStatuses')} />
          </div>
          {outputs.isError ? (
            <p className="ferr" role="alert">{applyProblemDetails(outputs.error).title}</p>
          ) : outputs.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('integrations.outputs.title')}
              columns={columns}
              rows={items}
              rowKey={(o) => String(o.id)}
              defaultSort={{ id: 'date', desc: true }}
              empty={status === 'UNDELIVERED' ? t('integrations.outputs.emptyUndelivered') : t('integrations.outputs.empty')}
              rowActions={[
                { key: 'view', label: t('integrations.outputs.view'), onClick: (o) => setViewing(o) },
                { key: 'retry', label: t('integrations.outbox.retry'), icon: <IconRotateCcw />, visible: (o) => o.status === 'UNDELIVERED', onClick: (o) => void run(retry.mutateAsync(o.id ?? 0), t('integrations.outputs.retried')) },
                { key: 'discard', label: t('integrations.outbox.discard'), icon: <IconTrash />, visible: (o) => o.status === 'UNDELIVERED' || o.status === 'PENDING', onClick: (o) => void run(discard.mutateAsync(o.id ?? 0), t('integrations.outputs.discarded')) },
              ]}
            />
          )}
          {outputs.data && (outputs.data.total ?? 0) > PAGE && (
            <div className="int-pager">
              <button type="button" className="btn sm" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>{t('integrations.prev')}</button>
              <span className="help">{t('integrations.pageOf', { from: skip + 1, to: Math.min(skip + PAGE, outputs.data.total ?? 0), total: outputs.data.total ?? 0 })}</span>
              <button type="button" className="btn sm" disabled={skip + PAGE >= (outputs.data.total ?? 0)} onClick={() => setSkip(skip + PAGE)}>{t('integrations.next')}</button>
            </div>
          )}
          <p className="help">{t('integrations.outputs.help')}</p>
        </div>
      </Panel>
      {viewing && (
        <Modal open size="lg" title={t('integrations.outputs.viewTitle', { file: viewing.fileName ?? '' })} onClose={() => setViewing(null)} footer={<button type="button" className="btn" onClick={() => setViewing(null)}>{t('integrations.close')}</button>}>
          <div className="pb">
            <p className="help">{t('integrations.outputs.viewInfo', { format: viewing.format ?? '', orders: viewing.orderCount ?? 0, bytes: viewing.sizeBytes ?? 0 })}</p>
            {viewing.error && <p className="ferr" role="alert">{viewing.error}</p>}
            <pre className="int-json" data-testid="output-content">{viewing.preview}</pre>
          </div>
        </Modal>
      )}
    </>
  )
}
