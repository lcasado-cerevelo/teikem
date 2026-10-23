// Responsables de una ruta o de una zona de despacho (Lote 212): a quién se le propone primero cuando alguien quiere «hablar sobre esta orden». Varios por entidad.
// Verlos: mensajería (messages.use). Cambiarlos: planear rutas (trips.plan) o administrar la flota (fleet.manage). Una ruta nueva nace con quien la creó.
import { useMemo, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal, Spinner, toast } from '../../kernel/ui'
import { UserPicker, type PersonOption } from '../notifications/UserPicker'
import { usePeople } from './messagesApi'
import { useMessages } from './MessagesProvider'
import { useResponsibles, useSetResponsibles, type ResponsibleDto } from './messagingAdminApi'
import './messages.css'

type Entity = 'TRIP' | 'DISPATCH_ZONE'

export function ResponsiblesModal({ entity, keyId, title, onClose }: { entity: Entity; keyId: string; title: string; onClose: () => void }) {
  const t = useT()
  const current = useResponsibles(entity, keyId)
  const save = useSetResponsibles()
  const [q, setQ] = useState('')
  const people = usePeople(q)
  // lo que se está armando: empieza con los responsables de hoy y solo se guarda al aceptar
  const [draft, setDraft] = useState<ResponsibleDto[] | null>(null)
  const list = draft ?? current.data ?? []
  const options = useMemo<PersonOption[]>(
    () => (people.data ?? []).map((p) => ({ id: p.id!, name: p.name ?? '', email: p.email ?? '', online: !!p.online })),
    [people.data],
  )
  const taken = useMemo(() => new Set(list.map((r) => r.userId)), [list])

  const accept = async () => {
    try {
      await save.mutateAsync({ entity, key: keyId, userIds: list.map((r) => r.userId!) })
      toast.success(t('messages.responsibles.saved'))
      onClose()
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('messages.responsibles.title', { name: title })}
      onClose={onClose}
      dismissible={!save.isPending}
      footer={
        <>
          <button type="button" className="btn" disabled={save.isPending} onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={save.isPending || current.isPending} onClick={() => void accept()}>
            {save.isPending ? t('common.loading') : t('messages.responsibles.save')}
          </button>
        </>
      }
    >
      <div className="pb msg-new">
        <p className="help">{t('messages.responsibles.help')}</p>
        {current.isPending ? (
          <Spinner block />
        ) : (
          <>
            {list.length === 0 ? (
              <p className="note">{t('messages.responsibles.none')}</p>
            ) : (
              <ul className="nr-chips">
                {list.map((r) => (
                  <li key={r.userId} className="nr-chip">
                    {r.name}
                    <button type="button" aria-label={t('messages.new.remove', { name: r.name ?? '' })} onClick={() => setDraft(list.filter((x) => x.userId !== r.userId))}>
                      ✕
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <div className="f">
              <label>{t('messages.responsibles.add')}</label>
              <UserPicker
                label={t('messages.responsibles.add')}
                placeholder={t('messages.new.toPh')}
                people={options}
                exclude={taken}
                onQuery={setQ}
                onPick={(p) => setDraft([...list, { userId: p.id, name: p.name, email: p.email }])}
              />
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}

/** Una línea «Responsables: Ana, Pedro» con el botón de cambiarlos (si tiene permiso). No se pinta sin mensajería. */
export function ResponsiblesLine({ entity, keyId, title }: { entity: Entity; keyId: string; title: string }) {
  const t = useT()
  const { enabled } = useMessages()
  const canEdit = useCan(entity === 'TRIP' ? 'trips.plan' : 'fleet.manage')
  const q = useResponsibles(entity, keyId, enabled)
  const [open, setOpen] = useState(false)
  if (!enabled || keyId === '') return null
  const names = (q.data ?? []).map((r) => r.name).join(', ')
  return (
    <div className="msg-resp">
      <span>
        {t('messages.responsibles.label')}: <b>{names || t('messages.responsibles.nobody')}</b>
      </span>
      {canEdit && (
        <button type="button" className="rowbtn" onClick={() => setOpen(true)} aria-label={t('messages.responsibles.edit')} title={t('messages.responsibles.edit')}>
          ✎
        </button>
      )}
      {open && <ResponsiblesModal entity={entity} keyId={keyId} title={title} onClose={() => setOpen(false)} />}
    </div>
  )
}
