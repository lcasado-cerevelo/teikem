// Llaves de API de una integración (Lote 135, P11): crear (el secreto se muestra UNA sola vez), cambiar el límite de uso, revocar y ver la bitácora de cada llave.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { useApiKeyLog, useApiKeyMutations, useApiKeys, type ApiKeyDto, type ApiLogDto, type IntegrationDto } from './integrationsApi'

function KeyLog({ publicId, keyId }: { publicId: string; keyId: string }) {
  const t = useT()
  const f = useFormat()
  const log = useApiKeyLog(publicId, keyId)
  const columns = useMemo<DataColumn<ApiLogDto>[]>(
    () => [
      { id: 'at', header: t('integrations.api.log.at'), cell: (r) => f.dateTime(r.atUtc), sortValue: (r) => r.atUtc ?? '', card: 'title' },
      { id: 'method', header: t('integrations.api.log.method'), cell: (r) => r.method, sortValue: (r) => r.method ?? '' },
      { id: 'path', header: t('integrations.api.log.path'), cell: (r) => <span className="mono">{r.path}</span>, sortValue: (r) => r.path ?? '' },
      { id: 'status', header: t('integrations.api.log.status'), cell: (r) => <Chip tone={(r.statusCode ?? 0) < 400 ? 'deliv' : 'warn'}>{r.statusCode}</Chip>, sortValue: (r) => r.statusCode ?? 0 },
      { id: 'ms', header: t('integrations.api.log.ms'), cell: (r) => r.durationMs, sortValue: (r) => r.durationMs ?? 0 },
    ],
    [t, f],
  )
  if (log.isError) return <p className="ferr" role="alert">{applyProblemDetails(log.error).title}</p>
  if (log.isPending) return <Spinner block />
  return <DataTable label={t('integrations.api.log.title')} columns={columns} rows={log.data} rowKey={(r) => `${r.atUtc}${r.path}`} defaultSort={{ id: 'at', desc: true }} empty={t('integrations.api.log.empty')} />
}

export function ApiKeysModal({ integration, onClose }: { integration: IntegrationDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const publicId = integration.publicId ?? ''
  const keys = useApiKeys(publicId)
  const { create, update, revoke } = useApiKeyMutations(publicId)
  const [label, setLabel] = useState('')
  const [limit, setLimit] = useState('')
  const [shown, setShown] = useState<{ keyId: string; secret: string } | null>(null)
  const [logFor, setLogFor] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const fail = (e: unknown) => setError(applyProblemDetails(e).title ?? '')
  const add = async () => {
    setError(null)
    if (!label.trim()) return setError(t('integrations.api.errors.label'))
    const n = limit.trim() === '' ? null : Number(limit)
    if (n !== null && (!Number.isInteger(n) || n < 1 || n > 6000)) return setError(t('integrations.api.errors.limit'))
    try {
      const r = await create.mutateAsync({ label: label.trim(), rateLimitPerMinute: n })
      setShown({ keyId: r.key?.keyId ?? '', secret: r.secret ?? '' })
      setLabel('')
      setLimit('')
    } catch (e) {
      fail(e)
    }
  }
  const copy = async (text: string) => {
    try {
      if (!navigator.clipboard) throw new Error('clipboard')
      await navigator.clipboard.writeText(text)
      toast.success(t('integrations.api.copied'))
    } catch {
      toast.error(t('integrations.api.copyFailed'))
    }
  }
  const changeLimit = async (k: ApiKeyDto) => {
    const raw = window.prompt(t('integrations.api.limitPrompt'), String(k.rateLimitPerMinute ?? 60))
    if (raw === null) return
    const n = Number(raw)
    if (!Number.isInteger(n) || n < 1 || n > 6000) return toast.error(t('integrations.api.errors.limit'))
    try {
      await update.mutateAsync({ keyId: k.keyId!, rateLimitPerMinute: n })
      toast.success(t('integrations.api.limitSaved'))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }
  const doRevoke = async (k: ApiKeyDto) => {
    if (!window.confirm(t('integrations.api.revokeConfirm', { key: k.keyId ?? '' }))) return
    try {
      await revoke.mutateAsync(k.keyId!)
      toast.success(t('integrations.api.revoked'))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<ApiKeyDto>[]>(
    () => [
      { id: 'label', header: t('integrations.api.col.label'), cell: (k) => <b>{k.label}</b>, sortValue: (k) => k.label ?? '', card: 'title' },
      { id: 'key', header: t('integrations.api.col.key'), cell: (k) => <span className="mono">{k.keyId}</span>, sortValue: (k) => k.keyId ?? '' },
      { id: 'secret', header: t('integrations.api.col.secret'), cell: (k) => <span className="mono">…{k.secretLast4}</span>, sortValue: (k) => k.secretLast4 ?? '' },
      { id: 'limit', header: t('integrations.api.col.limit'), cell: (k) => k.rateLimitPerMinute, sortValue: (k) => k.rateLimitPerMinute ?? 0 },
      { id: 'last', header: t('integrations.api.col.last'), cell: (k) => (k.lastUsedUtc ? f.dateTime(k.lastUsedUtc) : t('integrations.api.never')), sortValue: (k) => k.lastUsedUtc ?? '' },
      { id: 'state', header: t('integrations.api.col.state'), cell: (k) => <Chip tone={k.isActive ? 'deliv' : 'warn'}>{t(k.isActive ? 'integrations.api.active' : 'integrations.api.revokedState')}</Chip>, sortValue: (k) => (k.isActive ? 1 : 0) },
    ],
    [t, f],
  )

  return (
    <Modal open size="lg" title={t('integrations.api.title', { name: integration.name ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('integrations.close')}</button>}>
      <div className="pb">
        <p className="help">{t('integrations.api.help')}</p>
        {shown && (
          <div className="int-secret" role="status">
            <p><b>{t('integrations.api.secretOnce')}</b></p>
            <p>
              {t('integrations.api.keyIs')} <span className="mono">{shown.keyId}</span>{' '}
              <button type="button" className="btn" onClick={() => void copy(shown.keyId)}>{t('integrations.api.copy')}</button>
            </p>
            <p>
              {t('integrations.api.secretIs')} <span className="mono" data-testid="api-secret">{shown.secret}</span>{' '}
              <button type="button" className="btn" onClick={() => void copy(shown.secret)}>{t('integrations.api.copy')}</button>
            </p>
            <button type="button" className="btn" onClick={() => setShown(null)}>{t('integrations.api.saved')}</button>
          </div>
        )}
        <div className="int-newkey">
          <label className="fld">
            <span>{t('integrations.api.newLabel')}</span>
            <input value={label} maxLength={80} onChange={(e) => setLabel(e.target.value)} placeholder={t('integrations.api.newLabelPlaceholder')} />
          </label>
          <label className="fld">
            <span>{t('integrations.api.newLimit')}</span>
            <input inputMode="numeric" value={limit} maxLength={4} onChange={(e) => setLimit(e.target.value)} placeholder="60" />
          </label>
        </div>
        {error && <p className="ferr" role="alert">{error}</p>}
        <button type="button" className="btn flow" disabled={create.isPending} onClick={() => void add()}>{t('integrations.api.create')}</button>

        {keys.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(keys.error).title}</p>
        ) : keys.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('integrations.api.title', { name: integration.name ?? '' })}
            columns={columns}
            rows={keys.data}
            rowKey={(k) => k.keyId ?? String(k.id)}
            defaultSort={{ id: 'label', desc: false }}
            empty={t('integrations.api.empty')}
            rowActions={[
              { key: 'log', label: t('integrations.api.log.open'), onClick: (k) => setLogFor(logFor === k.keyId ? null : (k.keyId ?? null)) },
              { key: 'limit', label: t('integrations.api.changeLimit'), onClick: (k) => void changeLimit(k), visible: (k) => !!k.isActive },
              { key: 'revoke', label: t('integrations.api.revoke'), onClick: (k) => void doRevoke(k), visible: (k) => !!k.isActive },
            ]}
          />
        )}
        {logFor && (
          <div>
            <h3>{t('integrations.api.log.titleFor', { key: logFor })}</h3>
            <KeyLog publicId={publicId} keyId={logFor} />
          </div>
        )}
      </div>
    </Modal>
  )
}
