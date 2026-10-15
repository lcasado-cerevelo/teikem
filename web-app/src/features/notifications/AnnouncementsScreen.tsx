// Sistema → Enviar avisos (Lote 54, permiso notifications.send): un aviso a personas, a roles o a TODAS las personas de la compañía. No se responde:
// llega a la campanita de cada quien («De Ana») y, si tiene Teikem abierto, al instante. Un solo campo «Para» busca personas y roles; «Seleccionar todo (N)»
// escoge solo lo que coincide con lo escrito. Abajo, el historial: a quién se envió y cuántas personas ya lo leyeron.
import { useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { ConfirmDialog, DataTable, Panel, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconClock } from '../../kernel/ui/screenIcons'
import { announcementPageReader, PEOPLE_TAKE, useAnnouncementPeople, useAnnouncementRoles, useAnnouncements, useSendAnnouncement, type AnnouncementDto } from './announcementsApi'
import { UserPicker, type PersonOption } from './UserPicker'
import './notifications.css'

const MAX_TITLE = 150
const MAX_BODY = 1000
const CHIPS_SHOWN = 10

interface Chosen {
  id: number
  name: string
}

function Composer() {
  const t = useT()
  const send = useSendAnnouncement()
  const [q, setQ] = useState('')
  const people = useAnnouncementPeople(q)
  const everyone = useAnnouncementPeople('', true)
  const roles = useAnnouncementRoles()
  const [toAll, setToAll] = useState(false)
  const [chosen, setChosen] = useState<Chosen[]>([])
  const [chosenRoles, setChosenRoles] = useState<Chosen[]>([])
  const [title, setTitle] = useState('')
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)

  // roles con ids negativos para no chocar con los de las personas dentro del mismo buscador
  const options = useMemo<PersonOption[]>(() => {
    const persons = (people.data?.items ?? []).map((p) => ({ id: p.id!, name: p.name ?? '', email: p.email ?? '' }))
    const roleOptions = (roles.data ?? []).filter((r) => (r.memberCount ?? 0) > 0).map((r) => ({ id: -r.id!, name: r.name ?? '', email: t('announcements.roleOption', { n: r.memberCount ?? 0 }) }))
    return [...persons, ...roleOptions]
  }, [people.data, roles.data, t])
  const taken = useMemo(() => new Set([...chosen.map((c) => c.id), ...chosenRoles.map((r) => -r.id)]), [chosen, chosenRoles])

  const add = (items: PersonOption[]) => {
    setChosen((l) => {
      const have = new Set(l.map((x) => x.id))
      return [...l, ...items.filter((i) => i.id > 0 && !have.has(i.id)).map((i) => ({ id: i.id, name: i.name }))]
    })
    setChosenRoles((l) => {
      const have = new Set(l.map((x) => x.id))
      return [...l, ...items.filter((i) => i.id < 0 && !have.has(-i.id)).map((i) => ({ id: -i.id, name: i.name }))]
    })
  }

  const everyoneCount = everyone.data?.total ?? 0
  const recipientsLabel = toAll ? t('announcements.toAllCount', { n: everyoneCount }) : ''
  const nothing = !toAll && chosen.length === 0 && chosenRoles.length === 0

  const reset = () => {
    setChosen([])
    setChosenRoles([])
    setToAll(false)
    setTitle('')
    setText('')
    setError(null)
  }

  const submit = async () => {
    setConfirming(false)
    setError(null)
    try {
      const sent = await send.mutateAsync({ userIds: toAll ? undefined : chosen.map((c) => c.id), roleIds: toAll ? undefined : chosenRoles.map((r) => r.id), all: toAll || undefined, title, text: text || undefined })
      toast.success(t('announcements.sent', { n: sent.recipientCount ?? 0 }))
      reset()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }

  const start = () => {
    setError(null)
    if (nothing) return setError(t('announcements.errors.audience'))
    if (!title.trim()) return setError(t('announcements.errors.title'))
    if (toAll) setConfirming(true)
    else void submit()
  }

  return (
    <Panel icon={<IconClock />} title={t('announcements.new')}>
      <div className="pb nr-form">
        <div className="f">
          <label className="sw">
            <input type="checkbox" role="switch" checked={toAll} onChange={(e) => setToAll(e.target.checked)} />
            <span className="tk" aria-hidden="true" />
            <span>{t('announcements.toAll')}</span>
          </label>
          {toAll && <p className="help">{recipientsLabel}</p>}
        </div>
        {!toAll && (
          <div className="f">
            <label htmlFor="an-to">{t('announcements.to')}</label>
            {(chosen.length > 0 || chosenRoles.length > 0) && (
              <>
                <ul className="nr-chips">
                  {chosenRoles.map((r) => (
                    <li key={`r${r.id}`} className="nr-chip">
                      <span className="nr-kind">{t('announcements.role')}</span> {r.name}
                      <button type="button" aria-label={t('announcements.remove', { name: r.name })} onClick={() => setChosenRoles((l) => l.filter((x) => x.id !== r.id))}>
                        ✕
                      </button>
                    </li>
                  ))}
                  {chosen.slice(0, CHIPS_SHOWN).map((c) => (
                    <li key={c.id} className="nr-chip">
                      {c.name}
                      <button type="button" aria-label={t('announcements.remove', { name: c.name })} onClick={() => setChosen((l) => l.filter((x) => x.id !== c.id))}>
                        ✕
                      </button>
                    </li>
                  ))}
                  {chosen.length > CHIPS_SHOWN && <li className="nr-chip">{t('announcements.moreChosen', { n: chosen.length - CHIPS_SHOWN })}</li>}
                </ul>
                <p className="help">
                  {t('announcements.chosenCount', { people: chosen.length, roles: chosenRoles.length })}{' '}
                  <button type="button" className="linklike" onClick={() => (setChosen([]), setChosenRoles([]))}>
                    {t('announcements.clear')}
                  </button>
                </p>
              </>
            )}
            <UserPicker
              label={t('announcements.to')}
              placeholder={t('announcements.toPh')}
              people={options}
              exclude={taken}
              onQuery={setQ}
              onPick={(p) => add([p])}
              onPickAll={add}
            />
            {people.data && (people.data.total ?? 0) > PEOPLE_TAKE && <p className="help">{t('announcements.capNote', { n: PEOPLE_TAKE })}</p>}
          </div>
        )}
        <div className="f">
          <label htmlFor="an-title">{t('announcements.title')}</label>
          <input id="an-title" value={title} maxLength={MAX_TITLE} autoComplete="off" onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor="an-text">{t('announcements.text')}</label>
          <textarea id="an-text" rows={4} value={text} maxLength={MAX_BODY} onChange={(e) => setText(e.target.value)} />
          <p className="help">{t('announcements.counter', { n: text.length, max: MAX_BODY })}</p>
        </div>
        <p className="help">{t('announcements.noReply')}</p>
        {error && (
          <p className="ferr" role="alert">
            {error}
          </p>
        )}
        <div>
          <button type="button" className="btn flow" disabled={send.isPending} onClick={start}>
            {send.isPending ? t('common.loading') : t('announcements.send')}
          </button>
        </div>
      </div>
      <ConfirmDialog
        open={confirming}
        title={t('announcements.confirmTitle')}
        message={t('announcements.confirmBody', { n: everyoneCount })}
        confirmLabel={t('announcements.send')}
        onConfirm={submit}
        onClose={() => setConfirming(false)}
      />
    </Panel>
  )
}

function History() {
  const t = useT()
  const f = useFormat()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const list = useAnnouncements((page - 1) * pageSize, pageSize)
  const columns = useMemo<DataColumn<AnnouncementDto>[]>(
    () => [
      { id: 'when', header: t('announcements.col.when'), cell: (a) => f.dateTime(a.sentAtUtc), sortValue: (a) => a.sentAtUtc ?? '', card: 'title' },
      {
        id: 'title',
        header: t('announcements.col.title'),
        cell: (a) => (
          <>
            <b>{a.title}</b>
            {a.body && <div className="help">{a.body}</div>}
          </>
        ),
        exportValue: (a) => `${a.title}${a.body ? ` — ${a.body}` : ''}`,
        sortValue: (a) => a.title ?? '',
      },
      { id: 'to', header: t('announcements.col.to'), cell: (a) => a.audienceText, sortValue: (a) => a.audienceText ?? '' },
      { id: 'people', header: t('announcements.col.people'), cell: (a) => a.recipientCount, sortValue: (a) => a.recipientCount ?? 0 },
      { id: 'read', header: t('announcements.col.read'), cell: (a) => t('announcements.readOf', { read: a.readCount ?? 0, total: a.recipientCount ?? 0 }), sortValue: (a) => a.readCount ?? 0 },
      { id: 'by', header: t('announcements.col.by'), cell: (a) => a.sentByName, sortValue: (a) => a.sentByName ?? '' },
    ],
    [t, f],
  )
  return (
    <Panel flush icon={<IconClock />} title={t('announcements.history')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        {list.isError ? (
          <p className="ferr" role="alert">
            {applyProblemDetails(list.error).title}
          </p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <DataTable
            label={t('announcements.history')}
            columns={columns}
            rows={list.data?.items ?? []}
            rowKey={(a) => a.publicId ?? ''}
            page={page}
            pageSize={pageSize}
            total={list.data?.total ?? 0}
            onPage={setPage}
            onPageSize={(s) => (setPageSize(s), setPage(1))}
            empty={t('announcements.empty')}
            exportRows={() => fetchAllPages<AnnouncementDto>(announcementPageReader())}
          />
        )}
      </div>
    </Panel>
  )
}

export default function AnnouncementsScreen() {
  const t = useT()
  return (
    <div className="nr-screen">
      <div className="head">
        <div>
          <h1>{t('announcements.screenTitle')}</h1>
          <p>{t('announcements.subtitle')}</p>
        </div>
      </div>
      <Composer />
      <History />
    </div>
  )
}
