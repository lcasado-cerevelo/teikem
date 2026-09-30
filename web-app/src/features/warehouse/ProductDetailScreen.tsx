// Pantalla B (Lote F6) — vista de un producto. `/warehouse/products/:publicId`, pestañas Lotes / Series (solo lectura).
// Desde la Fase 5 de la reconciliación con la maqueta los datos del producto se ven y se editan SOLO en el modal único
// ProductEditorModal (la maqueta no tiene ficha aparte): al entrar sin `?tab=` (p. ej. un enlace de Actividad reciente) el
// modal se abre solo; "Editar producto" lo vuelve a abrir. `?tab=lots|serials` (lo que usan "Ver lotes"/"Ver series" del
// modal) abre esa pestaña sin el modal. Aquí la pestaña siempre va en la URL: sin parámetro significa "abrir el modal".
import { useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { StatusChip, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, Filters, Panel, QBox, SelectFilter, Spinner, Tabs, matchesQ, type DataColumn } from '../../kernel/ui'
import { useProduct, useProductLots, useProductSerials, type LotDto, type SerialDto } from './api'
import { ProductEditorModal } from './ProductEditorModal'
import { IconLayers } from '../../kernel/ui/screenIcons'

type TabKey = 'lots' | 'serials'

// ---- Pestaña Lotes ----
function LotsTab({ publicId }: { publicId: string }) {
  const t = useT()
  const { data: lots = [], isLoading } = useProductLots(publicId)
  const columns = useMemo<DataColumn<LotDto>[]>(
    () => [
      { id: 'lotNumber', header: t('warehouse.products.lots.lotNumber'), cell: (l) => l.lotNumber, sortValue: (l) => l.lotNumber, card: 'title' },
      { id: 'expiryDate', header: t('warehouse.products.lots.expiryDate'), cell: (l) => l.expiryDate ?? '', sortValue: (l) => l.expiryDate },
      {
        id: 'active',
        header: t('warehouse.products.fields.active'),
        cell: (l) => <Chip tone={l.isActive ? 'neutral' : 'fail'}>{l.isActive ? t('warehouse.products.active') : t('warehouse.products.inactive')}</Chip>,
        sortValue: (l) => l.isActive,
      },
    ],
    [t],
  )
  return (
    <DataTable
      label={t('warehouse.products.tabLots')}
      columns={columns}
      rows={lots}
      rowKey={(l) => l.id ?? 0}
      loading={isLoading}
      pageSize={25}
      empty={<EmptyState title={t('warehouse.products.lots.empty')} />}
    />
  )
}

// ---- Pestaña Series ----
function SerialsTab({ publicId }: { publicId: string }) {
  const t = useT()
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const { data: statuses = [] } = useStatuses('SerialStatus')
  const { data: serials = [], isLoading } = useProductSerials(publicId, { status: status || undefined })
  const rows = useMemo(() => serials.filter((s) => matchesQ(q, s.serialNumber, s.binCode)), [serials, q])

  const columns = useMemo<DataColumn<SerialDto>[]>(
    () => [
      { id: 'serialNumber', header: t('warehouse.products.serials.serialNumber'), cell: (s) => s.serialNumber, sortValue: (s) => s.serialNumber, card: 'title' },
      {
        id: 'status',
        header: t('warehouse.products.serials.status'),
        cell: (s) => <StatusChip domain="SerialStatus" code={s.statusCode} label={s.status} />,
        sortValue: (s) => s.status ?? s.statusCode,
      },
      { id: 'bin', header: t('warehouse.products.serials.bin'), cell: (s) => s.binCode ?? '', sortValue: (s) => s.binCode },
    ],
    [t],
  )

  return (
    <>
      <Filters onClear={() => setStatus('')}>
        <SelectFilter
          label={t('warehouse.products.serials.status')}
          value={status}
          onChange={setStatus}
          options={statuses.map((s) => ({ value: s.code, label: s.label }))}
        />
      </Filters>
      <div className="qrow">
        <QBox value={q} onChange={setQ} />
      </div>
      <DataTable
        label={t('warehouse.products.tabSerials')}
        columns={columns}
        rows={rows}
        rowKey={(s) => s.id ?? 0}
        loading={isLoading}
        pageSize={25}
        empty={<EmptyState title={t('warehouse.products.serials.empty')} />}
      />
    </>
  )
}

// ---- Pantalla ----
export default function ProductDetailScreen() {
  const t = useT()
  const { publicId = '' } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const canManage = useCan('inventory.manage')
  const { data: detail, isLoading, error } = useProduct(publicId)
  const tab: TabKey = searchParams.get('tab') === 'serials' ? 'serials' : 'lots'
  // sin `?tab=` al entrar, el producto se muestra en su modal (la maqueta no tiene ficha aparte)
  const [editing, setEditing] = useState(() => !searchParams.has('tab'))

  if (isLoading) return <Spinner block />
  if (error || !detail || !detail.product) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.products.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/products">
            {t('warehouse.products.back')}
          </Link>
        }
      />
    )
  }
  const product = detail.product

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{product.sku}</span> · {product.name}
          </h1>
          <p>{!product.isActive && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}</p>
        </div>
        <div className="act">
          <Link className="btn" to="/warehouse/products">
            {t('warehouse.products.back')}
          </Link>
          <button type="button" className="btn flow" onClick={() => setEditing(true)}>
            {canManage ? t('warehouse.products.editor.editTitle') : t('warehouse.products.editor.viewData')}
          </button>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('warehouse.products.title')}
          value={tab}
          onChange={(next) => setSearchParams({ tab: next }, { replace: true })}
          tabs={[
            { key: 'lots', label: t('warehouse.products.tabLots') },
            { key: 'serials', label: t('warehouse.products.tabSerials') },
          ]}
        />
      </div>

      {tab === 'lots' && (
        <Panel flush icon={<IconLayers />} title={t('warehouse.products.tabLots')}>
          <LotsTab publicId={publicId} />
        </Panel>
      )}
      {tab === 'serials' && (
        <Panel flush icon={<IconLayers />} title={t('warehouse.products.tabSerials')}>
          <SerialsTab publicId={publicId} />
        </Panel>
      )}

      <ProductEditorModal open={editing} product={detail} onClose={() => setEditing(false)} />
    </div>
  )
}
