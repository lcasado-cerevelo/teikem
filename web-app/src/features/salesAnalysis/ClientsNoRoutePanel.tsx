// Lote 244 — «Clientes sin ruta de venta»: lista paginada por el servidor (con exportRows: ordenar/exportar toma TODO lo filtrado) y
// asignación en bloque de la ruta propia. Un cliente sin ruta propia puede tener la de su representante; aquí se ven las dos.
import { useEffect, useState } from 'react'
import { useCan } from '../../kernel/access'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { DataTable, type DataColumn } from '../../kernel/ui/DataTable'
import { Filters, SelectFilter } from '../../kernel/ui/Filters'
import { Panel } from '../../kernel/ui/Panel'
import { QBox } from '../../kernel/ui/QBox'
import { toast } from '../../kernel/ui/toast'
import { fetchSalesClients, useAssignClientsRoute, useSalesClients, useSalesRoutes, type ClientRepQuery } from './salesAnalysisApi'
import { SALES_ANALYSIS_MANAGE, type SalesClientRep } from './salesAnalysisRules'
import { QueryError } from './SaParts'
import './saRouteSetup.css'

const NO_ROWS: never[] = []
const MAX_BULK = 2000

export function ClientsNoRoutePanel() {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const routes = useSalesRoutes()
  const assign = useAssignClientsRoute()
  const [scope, setScope] = useState<'noRoute' | 'all'>('noRoute')
  const [text, setText] = useState('')
  const [q, setQ] = useState('')
  useEffect(() => {
    const id = setTimeout(() => setQ(text), 300)
    return () => clearTimeout(id)
  }, [text])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query: ClientRepQuery = { unassigned: false, repId: null, noRoute: scope === 'noRoute', q }
  const list = useSalesClients(query, page, pageSize)
  useEffect(() => setPage(1), [scope, q])
  const [selected, setSelected] = useState<Set<number>>(new Set())
  useEffect(() => setSelected(new Set()), [scope, q])
  const [target, setTarget] = useState('')
  const items = list.data?.items ?? NO_ROWS
  const allOnPage = items.length > 0 && items.every((c: SalesClientRep) => selected.has(c.clientId ?? 0))
  const toggle = (id: number) =>
    setSelected((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })
  const columns: DataColumn<SalesClientRep>[] = [
    ...(canManage
      ? [{
          id: 'pick',
          header: t('salesAnalysis.setup.pick'),
          exportable: false,
          cell: (c: SalesClientRep) => <input type="checkbox" aria-label={t('salesAnalysis.routes.clients.pickClient', { name: c.name ?? '' })} checked={selected.has(c.clientId ?? 0)} onChange={() => toggle(c.clientId ?? 0)} />,
        } satisfies DataColumn<SalesClientRep>]
      : []),
    { id: 'code', header: t('salesAnalysis.setup.code'), cell: (c) => <span className="mono">{c.code ?? ''}</span>, sortValue: (c) => c.code ?? '', exportValue: (c) => c.code ?? '' },
    { id: 'name', header: t('salesAnalysis.col.client'), cell: (c) => c.name ?? '', sortValue: (c) => c.name ?? '', card: 'title' },
    { id: 'own', header: t('salesAnalysis.routes.clients.ownRoute'), cell: (c) => c.directRouteName || '—', sortValue: (c) => c.directRouteName ?? '', exportValue: (c) => c.directRouteName ?? '' },
    {
      id: 'route',
      header: t('salesAnalysis.routes.clients.effectiveRoute'),
      cell: (c) =>
        c.effectiveRouteName ? (
          <span className="sa-inline">{c.effectiveRouteName}{c.routeFromRep && <Chip>{t('salesAnalysis.routes.clients.fromRep')}</Chip>}</span>
        ) : (
          <Chip tone="warn">{t('salesAnalysis.routes.clients.noRoute')}</Chip>
        ),
      sortValue: (c) => c.effectiveRouteName ?? '',
      exportValue: (c) => c.effectiveRouteName ?? '',
    },
    { id: 'rep', header: t('salesAnalysis.col.rep'), cell: (c) => c.repName || '—', sortValue: (c) => c.repName ?? '', exportValue: (c) => c.repName ?? '' },
  ]
  const tooMany = selected.size > MAX_BULK
  const doAssign = async () => {
    try {
      const r = await assign.mutateAsync({ clientIds: [...selected], routeId: target === 'none' ? null : Number(target) })
      toast.success(t('salesAnalysis.routes.bulk.assignedN', { n: r.updated ?? 0 }))
      setSelected(new Set())
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const activeRoutes = (routes.data ?? []).filter((r) => r.isActive)
  return (
    <Panel title={t('salesAnalysis.routes.clients.title')} subtitle={t('salesAnalysis.routes.clients.sub')} badge={list.data?.total} flush>
      <div className="pb">
        <Filters>
          <SelectFilter
            label={t('salesAnalysis.setup.show')}
            value={scope}
            allLabel={null}
            onChange={(v) => setScope(v === 'all' ? 'all' : 'noRoute')}
            options={[{ value: 'noRoute', label: t('salesAnalysis.routes.clients.withoutRoute') }, { value: 'all', label: t('salesAnalysis.routes.clients.all') }]}
          />
        </Filters>
        <div className="qrow">
          <QBox value={text} onChange={setText} placeholder={t('salesAnalysis.routes.clients.search')} />
        </div>
        {canManage && (
          <div className="sa-bulk" role="group" aria-label={t('salesAnalysis.setup.bulk')}>
            <label className="sa-check">
              <input
                type="checkbox"
                checked={allOnPage}
                onChange={() =>
                  setSelected((s) => {
                    const n = new Set(s)
                    for (const c of items) {
                      if (allOnPage) n.delete(c.clientId ?? 0)
                      else n.add(c.clientId ?? 0)
                    }
                    return n
                  })
                }
              />{' '}
              {t('salesAnalysis.setup.pickPage')}
            </label>
            <span>{t('salesAnalysis.setup.selected', { n: selected.size })}</span>
            <label htmlFor="sa-route-bulk" className="sr-only">{t('salesAnalysis.routes.clients.pickRoute')}</label>
            <select id="sa-route-bulk" className="sa-route-sel" value={target} onChange={(e) => setTarget(e.target.value)}>
              <option value="">{t('salesAnalysis.routes.clients.pickRoute')}</option>
              {activeRoutes.map((r) => (
                <option key={r.id} value={String(r.id)}>{r.name}</option>
              ))}
              <option value="none">{t('salesAnalysis.routes.bulk.removeRoute')}</option>
            </select>
            <button type="button" className="btn flow sm" disabled={selected.size === 0 || tooMany || !target || assign.isPending} onClick={() => void doAssign()}>
              {t('salesAnalysis.routes.clients.assign')}
            </button>
          </div>
        )}
      </div>
      {list.isError ? (
        <div className="pb"><QueryError error={list.error} /></div>
      ) : (
        <DataTable
          label={t('salesAnalysis.routes.clients.title')}
          columns={columns}
          rows={items}
          rowKey={(c) => String(c.clientId)}
          loading={list.isPending}
          page={page}
          pageSize={pageSize}
          total={list.data?.total ?? 0}
          onPage={setPage}
          onPageSize={(n) => (setPageSize(n), setPage(1))}
          exportRows={() => fetchAllPages((skip, take) => fetchSalesClients(query, skip, take).then((r) => ({ items: r.items ?? [], total: r.total ?? 0 })))}
          empty={scope === 'noRoute' ? t('salesAnalysis.routes.clients.none') : t('salesAnalysis.setup.noClients')}
        />
      )}
    </Panel>
  )
}
