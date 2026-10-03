// Pieza "Conteo cíclico" (Lote F6; Lote 14 P8: dos paneles de la maqueta `conteo()`, Cambios.pdf pp. 14-15, D2-D4 y D7-D10)
// — `/warehouse/cycle-counts`. Lectura: inventory.view + WMS_LOTSERIAL (por la ruta).
// - Filtros arriba, todos al API (`CycleCountFilterBar`): almacén, zona, posición, producto, estatus, origen, creado (días
//   locales de la compañía) y buscador.
// - Dos paneles simultáneos en un `SplitPane` (`storageKey="cycle-counts"`, 34/66, barra arrastrable; bajo 900 px uno debajo
//   del otro): a la izquierda la lista de conteos paginada con el total (`CountTaskList`: estatus con su color, posición o
//   zona, origen, asignado; íconos Asignar y Eliminar), a la derecha el conteo elegido (`CountDetailPanel`: todas las
//   líneas con lo esperado, captura en la fila, escáner y "Confirmar conteo y ajustar" en un paso).
// - Filtros iniciales de la URL (Lote 15, `countFiltersFromUrl`): `warehousePublicIds`, `status`, `origins`, `from`/`to`.
// - El elegido va en `?count=<id>` (sin él, el primero de la lista; lo usan el Kárdex, la Actividad reciente y la ficha vieja
//   `/warehouse/cycle-counts/:id`, que redirige aquí). Ya no hay pestaña 'Tareas de conteo' (D10: se asigna desde la lista).
// - Cabecera: "Conteo de lo cambiado" (`ChangedCountModal`) y "Nuevo conteo" (`CreateCountModal`), ambos warehouse.count;
//   al crear, la lista vuelve a la página 1 y queda elegido el primero creado.
// Estatus (D7): Pendiente → Contado (solo a ciegas, desde la app) → Concordancia / Diferencia. Manual 06 §6.
// Lote F12 (conteo por producto): pestaña "Por revisar" (`?tab=review`, solo con warehouse.count; sin él la pestaña no se pinta
// y `?tab=review` abre la lista de siempre) con los conteos Contados, "Cerrar los que cuadran" y el conteo elegido a la derecha
// (`CountReviewList` + `CountDetailPanel` en modo revisión).
import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { FilterScope, SplitPane, Tabs } from '../../kernel/ui'
import { exportCycleCounts, useCycleCountsPage, type CycleCountDto } from './api'
import { ChangedCountModal } from './ChangedCountModal'
import { CountDetailPanel } from './CountDetailPanel'
import { CountReviewTab } from './CountReviewTab'
import { countTabFromParam, type CountTabKey as TabKey } from './countReview'
import { CountTaskList } from './CountTaskList'
import { countFilterQuery, countFiltersFromUrl, countListQuery, countParam, selectedCountId, type CountFilterState } from './countView'
import { CreateCountModal } from './CreateCountModal'
import { CycleCountFilterBar } from './CycleCountFilterBar'
import { useDebounced } from './lineRules'
import './warehouse.css'

const NO_ITEMS: CycleCountDto[] = []
const DEFAULT_PAGE_SIZE = 25


export default function CycleCountListScreen() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const countId = countParam(params)
  const canReview = useCan('warehouse.count')
  const tab = countTabFromParam(params.get('tab'), canReview)
  // al cambiar de pestaña se quita el conteo elegido (cada pestaña elige el suyo)
  const setTab = (key: TabKey) => setParams(key === 'counts' ? {} : { tab: key }, { replace: true })
  // Lote 15: filtros iniciales de la URL (una vez al montar; la franja "Almacén hoy" del Pulso manda estatus y almacén)
  const [filters, setFiltersState] = useState<CountFilterState>(() => countFiltersFromUrl(params))
  const [q, setQState] = useState('')
  const search = useDebounced(q.trim())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [creating, setCreating] = useState<null | 'manual' | 'changes'>(null)

  const effective = useMemo(() => ({ ...filters, search }), [filters, search])
  const query = useMemo(() => countListQuery(effective, page, pageSize), [effective, page, pageSize])
  // la lista de siempre solo se pide en su pestaña
  const list = useCycleCountsPage(query, { enabled: tab === 'counts' })
  const items = list.data?.items ?? NO_ITEMS
  const selected = selectedCountId(items, countId)

  const setFilters = useCallback(
    (f: CountFilterState) => {
      setFiltersState(f)
      setPage(1)
    },
    [setFiltersState, setPage],
  )
  const setQ = (v: string) => {
    setQState(v)
    setPage(1)
  }
  const select = (id: number) => {
    const next = new URLSearchParams(params)
    next.set('count', String(id))
    setParams(next, { replace: true })
  }
  const clearSelection = (id: number) => {
    if (countId !== id) return
    const next = new URLSearchParams(params)
    next.delete('count')
    setParams(next, { replace: true })
  }
  // al crear: la lista vuelve a la página 1 y queda elegido el primer conteo creado
  const onCreated = (first: number | null | undefined) => {
    setPage(1)
    // desde "Por revisar" se vuelve a la lista de siempre, con el creado elegido
    if (first != null) setParams({ count: String(first) }, { replace: true })
    else if (tab !== 'counts') setTab('counts')
  }

  const singleWarehouse = filters.warehousePublicIds.length === 1 ? filters.warehousePublicIds[0] : null

  return (
    <div className="wrap cc">
      <div className="head">
        <div>
          <h1>{t('warehouse.cycleCounts.title')}</h1>
          <p>{t('warehouse.cycleCounts.subtitle')}</p>
        </div>
        <div className="act">
          <span className="tag">{t('warehouse.cycleCounts.modeTag')}</span>
          <Can perm="warehouse.count">
            <button type="button" className="btn" onClick={() => setCreating('changes')}>
              {t('warehouse.cycleCounts.newChanges')}
            </button>
            <button type="button" className="btn flow" onClick={() => setCreating('manual')}>
              {t('warehouse.cycleCounts.new')}
            </button>
          </Can>
        </div>
      </div>

      <p className="note cc-intro">{t('warehouse.cycleCounts.intro')}</p>

      {canReview && (
        <div className="rcp-tabs cc-tabs">
          <Tabs<TabKey>
            label={t('warehouse.cycleCounts.title')}
            value={tab}
            onChange={setTab}
            tabs={[
              { key: 'counts', label: t('warehouse.cycleCounts.tabs.counts') },
              { key: 'review', label: t('warehouse.cycleCounts.tabs.review') },
            ]}
          />
        </div>
      )}

      {tab === 'review' ? (
        <CountReviewTab countId={countId} onSelect={select} />
      ) : (
        <CountsTab
          filters={filters}
          setFilters={setFilters}
          q={q}
          setQ={setQ}
          list={list}
          items={items}
          selected={selected}
          select={select}
          clearSelection={clearSelection}
          page={page}
          setPage={setPage}
          pageSize={pageSize}
          setPageSize={setPageSize}
          effective={effective}
        />
      )}

      {creating === 'manual' && <CreateCountModal onClose={() => setCreating(null)} onCreated={(c) => onCreated(c.count?.id)} />}
      {creating === 'changes' && (
        <ChangedCountModal initialWarehousePublicId={singleWarehouse} onClose={() => setCreating(null)} onCreated={(counts) => onCreated(counts[0]?.id)} />
      )}
    </div>
  )
}

interface CountsTabProps {
  filters: CountFilterState
  setFilters: (f: CountFilterState) => void
  q: string
  setQ: (v: string) => void
  list: ReturnType<typeof useCycleCountsPage>
  items: readonly CycleCountDto[]
  selected: number | null
  select: (id: number) => void
  clearSelection: (id: number) => void
  page: number
  setPage: (n: number) => void
  pageSize: number
  setPageSize: (n: number) => void
  effective: CountFilterState
}

/** Pestaña de siempre: filtros arriba, la lista de conteos y el elegido (Lote 14). */
function CountsTab({ filters, setFilters, q, setQ, list, items, selected, select, clearSelection, page, setPage, pageSize, setPageSize, effective }: CountsTabProps) {
  const t = useT()
  return (
    <>
      <CycleCountFilterBar value={filters} onChange={setFilters} q={q} onQ={setQ} />

      <SplitPane
        storageKey="cycle-counts"
        defaultRatio={0.34}
        minRatio={0.25}
        maxRatio={0.6}
        minPx={[300, 480]}
        label={t('warehouse.cycleCounts.splitLabel')}
        className="cc-split"
      >
        <CountTaskList
          items={items}
          total={list.data?.total}
          loading={list.isLoading}
          error={list.error}
          selectedId={selected}
          onSelect={select}
          onDeleted={clearSelection}
          page={page}
          pageSize={pageSize}
          onPage={setPage}
          onPageSize={(n) => {
            setPageSize(n)
            setPage(1)
          }}
          exportRows={() => exportCycleCounts(countFilterQuery(effective))}
        />
        {/* ámbito propio: las líneas del conteo elegido no dependen de los filtros de la lista (sin línea de filtros) */}
        <FilterScope>
          <CountDetailPanel id={selected} />
        </FilterScope>
      </SplitPane>
    </>
  )
}
