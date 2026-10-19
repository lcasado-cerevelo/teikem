// Webhooks de una integración de tipo API (Lote 136, P11): a qué URL del cliente avisa Teikem cuando pasa algo con lo suyo. Crear/editar (URL y eventos), probar, rotar el secreto de firma (se muestra una
// sola vez), reactivar uno suspendido y quitar. Las salidas no entregadas se atienden en la pestaña «Salidas».
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, Modal, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { WEBHOOK_EVENTS, eventKey, webhookState } from './integrationRules'
import { useWebhookMutations, useWebhooks, type IntegrationDto, type WebhookDto } from './integrationsApi'

export function WebhooksModal({ integration, onClose }: { integration: IntegrationDto; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const publicId = integration.publicId ?? ''
  const hooks = useWebhooks(publicId)
  const { create, update, remove, resume, rotate, test } = useWebhookMutations(publicId)
  const [url, setUrl] = useState('')
  const [events, setEvents] = useState<string[]>([...WEBHOOK_EVENTS])
  const [editing, setEditing] = useState<WebhookDto | null>(null)
  const [shown, setShown] = useState<{ url: string; secret: string } | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reset = () => {
    setEditing(null)
    setUrl('')
    setEvents([...WEBHOOK_EVENTS])
  }
  const save = async () => {
    setError(null)
    try {
      if (editing) {
        await update.mutateAsync({ id: editing.publicId!, url: url.trim(), events })
        toast.success(t('integrations.hooks.saved'))
      } else {
        const r = await create.mutateAsync({ url: url.trim(), events })
        setShown({ url: r.webhook?.url ?? '', secret: r.secret ?? '' })
      }
      reset()
    } catch (e) {
      setError(applyProblemDetails(e).title ?? '')
    }
  }
  const act = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
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
  const toggleEvent = (e: string, on: boolean) => setEvents(on ? [...events.filter((x) => x !== e), e] : events.filter((x) => x !== e))

  const columns = useMemo<DataColumn<WebhookDto>[]>(
    () => [
      { id: 'url', header: t('integrations.hooks.col.url'), cell: (w) => (
          <>
            <span className="mono">{w.url}</span>
            <div className="help">{t('integrations.hooks.secretEnds', { last: w.secretLast4 ?? '' })}</div>
          </>
        ),
        exportValue: (w) => w.url ?? '',
        sortValue: (w) => w.url ?? '',
        card: 'title',
      },
      {
        id: 'events',
        header: t('integrations.hooks.col.events'),
        cell: (w) => (w.events ?? []).map((e) => <Chip key={e} tone="neutral">{t(`integrations.hooks.event.${eventKey(e)}`)}</Chip>),
        exportValue: (w) => (w.events ?? []).join(' '),
        sortValue: (w) => (w.events ?? []).join(' '),
      },
      {
        id: 'state',
        header: t('integrations.hooks.col.state'),
        cell: (w) => {
          const s = webhookState(w)
          return <Chip tone={s === 'ACTIVE' ? 'deliv' : s === 'SUSPENDED' ? 'fail' : 'neutral'}>{t(`integrations.hooks.state.${s}`)}</Chip>
        },
        exportValue: (w) => webhookState(w),
        sortValue: (w) => webhookState(w),
      },
      { id: 'failures', header: t('integrations.hooks.col.failures'), cell: (w) => w.consecutiveFailures, sortValue: (w) => w.consecutiveFailures ?? 0 },
      { id: 'last', header: t('integrations.hooks.col.last'), cell: (w) => (w.lastDeliveryUtc ? f.dateTime(w.lastDeliveryUtc) : t('integrations.api.never')), sortValue: (w) => w.lastDeliveryUtc ?? '' },
    ],
    [t, f],
  )

  return (
    <Modal open size="lg" title={t('integrations.hooks.title', { name: integration.name ?? '' })} onClose={onClose} footer={<button type="button" className="btn" onClick={onClose}>{t('integrations.close')}</button>}>
      <div className="pb">
        <p className="help">{t('integrations.hooks.help')}</p>
        {shown && (
          <div className="int-secret" role="status">
            <p><b>{t('integrations.hooks.secretOnce')}</b></p>
            <p>
              {t('integrations.hooks.secretIs')} <span className="mono" data-testid="webhook-secret">{shown.secret}</span>{' '}
              <button type="button" className="btn" onClick={() => void copy(shown.secret)}>{t('integrations.api.copy')}</button>
            </p>
            <p className="help">{t('integrations.hooks.verifyHelp')}</p>
            <button type="button" className="btn" onClick={() => setShown(null)}>{t('integrations.api.saved')}</button>
          </div>
        )}
        <div className="int-newkey">
          <label className="fld">
            <span>{t('integrations.hooks.url')}</span>
            <input value={url} maxLength={500} onChange={(e) => setUrl(e.target.value)} placeholder="https://sistema.cliente.com/hooks/teikem" autoComplete="off" />
          </label>
        </div>
        <fieldset className="int-scopes">
          <legend>{t('integrations.hooks.events')}</legend>
          {WEBHOOK_EVENTS.map((e) => (
            <div key={e} className="int-opt">
              <label className="sw">
                <input type="checkbox" role="switch" aria-label={t(`integrations.hooks.event.${eventKey(e)}`)} checked={events.includes(e)} onChange={(ev) => toggleEvent(e, ev.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t(`integrations.hooks.event.${eventKey(e)}`)}</span> <span className="help mono">{e}</span>
              </label>
            </div>
          ))}
        </fieldset>
        {error && <p className="ferr" role="alert">{error}</p>}
        <button type="button" className="btn flow" disabled={create.isPending || update.isPending} onClick={() => void save()}>
          {editing ? t('integrations.hooks.saveChanges') : t('integrations.hooks.create')}
        </button>
        {editing && <button type="button" className="btn" onClick={reset}>{t('integrations.hooks.cancelEdit')}</button>}

        {hooks.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(hooks.error).title}</p>
        ) : hooks.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('integrations.hooks.title', { name: integration.name ?? '' })}
            columns={columns}
            rows={hooks.data}
            rowKey={(w) => w.publicId ?? ''}
            defaultSort={{ id: 'url', desc: false }}
            empty={t('integrations.hooks.empty')}
            rowActions={[
              {
                key: 'test',
                label: t('integrations.hooks.test'),
                onClick: (w) =>
                  void test
                    .mutateAsync(w.publicId!)
                    .then((r) => (r.ok ? toast.success(r.message ?? '') : toast.error(r.message ?? '')))
                    .catch((e) => toast.error(applyProblemDetails(e).title)),
              },
              { key: 'edit', label: t('integrations.hooks.edit'), onClick: (w) => { setEditing(w); setUrl(w.url ?? ''); setEvents([...(w.events ?? [])]); setError(null) } },
              { key: 'resume', label: t('integrations.hooks.resume'), visible: (w) => webhookState(w) === 'SUSPENDED', onClick: (w) => void act(resume.mutateAsync(w.publicId!), t('integrations.hooks.resumed')) },
              {
                key: 'rotate',
                label: t('integrations.hooks.rotate'),
                onClick: (w) => {
                  if (!window.confirm(t('integrations.hooks.rotateConfirm'))) return
                  void rotate.mutateAsync(w.publicId!).then((r) => setShown({ url: w.url ?? '', secret: r.secret ?? '' })).catch((e) => toast.error(applyProblemDetails(e).title))
                },
              },
              {
                key: 'remove',
                label: t('integrations.hooks.remove'),
                onClick: (w) => {
                  if (!window.confirm(t('integrations.hooks.removeConfirm', { url: w.url ?? '' }))) return
                  void act(remove.mutateAsync(w.publicId!), t('integrations.hooks.removed'))
                },
              },
            ]}
          />
        )}
      </div>
    </Modal>
  )
}
