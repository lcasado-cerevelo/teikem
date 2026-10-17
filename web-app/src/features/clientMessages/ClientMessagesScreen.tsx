// «Mensajes de clientes» (Lote 104, portal de clientes): dos paneles — a la izquierda las conversaciones (una por persona del cliente),
// a la derecha la conversación abierta con su compositor. Bajo 768 px se ve un panel a la vez con «Volver». La conversación abierta
// va en `?conversation=<publicId>` (así se llega desde la ficha del cliente). Al abrirla se marca como leída; el sondeo trae lo nuevo.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ClientPicker, EmptyState, QBox, Spinner } from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { useConversation, useConversations, useMessageMutations } from './api'
import {
  charsLeft,
  isStaffMessage,
  MESSAGE_MAX,
  PAGE_STEP,
  sortConversations,
  validateMessage,
  type ConversationSummary,
} from './rules'
import './clientMessages.css'

const CONVERSATION_PARAM = 'conversation'
const FIRST_PAGE_OF_MESSAGES = 50

function useDebouncedText(value: string, ms = 300): string {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const h = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(h)
  }, [value, ms])
  return debounced
}

function ConversationRow({ c, selected, onOpen }: { c: ConversationSummary; selected: boolean; onOpen: () => void }) {
  const f = useFormat()
  const unread = c.unread ?? 0
  return (
    <button type="button" className={selected ? 'unrow cm-row on' : 'unrow cm-row'} aria-current={selected || undefined} onClick={onOpen}>
      <span className="cm-row-top">
        <b className="cm-row-name">{c.personName || c.personEmail}</b>
        <span className="meta cm-row-time">{c.lastMessageAtUtc ? f.dateTime(c.lastMessageAtUtc) : ''}</span>
      </span>
      <span className="meta cm-row-client">{c.clientName}</span>
      <span className="cm-row-bottom">
        <span className="meta cm-row-preview">{c.lastMessagePreview}</span>
        {unread > 0 && (
          <span className="cm-badge" aria-label={String(unread)}>
            {unread}
          </span>
        )}
      </span>
    </button>
  )
}

function ConversationPane({ id, onBack }: { id: string; onBack: () => void }) {
  const t = useT()
  const f = useFormat()
  const [take, setTake] = useState(FIRST_PAGE_OF_MESSAGES)
  const [draft, setDraft] = useState('')
  const [showError, setShowError] = useState<string | null>(null)
  const { data, isPending, error } = useConversation(id, take)
  const { send, markRead } = useMessageMutations()
  const logRef = useRef<HTMLDivElement>(null)
  const markedRef = useRef<string | null>(null)
  const messages = useMemo(() => data?.messages ?? [], [data])
  const last = messages[messages.length - 1]
  const lastId = last?.id
  const lastFromClient = last !== undefined && !isStaffMessage(last)
  const markReadNow = markRead.mutate

  // al abrir marca como leída, y de nuevo cada vez que llega un mensaje de la persona mientras está abierta
  useEffect(() => {
    if (!data) return
    const key = `${id}:${lastId ?? 0}`
    if (markedRef.current === key) return
    const firstOpen = markedRef.current === null || !markedRef.current.startsWith(`${id}:`)
    markedRef.current = key
    if (firstOpen || lastFromClient) markReadNow(id)
  }, [data, id, lastId, lastFromClient, markReadNow])

  // el mensaje más nuevo queda a la vista
  useEffect(() => {
    const el = logRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [lastId])

  const submit = async () => {
    const problem = validateMessage(draft)
    if (problem) {
      setShowError(t(problem === 'empty' ? 'clientMessages.errors.empty' : 'clientMessages.errors.tooLong'))
      return
    }
    setShowError(null)
    try {
      await send.mutateAsync({ id, body: draft.trim() })
      setDraft('')
    } catch (e) {
      const p = applyProblemDetails(e)
      setShowError(p.errors.body?.[0] ?? p.title)
    }
  }

  if (isPending) return <Spinner block />
  if (error || !data) return <EmptyState icon={<IconUsers />} title={applyProblemDetails(error).title} />

  const left = charsLeft(draft)
  return (
    <div className="cm-chat">
      <header className="cm-chat-head">
        <button type="button" className="btn sm cm-back" onClick={onBack}>
          {t('clientMessages.back')}
        </button>
        <div className="cm-chat-who">
          <b>{data.personName || data.personEmail}</b>
          <span className="meta">{[data.clientName, data.personEmail].filter(Boolean).join(' · ')}</span>
        </div>
      </header>
      <div className="cm-log" ref={logRef} role="log" aria-label={t('clientMessages.conversation')}>
        {data.hasMore && (
          <button type="button" className="btn sm cm-older" onClick={() => setTake((n) => n + FIRST_PAGE_OF_MESSAGES)}>
            {t('clientMessages.older')}
          </button>
        )}
        {messages.length === 0 && <p className="meta cm-nomsg">{t('clientMessages.noMessages')}</p>}
        {messages.map((m) => (
          <div key={m.id} className={isStaffMessage(m) ? 'cm-msg staff' : 'cm-msg person'}>
            <div className="cm-bubble">
              <span className="cm-sender">{m.senderName}</span>
              <span className="cm-body">{m.body}</span>
              <span className="cm-when">{m.createdAtUtc ? f.dateTime(m.createdAtUtc) : ''}</span>
            </div>
          </div>
        ))}
      </div>
      <form
        className="cm-composer"
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        <label className="sr-only" htmlFor="cm-draft">
          {t('clientMessages.write')}
        </label>
        <textarea
          id="cm-draft"
          rows={3}
          value={draft}
          placeholder={t('clientMessages.write')}
          aria-invalid={showError ? true : undefined}
          onChange={(e) => {
            setDraft(e.target.value)
            if (showError) setShowError(null)
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
              e.preventDefault()
              void submit()
            }
          }}
        />
        <div className="cm-composer-foot">
          <span className={left < 0 ? 'meta cm-left over' : 'meta cm-left'}>{t('clientMessages.charsLeft', { n: left, max: MESSAGE_MAX })}</span>
          <button type="submit" className="btn flow" disabled={send.isPending}>
            {send.isPending ? t('common.loading') : t('clientMessages.send')}
          </button>
        </div>
        {showError && (
          <p className="ferr" role="alert">
            {showError}
          </p>
        )}
      </form>
    </div>
  )
}

export default function ClientMessagesScreen() {
  const t = useT()
  const canReply = useCan('clientmessages.reply')
  const [params, setParams] = useSearchParams()
  const selected = params.get(CONVERSATION_PARAM)
  const [search, setSearch] = useState('')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [clientId, setClientId] = useState<string | null>(null)
  const [take, setTake] = useState(PAGE_STEP)
  const q = useDebouncedText(search.trim())
  const { data, isPending, error } = useConversations({ q, unread: unreadOnly, clientId, take })
  const items = useMemo(() => sortConversations(data?.items ?? []), [data])
  const total = data?.total ?? 0

  const open = (id: string | null) => {
    const next = new URLSearchParams(params)
    if (id) next.set(CONVERSATION_PARAM, id)
    else next.delete(CONVERSATION_PARAM)
    setParams(next, { replace: true })
  }

  if (!canReply) return null
  return (
    <div className="wrap cm">
      <div className="head">
        <div>
          <h1>{t('clientMessages.title')}</h1>
          <p>{t('clientMessages.subtitle')}</p>
        </div>
      </div>
      <div className="cm-layout" data-view={selected ? 'chat' : 'list'}>
        <section className="panel cm-list" aria-label={t('clientMessages.listLabel')}>
          <div className="cm-filters">
            <QBox
              value={search}
              onChange={(v) => {
                setSearch(v)
                setTake(PAGE_STEP)
              }}
              placeholder={t('clientMessages.search')}
            />
            <ClientPicker
              value={clientId}
              onChange={(id) => {
                setClientId(id)
                setTake(PAGE_STEP)
              }}
              includeInactive
              placeholder={t('clientMessages.allClients')}
              aria-label={t('clientMessages.clientFilter')}
            />
            <label className="cm-unread-only">
              <input
                type="checkbox"
                checked={unreadOnly}
                onChange={(e) => {
                  setUnreadOnly(e.target.checked)
                  setTake(PAGE_STEP)
                }}
              />
              <span>{t('clientMessages.unreadOnly')}</span>
            </label>
          </div>
          <div className="cm-rows">
            {isPending ? (
              <Spinner block />
            ) : error ? (
              <EmptyState icon={<IconUsers />} title={applyProblemDetails(error).title} />
            ) : items.length === 0 ? (
              <EmptyState icon={<IconUsers />} title={t(q || unreadOnly || clientId ? 'clientMessages.noMatches' : 'clientMessages.empty')} />
            ) : (
              items.map((c) => <ConversationRow key={c.publicId} c={c} selected={c.publicId === selected} onOpen={() => open(c.publicId ?? null)} />)
            )}
            {items.length > 0 && items.length < total && (
              <button type="button" className="btn sm block cm-more" onClick={() => setTake((n) => n + PAGE_STEP)}>
                {t('clientMessages.loadMore', { shown: items.length, total })}
              </button>
            )}
          </div>
        </section>
        <section className="panel cm-detail" aria-label={t('clientMessages.conversation')}>
          {selected ? (
            <ConversationPane key={selected} id={selected} onBack={() => open(null)} />
          ) : (
            <EmptyState icon={<IconUsers />} title={t('clientMessages.pick')} />
          )}
        </section>
      </div>
    </div>
  )
}
