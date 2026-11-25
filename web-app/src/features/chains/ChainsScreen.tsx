// Lote 245 — Listas › Cadenas (Walgreens, CVS…): maestro-detalle como Clientes. A la izquierda la lista (buscador libre, «Ver inactivas», columnas Nombre · Clientes ·
// Notas · Activa) y a la derecha la ficha en panel (`?id=<publicId>`). Alta con sales.chain; ver con sales.chain|clients.read|sales.view. Módulo Órdenes de venta.
// Una cadena NO es una ruta: no cambia la ruta del cliente.
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, FilterScope, Panel, QBox, Spinner, SplitPane, type DataColumn } from '../../kernel/ui'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { useDebounced } from '../warehouse/lineRules'
import { ChainDetailPanel } from './ChainDetailPanel'
import { ChainCreateModal } from './ChainFormModal'
import { CHAIN_MANAGE, useChains, type ChainListItem } from './chainsApi'
import './chains.css'

const NO_CHAINS: ChainListItem[] = []

export default function ChainsScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const [q, setQ] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [creating, setCreating] = useState(false)
  const search = useDebounced(q.trim(), 250)
  const list = useChains(includeInactive, search)
  const rows = list.data ?? NO_CHAINS
  const selected = params.get('id') ?? rows[0]?.publicId ?? null

  const select = (publicId: string) => {
    const next = new URLSearchParams(params)
    next.set('id', publicId)
    setParams(next, { replace: true })
  }

  const columns = useMemo<DataColumn<ChainListItem>[]>(
    () => [
      { id: 'name', header: t('chains.col.name'), cell: (c) => <b className="chn-name">{c.name}</b>, sortValue: (c) => c.name ?? '', exportValue: (c) => c.name ?? '', card: 'title' },
      { id: 'clients', header: t('chains.col.clients'), cell: (c) => c.clientCount ?? 0, sortValue: (c) => c.clientCount ?? 0, align: 'end' },
      { id: 'notes', header: t('chains.col.notes'), cell: (c) => <span className="chn-notes" title={c.notes ?? undefined}>{c.notes ?? '—'}</span>, sortValue: (c) => c.notes ?? '', exportValue: (c) => c.notes ?? '' },
      {
        id: 'active',
        header: t('chains.col.active'),
        cell: (c) => <Chip tone={c.isActive ? 'cap' : 'warn'}>{c.isActive ? t('chains.active') : t('chains.inactive')}</Chip>,
        sortValue: (c) => (c.isActive ? 1 : 0),
        exportValue: (c) => (c.isActive ? t('chains.active') : t('chains.inactive')),
      },
    ],
    [t],
  )

  return (
    <div className="wrap chn">
      <div className="head">
        <div>
          <h1>{t('chains.title')}</h1>
          <p>{t('chains.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm={CHAIN_MANAGE}>
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('chains.new')}
            </button>
          </Can>
        </div>
      </div>

      <SplitPane storageKey="chains" defaultRatio={0.42} minRatio={0.3} maxRatio={0.6} minPx={[300, 440]} label={t('chains.splitLabel')} className="chn-split">
        <Panel flush icon={<IconLayers />} title={t('chains.list')} badge={list.isPending ? undefined : rows.length}>
          <div className="qrow chn-q">
            <QBox value={q} onChange={setQ} placeholder={t('chains.searchPlaceholder')} />
            <label className="sw chn-inactive">
              <input type="checkbox" role="switch" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('chains.showInactive')}</span>
            </label>
          </div>
          {list.isError ? (
            <p className="pb ferr" role="alert">
              {applyProblemDetails(list.error).title}
            </p>
          ) : list.isPending ? (
            <Spinner block />
          ) : (
            <DataTable
              label={t('chains.list')}
              columns={columns}
              rows={rows}
              rowKey={(c) => c.publicId ?? ''}
              rowClassName={(c) => ['chn-row', c.publicId === selected ? 'chn-on' : '', c.isActive ? '' : 'chn-dim'].filter(Boolean).join(' ')}
              onRowClick={(c) => c.publicId && select(c.publicId)}
              defaultSort={{ id: 'name', desc: false }}
              dense
              empty={search ? t('chains.noResults') : t('chains.empty')}
            />
          )}
        </Panel>
        <div className="chn-detail">
          <FilterScope>
            {selected ? (
              <ChainDetailPanel key={selected} publicId={selected} />
            ) : (
              <Panel>
                <EmptyState icon={<IconLayers />} title={list.isPending ? t('common.loading') : t('chains.selectChain')} />
              </Panel>
            )}
          </FilterScope>
        </div>
      </SplitPane>

      <ChainCreateModal
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(publicId) => {
          setQ('')
          select(publicId)
        }}
      />
    </div>
  )
}
