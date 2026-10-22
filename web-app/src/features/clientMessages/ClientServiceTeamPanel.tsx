// Panel «Equipo de servicio al cliente» de la ficha del cliente (Lote 104): quién atiende los mensajes de este cliente. Sin equipo
// designado lo atiende cualquiera con `clientmessages.reply` (UsesFallback). Se pinta con el módulo CLIENT_PORTAL y `clientmessages.reply`;
// «Designar equipo» pide `clientmessages.manage` y «Escribirle a una persona», `clientmessages.reply`.
import { useState } from 'react'
import { Can, useCan, useModule } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { EmptyState, Modal, Panel, Spinner, toast } from '../../kernel/ui'
import { IconSupportTeam } from '../../kernel/ui/screenIcons'
import { useServiceTeam, useSetServiceTeam, useTeamCandidates } from './api'
import { WriteFirstModal } from './WriteFirstModal'
import './clientMessages.css'

function DesignateModal({ clientPublicId, initial, open, onClose }: { clientPublicId: string; initial: number[]; open: boolean; onClose: () => void }) {
  const t = useT()
  const { data: candidates = [], isPending } = useTeamCandidates(open)
  const save = useSetServiceTeam(clientPublicId)
  const [chosen, setChosen] = useState<Set<number>>(() => new Set(initial))
  const [error, setError] = useState<string | null>(null)

  const toggle = (id: number) =>
    setChosen((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const submit = async () => {
    setError(null)
    try {
      await save.mutateAsync([...chosen])
      toast.success(t('clientMessages.team.saved'))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open={open}
      title={t('clientMessages.team.designateTitle')}
      size="sm"
      onClose={onClose}
      dismissible={!save.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={save.isPending}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void submit()} disabled={save.isPending}>
            {save.isPending ? t('common.loading') : t('clientMessages.team.save')}
          </button>
        </>
      }
    >
      <p className="help">{t('clientMessages.team.designateHelp')}</p>
      {isPending ? (
        <Spinner block />
      ) : candidates.length === 0 ? (
        <p className="note">{t('clientMessages.team.noCandidates')}</p>
      ) : (
        <div className="cm-candidates" role="group" aria-label={t('clientMessages.team.designateTitle')}>
          {candidates.map((c) => (
            <label key={c.userId} className="cm-candidate">
              <input type="checkbox" checked={chosen.has(c.userId ?? 0)} onChange={() => toggle(c.userId ?? 0)} />
              <span>
                {c.fullName}
                {c.email && <span className="meta"> · {c.email}</span>}
              </span>
            </label>
          ))}
        </div>
      )}
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}

export function ClientServiceTeamPanel({ clientPublicId, clientActive = true }: { clientPublicId: string; clientActive?: boolean }) {
  const t = useT()
  const moduleOn = useModule('CLIENT_PORTAL')
  const canReply = useCan('clientmessages.reply')
  const enabled = moduleOn && canReply
  const { data, isPending, error } = useServiceTeam(clientPublicId, enabled)
  const [designating, setDesignating] = useState(false)
  const [writing, setWriting] = useState(false)

  if (!enabled) return null
  const members = data?.members ?? []

  return (
    <Panel
      icon={<IconSupportTeam />}
      title={t('clientMessages.team.title')}
      badge={members.length}
      collapsible
      defaultCollapsed
      summary={data?.usesFallback ? t('clientMessages.team.summaryFallback') : t('clientMessages.team.summary', { n: members.length })}
      actions={
        <>
          <Can perm="clientmessages.reply">
            <button type="button" className="btn sm flow" disabled={!clientActive} onClick={() => setWriting(true)}>
              {t('clientMessages.writeFirst.button')}
            </button>
          </Can>
          <Can perm="clientmessages.manage">
            <button type="button" className="btn sm" onClick={() => setDesignating(true)}>
              {t('clientMessages.team.designate')}
            </button>
          </Can>
        </>
      }
    >
      <p className="help">{t('clientMessages.team.help')}</p>
      {isPending ? (
        <Spinner block />
      ) : error ? (
        <EmptyState icon={<IconSupportTeam />} title={applyProblemDetails(error).title} />
      ) : (
        <>
          {data?.usesFallback && <p className="note cm-team-note">{t('clientMessages.team.fallbackNote', { n: data.fallbackCount ?? 0 })}</p>}
          {members.length > 0 && (
            <ul className="cm-team-list">
              {members.map((m) => (
                <li key={m.userId}>
                  <b>{m.fullName}</b>
                  <span className="meta">{m.email}</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
      <DesignateModal
        key={designating ? `d-${members.map((m) => m.userId).join(',')}` : 'closed'}
        clientPublicId={clientPublicId}
        initial={members.map((m) => m.userId ?? 0)}
        open={designating}
        onClose={() => setDesignating(false)}
      />
      {writing && <WriteFirstModal clientPublicId={clientPublicId} open onClose={() => setWriting(false)} />}
    </Panel>
  )
}
