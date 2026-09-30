// Pieza "Recibo" — `/warehouse/receipts` (Lote F6; Lote 13: maestro-detalle de la maqueta `recibo()`, requisitos del dueño
// en Cambios.pdf pp. 9-11). Lectura: inventory.view + WMS_LOTSERIAL (por la ruta).
// - Pestaña Recibos (sin parámetro): filtros al API (`ReceiptFilterBar`), a la izquierda la lista de recibos
//   (`ReceiptMasterList`, 340 px) y a la derecha el detalle del elegido (`ReceiptDetailPanel`: líneas, notas, Confirmar y
//   tareas de acomodo). El elegido va en `?receipt=<publicId>` (sin él, el primero); la ruta vieja
//   `/warehouse/receipts/:publicId` redirige aquí. Doble clic en la lista o el lápiz del detalle: `ReceiptHeaderModal`.
// - "Nuevo recibo" (warehouse.receive) abre el mismo modal en alta: al guardar, el recibo queda primero en la lista, elegido
//   y con su detalle listo para capturar (vacío si es ciego; con las líneas del documento si nació de un aviso u OC), aunque
//   los filtros lo excluyan (`withPinned`), hasta que se cambie un filtro.
// - `?tab=asns`: avisos de llegada (`AsnsTab`); "Recibir" abre el modal con el aviso precargado.
// - `?tab=putaway`: 'Acomodo pendiente' (`PutawayPendingTab`): recibos confirmados con tareas de acomodo abiertas.
// Al cambiar de pestaña se quita `?receipt=`. Estatus: Esperado → Recibiendo ⇄ Discrepancia → Completado / Completado con
// diferencia → Acomodado (los cambia el servidor; manual 06 §4).
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { IconCheckin, Panel, Tabs } from '../../kernel/ui'
import { useReceipt, type AsnDto, type ReceiptDetailDto, type ReceiptListItemDto } from './api'
import { AsnsTab } from './AsnsTab'
import { PutawayPendingTab } from './PutawayPendingTab'
import { ReceiptDetailPanel } from './ReceiptDetailPanel'
import { ReceiptFilterBar } from './ReceiptFilterBar'
import { selectedReceiptId, withPinned, type ReceiptFilterState } from './receiptFilters'
import { ReceiptHeaderModal, type ReceiptHeaderModalProps } from './ReceiptHeaderModal'
import { ReceiptMasterList } from './ReceiptMasterList'
import { useReceiptList } from './useReceiptList'
import './warehouse.css'

const TAB_KEYS = ['receipts', 'asns', 'putaway'] as const
type TabKey = (typeof TAB_KEYS)[number]
const isTabKey = (v: string | null): v is TabKey => (TAB_KEYS as readonly string[]).includes(v ?? '')
const NO_ITEMS: ReceiptListItemDto[] = []

type HeaderModalState = null | { mode: 'create'; preset?: ReceiptHeaderModalProps['preset'] } | { mode: 'edit'; publicId: string }

// =====================================================================================================================
// Pestaña Recibos: filtros + lista (izquierda) + detalle del elegido (derecha)
// =====================================================================================================================
function ReceiptsTab({
  selectedParam,
  onSelect,
  onOpenHeader,
  pinned,
  onUnpin,
}: {
  selectedParam: string | null
  onSelect: (publicId: string) => void
  onOpenHeader: (publicId: string) => void
  /** Recibo recién creado: primero en la lista aunque los filtros lo excluyan. */
  pinned: string | null
  onUnpin: () => void
}) {
  const t = useT()
  const l = useReceiptList()
  const pinnedDetail = useReceipt(pinned)

  // al fijar un recibo recién creado se vuelve a la página 1 (donde se ve primero)
  const [prevPinned, setPrevPinned] = useState(pinned)
  if (pinned !== prevPinned) {
    setPrevPinned(pinned)
    if (pinned) l.setPage(1)
  }

  const pinnedRow = pinned ? (pinnedDetail.data?.header ?? null) : null
  const serverItems = l.list.data?.items ?? NO_ITEMS
  const items = useMemo(() => (l.page === 1 ? withPinned(serverItems, pinnedRow) : serverItems), [serverItems, pinnedRow, l.page])
  const selected = selectedReceiptId(items, selectedParam)

  const setFilters = (f: ReceiptFilterState) => {
    onUnpin()
    l.setFilters(f)
  }
  const setQ = (q: string) => {
    onUnpin()
    l.setQ(q)
  }

  return (
    <>
      <ReceiptFilterBar value={l.filters} onChange={setFilters} />
      <div className="rcp-cols">
        <ReceiptMasterList
          title={t('warehouse.receipts.title')}
          items={items}
          total={l.list.data?.total}
          loading={l.list.isLoading}
          error={l.list.error}
          selectedId={selected}
          onSelect={onSelect}
          onOpen={onOpenHeader}
          q={l.q}
          onQ={setQ}
          page={l.page}
          pageSize={l.pageSize}
          onPage={l.setPage}
          onPageSize={l.setPageSize}
          exportRows={l.exportRows}
        />
        <div className="rcp-side">
          {selected ? (
            <ReceiptDetailPanel publicId={selected} onEditHeader={() => onOpenHeader(selected)} />
          ) : (
            <Panel flush>
              <div className="empty rcp-empty lg">
                <div>
                  <IconCheckin />
                  <p>{t('warehouse.receipts.detail.select')}</p>
                </div>
              </div>
            </Panel>
          )}
        </div>
      </div>
    </>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function ReceiptListScreen() {
  const t = useT()
  // pestaña (?tab=asns|putaway) y recibo elegido (?receipt=) en la URL: se pueden enlazar (Actividad reciente, avisos)
  const [params, setParams] = useSearchParams()
  const raw = params.get('tab')
  const tab: TabKey = isTabKey(raw) ? raw : 'receipts'
  const receiptParam = params.get('receipt')
  const [modal, setModal] = useState<HeaderModalState>(null)
  const [pinned, setPinned] = useState<string | null>(null)

  // al cambiar de pestaña se quita el recibo elegido
  const setTab = (key: TabKey) => setParams(key === 'receipts' ? {} : { tab: key }, { replace: true })
  const select = (publicId: string) => {
    const next = new URLSearchParams(params)
    next.set('receipt', publicId)
    setParams(next, { replace: true })
  }
  const openHeader = (publicId: string) => setModal({ mode: 'edit', publicId })

  const onCreated = (created: ReceiptDetailDto) => {
    const id = created.header?.publicId
    if (!id) return
    // queda primero y elegido en la pestaña Recibos, con su detalle listo para capturar
    setPinned(id)
    setParams({ receipt: id })
  }
  const onDeleted = (publicId: string) => {
    if (pinned === publicId) setPinned(null)
    if (receiptParam === publicId) {
      const next = new URLSearchParams(params)
      next.delete('receipt')
      setParams(next, { replace: true })
    }
  }
  const receiveAsn = (a: AsnDto) =>
    setModal({ mode: 'create', preset: { asnId: a.id ?? 0, warehousePublicId: a.warehousePublicId ?? null, warehouseCode: a.warehouseCode ?? null } })

  return (
    <div className="wrap rcp">
      <div className="head">
        <div>
          <h1>{t('warehouse.receipts.title')}</h1>
          <p>{t('warehouse.receipts.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.receive">
            <button type="button" className="btn flow" onClick={() => setModal({ mode: 'create' })}>
              {t('warehouse.receipts.new')}
            </button>
          </Can>
        </div>
      </div>

      <div className="rcp-tabs">
        <Tabs<TabKey>
          label={t('warehouse.receipts.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'receipts', label: t('warehouse.receipts.tabReceipts') },
            { key: 'asns', label: t('warehouse.receipts.tabAsns') },
            { key: 'putaway', label: t('warehouse.receipts.tabPutaway') },
          ]}
        />
      </div>

      {tab === 'receipts' && (
        <ReceiptsTab selectedParam={receiptParam} onSelect={select} onOpenHeader={openHeader} pinned={pinned} onUnpin={() => setPinned(null)} />
      )}
      {tab === 'asns' && <AsnsTab onReceive={receiveAsn} />}
      {tab === 'putaway' && <PutawayPendingTab selectedParam={receiptParam} onSelect={select} onOpenHeader={openHeader} />}

      {modal !== null && (
        <ReceiptHeaderModal
          publicId={modal.mode === 'edit' ? modal.publicId : null}
          preset={modal.mode === 'create' ? modal.preset : undefined}
          onClose={() => setModal(null)}
          onCreated={onCreated}
          onDeleted={onDeleted}
        />
      )}
    </div>
  )
}
