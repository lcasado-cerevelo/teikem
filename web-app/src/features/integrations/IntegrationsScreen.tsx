// Integraciones (Lote 132, P11): de dónde (o a dónde) van los datos, con qué plantilla y cuándo. Pestañas «Integraciones» y «Bandeja» (con el contador de pendientes). Por integración: interruptor,
// cliente, conector, horario, último resultado y pendientes en la bandeja; acciones «Correr ahora», «Subir archivo», «Historial», «Editar». Alta con integrations.create (+ el permiso de la familia de cada alcance).
// Módulo INTEGRATIONS (la ruta lo exige). Nunca se borra: se apaga.
import { useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, IconEdit, IconPlay, IconPlus, Modal, Panel, Spinner, Tabs, toast, type DataColumn } from '../../kernel/ui'
import { IconClock, IconSwap } from '../../kernel/ui/screenIcons'
import { ApiKeysModal } from './ApiKeysModal'
import { InboxTab } from './InboxTab'
import { OutboxTab } from './OutboxTab'
import { OutputPreviewModal } from './OutputPreviewModal'
import { OutputsPanel } from './OutputsPanel'
import { WebhooksModal } from './WebhooksModal'
import { IntegrationFormModal } from './IntegrationFormModal'
import { FILE_TONE, RUN_TONE, scheduleText } from './integrationRules'
import { useIntegrationMutations, useIntegrationRuns, useIntegrations, useOutbox, useOutputs, type IntegrationDto, type IntegrationRunDto } from './integrationsApi'
import './integrations.css'

const NO_ROWS: IntegrationDto[] = []
type Tab = 'integrations' | 'inbox' | 'outbox'

function RunSummary({ run }: { run: IntegrationRunDto }) {
  const t = useT()
  return (
    <>
      <Chip tone={RUN_TONE[run.status ?? 'NEVER']}>{t(`integrations.status.${run.status}`)}</Chip> <span className="help">{run.message}</span>
      {(run.files ?? []).length > 0 && (
        <ul className="int-files">
          {(run.files ?? []).map((f) => (
            <li key={f.id}>
              <Chip tone={FILE_TONE[f.result ?? 'PROCESSED']}>{t(`integrations.file.${f.result}`)}</Chip> {f.fileName} <span className="help">{f.message}</span>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function HistoryModal({ integration, onClose }: { integration: IntegrationDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const runs = useIntegrationRuns(integration.publicId ?? null)
  const columns = useMemo<DataColumn<IntegrationRunDto>[]>(
    () => [
      { id: 'started', header: t('integrations.history.started'), cell: (r) => f.dateTime(r.startedUtc), sortValue: (r) => r.startedUtc ?? '', card: 'title' },
      { id: 'by', header: t('integrations.history.by'), cell: (r) => (r.triggeredByName ? `${t(`integrations.trigger.${r.triggeredBy}`)} · ${r.triggeredByName}` : t(`integrations.trigger.${r.triggeredBy}`)), sortValue: (r) => r.triggeredBy ?? '' },
      { id: 'files', header: t('integrations.history.files'), cell: (r) => r.filesRead, sortValue: (r) => r.filesRead ?? 0 },
      { id: 'created', header: t('integrations.history.created'), cell: (r) => r.ordersCreated, sortValue: (r) => r.ordersCreated ?? 0 },
      { id: 'rejected', header: t('integrations.history.rejected'), cell: (r) => r.rejected, sortValue: (r) => r.rejected ?? 0 },
      { id: 'result', header: t('integrations.history.result'), cell: (r) => <RunSummary run={r} />, exportValue: (r) => `${r.status ?? ''} ${r.message ?? ''}`.trim(), sortValue: (r) => r.status ?? '' },
    ],
    [t, f],
  )
  return (
    <Modal open size="lg" title={t('integrations.history.title', { name: integration.name ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('integrations.close')}</button>}>
      <div className="pb">
        {runs.isError ? (
          <p className="ferr" role="alert">
            {applyProblemDetails(runs.error).title}
          </p>
        ) : runs.isPending ? (
          <Spinner block />
        ) : (
          <DataTable label={t('integrations.history.title', { name: integration.name ?? '' })} columns={columns} rows={runs.data} rowKey={(r) => String(r.id)} defaultSort={{ id: 'started', desc: true }} empty={t('integrations.history.empty')} />
        )}
      </div>
    </Modal>
  )
}

export default function IntegrationsScreen() {
  const t = useT()
  const f = useFormat()
  const [params, setParams] = useSearchParams()
  const canManage = useCan('integrations.manage')
  const canInbox = useCan('integrations.inbox')
  const [showInactive, setShowInactive] = useState(false)
  const list = useIntegrations(showInactive)
  const { run, sendAll, setActive, upload, testConnection } = useIntegrationMutations()
  const [editing, setEditing] = useState<IntegrationDto | 'new' | null>(null)
  const [history, setHistory] = useState<IntegrationDto | null>(null)
  const [keysFor, setKeysFor] = useState<IntegrationDto | null>(null)
  const [hooksFor, setHooksFor] = useState<IntegrationDto | null>(null)
  const [previewFor, setPreviewFor] = useState<IntegrationDto | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const [uploadFor, setUploadFor] = useState<IntegrationDto | null>(null)
  const wanted = params.get('tab')
  const tab: Tab = wanted === 'inbox' && canInbox ? 'inbox' : wanted === 'outbox' && canInbox ? 'outbox' : 'integrations'
  const undelivered = (useOutbox('', 'UNDELIVERED', 0, 1).data?.undelivered ?? 0) + (useOutputs('', 'UNDELIVERED', 0, 1).data?.undelivered ?? 0)
  const inboxIntegration = params.get('integration') ?? ''
  const rows = list.data ?? NO_ROWS
  const pendingTotal = rows.reduce((n, i) => n + (i.pendingInbox ?? 0), 0)

  const go = (next: Tab) => {
    const p = new URLSearchParams(params)
    if (next === 'inbox') p.set('tab', 'inbox')
    else p.delete('tab')
    setParams(p, { replace: true })
  }
  const setIntegrationFilter = (publicId: string) => {
    const p = new URLSearchParams(params)
    if (publicId) p.set('integration', publicId)
    else p.delete('integration')
    setParams(p, { replace: true })
  }
  const act = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<IntegrationDto>[]>(
    () => [
      {
        id: 'name',
        header: t('integrations.col.name'),
        cell: (i) => (
          <>
            <b>{i.name}</b>
            {i.description && <div className="help">{i.description}</div>}
            {canManage && i.connectorKind === 'API' && (
              <div>
                <button type="button" className="btn sm" onClick={() => setKeysFor(i)}>
                  {t('integrations.api.open')}
                </button>
              </div>
            )}
          </>
        ),
        exportValue: (i) => i.name ?? '',
        sortValue: (i) => i.name ?? '',
        card: 'title',
      },
      {
        id: 'active',
        header: t('integrations.col.active'),
        cell: (i) => (
          <label className="sw">
            <input
              type="checkbox"
              role="switch"
              aria-label={t('integrations.col.activeFor', { name: i.name ?? '' })}
              checked={!!i.isActive}
              disabled={!canManage || setActive.isPending}
              onChange={(e) => void act(setActive.mutateAsync({ publicId: i.publicId!, active: e.target.checked }), t(e.target.checked ? 'integrations.on' : 'integrations.off', { name: i.name ?? '' }))}
            />
            <span className="tk" aria-hidden="true" />
          </label>
        ),
        exportValue: (i) => (i.isActive ? t('integrations.yes') : t('integrations.no')),
        sortValue: (i) => (i.isActive ? 1 : 0),
      },
      { id: 'client', header: t('integrations.col.client'), cell: (i) => i.clientName ?? '—', sortValue: (i) => i.clientName ?? '' },
      {
        id: 'connector',
        header: t('integrations.col.connector'),
        cell: (i) => (
          <>
            {i.direction === 'OUTBOUND' && <Chip tone="route">{t('integrations.direction.OUTBOUND')}</Chip>} {t(`integrations.connector.${i.connectorKind}`)}
            {i.folder && <div className="help mono">{i.folder}</div>}
          </>
        ),
        exportValue: (i) => t(`integrations.connector.${i.connectorKind}`),
        sortValue: (i) => i.connectorKind ?? '',
      },
      { id: 'schedule', header: t('integrations.col.schedule'), cell: (i) => scheduleText(i, t), sortValue: (i) => scheduleText(i, t) },
      {
        id: 'last',
        header: t('integrations.col.last'),
        cell: (i) => (
          <>
            <Chip tone={RUN_TONE[i.lastStatus ?? 'NEVER']}>{t(`integrations.status.${i.lastStatus}`)}</Chip>
            {i.lastFinishedUtc && <span className="help"> {f.dateTime(i.lastFinishedUtc)}</span>}
            {i.lastMessage && <div className="help">{i.lastMessage}</div>}
          </>
        ),
        exportValue: (i) => `${t(`integrations.status.${i.lastStatus}`)} ${i.lastMessage ?? ''}`.trim(),
        sortValue: (i) => i.lastFinishedUtc ?? '',
      },
      {
        id: 'inbox',
        header: t('integrations.col.inbox'),
        cell: (i) =>
          (i.pendingInbox ?? 0) > 0 ? (
            <button type="button" className="linkbtn" onClick={() => { const p = new URLSearchParams(params); p.set('tab', 'inbox'); p.set('integration', i.publicId ?? ''); setParams(p, { replace: true }) }}>
              <Chip tone="warn">{i.pendingInbox}</Chip>
            </button>
          ) : (
            '—'
          ),
        exportValue: (i) => String(i.pendingInbox ?? 0),
        sortValue: (i) => i.pendingInbox ?? 0,
      },
    ],
    // act cambia con cada render pero solo usa mutaciones estables
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, canManage, setActive.isPending, params],
  )

  return (
    <div className="wrap int-screen">
      <div className="head">
        <div>
          <h1>{t('integrations.title')}</h1>
          <p>{t('integrations.subtitle')}</p>
        </div>
        <div className="act">
          <Link className="btn" to="/system/templates">
            {t('integrations.templates')}
          </Link>
          <Can perm="integrations.create">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              <IconPlus /> {t('integrations.new')}
            </button>
          </Can>
        </div>
      </div>

      <div className="int-tabs">
        <Tabs
          tabs={[
            { key: 'integrations' as const, label: t('integrations.tabs.integrations') },
            ...(canInbox ? [{ key: 'inbox' as const, label: `${t('integrations.tabs.inbox')}${pendingTotal > 0 ? ` (${pendingTotal})` : ''}` }] : []),
            ...(canInbox ? [{ key: 'outbox' as const, label: `${t('integrations.tabs.outbox')}${undelivered > 0 ? ` (${undelivered})` : ''}` }] : []),
          ]}
          value={tab}
          onChange={go}
          label={t('integrations.tabs.label')}
        />
      </div>

      {tab === 'inbox' ? (
        <InboxTab integration={inboxIntegration} onIntegration={setIntegrationFilter} />
      ) : tab === 'outbox' ? (
        <>
          <OutboxTab integration={inboxIntegration} onIntegration={setIntegrationFilter} />
          <OutputsPanel integration={inboxIntegration} onIntegration={setIntegrationFilter} />
        </>
      ) : (
        <Panel flush icon={<IconSwap />} title={t('integrations.panel')} badge={list.isPending ? undefined : rows.length}>
          <div className="pb">
            <label className="sw int-inactive">
              <input type="checkbox" role="switch" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('integrations.showInactive')}</span>
            </label>
            {list.isError ? (
              <p className="ferr" role="alert">
                {applyProblemDetails(list.error).title}
              </p>
            ) : list.isPending ? (
              <Spinner block />
            ) : (
              <DataTable
                label={t('integrations.panel')}
                columns={columns}
                rows={rows}
                rowKey={(i) => i.publicId ?? String(i.id)}
                defaultSort={{ id: 'name', desc: false }}
                empty={t('integrations.empty')}
                rowActions={[
                  {
                    key: 'run',
                    label: t('integrations.runNow'),
                    icon: <IconPlay />,
                    onClick: (i) => void act(run.mutateAsync(i.publicId!), t('integrations.ran', { name: i.name ?? '' })),
                    visible: (i) => canManage && !!i.isActive && i.direction !== 'OUTBOUND' && (i.connectorKind === 'FOLDER' || i.connectorKind === 'EMAIL' || i.connectorKind === 'SFTP' || i.connectorKind === 'FTP') && i.lastStatus !== 'RUNNING',
                  },
                  {
                    key: 'send',
                    label: t('integrations.out.sendNow'),
                    icon: <IconPlay />,
                    onClick: (i) => void act(run.mutateAsync(i.publicId!), t('integrations.ran', { name: i.name ?? '' })),
                    visible: (i) => canManage && !!i.isActive && i.direction === 'OUTBOUND' && i.lastStatus !== 'RUNNING',
                  },
                  {
                    // 2026-10-21: salida de un catálogo (productos): manda TODO el catálogo ahora (lo que cambia sale solo)
                    key: 'sendall',
                    label: t('integrations.out.sendAll'),
                    icon: <IconPlay />,
                    onClick: (i) => void act(sendAll.mutateAsync(i.publicId!), t('integrations.ran', { name: i.name ?? '' })),
                    visible: (i) => canManage && !!i.isActive && !!i.entityKind && i.lastStatus !== 'RUNNING',
                  },
                  {
                    key: 'upload',
                    label: t('integrations.upload'),
                    icon: <IconPlus />,
                    onClick: (i) => {
                      setUploadFor(i)
                      fileRef.current?.click()
                    },
                    visible: (i) => canManage && !!i.isActive && i.connectorKind !== 'API' && i.direction !== 'OUTBOUND',
                  },
                  { key: 'outpreview', label: t('integrations.out.preview'), icon: <IconPlay />, onClick: (i) => setPreviewFor(i), visible: (i) => canManage && i.direction === 'OUTBOUND' },
                  { key: 'keys', label: t('integrations.api.open'), icon: <IconPlus />, onClick: (i) => setKeysFor(i), visible: (i) => canManage && i.connectorKind === 'API' },
                  { key: 'hooks', label: t('integrations.hooks.open'), icon: <IconPlus />, onClick: (i) => setHooksFor(i), visible: (i) => canManage && i.connectorKind === 'API' && (i.scopes ?? []).includes('webhooks:manage') },
                  {
                    key: 'test',
                    label: t('integrations.testConnection'),
                    icon: <IconPlay />,
                    onClick: (i) =>
                      void testConnection
                        .mutateAsync(i.publicId!)
                        .then((r) => (r.ok ? toast.success(r.message ?? '') : toast.error(r.message ?? '')))
                        .catch((e) => toast.error(applyProblemDetails(e).title)),
                    visible: (i) => canManage && (i.connectorKind === 'EMAIL' || i.connectorKind === 'SFTP' || i.connectorKind === 'FTP' || i.connectorKind === 'HTTP'),
                  },
                  { key: 'history', label: t('integrations.history.open'), icon: <IconClock />, onClick: (i) => setHistory(i) },
                  { key: 'edit', label: t('integrations.edit'), icon: <IconEdit />, onClick: (i) => setEditing(i), visible: () => canManage },
                ]}
              />
            )}
            <p className="help">{t('integrations.help')}</p>
          </div>
        </Panel>
      )}

      <input
        ref={fileRef}
        type="file"
        accept=".csv,.txt,.xlsx,.json,.xml,.eml,text/csv,text/plain,application/json,application/xml,text/xml,message/rfc822,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        className="int-file"
        aria-label={t('integrations.uploadFile')}
        onChange={(e) => {
          const file = e.target.files?.[0]
          const target = uploadFor
          e.target.value = ''
          if (file && target) void act(upload.mutateAsync({ publicId: target.publicId!, file }), t('integrations.uploaded', { name: target.name ?? '' }))
          setUploadFor(null)
        }}
      />
      {editing && <IntegrationFormModal key={editing === 'new' ? 'new' : editing.publicId} integration={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {previewFor && <OutputPreviewModal key={previewFor.publicId} integration={previewFor} onClose={() => setPreviewFor(null)} />}
      {hooksFor && <WebhooksModal key={hooksFor.publicId} integration={hooksFor} onClose={() => setHooksFor(null)} />}
      {keysFor && <ApiKeysModal key={keysFor.publicId} integration={keysFor} onClose={() => setKeysFor(null)} />}
      {history && <HistoryModal key={history.publicId} integration={history} onClose={() => setHistory(null)} />}
    </div>
  )
}
