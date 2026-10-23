// «Nuevo mensaje» (Lote 53): escoger a quién (personas con el buscador de nombre y correo —varias— o todo un rol), el nombre del grupo si son varios
// y el primer mensaje. Una sola persona abre (o vuelve a) el hilo entre dos; varias o un rol arman un grupo. La abre la pantalla de mensajes o Kemi.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'
import { UserPicker, type PersonOption } from '../notifications/UserPicker'
import { MAX_BODY } from './messageRules'
import { useMessageMutations, useMessageRoles, usePeople, useSuggested, type MessageSuggestionDto } from './messagesApi'
import { useMessages, type AboutRef } from './MessagesProvider'
import './messages.css'

interface Chosen {
  id: number
  name: string
  email: string
}

/** El texto de dónde sale un sugerido (para que se entienda por qué está ahí). */
export function suggestionSource(s: MessageSuggestionDto, t: (k: string) => string): string {
  return s.source === 'TRIP' ? t('messages.suggest.trip') : s.source === 'ROLE' ? t('messages.suggest.role') : s.source === 'RECENT' ? t('messages.suggest.recent') : (s.sourceLabel ?? '')
}

export function NewMessageModal({ onClose, initial, about }: { onClose: () => void; initial?: Chosen[]; about?: AboutRef | null }) {
  const t = useT()
  const navigate = useNavigate()
  const { create } = useMessageMutations()
  const [q, setQ] = useState('')
  const people = usePeople(q)
  const roles = useMessageRoles()
  const [chosen, setChosen] = useState<Chosen[]>(initial ?? [])
  const [roleId, setRoleId] = useState<number | null>(null)
  const [followRole, setFollowRole] = useState(false)   // el grupo «vivo» de un rol (uno solo por compañía) que se escogió de los sugeridos
  const suggested = useSuggested(about?.entity ?? 'ORDER', about?.publicId ?? '', !!about)
  const [title, setTitle] = useState('')
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)

  // un solo campo «Para»: lista las personas (con su correo y si están en línea) y los roles con personas (con cuántas), filtrando por lo escrito
  const options = useMemo<PersonOption[]>(() => {
    const persons = (people.data ?? []).map((p) => ({ id: p.id!, name: p.name ?? '', email: p.email ?? '', online: !!p.online }))
    const roleOptions = (roles.data ?? [])
      .filter((r) => (r.memberCount ?? 0) > 0)
      .map((r) => ({ id: -r.id!, name: r.name ?? '', email: t('messages.new.roleOption', { n: r.memberCount ?? 0 }) }))
    return [...persons, ...roleOptions]
  }, [people.data, roles.data, t])
  const taken = useMemo(() => new Set([...chosen.map((c) => c.id), ...(roleId !== null ? [-roleId] : [])]), [chosen, roleId])
  const role = (roles.data ?? []).find((r) => r.id === roleId)
  const isGroup = chosen.length + (roleId !== null ? 1 : 0) > 1 || roleId !== null

  const send = async () => {
    setError(null)
    if (chosen.length === 0 && roleId === null) return setError(t('messages.new.errors.recipients'))
    if (!text.trim()) return setError(t('messages.new.errors.text'))
    setSending(true)
    try {
      const thread = await create.mutateAsync({
        userIds: chosen.map((c) => c.id),
        roleId: roleId ?? undefined,
        followRole: roleId !== null && followRole ? true : undefined,
        title: title.trim() || undefined,
        text,
        ref: about ? { entity: about.entity, publicId: about.publicId } : undefined,
      })
      onClose()
      navigate(`/messages?thread=${thread.thread?.publicId}`)
    } catch (e) {
      setError(applyProblemDetails(e).title)
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      size="md"
      title={t('messages.new.title')}
      onClose={onClose}
      dismissible={!sending}
      footer={
        <>
          <button type="button" className="btn" disabled={sending} onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={sending} onClick={() => void send()}>
            {sending ? t('common.loading') : t('messages.new.send')}
          </button>
        </>
      }
    >
      <div className="pb msg-new">
        {about && (
          <p className="msg-about" aria-label={t('messages.about.label')}>
            <span aria-hidden="true">{about.entity === 'ORDER' ? '📦' : '🚚'}</span> {t('messages.about.on', { title: about.title })}
          </p>
        )}
        {about && (suggested.data ?? []).length > 0 && (
          <div className="f msg-suggest">
            <label>{t('messages.suggest.title')}</label>
            <ul className="nr-chips">
              {(suggested.data ?? []).map((s) => {
                const taken = s.userId != null ? chosen.some((c) => c.id === s.userId) : roleId === s.roleId
                return (
                  <li key={`${s.source}-${s.userId ?? 'r'}-${s.roleId ?? 'u'}`}>
                    <button
                      type="button"
                      className={`nr-chip msg-sug${taken ? ' on' : ''}`}
                      aria-pressed={taken}
                      onClick={() => {
                        if (s.userId != null) setChosen((l) => (l.some((x) => x.id === s.userId) ? l.filter((x) => x.id !== s.userId) : [...l, { id: s.userId!, name: s.name ?? '', email: '' }]))
                        else if (s.roleId != null) {
                          setRoleId(taken ? null : s.roleId)
                          setFollowRole(!taken)
                        }
                      }}
                    >
                      <b>{s.roleId != null ? `👥 ${s.name}` : s.name}</b>
                      <small>{suggestionSource(s, t)}</small>
                    </button>
                  </li>
                )
              })}
            </ul>
          </div>
        )}
        <div className="f">
          <label htmlFor="msg-to">{t('messages.new.to')}</label>
          {(chosen.length > 0 || role) && (
            <ul className="nr-chips">
              {chosen.map((c) => (
                <li key={c.id} className="nr-chip">
                  {c.name}
                  <button type="button" aria-label={t('messages.new.remove', { name: c.name })} onClick={() => setChosen((l) => l.filter((x) => x.id !== c.id))}>
                    ✕
                  </button>
                </li>
              ))}
              {role && (
                <li className="nr-chip">
                  <span className="nr-kind">{t('messages.new.role')}</span> {role.name}
                  <button type="button" aria-label={t('messages.new.remove', { name: role.name ?? '' })} onClick={() => {
                      setRoleId(null)
                      setFollowRole(false)
                    }}>
                    ✕
                  </button>
                </li>
              )}
            </ul>
          )}
          <UserPicker
            label={t('messages.new.to')}
            placeholder={t('messages.new.toPh')}
            people={options}
            exclude={taken}
            autoFocus
            onQuery={setQ}
            onPickAll={(all) => setChosen((l) => [...l, ...all.filter((p) => p.id > 0 && !l.some((x) => x.id === p.id)).map((p) => ({ id: p.id, name: p.name, email: p.email }))])}
            onPick={(p) => {
              if (p.id < 0) {
                setRoleId(-p.id)   // un rol (un solo rol por mensaje: escoger otro reemplaza al anterior)
                setFollowRole(false)
              }
              else setChosen((l) => (l.some((x) => x.id === p.id) ? l : [...l, { id: p.id, name: p.name, email: p.email }]))
            }}
          />
        </div>
        {roleId !== null && <p className="help">{t('messages.new.roleHelp')}</p>}
        {isGroup && (
          <div className="f">
            <label htmlFor="msg-title">{t('messages.new.groupName')}</label>
            <input id="msg-title" value={title} maxLength={100} autoComplete="off" placeholder={t('messages.new.groupNamePh')} onChange={(e) => setTitle(e.target.value)} />
          </div>
        )}
        <div className="f">
          <label htmlFor="msg-text">{t('messages.new.message')}</label>
          <textarea id="msg-text" rows={4} value={text} maxLength={MAX_BODY} onChange={(e) => setText(e.target.value)} />
          <p className="help">{t('messages.new.counter', { n: text.length, max: MAX_BODY })}</p>
        </div>
        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** El «Nuevo mensaje» global: lo abre la pantalla o Kemi con `openNewMessage()`. Se monta una sola vez en el shell. */
export function NewMessageHost() {
  const { enabled, newMessageOpen, newMessageAbout, closeNewMessage } = useMessages()
  if (!enabled || !newMessageOpen) return null
  return <NewMessageModal onClose={closeNewMessage} about={newMessageAbout} />
}
