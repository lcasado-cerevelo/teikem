// Lote 244 — «Personas de la ruta de venta»: titular y apoyos vigentes, agregar (persona activa, papel y fecha), cerrar con fecha,
// anular («registrada por error», con confirmación) e historial. Con `sales.analysis.manage` se edita; con solo «ver», solo se lee.
import { useEffect, useState, type FormEvent } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { Modal } from '../../kernel/ui/Modal'
import { SearchMultiSelect } from '../../kernel/ui/SearchSelect'
import { Spinner } from '../../kernel/ui/Spinner'
import { toast } from '../../kernel/ui/toast'
import { useAssignRoutePerson, useCloseRoutePerson, useRoutePeople, useSellerCandidates, useVoidRoutePerson, type RoutePersonRole } from './salesAnalysisApi'
import { SALES_ANALYSIS_MANAGE } from './salesAnalysisRules'
import { QueryError } from './SaParts'
import './saRouteSetup.css'

type Assignment = components['schemas']['SalesRouteAssignmentDto']

export interface RouteRef {
  id: number
  name: string
}

export function RoutePeoplePanel({ route, onClose }: { route: RouteRef | null; onClose: () => void }) {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const [history, setHistory] = useState(false)
  const [voided, setVoided] = useState(false)
  const q = useRoutePeople(route?.id ?? null, history, voided)
  const [closing, setClosing] = useState<Assignment | null>(null)
  const [voiding, setVoiding] = useState<Assignment | null>(null)
  const voidMut = useVoidRoutePerson()
  useEffect(() => {
    setHistory(false)
    setVoided(false)
  }, [route?.id])
  const data = q.data
  const current = [...(data?.primary ? [data.primary] : []), ...(data?.supports ?? [])]
  const past = data?.history ?? []
  return (
    <Modal open={route !== null} title={t('salesAnalysis.routes.peopleOf', { route: route?.name ?? '' })} onClose={onClose} size="lg" footer={<button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>}>
      <div className="sa-route-people">
        <p className="sa-muted">{t('salesAnalysis.routes.sub')}</p>
        {q.isError ? (
          <QueryError error={q.error} />
        ) : !data ? (
          <Spinner block label={t('common.loading')} />
        ) : (
          <>
            {!data.primary && <p className="sa-route-note" role="status">{t('salesAnalysis.routes.noPrimary')}</p>}
            <h3 className="sa-route-sub">{t('salesAnalysis.routes.currentTitle')}</h3>
            {current.length === 0 ? <p className="sa-muted">{t('salesAnalysis.routes.none')}</p> : <PeopleTable rows={current} canManage={canManage} onClose={setClosing} onVoid={setVoiding} />}
            {canManage && route && <AddPerson routeId={route.id} routeName={route.name} />}
            <div className="sa-route-opts">
              <label className="sa-check"><input type="checkbox" checked={history} onChange={(e) => setHistory(e.target.checked)} /> {t('salesAnalysis.routes.showHistory')}</label>
              {history && canManage && <label className="sa-check"><input type="checkbox" checked={voided} onChange={(e) => setVoided(e.target.checked)} /> {t('salesAnalysis.routes.showVoided')}</label>}
            </div>
            {history && (
              <>
                <h3 className="sa-route-sub">{t('salesAnalysis.routes.history')}</h3>
                {past.length === 0 ? <p className="sa-muted">{t('salesAnalysis.routes.historyEmpty')}</p> : <PeopleTable rows={past} canManage={false} onClose={setClosing} onVoid={setVoiding} />}
              </>
            )}
          </>
        )}
      </div>
      <CloseDialog routeId={route?.id ?? 0} value={closing} onClose={() => setClosing(null)} />
      <ConfirmDialog
        open={voiding !== null}
        title={t('salesAnalysis.routes.voidTitle', { name: voiding?.userName ?? '' })}
        message={t('salesAnalysis.routes.voidMsg')}
        tone="danger"
        confirmLabel={t('salesAnalysis.routes.void')}
        onConfirm={async () => {
          if (!route || !voiding) return
          await voidMut.mutateAsync({ routeId: route.id, assignmentId: voiding.id ?? 0 })
          toast.success(t('salesAnalysis.routes.voidedOk', { name: voiding.userName ?? '' }))
        }}
        onClose={() => setVoiding(null)}
      />
    </Modal>
  )
}

function PeopleTable({ rows, canManage, onClose, onVoid }: { rows: Assignment[]; canManage: boolean; onClose: (a: Assignment) => void; onVoid: (a: Assignment) => void }) {
  const t = useT()
  const f = useFormat()
  return (
    <div className="sa-route-scroll">
      <table className="sa-route-table">
        <thead>
          <tr>
            <th scope="col">{t('salesAnalysis.routes.user')}</th>
            <th scope="col">{t('salesAnalysis.routes.role')}</th>
            <th scope="col">{t('salesAnalysis.routes.validFrom')}</th>
            <th scope="col">{t('salesAnalysis.routes.validTo')}</th>
            <th scope="col">{t('salesAnalysis.setup.status')}</th>
            {canManage && <th scope="col"><span className="sr-only">{t('salesAnalysis.setup.edit')}</span></th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((a) => (
            <tr key={a.id} className={a.isVoided ? 'dim' : undefined}>
              <td className="sa-route-name">
                <span className="sa-route-line">
                  {a.userName}
                  {a.isCurrent && a.userIsActiveMember === false && <Chip tone="warn">{t('salesAnalysis.routes.userInactive')}</Chip>}
                </span>
              </td>
              <td><Chip tone={a.role === 'PRIMARY' ? 'route' : 'neutral'}>{t(`salesAnalysis.routes.role${a.role === 'PRIMARY' ? 'PRIMARY' : 'SUPPORT'}`)}</Chip></td>
              <td>{f.date(a.validFrom)}</td>
              <td>{a.validTo ? f.date(a.validTo) : <span className="sa-muted">{t('salesAnalysis.routes.open')}</span>}</td>
              <td>
                {a.isVoided ? <Chip tone="fail">{t('salesAnalysis.routes.voided')}</Chip> : a.isCurrent ? <Chip tone="deliv">{t('salesAnalysis.routes.current')}</Chip> : <Chip tone="cap">{t('salesAnalysis.routes.closed')}</Chip>}
              </td>
              {canManage && (
                <td className="sa-route-actions">
                  {a.isCurrent && !a.isVoided && (
                    <span className="sa-inline">
                      <button type="button" className="btn sm" aria-label={t('salesAnalysis.routes.closeBtnOf', { name: a.userName ?? '' })} onClick={() => onClose(a)}>{t('salesAnalysis.routes.close')}</button>
                      <button type="button" className="btn sm danger" aria-label={t('salesAnalysis.routes.voidBtnOf', { name: a.userName ?? '' })} onClick={() => onVoid(a)}>{t('salesAnalysis.routes.void')}</button>
                    </span>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function AddPerson({ routeId, routeName }: { routeId: number; routeName: string }) {
  const t = useT()
  const f = useFormat()
  const [text, setText] = useState('')
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [user, setUser] = useState<string[]>([])
  const [role, setRole] = useState<RoutePersonRole>('SUPPORT')
  const [from, setFrom] = useState('')
  const [error, setError] = useState<string | null>(null)
  const cands = useSellerCandidates(q, open || user.length > 0)
  const assign = useAssignRoutePerson()
  const [known, setKnown] = useState<{ value: string; label: string }[]>([])
  useEffect(() => {
    const id = setTimeout(() => setQ(text), 250)
    return () => clearTimeout(id)
  }, [text])
  const options = (cands.data ?? []).map((c) => ({ value: String(c.userId), label: c.email ? `${c.name} · ${c.email}` : (c.name ?? '') }))
  useEffect(() => {
    if (options.length) setKnown((k) => [...k.filter((x) => !options.some((o) => o.value === x.value)), ...options])
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cands.data])
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      await assign.mutateAsync({ routeId, userId: Number(user[0] ?? 0), role, validFrom: from || null })
      toast.success(t('salesAnalysis.routes.assigned', { name: known.find((o) => o.value === user[0])?.label.split(' · ')[0] ?? '' }))
      setUser([])
      setFrom('')
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }
  return (
    <form className="sa-route-add" onSubmit={(e) => void submit(e)} aria-label={t('salesAnalysis.routes.assignTitle', { route: routeName })}>
      {error && <p className="ferr" role="alert" style={{ gridColumn: '1 / -1' }}>{error}</p>}
      <div className="f">
        <label id="sa-route-user-l" htmlFor="sa-route-user">{t('salesAnalysis.routes.user')}</label>
        <SearchMultiSelect
          id="sa-route-user"
          labelledBy="sa-route-user-l"
          options={options}
          chosen={known.filter((k) => user.includes(k.value))}
          value={user}
          onChange={(v) => setUser(v.length ? [v[v.length - 1]] : [])}
          placeholder={t('salesAnalysis.routes.pickUser')}
          onSearch={setText}
          loading={cands.isFetching}
          onOpenChange={setOpen}
        />
      </div>
      <div className="f">
        <label htmlFor="sa-route-role">{t('salesAnalysis.routes.role')}</label>
        <select id="sa-route-role" value={role} onChange={(e) => setRole(e.target.value as RoutePersonRole)}>
          <option value="SUPPORT">{t('salesAnalysis.routes.roleSUPPORT')}</option>
          <option value="PRIMARY">{t('salesAnalysis.routes.rolePRIMARY')}</option>
        </select>
      </div>
      <div className="f">
        <label htmlFor="sa-route-from">{t('salesAnalysis.routes.validFrom')}</label>
        <input id="sa-route-from" type="date" max={f.today()} value={from} onChange={(e) => setFrom(e.target.value)} />
      </div>
      <button type="submit" className="btn flow" disabled={user.length === 0 || assign.isPending}>{t('salesAnalysis.routes.assign')}</button>
      <p className="sa-help" style={{ gridColumn: '1 / -1', margin: 0 }}>{t('salesAnalysis.routes.roleHelp')}</p>
    </form>
  )
}

function CloseDialog({ routeId, value, onClose }: { routeId: number; value: Assignment | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const [date, setDate] = useState('')
  const [error, setError] = useState<string | null>(null)
  const close = useCloseRoutePerson()
  useEffect(() => {
    setDate('')
    setError(null)
  }, [value])
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!value) return
    setError(null)
    try {
      await close.mutateAsync({ routeId, assignmentId: value.id ?? 0, validTo: date || null })
      toast.success(t('salesAnalysis.routes.closedOk', { name: value.userName ?? '' }))
      onClose()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }
  return (
    <Modal
      open={value !== null}
      title={t('salesAnalysis.routes.closeTitle', { name: value?.userName ?? '' })}
      onClose={onClose}
      size="sm"
      dismissible={!close.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={close.isPending}>{t('common.cancel')}</button>
          <button type="submit" form="sa-route-close" className="btn flow" disabled={close.isPending}>{t('salesAnalysis.routes.close')}</button>
        </>
      }
    >
      <form id="sa-route-close" onSubmit={(e) => void submit(e)}>
        {error && <p className="ferr" role="alert">{error}</p>}
        <div className="f">
          <label htmlFor="sa-route-to">{t('salesAnalysis.routes.closeDate')}</label>
          <input id="sa-route-to" type="date" min={value?.validFrom} max={f.today()} value={date} onChange={(e) => setDate(e.target.value)} />
          <p className="sa-help">{t('salesAnalysis.routes.closeHelp')}</p>
        </div>
      </form>
    </Modal>
  )
}
