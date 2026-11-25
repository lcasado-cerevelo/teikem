// Lote 245 — «Clientes de la cadena»: tabla paginada por el servidor (con `exportRows`: ordenar y exportar leen TODO el filtro), agregar clientes (se acumulan y
// luego «Agregar N»; los que ya están en otra cadena se preguntan: «¿moverlos?») y quitar en bloque. Administrar con sales.chain.
import { useMemo, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, ClientSearchSelect, ConfirmDialog, DataTable, Panel, QBox, Spinner, toast, type ClientFilterItem, type DataColumn } from '../../kernel/ui'
import { IconUsers } from '../../kernel/ui/screenIcons'
import { useDebounced } from '../warehouse/lineRules'
import { skippedCount } from './chainRules'
import {
  CHAIN_MANAGE,
  fetchAllChainClients,
  useAssignChainClients,
  useChainClients,
  useRemoveChainClients,
  type ChainClient,
  type ChainDetail,
  type ChainSkipped,
} from './chainsApi'

export function ChainClientsPanel({ chain }: { chain: ChainDetail }) {
  const t = useT()
  const publicId = chain.publicId ?? ''
  const [q, setQ] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const [pending, setPending] = useState<ClientFilterItem[]>([])
  const [skipped, setSkipped] = useState<ChainSkipped[] | null>(null)
  const [removing, setRemoving] = useState(false)
  const search = useDebounced(q.trim(), 250)
  const query = useMemo(() => ({ q: search, includeInactive }), [search, includeInactive])
  const list = useChainClients(publicId, query, (page - 1) * pageSize, pageSize)
  const assign = useAssignChainClients(publicId)
  const remove = useRemoveChainClients(publicId)

  const toggle = (id: string) =>
    setSelected((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })

  const columns = useMemo<DataColumn<ChainClient>[]>(
    () => [
      {
        id: 'pick',
        header: '',
        sortable: false,
        exportable: false,
        cell: (c) => (
          <Can perm={CHAIN_MANAGE}>
            <input
              type="checkbox"
              className="chn-check"
              aria-label={t('chains.clients.pick', { name: c.name ?? '' })}
              checked={selected.has(c.clientPublicId ?? '')}
              onChange={() => toggle(c.clientPublicId ?? '')}
              onClick={(e) => e.stopPropagation()}
            />
          </Can>
        ),
      },
      { id: 'code', header: t('chains.clients.code'), cell: (c) => <span className="ref">{c.code}</span>, sortValue: (c) => c.code ?? '', exportValue: (c) => c.code ?? '' },
      { id: 'name', header: t('chains.clients.name'), cell: (c) => <b className="chn-name">{c.name}</b>, sortValue: (c) => c.name ?? '', exportValue: (c) => c.name ?? '', card: 'title' },
      {
        id: 'status',
        header: t('chains.clients.status'),
        cell: (c) => <Chip tone={c.isActive ? 'cap' : 'warn'}>{c.statusLabel}</Chip>,
        sortValue: (c) => c.statusLabel ?? '',
        exportValue: (c) => c.statusLabel ?? '',
      },
      { id: 'rep', header: t('chains.clients.rep'), cell: (c) => c.salesRepName ?? '—', sortValue: (c) => c.salesRepName ?? '', exportValue: (c) => c.salesRepName ?? '' },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, selected],
  )

  const addPending = async (moveFromOtherChains: boolean, ids: string[]) => {
    const r = await assign.mutateAsync({ clientPublicIds: ids, moveFromOtherChains })
    return r
  }

  const onAdd = async () => {
    const ids = pending.map((p) => p.publicId)
    try {
      const r = await addPending(false, ids)
      const done = (r.assigned ?? 0) + (r.moved ?? 0)
      setPending([])
      if (done > 0 || (r.alreadyInChain ?? 0) > 0) toast.success(t('chains.clients.added', { n: done, already: r.alreadyInChain ?? 0 }))
      if (skippedCount(r) > 0) setSkipped([...(r.skippedOtherChain ?? [])])
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  return (
    <Panel
      icon={<IconUsers />}
      title={t('chains.clients.title')}
      badge={list.data?.total}
      subtitle={t('chains.clients.subtitle')}
      flush
    >
      <Can perm={CHAIN_MANAGE}>
        {chain.isActive && (
          <div className="chn-add">
            <ClientSearchSelect label={t('chains.clients.addLabel')} value={pending} onChange={setPending} placeholder={t('chains.clients.addPlaceholder')} />
            <button type="button" className="btn flow" disabled={pending.length === 0 || assign.isPending} onClick={() => void onAdd()}>
              {t('chains.clients.addN', { n: pending.length })}
            </button>
          </div>
        )}
      </Can>
      <div className="qrow chn-q">
        <QBox
          value={q}
          onChange={(v) => {
            setQ(v)
            setPage(1)
          }}
          placeholder={t('chains.clients.search')}
        />
        <label className="sw chn-inactive">
          <input
            type="checkbox"
            role="switch"
            checked={includeInactive}
            onChange={(e) => {
              setIncludeInactive(e.target.checked)
              setPage(1)
            }}
          />
          <span className="tk" aria-hidden="true" />
          <span>{t('chains.clients.showInactive')}</span>
        </label>
        <Can perm={CHAIN_MANAGE}>
          <button type="button" className="btn danger sm" disabled={selected.size === 0} onClick={() => setRemoving(true)}>
            {t('chains.clients.removeN', { n: selected.size })}
          </button>
        </Can>
      </div>
      {list.isError ? (
        <p className="pb ferr" role="alert">
          {applyProblemDetails(list.error).title}
        </p>
      ) : list.isPending ? (
        <Spinner block />
      ) : (
        <DataTable
          label={t('chains.clients.title')}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(c) => c.clientPublicId ?? ''}
          page={page}
          pageSize={pageSize}
          total={list.data?.total ?? 0}
          onPage={setPage}
          onPageSize={(s) => {
            setPageSize(s)
            setPage(1)
          }}
          dense
          empty={t('chains.clients.empty')}
          exportRows={() => fetchAllChainClients(publicId, query)}
          exportFileName={`chain-clients-${chain.name ?? ''}`}
        />
      )}

      <ConfirmDialog
        open={skipped !== null}
        title={t('chains.clients.moveTitle')}
        message={
          <>
            <p>{t('chains.clients.moveMessage', { n: skipped?.length ?? 0 })}</p>
            <ul className="chn-skipped">
              {(skipped ?? []).slice(0, 8).map((s) => (
                <li key={s.clientPublicId}>
                  {s.code} · {s.name} <span className="sa-muted">({s.chainName})</span>
                </li>
              ))}
              {(skipped?.length ?? 0) > 8 && <li>{t('chains.clients.andMore', { n: (skipped?.length ?? 0) - 8 })}</li>}
            </ul>
          </>
        }
        confirmLabel={t('chains.clients.move')}
        onClose={() => setSkipped(null)}
        onConfirm={async () => {
          const ids = (skipped ?? []).map((s) => s.clientPublicId ?? '')
          const r = await addPending(true, ids)
          toast.success(t('chains.clients.moved', { n: r.moved ?? 0 }))
        }}
      />
      <ConfirmDialog
        open={removing}
        title={t('chains.clients.removeTitle')}
        message={t('chains.clients.removeMessage', { n: selected.size, name: chain.name ?? '' })}
        confirmLabel={t('chains.clients.remove')}
        tone="danger"
        onClose={() => setRemoving(false)}
        onConfirm={async () => {
          const r = await remove.mutateAsync([...selected])
          setSelected(new Set())
          toast.success(t('chains.clients.removed', { n: r.removed ?? 0 }))
        }}
      />
    </Panel>
  )
}
