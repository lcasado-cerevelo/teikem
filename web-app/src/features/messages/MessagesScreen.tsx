// Mensajes (Lote 53): la pantalla de chat. A la izquierda los hilos (avatar, «en línea», último mensaje, no leídos); a la derecha la conversación con
// burbujas (lo tuyo a la derecha), separadores de día, «Cargar anteriores», la caja para escribir (Enter envía, Mayús+Enter salto de línea) y, en
// un grupo, sus personas. Todo llega en vivo; permiso messages.use (la ruta lo exige). En teléfono se ve una cosa a la vez.
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useSession } from '../../app/session'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, Spinner, toast } from '../../kernel/ui'
import { UserPicker, type PersonOption } from '../notifications/UserPicker'
import { avatarTone, dayLabel, groupMessages, initialsOf, isOnline, MAX_BODY, presenceText, shouldSend } from './messageRules'
import { fetchOlder, useMessageMutations, usePeople, useThread, useThreads, type MessageDto, type MessageThreadSummaryDto } from './messagesApi'
import { useMessages } from './MessagesProvider'
import './messages.css'

function Avatar({ name, online, group }: { name: string; online?: boolean; group?: boolean }) {
  return (
    <span className={`mavatar tone${avatarTone(name)}${group ? ' grp' : ''}`} aria-hidden="true">
      {group ? '👥' : initialsOf(name)}
      {online !== undefined && <i className={`mdot${online ? ' on' : ''}`} />}
    </span>
  )
}

function ThreadList({ selected, onSelect }: { selected: string | null; onSelect: (id: string) => void }) {
  const t = useT()
  const f = useFormat()
  const { me } = useSession()
  const { openNewMessage } = useMessages()
  const threads = useThreads()
  const [q, setQ] = useState('')
  const rows = useMemo(() => {
    const term = q.trim().toLowerCase()
    return (threads.data ?? []).filter((x) => !term || (x.name ?? '').toLowerCase().includes(term) || (x.lastMessagePreview ?? '').toLowerCase().includes(term))
  }, [threads.data, q])
  const now = Date.now()
  const when = (iso: string) => (dayLabel(iso, now) === 'today' ? f.time(iso) : f.date(iso))
  const preview = (x: MessageThreadSummaryDto) => {
    const who = x.lastSenderName && x.lastSenderName === (me?.fullName ?? me?.email) ? t('messages.you') : x.kind === 'GROUP' ? x.lastSenderName : null
    return who ? `${who}: ${x.lastMessagePreview ?? ''}` : (x.lastMessagePreview ?? '')
  }
  return (
    <aside className="chat-list" aria-label={t('messages.list.title')}>
      <div className="chat-list-head">
        <h2>{t('messages.list.title')}</h2>
        <button type="button" className="btn sm flow" onClick={openNewMessage}>
          {t('messages.new.button')}
        </button>
      </div>
      <input className="chat-search" type="search" value={q} placeholder={t('messages.list.search')} aria-label={t('messages.list.search')} onChange={(e) => setQ(e.target.value)} />
      {threads.isError ? (
        <p className="ferr" role="alert">
          {applyProblemDetails(threads.error).title}
        </p>
      ) : threads.isPending ? (
        <Spinner block />
      ) : rows.length === 0 ? (
        <p className="chat-empty">{(threads.data ?? []).length === 0 ? t('messages.list.empty') : t('messages.list.noMatch')}</p>
      ) : (
        <ul>
          {rows.map((x) => (
            <li key={x.publicId}>
              <button type="button" className={`chat-item${x.publicId === selected ? ' on' : ''}${(x.unread ?? 0) > 0 ? ' unread' : ''}`} onClick={() => onSelect(x.publicId!)}>
                <Avatar name={x.name ?? ''} group={x.kind === 'GROUP'} online={x.kind === 'DIRECT' ? !!x.other?.online : undefined} />
                <span className="chat-item-body">
                  <span className="chat-item-top">
                    <b>{x.name}</b>
                    <time dateTime={x.lastMessageAtUtc}>{when(x.lastMessageAtUtc!)}</time>
                  </span>
                  <span className="chat-item-bottom">
                    <span className="chat-prev">{preview(x)}</span>
                    {x.isMuted && <span className="chat-muted" title={t('messages.muted')} aria-label={t('messages.muted')}>🔕</span>}
                    {(x.unread ?? 0) > 0 && <span className="chat-badge" aria-label={t('messages.unreadN', { n: x.unread ?? 0 })}>{x.unread}</span>}
                  </span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </aside>
  )
}

function Conversation({ id, onBack }: { id: string; onBack: () => void }) {
  const t = useT()
  const f = useFormat()
  const navigate = useNavigate()
  const { setActiveThread } = useMessages()
  const thread = useThread(id)
  const { send, read, mute, leave, addMembers } = useMessageMutations()
  const [older, setOlder] = useState<MessageDto[]>([])
  const [hasMoreOlder, setHasMoreOlder] = useState<boolean | null>(null)
  const [loadingOlder, setLoadingOlder] = useState(false)
  const [text, setText] = useState('')
  const [membersOpen, setMembersOpen] = useState(false)
  const [leaving, setLeaving] = useState(false)
  const [q, setQ] = useState('')
  const people = usePeople(q, membersOpen)
  const scroller = useRef<HTMLDivElement>(null)
  const stick = useRef(true)
  const lastRead = useRef<number>(0)
  const data = thread.data

  useEffect(() => {
    setActiveThread(id)
    return () => setActiveThread(null)
  }, [id, setActiveThread])

  // al cambiar de hilo se empieza de cero
  useEffect(() => {
    setOlder([])
    setHasMoreOlder(null)
    setText('')
    setMembersOpen(false)
    stick.current = true
    lastRead.current = 0
  }, [id])

  const messages = useMemo(() => {
    const seen = new Set<number>()
    return [...older, ...(data?.messages ?? [])].filter((m) => (seen.has(m.id!) ? false : (seen.add(m.id!), true)))
  }, [older, data?.messages])
  const showMore = hasMoreOlder ?? !!data?.hasMore

  // marcar leído: lo que se ve en pantalla (hilo abierto y ventana visible) no queda pendiente
  const lastId = data?.messages?.length ? data.messages[data.messages.length - 1].id! : 0
  const unread = data?.thread?.unread ?? 0
  useEffect(() => {
    if (!data || lastId === 0 || document.visibilityState !== 'visible') return
    if (unread > 0 && lastRead.current !== lastId) {
      lastRead.current = lastId
      read.mutate(id)
    }
  }, [data, lastId, unread, id, read])

  // bajar al último mensaje cuando llega uno (si ya estaba abajo o es mío)
  useEffect(() => {
    const el = scroller.current
    if (!el) return
    const mine = data?.messages?.[data.messages.length - 1]?.mine
    if (stick.current || mine) el.scrollTop = el.scrollHeight
  }, [lastId, data?.messages])

  const onScroll = () => {
    const el = scroller.current
    if (el) stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80
  }

  const loadOlder = useCallback(async () => {
    if (messages.length === 0) return
    setLoadingOlder(true)
    const el = scroller.current
    const before = el ? el.scrollHeight : 0
    try {
      const page = await fetchOlder(id, messages[0].id!)
      setOlder((o) => [...(page.messages ?? []), ...o])
      setHasMoreOlder(!!page.hasMore)
      requestAnimationFrame(() => {
        if (el) el.scrollTop = el.scrollHeight - before
      })
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    } finally {
      setLoadingOlder(false)
    }
  }, [id, messages])

  const submit = async () => {
    const body = text
    if (!body.trim()) return
    setText('')
    stick.current = true
    try {
      await send.mutateAsync({ publicId: id, text: body })
    } catch (e) {
      setText(body)   // no se pierde lo escrito
      toast.error(applyProblemDetails(e).title)
    }
  }

  const sum = data?.thread
  const now = Date.now()
  const groups = useMemo(() => groupMessages(messages), [messages])
  const memberOptions = useMemo<PersonOption[]>(() => {
    const inThread = new Set((data?.members ?? []).map((m) => m.userId))
    return (people.data ?? []).filter((p) => !inThread.has(p.id!)).map((p) => ({ id: p.id!, name: p.name ?? '', email: p.email ?? '', online: !!p.online }))
  }, [people.data, data?.members])

  if (thread.isError) return <div className="chat-pane"><p className="ferr" role="alert">{applyProblemDetails(thread.error).title}</p></div>
  if (!sum) return <div className="chat-pane"><Spinner block /></div>
  const other = sum.other
  const status = sum.kind === 'DIRECT'
    ? presenceText(isOnline(other?.lastSeenUtc, now) || !!other?.online, other?.lastSeenUtc, now, t, f.date)
    : t('messages.members', { n: sum.memberCount ?? 0 })

  return (
    <section className="chat-pane" aria-label={sum.name ?? ''}>
      <header className="chat-head">
        <button type="button" className="iconbtn chat-back" aria-label={t('messages.back')} onClick={onBack}>
          ←
        </button>
        <Avatar name={sum.name ?? ''} group={sum.kind === 'GROUP'} online={sum.kind === 'DIRECT' ? !!other?.online : undefined} />
        <div className="chat-head-text">
          <b>{sum.name}</b>
          <span className={sum.kind === 'DIRECT' && other?.online ? 'on' : undefined}>{status}</span>
        </div>
        <div className="chat-head-actions">
          {sum.kind === 'GROUP' && (
            <button type="button" className="btn sm" aria-expanded={membersOpen} onClick={() => setMembersOpen((o) => !o)}>
              {t('messages.membersBtn')}
            </button>
          )}
          <button type="button" className="btn sm" onClick={() => mute.mutate({ publicId: id, muted: !sum.isMuted }, { onError: (e) => toast.error(applyProblemDetails(e).title) })}>
            {sum.isMuted ? t('messages.unmute') : t('messages.mute')}
          </button>
          {sum.kind === 'GROUP' && (
            <button type="button" className="btn sm" onClick={() => setLeaving(true)}>
              {t('messages.leave')}
            </button>
          )}
        </div>
      </header>
      {membersOpen && (
        <div className="chat-members" role="region" aria-label={t('messages.membersBtn')}>
          <ul>
            {(data?.members ?? []).map((m) => (
              <li key={m.userId}>
                <Avatar name={m.name ?? ''} online={!!m.online} />
                <span>
                  <b>{m.name}</b>
                  {m.isCreator && <em> {t('messages.creator')}</em>}
                  <small>{m.email}</small>
                </span>
              </li>
            ))}
          </ul>
          {data?.canAddMembers && (
            <div className="f">
              <label>{t('messages.addPeople')}</label>
              <UserPicker
                label={t('messages.addPeople')}
                placeholder={t('messages.new.toPh')}
                people={memberOptions}
                onQuery={setQ}
                onPick={(p) => addMembers.mutate({ publicId: id, userIds: [p.id] }, { onError: (e) => toast.error(applyProblemDetails(e).title) })}
              />
            </div>
          )}
        </div>
      )}
      <div className="chat-scroll" ref={scroller} onScroll={onScroll} role="log" aria-live="polite" aria-label={t('messages.conversation')}>
        {showMore && (
          <button type="button" className="btn sm chat-older" disabled={loadingOlder} onClick={() => void loadOlder()}>
            {loadingOlder ? t('common.loading') : t('messages.older')}
          </button>
        )}
        {groups.map((g, i) => (
          <div key={`${g.day}-${g.messages[0].id}-${i}`}>
            {g.showDay && (
              <div className="chat-day">
                <span>{dayLabel(g.messages[0].createdAtUtc!, now) === 'date' ? f.date(g.messages[0].createdAtUtc!) : t(`messages.day.${dayLabel(g.messages[0].createdAtUtc!, now)}`)}</span>
              </div>
            )}
            <div className={`chat-group${g.mine ? ' mine' : ''}`}>
              {!g.mine && <Avatar name={g.senderName} />}
              <div className="chat-bubbles">
                {!g.mine && sum.kind === 'GROUP' && <span className="chat-sender">{g.senderName}</span>}
                {g.messages.map((m, j) => (
                  <div key={m.id} className="bub">
                    <p>{m.body}</p>
                    {j === g.messages.length - 1 && <time dateTime={m.createdAtUtc}>{f.time(m.createdAtUtc!)}</time>}
                  </div>
                ))}
              </div>
            </div>
          </div>
        ))}
        {messages.length === 0 && <p className="chat-empty">{t('messages.noMessages')}</p>}
      </div>
      <form
        className="chat-composer"
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        <textarea
          rows={1}
          value={text}
          maxLength={MAX_BODY}
          placeholder={t('messages.write')}
          aria-label={t('messages.write')}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (shouldSend({ key: e.key, shiftKey: e.shiftKey, isComposing: e.nativeEvent.isComposing }, text)) {
              e.preventDefault()
              void submit()
            }
          }}
        />
        <button type="submit" className="btn flow" disabled={!text.trim() || send.isPending}>
          {t('messages.send')}
        </button>
      </form>
      <ConfirmDialog
        open={leaving}
        tone="danger"
        title={t('messages.leaveTitle')}
        message={t('messages.leaveBody', { name: sum.name ?? '' })}
        confirmLabel={t('messages.leave')}
        onConfirm={async () => {
          await leave.mutateAsync(id)
          toast.success(t('messages.left', { name: sum.name ?? '' }))
          navigate('/messages')
        }}
        onClose={() => setLeaving(false)}
      />
    </section>
  )
}

export default function MessagesScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const selected = params.get('thread')
  const select = (id: string | null) => setParams(id ? { thread: id } : {}, { replace: false })
  return (
    <div className={`chat${selected ? ' has-thread' : ''}`}>
      <h1 className="sr-only">{t('messages.title')}</h1>
      <ThreadList selected={selected} onSelect={select} />
      {selected ? (
        <Conversation key={selected} id={selected} onBack={() => select(null)} />
      ) : (
        <section className="chat-pane chat-blank" aria-label={t('messages.title')}>
          <div>
            <h2>{t('messages.blank.title')}</h2>
            <p>{t('messages.blank.body')}</p>
          </div>
        </section>
      )}
    </div>
  )
}
