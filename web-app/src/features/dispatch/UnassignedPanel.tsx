// Lote F-E2a — «Órdenes sin asignar» (maqueta `dispatch()`): contador, «Marcar todas», «Añadir a la ruta (N)» y la lista con casilla (referencia,
// consignatario, pueblo · CP y zona; el COD no se pinta: la lista del servidor no trae montos, regla del capítulo 05). Se piden hasta 500 al servidor con los filtros de la barra; el buscador filtra lo cargado. Si el servidor
// rechaza la asignación (422), las órdenes con su motivo quedan a la vista y la selección se conserva para quitar las culpables.
import { useMemo, useState } from 'react'
import { useT } from '../../kernel/i18n'
import { Can } from '../../kernel/access'
import { EmptyState, IconCheck, IconLayers, IconPlus, Panel, QBox, Spinner } from '../../kernel/ui'
import { MAX_ORDERS_PER_REQUEST, orderProblems, unassignedMatches, type UnassignedOrder } from './dispatchRules'

export interface AddProblem {
  title: string
  errors: Record<string, string[]>
}

export function UnassignedPanel({
  items,
  total,
  loading,
  failed,
  filtered,
  onRetry,
  checked,
  onChecked,
  addTarget,
  adding,
  onAdd,
  problem,
  onDismissProblem,
}: {
  items: readonly UnassignedOrder[]
  total: number
  loading: boolean
  failed: boolean
  filtered: boolean
  onRetry: () => void
  checked: readonly string[]
  onChecked: (ids: string[]) => void
  /** La ruta que recibe las órdenes: su código, o null si no hay una ruta abierta que las pueda recibir. */
  addTarget: string | null
  adding: boolean
  onAdd: () => void
  problem: AddProblem | null
  onDismissProblem: () => void
}) {
  const t = useT()
  const [q, setQ] = useState('')
  const rows = useMemo(() => items.filter((o) => unassignedMatches(o, q)), [items, q])
  const ids = useMemo(() => new Set(items.map((o) => o.publicId ?? '')), [items])
  // una orden que ya no está en la lista (otro usuario la asignó) sale de la selección sola
  const live = checked.filter((id) => ids.has(id))
  const allChecked = rows.length > 0 && rows.every((o) => live.includes(o.publicId ?? ''))
  const tooMany = live.length > MAX_ORDERS_PER_REQUEST
  const problems = problem ? orderProblems(problem.errors) : []
  const failedNumbers = new Set(problems.map((p) => p.number))

  const toggle = (id: string, on: boolean) => onChecked(on ? [...live, id] : live.filter((x) => x !== id))
  const toggleAll = (on: boolean) => {
    const visible = rows.map((o) => o.publicId ?? '').filter(Boolean)
    onChecked(on ? [...new Set([...live, ...visible])] : live.filter((id) => !visible.includes(id)))
  }

  return (
    <Panel icon={<IconLayers />} title={t('dispatch.unassigned.title')} badge={failed ? undefined : total} flush>
      <div className="disp-un-tools">
        <label>
          <input type="checkbox" checked={allChecked} disabled={rows.length === 0} onChange={(e) => toggleAll(e.target.checked)} />
          {t('dispatch.unassigned.markAll')}
        </label>
        <div style={{ flex: '1 1 140px', minWidth: 0 }}>
          <QBox value={q} onChange={setQ} placeholder={t('dispatch.unassigned.search')} />
        </div>
      </div>
      <Can perm="trips.plan">
        <div className="disp-un-add">
          <button type="button" className="btn sm flow" disabled={live.length === 0 || addTarget === null || tooMany || adding} onClick={onAdd}>
            <IconPlus /> {adding ? t('common.loading') : live.length > 0 ? t('dispatch.unassigned.addN', { count: live.length }) : t('dispatch.unassigned.add')}
          </button>
        </div>
        {addTarget === null && <p className="disp-un-note">{t('dispatch.unassigned.noTarget')}</p>}
        {addTarget !== null && live.length > 0 && !tooMany && (
          <p className="disp-un-note">{t('dispatch.unassigned.target', { code: addTarget })}</p>
        )}
        {tooMany && (
          <p className="disp-un-note" role="alert" style={{ color: 'var(--danger)' }}>
            {t('dispatch.errors.addMax')}
          </p>
        )}
      </Can>
      {problem && (
        <div className="disp-problems" role="alert">
          <b>{problem.title}</b>
          {problems.length > 0 && (
            <ul>
              {problems.map((p) => (
                <li key={`${p.number}-${p.reason}`}>
                  <span className="mono">{p.number}</span>: {p.reason}
                </li>
              ))}
            </ul>
          )}
          <button type="button" className="btn sm" style={{ marginTop: 8 }} onClick={onDismissProblem}>
            {t('dispatch.dismiss')}
          </button>
        </div>
      )}
      {loading && items.length === 0 ? (
        <Spinner block label={t('common.loading')} />
      ) : failed ? (
        <EmptyState
          title={t('dispatch.unassigned.error')}
          action={
            <button type="button" className="btn sm" onClick={onRetry}>
              {t('common.retry')}
            </button>
          }
        />
      ) : rows.length === 0 ? (
        <EmptyState icon={<IconCheck />} title={filtered || q ? t('dispatch.unassigned.emptyFiltered') : t('dispatch.unassigned.empty')} />
      ) : (
        <div className="disp-un-list">
          {rows.map((o) => {
            const id = o.publicId ?? ''
            const town = [o.city, o.postalCode].filter(Boolean).join(' · ')
            const zone = o.zoneAmbiguous ? t('dispatch.unassigned.zoneAmbiguous') : (o.zoneCode ?? t('dispatch.unassigned.noZone'))
            return (
              <label key={id} className={failedNumbers.has(o.orderNumber ?? '') ? 'unrow err' : 'unrow'}>
                <input type="checkbox" checked={live.includes(id)} onChange={(e) => toggle(id, e.target.checked)} aria-label={o.orderNumber ?? ''} />
                <span className="ref">{o.orderNumber}</span>
                <span className="un-body">
                  <span className="un-name">{o.consigneeName || o.clientName}</span>
                  <span className="meta">
                    {[town, zone].filter(Boolean).join(' · ')}
                  </span>
                </span>
              </label>
            )
          })}
        </div>
      )}
      {total > items.length && <p className="disp-un-note">{t('dispatch.unassigned.more', { shown: items.length, total })}</p>}
    </Panel>
  )
}
