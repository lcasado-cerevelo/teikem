// Lote 68 (kits) — pestaña «Kit» de la ficha del producto. Un kit es un producto que se arma al pedir con otros productos y NO tiene existencia propia: aquí se
// define de qué se compone (componentes y cantidad por kit), si se puede ajustar al pedir, y se ve cuántos se pueden armar por almacén con lo disponible para vender.
// Convertir en kit y editar los componentes exigen inventory.manage; volver a producto normal pide confirmación (el servidor lo niega si algún pedido ya lo usó).
import { useId, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, EmptyState, Modal, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { useKit, useSetKit, type ProductListItemDto } from './api'
import { bestCanBuild, kitDraftError, newKitRow, rowsFromKit, toKitRequest, type KitDto, type KitRow } from './kitRules'
import { ProductPicker } from './pickers'

type Component = NonNullable<KitDto['components']>[number]

export function KitTab({ publicId, product, canManage }: { publicId: string; product: ProductListItemDto; canManage: boolean }) {
  const t = useT()
  const f = useFormat()
  const { data: kit, isLoading } = useKit(publicId)
  const setKit = useSetKit()
  const [editing, setEditing] = useState(false)
  const [reverting, setReverting] = useState(false)

  const columns = useMemo<DataColumn<Component>[]>(
    () => [
      { id: 'sku', header: t('warehouse.products.kit.sku'), cell: (c) => <b>{c.sku}</b>, sortValue: (c) => c.sku ?? '', card: 'title' },
      {
        id: 'name',
        header: t('warehouse.products.kit.name'),
        cell: (c) => (
          <>
            {c.name} {!c.isActive && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}
          </>
        ),
        sortValue: (c) => c.name ?? '',
      },
      { id: 'quantity', header: t('warehouse.products.kit.quantity'), cell: (c) => f.number(c.quantity ?? 0), sortValue: (c) => c.quantity ?? 0, align: 'end' },
    ],
    [t, f],
  )

  if (isLoading) return <Spinner block />
  const isKit = kit?.isKit === true

  if (!isKit) {
    const trackable = product.trackingTypeCode !== 'NONE'
    return (
      <>
        <EmptyState
          title={t('warehouse.products.kit.notKit')}
          body={trackable ? t('warehouse.products.kit.trackingNote') : t('warehouse.products.kit.notKitBody')}
          action={
            canManage && !trackable ? (
              <button type="button" className="btn flow" onClick={() => setEditing(true)}>
                {t('warehouse.products.kit.convert')}
              </button>
            ) : undefined
          }
        />
        {editing && <KitEditorModal publicId={publicId} product={product} kit={kit} onClose={() => setEditing(false)} />}
      </>
    )
  }

  const best = bestCanBuild(kit)
  return (
    <div className="pb">
      <p>
        <Chip tone="neutral">{t('warehouse.products.kit.chip')}</Chip>{' '}
        {kit.adjustable && <Chip tone="neutral">{t('warehouse.products.kit.adjustableChip')}</Chip>}{' '}
        <span>{t('warehouse.products.kit.canBuildBest', { count: best })}</span>
      </p>
      <DataTable
        label={t('warehouse.products.kit.components')}
        columns={columns}
        rows={kit.components ?? []}
        rowKey={(c) => c.productPublicId ?? ''}
        pageSize={25}
        empty={<EmptyState title={t('warehouse.products.kit.noComponents')} />}
      />
      {(kit.availability ?? []).length > 0 && (
        <p style={{ marginTop: 10 }}>
          {(kit.availability ?? []).map((a) => (
            <Chip key={a.warehousePublicId} tone={a.canBuild ? 'neutral' : 'warn'}>
              {t('warehouse.products.kit.canBuildAt', { warehouse: a.warehouseCode ?? '', count: a.canBuild ?? 0 })}
            </Chip>
          ))}
        </p>
      )}
      {canManage && (
        <p style={{ marginTop: 10 }}>
          <button type="button" className="btn flow" onClick={() => setEditing(true)}>
            {t('warehouse.products.kit.edit')}
          </button>{' '}
          <button type="button" className="btn danger" onClick={() => setReverting(true)}>
            {t('warehouse.products.kit.revert')}
          </button>
        </p>
      )}
      {editing && <KitEditorModal publicId={publicId} product={product} kit={kit} onClose={() => setEditing(false)} />}
      <ConfirmDialog
        open={reverting}
        title={t('warehouse.products.kit.revertTitle', { sku: product.sku ?? '' })}
        message={t('warehouse.products.kit.revertBody')}
        confirmLabel={t('warehouse.products.kit.revert')}
        tone="danger"
        onConfirm={async () => {
          try {
            await setKit.mutateAsync({ publicId, body: { isKit: false } })
            toast.success(t('warehouse.products.kit.reverted', { sku: product.sku ?? '' }))
            setReverting(false)
          } catch (e) {
            toast.error(applyProblemDetails(e).title)
            setReverting(false)
          }
        }}
        onClose={() => setReverting(false)}
      />
    </div>
  )
}

function KitEditorModal({ publicId, product, kit, onClose }: { publicId: string; product: ProductListItemDto; kit: KitDto | undefined; onClose: () => void }) {
  const t = useT()
  const uid = useId()
  const setKit = useSetKit()
  let seq = 0
  const [rows, setRows] = useState<KitRow[]>(() => rowsFromKit(kit, (i) => `${uid}-${i}`))
  const [adjustable, setAdjustable] = useState(kit?.adjustable === true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const nextKey = () => `${uid}-n${Date.now()}-${seq++}`

  const set = (key: string, patch: Partial<KitRow>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const save = async () => {
    const problem = kitDraftError(rows)
    if (problem) {
      setError(t(`warehouse.products.kit.err.${problem}`))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await setKit.mutateAsync({ publicId, body: toKitRequest(rows, adjustable) })
      toast.success(t('warehouse.products.kit.saved', { sku: product.sku ?? '' }))
      onClose()
    } catch (e) {
      setError(applyProblemDetails(e).title)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.products.kit.editorTitle', { sku: product.sku ?? '' })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('warehouse.products.kit.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>
            {busy ? t('common.loading') : t('warehouse.products.kit.save')}
          </button>
        </>
      }
    >
      <div className="pb">
        <p>{t('warehouse.products.kit.editorHelp')}</p>
        {rows.map((r, i) => (
          <div className="r2" key={r.key} style={{ alignItems: 'end', marginBottom: 8 }}>
            <div className="f">
              <label htmlFor={`${r.key}-p`}>{t('warehouse.products.kit.component', { n: i + 1 })}</label>
              <ProductPicker
                id={`${r.key}-p`}
                ownOnly={product.isOwn === true}
                ownerClientPublicId={product.ownerClientPublicId ?? undefined}
                value={r.productPublicId}
                onChange={(id, p) => set(r.key, { productPublicId: id, sku: p?.sku ?? '', name: p?.name ?? '' })}
              />
            </div>
            <div className="f">
              <label htmlFor={`${r.key}-q`}>{t('warehouse.products.kit.quantity')}</label>
              <input id={`${r.key}-q`} inputMode="decimal" value={r.quantity} onChange={(e) => set(r.key, { quantity: e.target.value })} />
            </div>
            <button type="button" className="btn" aria-label={t('warehouse.products.kit.removeComponent', { n: i + 1 })} onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))} disabled={rows.length === 1}>
              ×
            </button>
          </div>
        ))}
        <p>
          <button type="button" className="btn" onClick={() => setRows((rs) => [...rs, newKitRow(nextKey())])}>
            {t('warehouse.products.kit.addComponent')}
          </button>
        </p>
        <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input type="checkbox" checked={adjustable} onChange={(e) => setAdjustable(e.target.checked)} />
          {t('warehouse.products.kit.adjustable')}
        </label>
        {error && (
          <p role="alert" className="err">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}
